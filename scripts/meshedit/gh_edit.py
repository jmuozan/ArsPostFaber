import cv2
import numpy as np
import open3d as o3d
import mediapipe as mp
import threading
import time
import copy
import math
import os
import argparse
from collections import deque

class MeshEditor:
    """
    Professional 3D mesh editor with hand tracking and mouse controls.
    Supports multiple editing modes, view management, and advanced visualization options.
    """
    
    def __init__(self, mesh_path="cylinder.stl"):
        # Application state
        self.operation_mode = "view"  # Modes: "view", "lasso", "edit"
        self.lasso_points = []
        self.lasso_origin = None
        self.lasso_close_threshold = 15
        self.lasso_sampling_distance = 3
        self.selected_vertices = []
        self.hand_landmarks = None
        self.application_running = True
        self.mesh_modified = False
        self.gesture_active = False
        self.gesture_origin = None
        self.mouse_edit_origin = None
        self.use_mouse_editing = True
        self.active_hand = "right"
        self.help_visible = False
        
        # Store output path for saving mesh (can be overridden)
        self._output_path = "modified_mesh.stl"
        
        # Default device index for webcam (can be overridden)
        self._device_index = 0
        
        # UI configuration
        self.ui_background = (20, 20, 20)
        self.ui_foreground = (240, 240, 240)
        self.ui_accent = (70, 70, 70)
        self.ui_highlight = (200, 200, 200)
        self.ui_active = (240, 240, 240)
        
        # Mesh visualization configuration
        self.default_color = [1.0, 1.0, 1.0]  # Pure white
        self.selection_color = [1.0, 0.3, 0.3]  # Red-orange
        self.wireframe_color = [0.2, 0.2, 0.2]  # Dark gray
        
        # Interaction parameters
        self.rotation_sensitivity = 0.2
        self.zoom_sensitivity = 0.03
        self.manipulation_sensitivity = 0.15
        
        # Keyboard control mapping and status
        self.KEYBOARD_CONTROLS = {
            # Key: (Key Code, Description)
            'ESC': (27, "Exit application"),
            'V': (ord('v'), "View mode"),
            'L': (ord('l'), "Lasso select mode"),
            'E': (ord('e'), "Edit mode"),
            'F': (ord('f'), "Front view"),
            'T': (ord('t'), "Top view"),
            'D': (ord('d'), "Side view"),
            'S': (ord('s'), "Save mesh"),
            'R': (ord('r'), "Reset mesh"),
            'Z': (ord('z'), "Undo last edit"),
            'M': (ord('m'), "Toggle mouse/hand input"),
            'H': (ord('h'), "Toggle help overlay"),
            '+': (ord('+'), "Zoom in"),
            '=': (ord('='), "Zoom in (alternate)"),
            '-': (ord('-'), "Zoom out"),
            '_': (ord('_'), "Zoom out (alternate)")
        }
        
        # Key state tracking
        self.key_pressed = set()  # Currently pressed keys
        self.last_key_time = {}   # Last time a key was processed
        
        # Initialize hand tracking
        print("Initializing hand tracking system...")
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        self.hand_tracker = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5
        )
        
        # Initialize mesh
        self.ensure_mesh_exists(mesh_path)
        print(f"Loading mesh from {mesh_path}...")
        self.mesh = o3d.io.read_triangle_mesh(mesh_path)
        if not self.mesh.has_vertex_normals():
            self.mesh.compute_vertex_normals()
        self.mesh.paint_uniform_color(self.default_color)
        self.mesh.translate(-self.mesh.get_center())
        
        # Clean the mesh
        self.remove_artifacts()
        
        # Store original state for resets
        self.original_mesh = copy.deepcopy(self.mesh)
        
        # Initialize camera
        print("Initializing camera...")
        self.camera = cv2.VideoCapture(self._device_index)  # Use the configured device index
        if not self.camera.isOpened():
            print(f"Warning: Could not open camera {self._device_index}!")
            # Try fallback to camera 0
            if self._device_index != 0:
                print("Trying fallback to default camera (0)...")
                self.camera = cv2.VideoCapture(0)
        
        # Create application window
        cv2.namedWindow("Mesh Editor", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("Mesh Editor", 1280, 720)
        cv2.setMouseCallback("Mesh Editor", self.process_mouse_event)
        
        # Initialize renderer
        print("Setting up rendering pipeline...")
        self.initialize_renderer()
        
        # Create UI elements
        self.initialize_interface()
        
        # Interaction state
        self.last_cursor_position = None
        self.is_dragging = False
        self.is_right_dragging = False
        
        # Edit history for undo
        self.edit_history = deque(maxlen=10)
    
    def remove_artifacts(self):
        """Remove unwanted artifacts from loaded mesh."""
        print("Cleaning mesh...")
        try:
            vertices = np.asarray(self.mesh.vertices)
            triangles = np.asarray(self.mesh.triangles)
            colors = np.asarray(self.mesh.vertex_colors)
            
            # Use position-based detection
            center = np.mean(vertices, axis=0)
            distances = np.linalg.norm(vertices - center, axis=1)
            threshold = np.median(distances) * 0.3
            central_vertices = set(np.where(distances < threshold)[0])
            
            # Check for purple/colored vertices
            if len(colors) > 0:
                for i, color in enumerate(colors):
                    if color[0] > 0.7 and color[2] > 0.7 and color[1] < 0.3:
                        central_vertices.add(i)
            
            # Filter triangles to remove artifacts
            if central_vertices:
                keep_triangles = []
                for i, triangle in enumerate(triangles):
                    if not (triangle[0] in central_vertices or 
                           triangle[1] in central_vertices or 
                           triangle[2] in central_vertices):
                        keep_triangles.append(i)
                
                if len(keep_triangles) < len(triangles):
                    new_triangles = triangles[keep_triangles]
                    new_mesh = o3d.geometry.TriangleMesh()
                    new_mesh.vertices = o3d.utility.Vector3dVector(vertices)
                    new_mesh.triangles = o3d.utility.Vector3iVector(new_triangles)
                    new_mesh.paint_uniform_color(self.default_color)
                    new_mesh.compute_vertex_normals()
                    self.mesh = new_mesh
                    print("Removed central geometry from mesh")
        except Exception as e:
            print(f"Error during mesh cleaning: {e}")
    
    def ensure_mesh_exists(self, mesh_path):
        """Create a default mesh if file doesn't exist."""
        if not os.path.exists(mesh_path):
            print(f"{mesh_path} not found, creating default mesh...")
            cylinder = o3d.geometry.TriangleMesh.create_cylinder(radius=1.0, height=2.0, resolution=20)
            cylinder.compute_vertex_normals()
            o3d.io.write_triangle_mesh(mesh_path, cylinder)
            print(f"Created {mesh_path}")
    
    def initialize_renderer(self):
        """Set up Open3D rendering pipeline with completely custom lighting to eliminate black spots."""
        self.renderer = o3d.visualization.Visualizer()
        self.renderer.create_window(visible=False, width=1280, height=720)
        
        # Configure basic properties
        render_option = self.renderer.get_render_option()
        render_option.background_color = [0.2, 0.2, 0.2]  # Slightly lighter background to avoid contrast with black areas
        render_option.point_size = 8.0
        render_option.line_width = 1.5
        
        # Try to force the simplest shading model (NO reflections)
        try:
            render_option.mesh_shade_option = o3d.visualization.MeshShadeOption.FLAT
            render_option.mesh_color_option = o3d.visualization.MeshColorOption.COLOR
        except (AttributeError, TypeError):
            pass
            
        # Force all known render settings that might affect black spots
        try:
            # Explicitly turn OFF specific advanced rendering features
            for setting in ["light_on", "mesh_show_wireframe", "mesh_show_back_face", 
                           "point_show_normal", "show_coordinate_frame"]:
                if hasattr(render_option, setting):
                    setattr(render_option, setting, False)
                    
            # Explicitly disable ALL reflection/environment settings
            for setting in dir(render_option):
                if any(x in setting.lower() for x in ["specular", "reflection", "environment", "skybox", "normal_map", "pbr"]):
                    try:
                        value = getattr(render_option, setting)
                        if isinstance(value, bool):
                            setattr(render_option, setting, False)
                        elif isinstance(value, (int, float)):
                            setattr(render_option, setting, 0.0)
                    except:
                        pass
        except:
            pass
            
        # Apply custom material that will completely override the standard material
        self.create_custom_material()
        
        # Add the mesh with our custom material
        self.renderer.add_geometry(self.mesh)
        
        # Setup our completely custom lighting approach
        self.setup_custom_lighting()
        
        # Initialize camera
        self.view_controller = self.renderer.get_view_control()
        self.view_controller.set_zoom(0.8)
        self.set_front_view()
    
    def create_custom_material(self):
        """Create completely custom flat white material with no reflections or lighting effects."""
        # Reset the mesh to pure solid white
        self.mesh.paint_uniform_color(self.default_color)
        
        # Recompute normals with a very large smoothing radius
        # This helps create more uniform normals that won't cause dark spots
        try:
            self.mesh.compute_vertex_normals()
            
            # If possible, try to manually adjust normals to avoid black spots
            vertices = np.asarray(self.mesh.vertices)
            
            # Create normalized camera-facing normals (all pointing towards positive Z)
            custom_normals = np.zeros_like(vertices)
            custom_normals[:, 2] = 1.0  # All normals point at positive Z
            
            # Try to set these uniform normals
            try:
                self.mesh.vertex_normals = o3d.utility.Vector3dVector(custom_normals)
            except:
                pass
                
        except:
            pass
        
        # Try every possible approach to bypass standard material system
        
        # Approach 1: Open3D material system if available
        try:
            material = o3d.visualization.Material()
            # Force pure unlit white material
            material.base_color = [1.0, 1.0, 1.0, 1.0]
            material.base_roughness = 1.0
            material.base_metallic = 0.0
            material.reflectance = 0.0
            material.transmission = 0.0
            material.thickness = 1.0
            material.absorption_color = [0.0, 0.0, 0.0]
            material.absorption_distance = 1000.0
            
            # Disable all advanced material properties
            for prop in ["normal_map", "reflectance_texture", "clearcoat", 
                        "anisotropy", "emission", "thickness_texture"]:
                if hasattr(material, prop):
                    try:
                        setattr(material, prop, None)
                    except:
                        pass
            
            # Try to force the most basic unlit material if available
            if hasattr(material, "shader"):
                try:
                    # Try to set to unlit mode - no lighting will be applied
                    material.shader = "unlit"
                except:
                    pass
            
            self.mesh.material = material
        except:
            pass
        
        # Approach 2: Legacy material properties
        try:
            # Set all standard material properties to flat white
            if hasattr(self.mesh, 'mat_diffuse'):
                self.mesh.mat_diffuse = [1.0, 1.0, 1.0, 1.0]
            if hasattr(self.mesh, 'mat_ambient'):
                self.mesh.mat_ambient = [1.0, 1.0, 1.0, 1.0]
            if hasattr(self.mesh, 'mat_specular'):
                self.mesh.mat_specular = [0.0, 0.0, 0.0, 1.0]
            if hasattr(self.mesh, 'mat_shininess'):
                self.mesh.mat_shininess = 0.0
            if hasattr(self.mesh, 'mat_reflectance'):
                self.mesh.mat_reflectance = 0.0
            if hasattr(self.mesh, 'mat_transparency'):
                self.mesh.mat_transparency = 0.0
        except:
            pass
    
    def setup_custom_lighting(self):
        """Set up completely custom lighting approach to avoid black areas."""
        render_option = self.renderer.get_render_option()
        
        # Force lighting off for our custom solution
        try:
            # Disable standard lighting
            render_option.light_on = False
            
            # Ensure we have an unlit mode if possible
            if hasattr(render_option, 'shader_mode'):
                try:
                    render_option.shader_mode = "unlit"
                except:
                    pass
        except:
            pass
        
        # Set extreme ambient light (this creates a flat white appearance with no shading)
        try:
            render_option.ambient_light = np.array([1.0, 1.0, 1.0])
        except:
            pass
        
        # Disable all known advanced rendering features
        try:
            # Try to completely disable any form of lighting calculation
            if hasattr(render_option, 'lighting_enabled'):
                render_option.lighting_enabled = False
                
            # Explicitly disable all known rendering features that might cause black areas
            for feature in ['shadow', 'global_illumination', 'ambient_occlusion', 
                           'reflections', 'tone_mapping', 'fog', 'ground_reflection']:
                feature_name = f"{feature}_enabled"
                if hasattr(render_option, feature_name):
                    setattr(render_option, feature_name, False)
        except:
            pass
        
        # Try to hack the rendering system if all else fails
        try:
            # Create a completely custom renderer setup by directly accessing renderer options
            renderer_options = self.renderer.get_render_option()
            
            # Try to set to a debug/basic mode if available
            if hasattr(renderer_options, 'debug_mode'):
                renderer_options.debug_mode = True
                
            # Ensure minimal solid color rendering
            if hasattr(renderer_options, 'quality_level'):
                renderer_options.quality_level = "minimum"
                
            # Directly try to access underlying renderer if possible
            if hasattr(self.renderer, '_rendering'):
                try:
                    # Try to set most basic rendering mode (fallback to absolute basics)
                    self.renderer._rendering.set_mode("basic")
                except:
                    pass
        except:
            pass
    
    def initialize_interface(self):
        """Initialize UI elements and menu structure."""
        # Menu bar configuration
        self.menu_height = 40
        self.menu_padding = 15
        self.dropdown_width = 200
        self.dropdown_item_height = 36
        
        # Define main menu structure
        self.main_menu = [
            {"name": "Mode", "x": 10, "width": 100, "active": False, "dropdown": [
                {"name": "View", "action": "set_view_mode"},
                {"name": "Lasso Select", "action": "set_lasso_mode"},
                {"name": "Edit", "action": "set_edit_mode"}
            ]},
            {"name": "View", "x": 120, "width": 100, "active": False, "dropdown": [
                {"name": "Front", "action": "set_front_view"},
                {"name": "Top", "action": "set_top_view"},
                {"name": "Side", "action": "set_side_view"}
            ]},
            {"name": "Mesh", "x": 230, "width": 100, "active": False, "dropdown": [
                {"name": "Reset", "action": "reset_mesh"},
                {"name": "Save", "action": "save_mesh"}
            ]},
            {"name": "Input", "x": 340, "width": 100, "active": False, "dropdown": [
                {"name": "Use Hand Tracking", "action": "toggle_input_method"},
                {"name": "Use Mouse (Right-Click)", "action": "toggle_input_method"}
            ]},
            {"name": "Hand", "x": 450, "width": 100, "active": False, "dropdown": [
                {"name": "Use Right Hand", "action": "toggle_active_hand"},
                {"name": "Use Left Hand", "action": "toggle_active_hand"}
            ]},
            {"name": "Help", "x": 560, "width": 100, "active": False, "action": "show_help"}
        ]
        
        # Calculate positions
        total_width = 0
        for item in self.main_menu:
            item["x"] = 10 + total_width
            total_width += item["width"] + 10
        
        # Initialize dropdown state
        self.active_dropdown = None
        
        # Initialize keyboard handler
        self.setup_keyboard_handler()
    
    def setup_keyboard_handler(self):
        """Set up the keyboard control system."""
        # Create a function to register keyboard event handlers
        def register_keyboard():
            window_name = "Mesh Editor"
            
            # First, try creating a callback function
            def keyboard_callback(key, x, y):
                # Execute the immediate action for this key press
                self.process_key(key)
                return 0
            
            # Try multiple approaches to set up keyboard handling
            
            # Method 1: Direct callback if available
            try:
                cv2.setKeyboardCallback(window_name, keyboard_callback)
                print("Using direct keyboard callback")
                return True
            except:
                pass
            
            # Method 2: Try to set up a specific keyboard handler
            try:
                # Store the callback for reference
                self.keyboard_callback = keyboard_callback
                
                # Set window properties to activate keyboard focus
                cv2.setWindowProperty(window_name, cv2.WND_PROP_TOPMOST, 1)
                cv2.setWindowProperty(window_name, cv2.WND_PROP_AUTOSIZE, 1)
                print("Using window property keyboard handling")
                return True
            except:
                pass
            
            # Method 3: Fallback to internal key tracking system
            print("Using internal keyboard tracking system")
            return False
        
        # Initialize the keyboard input system
        self.direct_keyboard_available = register_keyboard()
        print(f"Keyboard controls initialized: {'direct' if self.direct_keyboard_available else 'polling'} mode")
        
        # Create reverse lookup to find key names from key codes
        self.key_to_name = {}
        for name, (code, _) in self.KEYBOARD_CONTROLS.items():
            self.key_to_name[code] = name
            # Add lowercase version too (for case insensitivity)
            if 65 <= code <= 90:  # A-Z
                lower_code = code + 32  # Convert to lowercase
                self.key_to_name[lower_code] = name
    
    def process_key(self, key):
        """Process a single key press with immediate action."""
        # Check if this is a valid key code
        if key == 255:  # No key pressed
            return
            
        # Get current time for debouncing
        current_time = time.time()
        
        # Normalize key for case insensitivity
        if 97 <= key <= 122:  # a-z lowercase
            key_upper = key - 32  # Convert to uppercase for processing
        else:
            key_upper = key
            
        # Check if we need to debounce this key
        if key in self.last_key_time:
            # Only process if enough time has passed (20ms debounce)
            if current_time - self.last_key_time[key] < 0.02:
                return
                
        # Update last press time for this key
        self.last_key_time[key] = current_time
        
        # Check which key was pressed and execute the appropriate action
        # Always log the key press for user feedback
        key_name = self.key_to_name.get(key, chr(key) if 32 <= key <= 126 else str(key))
        print(f"Key pressed: {key_name} (code: {key})")
        
        # Process specific key actions
        
        # ESC - Exit application
        if key == 27:  # ESC
            print("ESC pressed - exiting application")
            self.application_running = False
            return
            
        # View mode toggle
        elif key_upper == ord('V'):
            print("V pressed - switching to view mode")
            self.set_view_mode()
            
        # Lasso select mode
        elif key_upper == ord('L'):
            print("L pressed - switching to lasso select mode")
            self.set_lasso_mode()
            
        # Edit mode toggle
        elif key_upper == ord('E'):
            print("E pressed - toggling edit mode")
            if self.operation_mode == "lasso":
                self.set_edit_mode()
            elif self.operation_mode == "edit":
                self.set_lasso_mode()
            else:
                self.set_lasso_mode()
                
        # Camera view presets
        elif key_upper == ord('F'):
            print("F pressed - switching to front view")
            self.set_front_view()
            
        elif key_upper == ord('T'):
            print("T pressed - switching to top view")
            self.set_top_view()
            
        elif key_upper == ord('D'):
            print("D pressed - switching to side view")
            self.set_side_view()
            
        # File operations
        elif key_upper == ord('S'):
            print("S pressed - saving mesh")
            self.save_mesh()
            
        elif key_upper == ord('R'):
            print("R pressed - resetting mesh")
            self.reset_mesh()
            
        # Edit operations
        elif key_upper == ord('Z'):
            if self.edit_history:
                print("Z pressed - undoing last edit")
                before_positions, _ = self.edit_history.pop()
                vertices = np.asarray(self.mesh.vertices)
                for idx, pos in before_positions.items():
                    vertices[idx] = pos
                self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
                self.mesh.compute_vertex_normals()
                self.update_visualization()
                
        # Input mode toggle
        elif key_upper == ord('M'):
            print("M pressed - toggling input method")
            self.toggle_input_method()
            
        # Help overlay
        elif key_upper == ord('H'):
            print("H pressed - toggling help overlay")
            self.help_visible = not self.help_visible
            
        # Zoom controls
        elif key == ord('+') or key == ord('='):
            print("+ pressed - zooming in")
            # Scale factor <1 to zoom in (move camera closer)
            zoom_factor = 0.9
            self.view_controller.scale(zoom_factor)
            self.update_visualization()
            
        elif key == ord('-') or key == ord('_'):
            print("- pressed - zooming out")
            # Scale factor >1 to zoom out (move camera further)
            zoom_factor = 1.1
            self.view_controller.scale(zoom_factor)
            self.update_visualization()
            
        # Close help overlay with any key except H
        if self.help_visible and key_upper != ord('H'):
            self.help_visible = False
    
    def ensure_window_focus(self):
        """Ensure window has focus for keyboard input."""
        try:
            # Make window topmost to ensure it gets keyboard focus
            cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_TOPMOST, 1)
            
            # Reset topmost property - this helps ensure focus
            cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_TOPMOST, 0)
            
            # Try to make window have keyboard focus (depends on OpenCV version)
            try:
                cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_VISIBLE, 1)
            except:
                pass
                
            # Attempt to bring window to front on some systems
            try:
                cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_FULLSCREEN, 1)
                cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_FULLSCREEN, 0)
            except:
                pass
                
            # Set autosize property to ensure window layout is complete
            cv2.setWindowProperty("Mesh Editor", cv2.WND_PROP_AUTOSIZE, 1)
            
        except Exception as e:
            print(f"Note: Window focus management limited: {e}")
            
        # Print reminder about keyboard focus
        print("\nIMPORTANT: Click on the window to ensure keyboard focus for controls.")
        print("If keys don't respond, try clicking on the window title bar.\n")
    
    def set_front_view(self):
        """Set front view of the mesh."""
        print("Front view activated")
        self.view_controller.set_lookat([0, 0, 0])
        self.view_controller.set_front([0, 0, 1])
        self.view_controller.set_up([0, 1, 0])
        self.update_visualization()
        self.refresh_rendering()
    
    def set_top_view(self):
        """Set top view of the mesh."""
        print("Top view activated")
        self.view_controller.set_lookat([0, 0, 0])
        self.view_controller.set_front([0, 1, 0])
        self.view_controller.set_up([0, 0, -1])
        self.update_visualization()
        self.refresh_rendering()
    
    def set_side_view(self):
        """Set side view of the mesh."""
        print("Side view activated")
        self.view_controller.set_lookat([0, 0, 0])
        self.view_controller.set_front([1, 0, 0])
        self.view_controller.set_up([0, 1, 0])
        self.update_visualization()
        self.refresh_rendering()
    
    def set_view_mode(self):
        """Switch to view mode."""
        print("View mode activated")
        self.operation_mode = "view"
        self.lasso_points = []
        self.lasso_origin = None
        
    def set_lasso_mode(self):
        """Switch to lasso selection mode."""
        print("Lasso selection mode activated")
        self.operation_mode = "lasso"
        self.lasso_points = []
        self.lasso_origin = None
        
    def set_edit_mode(self):
        """Switch to edit mode."""
        print("Edit mode activated")
        self.operation_mode = "edit"
        self.lasso_points = []
        self.lasso_origin = None
        
    def toggle_input_method(self):
        """Toggle between mouse and hand tracking for editing."""
        self.use_mouse_editing = not self.use_mouse_editing
        if self.use_mouse_editing:
            print("Switched to mouse editing (right-click and drag)")
        else:
            print("Switched to hand tracking")
        
    def toggle_active_hand(self):
        """Toggle between right and left hand for tracking."""
        if self.active_hand == "right":
            self.active_hand = "left"
            print("Switched to left hand tracking")
        else:
            self.active_hand = "right"
            print("Switched to right hand tracking")
    
    def show_help(self):
        """Show help overlay."""
        self.help_visible = True
        print("Help overlay activated - hold to keep visible")
    
    def reset_mesh(self):
        """Reset mesh to original state."""
        self.mesh = copy.deepcopy(self.original_mesh)
        self.selected_vertices = []
        self.lasso_points = []
        self.lasso_origin = None
        self.mesh_modified = False
        
        # Update visualization
        self.renderer.clear_geometries()
        self.renderer.add_geometry(self.mesh)
        self.update_visualization()
        self.refresh_rendering()
        print("Mesh reset to original state")
    
    def save_mesh(self):
        """Save modified mesh to file."""
        if self.mesh_modified:
            try:
                output_path = getattr(self, '_output_path', 'modified_mesh.stl')
                o3d.io.write_triangle_mesh(output_path, self.mesh)
                print(f"Mesh saved as {output_path}")
            except Exception as e:
                print(f"Error saving mesh: {e}")
        else:
            # Even if mesh wasn't modified, save it anyway to enable GH component to get it
            output_path = getattr(self, '_output_path', 'modified_mesh.stl')
            o3d.io.write_triangle_mesh(output_path, self.mesh)
            print(f"Mesh saved as {output_path} (unmodified)")
    
    def update_visualization(self):
        """Update mesh visualization with forced flat-white appearance with no lighting effects."""
        try:
            # Create pure white vertex colors - use oversaturated values to ensure brightness
            vertices = np.asarray(self.mesh.vertices)
            num_vertices = len(vertices)
            
            # Use pure white color for everything
            vertex_colors = np.ones((num_vertices, 3)) * np.array([1.0, 1.0, 1.0])
            
            # Only apply selection highlight
            for idx in self.selected_vertices:
                if 0 <= idx < num_vertices:
                    vertex_colors[idx] = self.selection_color
            
            # Apply vertex colors
            self.mesh.vertex_colors = o3d.utility.Vector3dVector(vertex_colors)
            
            # Override normals to avoid black spots
            try:
                # Create camera-facing normals to eliminate view-dependent shading
                # This is an extremely aggressive approach to remove all shading effects
                
                # Get camera direction
                camera_params = self.view_controller.convert_to_pinhole_camera_parameters()
                extrinsic = np.array(camera_params.extrinsic)
                camera_dir = -extrinsic[:3, 2]  # Negative Z axis of camera
                
                # Create custom normals all facing the camera
                custom_normals = np.tile(camera_dir, (num_vertices, 1))
                
                # Apply the custom normals
                self.mesh.vertex_normals = o3d.utility.Vector3dVector(custom_normals)
            except:
                # Fallback to standard normals if custom approach fails
                self.mesh.compute_vertex_normals()
            
            # Ensure render options are maintained
            render_option = self.renderer.get_render_option()
            
            # Make sure we're using the most basic rendering approach
            try:
                # Disable all lighting effects
                render_option.light_on = False
                
                # Use flat shading
                render_option.mesh_shade_option = o3d.visualization.MeshShadeOption.FLAT
                
                # Ensure full ambient light (making everything uniformly visible)
                render_option.ambient_light = np.array([1.0, 1.0, 1.0])
                
                # Disable all advanced effects
                for feature in ['specular_intensity', 'specular_enabled', 
                               'environment_lighting_enabled', 'environment_map_enabled']:
                    if hasattr(render_option, feature):
                        if isinstance(getattr(render_option, feature), bool):
                            setattr(render_option, feature, False)
                        elif isinstance(getattr(render_option, feature), (int, float)):
                            setattr(render_option, feature, 0.0)
            except:
                pass
            
            # Try the brute-force approach - completely override all material settings
            try:
                # Create a new material for each update
                material = o3d.visualization.Material()
                material.base_color = [1.0, 1.0, 1.0, 1.0]
                material.base_roughness = 1.0
                material.base_metallic = 0.0
                material.reflectance = 0.0
                
                # Try to use unlit shader
                if hasattr(material, 'shader'):
                    material.shader = "unlit"
                
                self.mesh.material = material
            except:
                pass
            
            # Update wireframe visibility based on selection state
            try:
                render_option.mesh_show_wireframe = len(self.selected_vertices) > 0
            except:
                pass
                
            # Force geometry update and rerender
            self.renderer.update_geometry(self.mesh)
            self.renderer.poll_events()
            self.renderer.update_renderer()
            
        except Exception as e:
            print(f"Visualization update error: {e}")
    
    def refresh_rendering(self):
        """Force a complete refresh of the rendering pipeline."""
        try:
            # Update visualization
            self.update_visualization()
            
            # Force renderer update
            self.renderer.update_renderer()
            
            # Small camera adjustment to trigger lighting recalculation
            self.view_controller.rotate(0.1, 0.1)
            self.view_controller.rotate(-0.1, -0.1)
            
        except Exception as e:
            print(f"Rendering refresh error: {e}")
    
    def end_gesture(self):
        """End hand gesture interaction."""
        self.gesture_active = False
        self.gesture_origin = None
        
    def render_interface(self, frame):
        """Render the modern UI elements."""
        h, w = frame.shape[:2]
        
        # Draw top menu bar
        cv2.rectangle(frame, (0, 0), (w, self.menu_height), self.ui_background, -1)
        
        # Draw status information
        input_method = "Mouse (Right-Click)" if self.use_mouse_editing else f"{self.active_hand.upper()} Hand"
        status_text = f"Mode: {self.operation_mode.upper()} | Input: {input_method} | Selected: {len(self.selected_vertices)}"
        status_x = w - 10 - cv2.getTextSize(status_text, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)[0][0]
        cv2.putText(frame, status_text, (status_x, 27), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, self.ui_foreground, 1)
        
        # Draw menu items
        for item in self.main_menu:
            is_open = self.active_dropdown == item["name"] if self.active_dropdown else False
            menu_color = self.ui_active if is_open else self.ui_foreground
            
            # Draw menu text
            cv2.putText(frame, item["name"], (item["x"] + 10, 27), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, menu_color, 1)
            
            # Draw dropdown indicator
            if "dropdown" in item:
                triangle_pts = np.array([
                    [item["x"] + item["width"] - 15, 17], 
                    [item["x"] + item["width"] - 5, 17], 
                    [item["x"] + item["width"] - 10, 25]
                ], np.int32)
                cv2.fillPoly(frame, [triangle_pts], menu_color)
            
            # Draw open dropdown
            if is_open and "dropdown" in item:
                dropdown_height = len(item["dropdown"]) * self.dropdown_item_height
                dropdown_y = self.menu_height
                dropdown_x = item["x"]
                
                # Background
                cv2.rectangle(frame, 
                             (dropdown_x, dropdown_y), 
                             (dropdown_x + self.dropdown_width, dropdown_y + dropdown_height), 
                             self.ui_accent, -1)
                
                # Border
                cv2.rectangle(frame, 
                             (dropdown_x, dropdown_y), 
                             (dropdown_x + self.dropdown_width, dropdown_y + dropdown_height), 
                             self.ui_highlight, 1)
                
                # Dropdown items
                for i, option in enumerate(item["dropdown"]):
                    option_y = dropdown_y + i * self.dropdown_item_height
                    
                    # Check if option is active
                    highlighted = False
                    
                    if item["name"] == "Mode" and option["name"].lower().replace(" ", "_") == self.operation_mode:
                        highlighted = True
                    elif item["name"] == "Hand" and (
                        (option["name"].lower().find("right") >= 0 and self.active_hand == "right") or
                        (option["name"].lower().find("left") >= 0 and self.active_hand == "left")):
                        highlighted = True
                    elif item["name"] == "Input" and (
                        (option["name"].lower().find("mouse") >= 0 and self.use_mouse_editing) or
                        (option["name"].lower().find("hand") >= 0 and not self.use_mouse_editing)):
                        highlighted = True
                        
                    # Draw highlight
                    if highlighted:
                        cv2.rectangle(frame, 
                                     (dropdown_x, option_y), 
                                     (dropdown_x + self.dropdown_width, option_y + self.dropdown_item_height), 
                                     self.ui_highlight, -1)
                    
                    # Draw text
                    option_color = self.ui_active if highlighted else self.ui_foreground
                    cv2.putText(frame, option["name"], 
                               (dropdown_x + 10, option_y + 25), 
                               cv2.FONT_HERSHEY_SIMPLEX, 0.5, option_color, 1)
        
        # Draw lasso if in lasso mode
        if self.operation_mode == "lasso" and len(self.lasso_points) > 1:
            pts = np.array(self.lasso_points, np.int32)
            pts = pts.reshape((-1, 1, 2))
            cv2.polylines(frame, [pts], False, (0, 255, 200), 1)
            
            # Show closing indicator
            if self.lasso_origin and len(self.lasso_points) > 3:
                last_point = self.lasso_points[-1]
                dist_to_start = np.sqrt((last_point[0] - self.lasso_origin[0])**2 + 
                                        (last_point[1] - self.lasso_origin[1])**2)
                
                if dist_to_start < self.lasso_close_threshold * 2:
                    cv2.line(frame, 
                             last_point, 
                             self.lasso_origin, 
                             (0, 255, 255), 1)
                             
        # Draw mouse edit indicator
        if self.operation_mode == "edit" and self.use_mouse_editing and self.is_right_dragging:
            cv2.circle(frame, self.mouse_edit_origin, 5, (0, 200, 255), -1)
            if self.last_cursor_position:
                cv2.line(frame, self.mouse_edit_origin, self.last_cursor_position, (0, 200, 255), 2)
        
        # Draw help overlay if active
        if self.help_visible:
            self.render_help_overlay(frame)
    
    def render_help_overlay(self, frame):
        """Render help overlay with controls information."""
        h, w = frame.shape[:2]
        
        # Create dark overlay
        overlay = frame.copy()
        cv2.rectangle(overlay, (0, 0), (w, h), (0, 0, 0), -1)
        
        # Define help content
        help_commands = [
            "KEYBOARD SHORTCUTS:",
            "ESC: Exit application",
            "V: View mode - Rotate with left-click and drag",
            "L: Lasso select mode - Draw selection with left-click and drag",
            "E: Edit mode - Move vertices with right-click or hand gesture",
            "F: Front view",
            "T: Top view",
            "D: Side view",
            "R: Reset mesh",
            "S: Save mesh",
            "Z: Undo last movement",
            "+/-: Zoom in/out",
            "M: Toggle between mouse and hand input",
            "",
            "MOUSE CONTROLS:",
            "Left-click + drag: Rotate in view mode / Draw lasso in lasso mode",
            "Right-click + drag: Move selected vertices in edit mode",
            "Mouse wheel: Zoom in/out",
            "",
            "HAND GESTURES:",
            "Pinch index finger and thumb: Move selected vertices",
            "",
            "RELEASE MOUSE BUTTON TO CLOSE HELP"
        ]
        
        # Configure layout
        text_spacing = 26
        text_height = len(help_commands) * text_spacing
        text_width = int(w * 0.8)
        start_x = (w - text_width) // 2
        start_y = (h - text_height) // 2
        
        # Draw panel
        margin = 20
        panel_top = start_y - 40
        panel_bottom = start_y + text_height + margin
        panel_left = start_x - margin
        panel_right = start_x + text_width + margin
        
        # Ensure panel fits on screen
        panel_top = max(10, panel_top)
        panel_bottom = min(h - 10, panel_bottom)
        
        # Draw panel background
        cv2.rectangle(overlay, 
                     (panel_left, panel_top), 
                     (panel_right, panel_bottom), 
                     (40, 40, 40), -1)
        
        cv2.rectangle(overlay, 
                     (panel_left, panel_top), 
                     (panel_right, panel_bottom), 
                     (200, 200, 200), 2)
        
        # Draw title
        cv2.putText(overlay, "MESH EDITOR CONTROLS", 
                   (start_x, start_y - 15), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
        
        # Draw commands
        font_size = 0.6
        for i, command in enumerate(help_commands):
            y_pos = start_y + text_spacing * i + 15
            
            if y_pos > h - 10:
                break
                
            if command.endswith(':'):
                cv2.putText(overlay, command, 
                           (start_x, y_pos), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 255), 2)
            else:
                cv2.putText(overlay, command, 
                           (start_x, y_pos), 
                           cv2.FONT_HERSHEY_SIMPLEX, font_size, (255, 255, 255), 1)
        
        # Blend overlay
        alpha = 0.9
        cv2.addWeighted(overlay, alpha, frame, 1 - alpha, 0, frame)
    
    def detect_hand_type(self, hand_landmarks, results=None):
        """Determine if the hand is right or left."""
        # Use MediaPipe classification when available
        if results and hasattr(results, 'multi_handedness') and results.multi_handedness:
            for idx, handedness in enumerate(results.multi_handedness):
                if results.multi_hand_landmarks[idx] == hand_landmarks:
                    classification = handedness.classification[0]
                    return classification.label.lower() == "right"
        
        # Fallback to spatial analysis
        try:
            thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
            pinky_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.PINKY_TIP]
            
            # Check thumb position relative to pinky
            return thumb_tip.x < pinky_tip.x
        except:
            # Default to right hand
            return True
    
    def is_tracked_hand(self, hand_landmarks, results=None):
        """Check if the detected hand matches active hand selection."""
        is_right = self.detect_hand_type(hand_landmarks, results)
        return (is_right and self.active_hand == "right") or (not is_right and self.active_hand == "left")
    
    def visualize_hand(self, frame, hand_landmarks, results=None):
        """Render hand tracking visualization."""
        # Only show in edit mode when hand tracking is enabled
        if self.operation_mode == "edit" and not self.use_mouse_editing and hand_landmarks:
            # Check if this is the active hand
            is_active = self.is_tracked_hand(hand_landmarks, results)
            
            if is_active:
                # Configure drawing styles
                landmark_style = self.mp_drawing_styles.get_default_hand_landmarks_style()
                # Set custom colors
                for i in range(21):
                    landmark_style[i].color = self.ui_foreground
                    landmark_style[i].thickness = 1
                    landmark_style[i].circle_radius = 2
                
                # Configure connection style
                connection_style = self.mp_drawing_styles.get_default_hand_connections_style()
                for connection in self.mp_hands.HAND_CONNECTIONS:
                    connection_style[connection].color = self.ui_foreground
                    connection_style[connection].thickness = 1
                
                # Draw hand landmarks
                self.mp_drawing.draw_landmarks(
                    frame,
                    hand_landmarks,
                    self.mp_hands.HAND_CONNECTIONS,
                    landmark_style,
                    connection_style
                )
                
                # Visualize pinch gesture
                thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
                index_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.INDEX_FINGER_TIP]
                
                h, w, _ = frame.shape
                thumb_pos = (int(thumb_tip.x * w), int(thumb_tip.y * h))
                index_pos = (int(index_tip.x * w), int(index_tip.y * h))
                
                # Calculate pinch distance
                distance = math.sqrt(
                    (thumb_tip.x - index_tip.x) ** 2 + 
                    (thumb_tip.y - index_tip.y) ** 2
                )
                
                # Visualize pinch status
                color = (0, 200, 255) if distance < 0.08 else (0, 255, 200)
                cv2.line(frame, thumb_pos, index_pos, color, 2)
                
                # Draw center point
                mid_x = (thumb_pos[0] + index_pos[0]) // 2
                mid_y = (thumb_pos[1] + index_pos[1]) // 2
                cv2.circle(frame, (mid_x, mid_y), 5, color, -1)
    
    def process_mouse_event(self, event, x, y, flags, param):
        """Process mouse interaction events."""
        # Handle help overlay behavior when clicking outside help menu
        if self.help_visible and event == cv2.EVENT_LBUTTONDOWN:
            # Only hide when clicking outside the Help menu item area
            is_help_button = False
            for item in self.main_menu:
                if item["name"] == "Help":
                    if (item["x"] <= x <= item["x"] + item["width"]) and y <= self.menu_height:
                        is_help_button = True
                        break
            
            if not is_help_button:
                self.help_visible = False
                print("Help overlay closed")
                return
        
        # Press-and-hold help button
        if event == cv2.EVENT_LBUTTONDOWN:
            # Check for Help menu item click
            for item in self.main_menu:
                if item["name"] == "Help":
                    if (item["x"] <= x <= item["x"] + item["width"]) and y <= self.menu_height:
                        print("Help activated - hold to keep visible")
                        self.help_visible = True
                        return
        
        # Handle releasing help button
        elif event == cv2.EVENT_LBUTTONUP:
            # Check if we're releasing over the Help menu item
            for item in self.main_menu:
                if item["name"] == "Help":
                    if (item["x"] <= x <= item["x"] + item["width"]) and y <= self.menu_height:
                        print("Help button released - help overlay closed")
                        self.help_visible = False
                        return
        
        # Regular menu interaction
        if event == cv2.EVENT_LBUTTONDOWN:
            # Check for menu clicks
            if y <= self.menu_height:
                for item in self.main_menu:
                    if (item["x"] <= x <= item["x"] + item["width"]):
                        if "action" in item:
                            # Execute direct action
                            method = getattr(self, item["action"], None)
                            if method:
                                method()
                        elif "dropdown" in item:
                            # Toggle dropdown
                            if self.active_dropdown == item["name"]:
                                self.active_dropdown = None
                            else:
                                self.active_dropdown = item["name"]
                        return
            
            # Handle open dropdown clicks
            if self.active_dropdown:
                for item in self.main_menu:
                    if item["name"] == self.active_dropdown and "dropdown" in item:
                        dropdown_x = item["x"]
                        dropdown_y = self.menu_height
                        dropdown_width = self.dropdown_width
                        
                        # Check if click is within dropdown area
                        if (dropdown_x <= x <= dropdown_x + dropdown_width):
                            dropdown_height = len(item["dropdown"]) * self.dropdown_item_height
                            if (dropdown_y <= y <= dropdown_y + dropdown_height):
                                # Determine which item was clicked
                                item_idx = (y - dropdown_y) // self.dropdown_item_height
                                if 0 <= item_idx < len(item["dropdown"]):
                                    option = item["dropdown"][item_idx]
                                    # Execute menu action
                                    if "action" in option:
                                        method = getattr(self, option["action"], None)
                                        if method:
                                            method()
                                self.active_dropdown = None
                                return
                
                # Close dropdown if clicked elsewhere
                self.active_dropdown = None
                return
            
            # Handle lasso selection in lasso mode
            if self.operation_mode == "lasso" and y > self.menu_height:
                self.lasso_points = [(x, y)]
                self.lasso_origin = (x, y)
            
            # Handle rotation in view mode
            if self.operation_mode == "view" and y > self.menu_height:
                self.last_cursor_position = (x, y)
                self.is_dragging = True
        
        # Handle right-click for edit mode
        elif event == cv2.EVENT_RBUTTONDOWN:
            if self.operation_mode == "edit" and self.use_mouse_editing and y > self.menu_height:
                self.is_right_dragging = True
                self.mouse_edit_origin = (x, y)
                self.last_cursor_position = (x, y)
        
        # Handle mouse movement
        elif event == cv2.EVENT_MOUSEMOVE:
            # Process lasso drawing with enhanced smoothing
            if self.operation_mode == "lasso" and len(self.lasso_points) > 0 and y > self.menu_height:
                # Check for auto-closing when near starting point
                if self.lasso_origin and len(self.lasso_points) > 3:
                    dist_to_start = np.sqrt((x - self.lasso_origin[0])**2 + 
                                           (y - self.lasso_origin[1])**2)
                    
                    if dist_to_start < self.lasso_close_threshold:
                        # Add smooth curve to close the lasso
                        last_point = self.lasso_points[-1]
                        dx = self.lasso_origin[0] - last_point[0]
                        dy = self.lasso_origin[1] - last_point[1]
                        steps = 5
                        
                        for i in range(1, steps):
                            t = i / steps
                            interp_x = int(last_point[0] + dx * t)
                            interp_y = int(last_point[1] + dy * t)
                            self.lasso_points.append((interp_x, interp_y))
                            
                        self.lasso_points.append(self.lasso_origin)
                        self.process_lasso_selection()
                        return
                
                # Apply intelligent point sampling for smooth curves
                last_point = self.lasso_points[-1]
                dist_to_last = np.sqrt((x - last_point[0])**2 + (y - last_point[1])**2)
                
                if dist_to_last > self.lasso_sampling_distance:
                    # Use Bezier interpolation for smoother curves
                    if dist_to_last > self.lasso_sampling_distance * 5:
                        # Create more intermediate points for larger movements
                        prev_point = self.lasso_points[-2] if len(self.lasso_points) > 1 else last_point
                        
                        # Calculate control point for quadratic Bezier curve
                        control_x = last_point[0] * 2 - prev_point[0]
                        control_y = last_point[1] * 2 - prev_point[1]
                        
                        # Determine number of interpolation steps
                        steps = int(dist_to_last / (self.lasso_sampling_distance * 2))
                        steps = min(max(steps, 2), 10)
                        
                        # Generate smooth curve points
                        for i in range(1, steps):
                            t = i / steps
                            # Quadratic Bezier interpolation
                            bezier_x = int((1-t)**2 * last_point[0] + 
                                        2*(1-t)*t * control_x + 
                                        t**2 * x)
                            bezier_y = int((1-t)**2 * last_point[1] + 
                                        2*(1-t)*t * control_y + 
                                        t**2 * y)
                            self.lasso_points.append((bezier_x, bezier_y))
                    else:
                        # Add point directly for small movements
                        self.lasso_points.append((x, y))
            
            # Handle view rotation with consistent orbital control
            if self.operation_mode == "view" and self.is_dragging and self.last_cursor_position and y > self.menu_height:
                # Calculate mouse movement delta
                dx = x - self.last_cursor_position[0]
                dy = y - self.last_cursor_position[1]
                
                # Apply rotation based on view orientation
                rot_y = dx * self.rotation_sensitivity  # Horizontal movement → vertical axis rotation
                rot_x = dy * self.rotation_sensitivity  # Vertical movement → horizontal axis rotation
                
                # Apply rotations using view controller
                self.view_controller.rotate(rot_y, rot_x)
                
                # Update cursor position
                self.last_cursor_position = (x, y)
                
            # Handle mouse-based vertex manipulation
            if self.operation_mode == "edit" and self.use_mouse_editing and self.is_right_dragging and self.last_cursor_position and y > self.menu_height:
                # Only proceed if vertices are selected
                if len(self.selected_vertices) > 0:
                    # Calculate cursor movement
                    dx = x - self.last_cursor_position[0]
                    dy = y - self.last_cursor_position[1]
                    
                    # Transform screen-space movement to view-relative world space
                    movement_scale = 0.01
                    camera_params = self.view_controller.convert_to_pinhole_camera_parameters()
                    extrinsic = np.array(camera_params.extrinsic)
                    
                    # Extract view-space axes
                    right_axis = extrinsic[:3, 0]  # Camera X axis
                    up_axis = extrinsic[:3, 1]     # Camera Y axis
                    
                    # Map screen movement to world space
                    screen_movement = np.array([dx, dy, 0]) * movement_scale
                    world_movement = np.zeros(3)
                    world_movement += right_axis * screen_movement[0]
                    world_movement -= up_axis * screen_movement[1]  # Invert Y for screen coordinates
                    
                    # Apply sensitivity scaling
                    movement = world_movement * (self.manipulation_sensitivity * 10)
                    
                    # Store current positions for undo
                    vertices = np.asarray(self.mesh.vertices)
                    before_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
                    
                    # Apply movement to selected vertices
                    for idx in self.selected_vertices:
                        vertices[idx] += movement
                    
                    # Save for undo history
                    after_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
                    self.edit_history.append((before_positions, after_positions))
                    
                    # Update mesh
                    self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
                    self.mesh.compute_vertex_normals()
                    self.mesh_modified = True
                    
                    # Update visualization
                    self.update_visualization()
                
                # Update cursor position
                self.last_cursor_position = (x, y)
        
        # Handle mouse button release
        elif event == cv2.EVENT_LBUTTONUP:
            # Finalize lasso selection
            if self.operation_mode == "lasso" and len(self.lasso_points) > 2 and y > self.menu_height:
                # Add smooth curve to close the lasso
                if self.lasso_origin:
                    last_point = self.lasso_points[-1]
                    dx = self.lasso_origin[0] - last_point[0]
                    dy = self.lasso_origin[1] - last_point[1]
                    dist_to_start = np.sqrt(dx**2 + dy**2)
                    
                    # Add interpolated points for smooth closure
                    steps = max(3, min(10, int(dist_to_start / self.lasso_sampling_distance)))
                    for i in range(1, steps):
                        t = i / steps
                        interp_x = int(last_point[0] + dx * t)
                        interp_y = int(last_point[1] + dy * t)
                        self.lasso_points.append((interp_x, interp_y))
                        
                    self.lasso_points.append(self.lasso_origin)
                self.process_lasso_selection()
            
            # End rotation
            self.is_dragging = False
            self.last_cursor_position = None
            
        # End right-button dragging
        elif event == cv2.EVENT_RBUTTONUP:
            self.is_right_dragging = False
            self.last_cursor_position = None
        
        # Handle mouse wheel for vertical zooming
        elif event == cv2.EVENT_MOUSEWHEEL:
            # Vertical wheel delta is in high 16 bits of flags
            delta = np.sign(flags >> 16)
            print(f"Mouse wheel vertical event: delta={delta}")
            if delta == 0:
                return
            zoom_factor = 0.9 if delta > 0 else 1.1
            print(f"Applying zoom factor {zoom_factor}")
            self.view_controller.scale(zoom_factor)
            self.update_visualization()
        # Handle horizontal wheel (macOS trackpads sometimes send horizontal scroll for vertical gesture)
        elif event == cv2.EVENT_MOUSEHWHEEL:
            # Horizontal wheel delta is in low bits of flags
            delta = -np.sign(flags)
            print(f"Mouse wheel horizontal event: delta={delta}")
            if delta == 0:
                return
            zoom_factor = 0.9 if delta > 0 else 1.1
            print(f"Applying zoom factor {zoom_factor}")
            self.view_controller.scale(zoom_factor)
            self.update_visualization()
    
    def process_lasso_selection(self):
        """Process lasso selection to identify selected vertices."""
        if not self.lasso_points or len(self.lasso_points) < 3:
            return
        
        # Create a mask for the lasso selection region
        h, w, _ = 720, 1280, 3  # Frame dimensions
        mask = np.zeros((h, w), dtype=np.uint8)
        
        # Convert points to numpy array for OpenCV
        points = np.array(self.lasso_points, dtype=np.int32)
        
        # Fill the lasso region in the mask
        cv2.fillPoly(mask, [points], 255)
        
        # Capture mesh visualization
        render_img = self.renderer.capture_screen_float_buffer(True)
        if render_img is None:
            print("Error: Could not capture render buffer")
            return
        
        render_img = np.asarray(render_img)
        render_img = (render_img * 255).astype(np.uint8)
        render_img = cv2.cvtColor(render_img, cv2.COLOR_RGB2BGR)
        
        # Project mesh vertices to screen space
        vertices = np.asarray(self.mesh.vertices)
        selected_indices = []
        
        # Get current camera parameters
        camera_params = self.view_controller.convert_to_pinhole_camera_parameters()
        
        # Check each vertex
        for idx, vertex in enumerate(vertices):
            # Project 3D point to 2D screen coordinates
            point_2d = self.project_vertex_to_screen(vertex, camera_params, w, h)
            
            # Check if point is inside lasso
            if 0 <= point_2d[0] < w and 0 <= point_2d[1] < h:
                if mask[int(point_2d[1]), int(point_2d[0])] > 0:
                    selected_indices.append(idx)
        
        # Update selection
        self.selected_vertices = selected_indices
        print(f"Selected {len(selected_indices)} vertices")
        
        # Clear lasso
        self.lasso_points = []
        self.lasso_origin = None
        
        # Update visualization
        self.update_visualization()
    
    def project_vertex_to_screen(self, vertex, camera_params, width, height):
        """Project a 3D vertex to screen coordinates."""
        # Extract camera parameters
        extrinsic = np.array(camera_params.extrinsic)
        intrinsic = np.array(camera_params.intrinsic.intrinsic_matrix)
        
        # Convert to homogeneous coordinates
        vertex_h = np.append(vertex, 1)
        
        # Transform to camera space
        vertex_camera = extrinsic @ vertex_h
        
        # Handle points behind camera
        if vertex_camera[2] <= 0:
            return [-1000, -1000]  # Off-screen
        
        # Project to normalized device coordinates
        point_ndc = intrinsic @ vertex_camera[:3]
        
        # Convert to screen coordinates
        x = (point_ndc[0] / point_ndc[2])
        y = (point_ndc[1] / point_ndc[2])
        
        return [x, y]
    
    def process_hand_gesture(self, hand_landmarks, frame, results=None):
        """Process hand gestures for mesh manipulation."""
        # Skip hand processing if mouse editing is enabled
        if self.operation_mode != "edit" or self.use_mouse_editing or not hand_landmarks:
            return
            
        # Check if this is the active hand
        is_active_hand = self.is_tracked_hand(hand_landmarks, results)
        
        if not is_active_hand:
            # End ongoing gesture if hand changes
            if self.gesture_active:
                self.end_gesture()
            return
        
        # Get thumb and index finger positions
        thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
        index_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.INDEX_FINGER_TIP]
        
        # Calculate pinch distance
        distance = math.sqrt(
            (thumb_tip.x - index_tip.x) ** 2 + 
            (thumb_tip.y - index_tip.y) ** 2
        )
        
        # Detect pinch gesture
        is_pinching = distance < 0.15
        
        # Calculate gesture position
        gesture_position = [(thumb_tip.x + index_tip.x) / 2, 
                           (thumb_tip.y + index_tip.y) / 2, 
                           (thumb_tip.z + index_tip.z) / 2]
        
        # Initialize gesture position when starting
        if is_pinching and not self.gesture_active:
            self.gesture_origin = gesture_position
        
        # Update gesture state
        self.gesture_active = is_pinching
        
        # Process vertex manipulation if pinching
        if self.gesture_active and self.selected_vertices and self.gesture_origin:
            # Calculate hand movement
            delta_x = gesture_position[0] - self.gesture_origin[0]
            delta_y = gesture_position[1] - self.gesture_origin[1]
            
            # Apply scaling
            movement_scale = 25.0
            
            # Transform to view-relative world space
            camera_params = self.view_controller.convert_to_pinhole_camera_parameters()
            extrinsic = np.array(camera_params.extrinsic)
            
            # Get view-space axes
            right_axis = extrinsic[:3, 0]
            up_axis = extrinsic[:3, 1]
            
            # Map screen movement to world space
            screen_movement = np.array([delta_x, delta_y, 0]) * movement_scale
            world_movement = np.zeros(3)
            world_movement += right_axis * screen_movement[0]
            world_movement -= up_axis * screen_movement[1]
            
            # Apply sensitivity scaling
            movement = world_movement * self.manipulation_sensitivity
            
            # Store current positions for undo
            vertices = np.asarray(self.mesh.vertices)
            before_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            
            # Apply movement to selected vertices
            for idx in self.selected_vertices:
                vertices[idx] += movement
            
            # Save for undo history
            after_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            self.edit_history.append((before_positions, after_positions))
            
            # Update mesh
            self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
            self.mesh.compute_vertex_normals()
            self.mesh_modified = True
            
            # Update visualization
            self.update_visualization()
            
            # Update gesture origin with smoothing for continuous movement
            self.gesture_origin = [
                self.gesture_origin[0] + (gesture_position[0] - self.gesture_origin[0]) * 0.8,
                self.gesture_origin[1] + (gesture_position[1] - self.gesture_origin[1]) * 0.8,
                self.gesture_origin[2] + (gesture_position[2] - self.gesture_origin[2]) * 0.8
            ]
    
    def run(self):
        """Main application loop."""
        print("\nStarting Professional Mesh Editor")
        print("================================")
        print("Controls:")
        for key_name, (_, description) in self.KEYBOARD_CONTROLS.items():
            print(f"  {key_name}: {description}")
        print("  Click on Help menu item for complete controls")
        
        # Setup to ensure window focus for keyboard
        self.ensure_window_focus()
        
        while self.application_running:
            try:
                # Capture camera frame
                success, frame = self.camera.read()
                if not success:
                    print("Warning: Camera frame acquisition failed")
                    time.sleep(0.1)
                    continue
                
                # Mirror camera frame
                frame = cv2.flip(frame, 1)
                
                # Process hand tracking
                frame.flags.writeable = False
                frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                results = self.hand_tracker.process(frame_rgb)
                frame.flags.writeable = True
                
                # Render mesh
                color_image = self.renderer.capture_screen_float_buffer(True)
                self.refresh_rendering()
                if color_image is None:
                    continue
                
                # Convert to OpenCV format
                mesh_img = np.asarray(color_image)
                mesh_img = (mesh_img * 255).astype(np.uint8)
                mesh_img = cv2.cvtColor(mesh_img, cv2.COLOR_RGB2BGR)
                
                # Resize camera frame to match mesh image
                frame = cv2.resize(frame, (mesh_img.shape[1], mesh_img.shape[0]))
                
                # Create visualization based on mode
                if self.operation_mode == "edit" and not self.use_mouse_editing:
                    # Blend mesh with camera view for hand tracking
                    alpha = 0.7  # Mesh visibility
                    beta = 0.3   # Camera visibility
                    display_img = cv2.addWeighted(mesh_img, alpha, frame, beta, 0)
                else:
                    # Show mesh only
                    display_img = mesh_img.copy()
                
                # Process and visualize hand tracking
                if not self.use_mouse_editing and results.multi_hand_landmarks:
                    for hand_landmarks in results.multi_hand_landmarks:
                        self.hand_landmarks = hand_landmarks
                        self.visualize_hand(display_img, hand_landmarks, results)
                        self.process_hand_gesture(hand_landmarks, frame, results)
                else:
                    self.hand_landmarks = None
                    if self.gesture_active:
                        self.end_gesture()
                
                # Draw UI elements
                self.render_interface(display_img)
                
                # Display final image
                cv2.imshow("Mesh Editor", display_img)
                
                # Maximally responsive keyboard handling with multiple approaches
                
                # APPROACH 1: Check for key press with minimal wait (most responsive for real-time interaction)
                key = cv2.waitKey(1) & 0xFF
                if key != 255 and key != 0:  # Valid key press
                    self.process_key(key)
                
                # APPROACH 2: Use a second check with longer wait for better detection
                # Check again with longer wait time for key presses that might be missed
                key = cv2.waitKey(5) & 0xFF  # Longer wait time for more reliable detection
                if key != 255 and key != 0:  # Valid key press
                    self.process_key(key)
                    
                # APPROACH 3: If we have a direct keyboard callback set up, let it handle key presses
                # This approach is handled automatically by OpenCV in the background
                
                # Short delay for efficiency
                time.sleep(0.01)
                
            except Exception as e:
                print(f"Runtime error: {e}")
                time.sleep(0.1)
        
        # Always save the mesh before closing, even if not modified
        # This is needed for proper Grasshopper integration
        self.save_mesh()
            
        # Cleanup
        self.camera.release()
        cv2.destroyAllWindows()
        self.renderer.destroy_window()
        print("\nApplication closed")

def parse_args():
    """
    Parse command-line arguments for the mesh editor.
    """
    parser = argparse.ArgumentParser(description='Professional 3D Mesh Editor')
    parser.add_argument('--input', type=str, default='cylinder.stl',
                        help='Path to input mesh file')
    parser.add_argument('--output', type=str, default='modified_mesh.stl',
                        help='Path to output modified mesh file')
    parser.add_argument('--device', type=int, default=0,
                        help='Camera device index (default: 0)')
    return parser.parse_args()

def main():
    """Entry point for the application."""
    print("\nInitializing Professional Mesh Editor")
    try:
        # Parse command-line arguments
        args = parse_args()
        
        # Create editor with input mesh path and device index
        editor = MeshEditor(args.input)
        
        # Configure output path
        editor._output_path = args.output
        
        # Set camera device index
        editor._device_index = args.device
        
        # Run the editor
        editor.run()
    except Exception as e:
        print(f"Fatal error: {e}")

if __name__ == "__main__":
    main()