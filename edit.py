import cv2
import numpy as np
import open3d as o3d
import mediapipe as mp
import threading
import time
import copy
import math
import os
from collections import deque

class IntegratedMeshEditor:
    def __init__(self, mesh_path="cylinder.stl"):
        # State variables
        self.mode = "view"  # "view", "lasso", "edit"
        self.lasso_points = []
        self.lasso_start_point = None  # Track the starting point for auto-closing
        self.lasso_auto_close_threshold = 15  # Distance in pixels to auto-close the lasso
        self.lasso_smoothing_factor = 2  # Points to skip for smoothing (higher = smoother)
        self.lasso_min_distance = 3  # Minimum distance between points (lower = more detail)
        self.selected_vertices = []
        self.hand_landmarks = None
        self.running = True
        self.mesh_modified = False
        self.is_pinching = False
        self.initial_pinch_pos = None
        self.active_hand = "right"  # Can be "right" or "left"
        self.show_help = False  # Toggle help overlay
        
        # Modern UI colors
        self.bg_color = (20, 20, 20)  # Dark background
        self.primary_color = (240, 240, 240)  # White text/UI elements
        self.accent_color = (70, 70, 70)  # Dark gray for buttons
        self.highlight_color = (200, 200, 200)  # Light gray for highlights
        self.active_color = (240, 240, 240)  # White for active buttons
        
        # Mesh colors
        self.vertex_color = [0.3, 0.7, 1.0]  # Blue for vertices
        self.selected_vertex_color = [1.0, 0.3, 0.3]  # Red for selected vertices
        self.face_color = [0.85, 0.85, 0.85]  # Light gray for faces
        self.edge_color = [0.2, 0.2, 0.2]  # Dark gray for edges
        
        # Sensitivity settings
        self.rotation_sensitivity = 0.2
        self.zoom_sensitivity = 0.03
        self.movement_sensitivity = 0.15
        
        # Initialize MediaPipe hands
        print("Initializing MediaPipe...")
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5
        )
        
        # Create or load the mesh
        self.ensure_mesh_exists(mesh_path)
        print(f"Loading mesh from {mesh_path}...")
        self.mesh = o3d.io.read_triangle_mesh(mesh_path)
        if not self.mesh.has_vertex_normals():
            self.mesh.compute_vertex_normals()
        self.mesh.paint_uniform_color([0.85, 0.85, 0.85])  # Light gray
        self.mesh.translate(-self.mesh.get_center())
        
        # First, try to remove the purple sphere
        self.clean_mesh()
        
        # Now save the cleaned mesh as the original
        self.original_mesh = copy.deepcopy(self.mesh)
        
        # Initialize webcam
        print("Opening webcam...")
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            print("Warning: Could not open webcam!")
        
        # Create the integrated interface window
        cv2.namedWindow("Mesh Editor", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("Mesh Editor", 1280, 720)
        cv2.setMouseCallback("Mesh Editor", self.mouse_callback)
        
        # Initialize Open3D offscreen renderer for mesh visualization
        print("Setting up Open3D renderer...")
        self.setup_renderer()
        
        # Create modern UI menus
        self.setup_modern_ui()
        
        # Last mouse position
        self.last_position = None
        self.is_dragging = False
        self.rotation = [0, 0, 0]  # Euler angles for rotation
        self.translation = [0, 0, 0]
        self.zoom = 0.8
        
        # Movement history for mesh vertices
        self.vertex_movement_history = deque(maxlen=10)  # Store last 10 movements
    
    def clean_mesh(self):
        """Clean the mesh by removing any purple sphere in the center."""
        print("Cleaning mesh...")
        try:
            # Remove purple vertices first
            vertices = np.asarray(self.mesh.vertices)
            triangles = np.asarray(self.mesh.triangles)
            colors = np.asarray(self.mesh.vertex_colors)
            
            # Start with a position-based approach - more reliable
            center = np.mean(vertices, axis=0)
            distances = np.linalg.norm(vertices - center, axis=1)
            threshold = np.median(distances) * 0.3
            central_vertices = set(np.where(distances < threshold)[0])
            
            # If colors exist, also try to find purple vertices
            if len(colors) > 0:
                for i, color in enumerate(colors):
                    if color[0] > 0.7 and color[2] > 0.7 and color[1] < 0.3:
                        central_vertices.add(i)
            
            # Filter triangles
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
                    new_mesh.paint_uniform_color(self.face_color)
                    new_mesh.compute_vertex_normals()
                    self.mesh = new_mesh
                    print("Central geometry removed from mesh")
        except Exception as e:
            print(f"Error cleaning mesh: {e}")
    
    def ensure_mesh_exists(self, mesh_path):
        """Create a cylinder mesh if the file doesn't exist."""
        if not os.path.exists(mesh_path):
            print(f"{mesh_path} not found, creating a new one...")
            mesh_cylinder = o3d.geometry.TriangleMesh.create_cylinder(radius=1.0, height=2.0, resolution=20)
            mesh_cylinder.compute_vertex_normals()
            o3d.io.write_triangle_mesh(mesh_path, mesh_cylinder)
            print(f"Created {mesh_path}")
    
    def setup_renderer(self):
        """Setup the Open3D offscreen renderer."""
        self.vis = o3d.visualization.Visualizer()
        self.vis.create_window(visible=False, width=1280, height=720)
        self.vis.add_geometry(self.mesh)
        
        # Set rendering options to make vertices and faces distinct
        render_option = self.vis.get_render_option()
        render_option.point_size = 6.0  # Vertices size
        render_option.line_width = 1.5  # Edge thickness
        render_option.background_color = [0.1, 0.1, 0.1]  # Dark background
        render_option.light_on = True
        
        # Try to set advanced rendering options, with fallbacks for older versions
        try:
            render_option.mesh_shade_option = o3d.visualization.MeshShadeOption.Color
        except (AttributeError, TypeError):
            pass
            
        try:
            render_option.mesh_color_option = o3d.visualization.MeshColorOption.Color
        except (AttributeError, TypeError):
            pass
            
        try:
            render_option.show_point_color = True
        except (AttributeError, TypeError):
            pass
            
        # Try to display wireframe (edges)
        try:
            render_option.mesh_show_wireframe = True
        except (AttributeError, TypeError):
            pass
            
        # Remove coordinate frame
        try:
            render_option.show_coordinate_frame = False
        except (AttributeError, TypeError):
            pass
        
        # Set initial camera view
        self.view_control = self.vis.get_view_control()
        self.view_control.set_zoom(0.8)
        self.set_front_view()

    def setup_modern_ui(self):
        """Create modern UI layout with dropdown menu style."""
        # Top menu bar height and options
        self.menu_height = 40
        self.menu_padding = 15
        self.dropdown_width = 200
        self.dropdown_item_height = 36
        
        # Define the top menu items
        self.top_menu = [
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
            {"name": "Hand", "x": 340, "width": 100, "active": False, "dropdown": [
                {"name": "Use Right Hand", "action": "toggle_hand"},
                {"name": "Use Left Hand", "action": "toggle_hand"}
            ]},
            {"name": "Help", "x": 450, "width": 100, "active": False, "action": "toggle_help"}
        ]
        
        # Calculate positions for each menu item
        total_width = 0
        for item in self.top_menu:
            item["x"] = 10 + total_width
            total_width += item["width"] + 10
        
        # Track open dropdown
        self.open_dropdown = None
    
    def set_front_view(self):
        """Set front view for the mesh."""
        self.view_control.set_lookat([0, 0, 0])  # Look at origin
        self.view_control.set_front([0, 0, 1])   # Look from +Z axis
        self.view_control.set_up([0, 1, 0])      # Y is up
        self.rotation = [0, 0, 0]
        self.update_mesh_visualization()
    
    def set_top_view(self):
        """Set top view for the mesh."""
        self.view_control.set_lookat([0, 0, 0])  # Look at origin
        self.view_control.set_front([0, 1, 0])   # Look from +Y axis
        self.view_control.set_up([0, 0, -1])     # -Z is up
        self.rotation = [90, 0, 0]
        self.update_mesh_visualization()
    
    def set_side_view(self):
        """Set side view for the mesh."""
        self.view_control.set_lookat([0, 0, 0])  # Look at origin
        self.view_control.set_front([1, 0, 0])   # Look from +X axis
        self.view_control.set_up([0, 1, 0])      # Y is up
        self.rotation = [0, 90, 0]
        self.update_mesh_visualization()
    
    def set_view_mode(self):
        """Set the application to view mode."""
        self.mode = "view"
        self.lasso_points = []
        self.lasso_start_point = None
        
    def set_lasso_mode(self):
        """Set the application to lasso select mode."""
        self.mode = "lasso"
        self.lasso_points = []
        self.lasso_start_point = None
        
    def set_edit_mode(self):
        """Set the application to edit mode."""
        self.mode = "edit"
        self.lasso_points = []
        self.lasso_start_point = None
        
    def toggle_hand(self):
        """Toggle between right and left hand for control."""
        if self.active_hand == "right":
            self.active_hand = "left"
            print("Switched to LEFT hand control")
        else:
            self.active_hand = "right"
            print("Switched to RIGHT hand control")
            
    def toggle_help(self):
        """Toggle help display."""
        self.show_help = not self.show_help
    
    def reset_mesh(self):
        """Reset mesh to original state."""
        self.mesh = copy.deepcopy(self.original_mesh)
        self.selected_vertices = []
        self.lasso_points = []
        self.lasso_start_point = None
        self.mesh_modified = False
        
        # Update visualization
        self.vis.clear_geometries()
        self.vis.add_geometry(self.mesh)
        self.update_mesh_visualization()
        print("Mesh reset to original state")
    
    def save_mesh(self):
        """Save the current mesh to file."""
        if self.mesh_modified:
            try:
                o3d.io.write_triangle_mesh("modified_cylinder.stl", self.mesh)
                print("Mesh saved as modified_cylinder.stl")
            except Exception as e:
                print(f"Error saving mesh: {e}")
    
    def update_mesh_visualization(self):
        """Update the mesh visualization."""
        try:
            # Highlight selected vertices
            vertex_colors = np.asarray(self.mesh.vertex_colors)
            if len(vertex_colors) == 0:
                vertex_colors = np.ones((len(self.mesh.vertices), 3)) * self.face_color
            
            # Reset all vertices to default color
            vertex_colors[:] = self.face_color
            
            # Highlight selected vertices
            for idx in self.selected_vertices:
                if 0 <= idx < len(vertex_colors):
                    vertex_colors[idx] = self.selected_vertex_color
            
            # Apply vertex colors to mesh
            self.mesh.vertex_colors = o3d.utility.Vector3dVector(vertex_colors)
            
            # Update mesh in visualizer
            self.vis.update_geometry(self.mesh)
            self.vis.poll_events()
            self.vis.update_renderer()
        except Exception as e:
            print(f"Visualization update error: {e}")
    
    def end_pinch(self):
        """End the pinching gesture."""
        self.is_pinching = False
        self.initial_pinch_pos = None
    
    def draw_modern_ui(self, frame):
        """Draw the modern UI elements on the frame."""
        h, w = frame.shape[:2]
        
        # Draw top menu bar background
        cv2.rectangle(frame, (0, 0), (w, self.menu_height), self.bg_color, -1)
        
        # Draw mode indicator and status info
        status_text = f"Mode: {self.mode.upper()} | Selected: {len(self.selected_vertices)}"
        status_x = w - 10 - cv2.getTextSize(status_text, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)[0][0]
        cv2.putText(frame, status_text, (status_x, 27), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, self.primary_color, 1)
        
        # Draw top menu items
        for item in self.top_menu:
            # Determine if this item has the open dropdown
            is_open = self.open_dropdown == item["name"] if self.open_dropdown else False
            
            # Draw menu item
            menu_color = self.active_color if is_open else self.primary_color
            cv2.putText(frame, item["name"], (item["x"] + 10, 27), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, menu_color, 1)
            
            # Draw dropdown triangle indicator
            if "dropdown" in item:
                triangle_pts = np.array([
                    [item["x"] + item["width"] - 15, 17], 
                    [item["x"] + item["width"] - 5, 17], 
                    [item["x"] + item["width"] - 10, 25]
                ], np.int32)
                cv2.fillPoly(frame, [triangle_pts], menu_color)
            
            # Draw dropdown if open
            if is_open and "dropdown" in item:
                # Dropdown background
                dropdown_height = len(item["dropdown"]) * self.dropdown_item_height
                dropdown_y = self.menu_height
                dropdown_x = item["x"]
                
                cv2.rectangle(frame, 
                             (dropdown_x, dropdown_y), 
                             (dropdown_x + self.dropdown_width, dropdown_y + dropdown_height), 
                             self.accent_color, -1)
                
                # Dropdown border
                cv2.rectangle(frame, 
                             (dropdown_x, dropdown_y), 
                             (dropdown_x + self.dropdown_width, dropdown_y + dropdown_height), 
                             self.highlight_color, 1)
                
                # Dropdown items
                for i, option in enumerate(item["dropdown"]):
                    option_y = dropdown_y + i * self.dropdown_item_height
                    
                    # Highlight item for active mode or hand
                    highlighted = False
                    if item["name"] == "Mode" and option["name"].lower().replace(" ", "_") == self.mode:
                        highlighted = True
                    elif item["name"] == "Hand" and (
                        (option["name"].lower().find("right") >= 0 and self.active_hand == "right") or
                        (option["name"].lower().find("left") >= 0 and self.active_hand == "left")):
                        highlighted = True
                        
                    # Draw item background if highlighted
                    if highlighted:
                        cv2.rectangle(frame, 
                                     (dropdown_x, option_y), 
                                     (dropdown_x + self.dropdown_width, option_y + self.dropdown_item_height), 
                                     self.highlight_color, -1)
                    
                    # Draw item text
                    option_color = self.active_color if highlighted else self.primary_color
                    cv2.putText(frame, option["name"], 
                               (dropdown_x + 10, option_y + 25), 
                               cv2.FONT_HERSHEY_SIMPLEX, 0.5, option_color, 1)
        
        # Draw lasso if in lasso mode
        if self.mode == "lasso" and len(self.lasso_points) > 1:
            # Draw the current lasso points
            pts = np.array(self.lasso_points, np.int32)
            pts = pts.reshape((-1, 1, 2))
            cv2.polylines(frame, [pts], False, (0, 255, 200), 1)
            
            # Draw a line from the last point to the first point if close enough
            if self.lasso_start_point and len(self.lasso_points) > 3:
                last_point = self.lasso_points[-1]
                dist_to_start = np.sqrt((last_point[0] - self.lasso_start_point[0])**2 + 
                                        (last_point[1] - self.lasso_start_point[1])**2)
                
                # Show potential closing line if close enough
                if dist_to_start < self.lasso_auto_close_threshold * 2:
                    cv2.line(frame, 
                             last_point, 
                             self.lasso_start_point, 
                             (0, 255, 255), 1)
        
        # Draw help overlay if enabled
        if self.show_help:
            self.draw_help_overlay(frame)

    def draw_help_overlay(self, frame):
        """Draw the help overlay with keyboard shortcuts."""
        h, w = frame.shape[:2]
        
        # Semi-transparent overlay
        overlay = frame.copy()
        cv2.rectangle(overlay, (0, 0), (w, h), (0, 0, 0), -1)
        
        # Add help text
        help_commands = [
            "KEYBOARD CONTROLS:",
            "ESC: Exit application",
            "V: View mode",
            "L: Lasso select mode",
            "E: Edit mode (toggle between Lasso/Edit)",
            "F: Front view",
            "T: Top view",
            "D: Side view",
            "R: Reset mesh",
            "S: Save mesh",
            "Z: Undo last movement",
            "+/-: Zoom in/out",
            "H: Toggle help overlay",
            "",
            "MOUSE CONTROLS:",
            "Left click + drag: Rotate in view mode",
            "Left click + drag: Draw lasso in lasso mode",
            "Mouse wheel: Zoom in/out",
            "",
            "HAND GESTURES:",
            "Pinch index finger and thumb: Grab and move selected vertices",
            "",
            "Press any key to close help"
        ]
        
        # Calculate text block dimensions
        text_height = len(help_commands) * 25
        text_width = 400
        start_x = (w - text_width) // 2
        start_y = (h - text_height) // 2
        
        # Draw background box
        cv2.rectangle(overlay, 
                     (start_x - 20, start_y - 20), 
                     (start_x + text_width + 20, start_y + text_height + 20), 
                     self.accent_color, -1)
        
        cv2.rectangle(overlay, 
                     (start_x - 20, start_y - 20), 
                     (start_x + text_width + 20, start_y + text_height + 20), 
                     self.highlight_color, 1)
        
        # Draw title
        cv2.putText(overlay, "MESH EDITOR HELP", 
                   (start_x, start_y - 5), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, self.active_color, 2)
        
        # Draw commands
        for i, command in enumerate(help_commands):
            y_pos = start_y + 25 * i + 25
            
            # Highlight titles
            if command.endswith(':'):
                cv2.putText(overlay, command, 
                           (start_x, y_pos), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.6, self.active_color, 1)
            else:
                cv2.putText(overlay, command, 
                           (start_x, y_pos), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, self.primary_color, 1)
        
        # Blend the overlay
        alpha = 0.85  # Transparency factor
        cv2.addWeighted(overlay, alpha, frame, 1 - alpha, 0, frame)
    
    def detect_right_hand(self, hand_landmarks, results=None):
        """Detect if the hand is a right hand."""
        # MediaPipe provides hand classification (right/left) in multi_handedness
        if results and hasattr(results, 'multi_handedness') and results.multi_handedness:
            for idx, handedness in enumerate(results.multi_handedness):
                # Check if this is the hand we're processing
                if results.multi_hand_landmarks[idx] == hand_landmarks:
                    # Get the classification (right or left hand)
                    classification = handedness.classification[0]
                    # Check if it's a right hand
                    return classification.label.lower() == "right"
        
        # Fallback method if results not provided
        try:
            # Get thumb and pinky positions
            thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
            pinky_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.PINKY_TIP]
            
            # Check thumb position relative to other fingers
            if thumb_tip.x < pinky_tip.x:
                # Thumb is to the left of pinky, likely a right hand
                return True
            else:
                # Thumb is to the right of pinky, likely a left hand
                return False
        except:
            # Default to assuming it's a right hand for safety
            return True
    
    def is_active_hand(self, hand_landmarks, results=None):
        """Check if the detected hand is the active hand for editing."""
        is_right = self.detect_right_hand(hand_landmarks, results)
        return (is_right and self.active_hand == "right") or (not is_right and self.active_hand == "left")
    
    def draw_hand_landmarks(self, frame, hand_landmarks, results=None):
        """Draw hand landmarks on the frame using MediaPipe."""
        # Only show hand landmarks in edit mode and only for the active hand
        if self.mode == "edit" and hand_landmarks:
            # Detect if this is the active hand
            is_right = self.detect_right_hand(hand_landmarks, results)
            is_active = (is_right and self.active_hand == "right") or (not is_right and self.active_hand == "left")
            
            # Only draw landmarks for the active hand
            if is_active:
                # FIX: Create proper drawing specs instead of custom styles dictionary
                landmark_drawing_spec = self.mp_drawing_styles.get_default_hand_landmarks_style()
                # Override default colors to match our UI
                for i in range(21):  # MediaPipe has 21 hand landmarks
                    landmark_drawing_spec[i].color = self.primary_color
                    landmark_drawing_spec[i].thickness = 1
                    landmark_drawing_spec[i].circle_radius = 2
                
                # Same for connections
                connection_drawing_spec = self.mp_drawing_styles.get_default_hand_connections_style()
                for connection in self.mp_hands.HAND_CONNECTIONS:
                    # Each connection is a tuple of two indices
                    connection_drawing_spec[connection].color = self.primary_color
                    connection_drawing_spec[connection].thickness = 1
                
                # Draw hand landmarks with properly configured styles
                self.mp_drawing.draw_landmarks(
                    frame,
                    hand_landmarks,
                    self.mp_hands.HAND_CONNECTIONS,
                    landmark_drawing_spec,
                    connection_drawing_spec
                )
                
                # Draw pinch visualization
                thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
                index_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.INDEX_FINGER_TIP]
                
                h, w, _ = frame.shape
                thumb_pos = (int(thumb_tip.x * w), int(thumb_tip.y * h))
                index_pos = (int(index_tip.x * w), int(index_tip.y * h))
                
                # Check if pinching
                distance = math.sqrt(
                    (thumb_tip.x - index_tip.x) ** 2 + 
                    (thumb_tip.y - index_tip.y) ** 2
                )
                
                # Set color based on pinch - convert from BGR to tuple for OpenCV
                color = (0, 200, 255) if distance < 0.08 else (0, 255, 200)
                
                # Draw line between index and thumb
                cv2.line(frame, thumb_pos, index_pos, color, 2)
                
                # Draw a circle at midpoint
                mid_x = (thumb_pos[0] + index_pos[0]) // 2
                mid_y = (thumb_pos[1] + index_pos[1]) // 2
                cv2.circle(frame, (mid_x, mid_y), 5, color, -1)

    
    def mouse_callback(self, event, x, y, flags, param):
        """Handle mouse events."""
        # Skip all events if help is showing except for key press to close it
        if self.show_help:
            return
            
        # Check for menu clicks
        if event == cv2.EVENT_LBUTTONDOWN:
            # If a dropdown is open, check for clicks on its items
            if self.open_dropdown:
                for item in self.top_menu:
                    if item["name"] == self.open_dropdown and "dropdown" in item:
                        dropdown_x = item["x"]
                        dropdown_y = self.menu_height
                        dropdown_w = self.dropdown_width
                        
                        # Check if click is within dropdown area
                        if (dropdown_x <= x <= dropdown_x + dropdown_w):
                            for i, option in enumerate(item["dropdown"]):
                                option_y = dropdown_y + i * self.dropdown_item_height
                                if (option_y <= y <= option_y + self.dropdown_item_height):
                                    # Execute menu action
                                    if "action" in option:
                                        method = getattr(self, option["action"], None)
                                        if method:
                                            method()
                                    self.open_dropdown = None
                                    return
                
                # If clicked outside dropdown, close it
                self.open_dropdown = None
                return
            
            # Check for clicks on top menu items
            if y <= self.menu_height:
                for item in self.top_menu:
                    if (item["x"] <= x <= item["x"] + item["width"]):
                        if "action" in item:
                            # Direct action
                            method = getattr(self, item["action"], None)
                            if method:
                                method()
                        elif "dropdown" in item:
                            # Open dropdown
                            self.open_dropdown = item["name"]
                        return
            
            # Start lasso selection if in lasso mode
            if self.mode == "lasso" and y > self.menu_height:
                self.lasso_points = [(x, y)]
                self.lasso_start_point = (x, y)  # Store start point
            
            # Start rotation if in view mode
            if self.mode == "view" and y > self.menu_height:
                self.last_position = (x, y)
                self.is_dragging = True
        
        # Continue lasso selection if in lasso mode with improved smoothing
        elif event == cv2.EVENT_MOUSEMOVE:
            if self.mode == "lasso" and len(self.lasso_points) > 0 and y > self.menu_height:
                # Check if the new point is close to the start point
                if self.lasso_start_point and len(self.lasso_points) > 3:
                    dist_to_start = np.sqrt((x - self.lasso_start_point[0])**2 + 
                                           (y - self.lasso_start_point[1])**2)
                    
                    # Auto-close the lasso if we get close to the start point
                    if dist_to_start < self.lasso_auto_close_threshold:
                        # Add intermediate points for a smoother curve
                        last_point = self.lasso_points[-1]
                        dx = self.lasso_start_point[0] - last_point[0]
                        dy = self.lasso_start_point[1] - last_point[1]
                        steps = 5  # Number of interpolation steps
                        
                        for i in range(1, steps):
                            t = i / steps
                            interp_x = int(last_point[0] + dx * t)
                            interp_y = int(last_point[1] + dy * t)
                            self.lasso_points.append((interp_x, interp_y))
                            
                        self.lasso_points.append(self.lasso_start_point)  # Close the lasso
                        self.process_lasso_selection()
                        return
                
                # Calculate distance to last point for smoothing
                last_point = self.lasso_points[-1]
                dist_to_last = np.sqrt((x - last_point[0])**2 + (y - last_point[1])**2)
                
                # Only add points that are at least min_distance away from the last point
                if dist_to_last > self.lasso_min_distance:
                    # Apply Bezier-like smoothing by adding intermediate points
                    # when the distance is large
                    if dist_to_last > self.lasso_min_distance * 5:
                        # Add intermediate points
                        steps = int(dist_to_last / (self.lasso_min_distance * 2))
                        steps = min(max(steps, 2), 10)  # Limit number of steps
                        
                        # Get previous point for curvature
                        prev_point = self.lasso_points[-2] if len(self.lasso_points) > 1 else last_point
                        
                        # Calculate control points for quadratic Bezier curve
                        # This creates a smoother curve by considering direction of movement
                        control_x = last_point[0] * 2 - prev_point[0]
                        control_y = last_point[1] * 2 - prev_point[1]
                        
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
                        # Just add the point directly for small movements
                        self.lasso_points.append((x, y))
            
            # Rotate mesh if dragging in view mode
            if self.mode == "view" and self.is_dragging and self.last_position and y > self.menu_height:
                # Get mouse movement delta
                dx = x - self.last_position[0]
                dy = y - self.last_position[1]
                
                # Convert to rotation angles (negative dx for natural rotation direction)
                # When moving mouse to the right, object should rotate to the right
                # When moving mouse up, object should rotate upward
                delta_yaw = -dx * self.rotation_sensitivity
                delta_pitch = -dy * self.rotation_sensitivity
                
                # Update rotation angles
                self.rotation[1] += delta_yaw
                self.rotation[0] += delta_pitch
                
                # Apply the rotation to the view
                self.update_view_from_rotation()
                
                # Update last position
                self.last_position = (x, y)
        
        # End lasso selection or rotation
        elif event == cv2.EVENT_LBUTTONUP:
            if self.mode == "lasso" and len(self.lasso_points) > 2 and y > self.menu_height:
                # Add the starting point to close the lasso with smoothing
                if self.lasso_start_point:
                    last_point = self.lasso_points[-1]
                    dx = self.lasso_start_point[0] - last_point[0]
                    dy = self.lasso_start_point[1] - last_point[1]
                    dist_to_start = np.sqrt(dx**2 + dy**2)
                    
                    # Add intermediate points for a smoother curve
                    steps = max(3, min(10, int(dist_to_start / self.lasso_min_distance)))
                    for i in range(1, steps):
                        t = i / steps
                        interp_x = int(last_point[0] + dx * t)
                        interp_y = int(last_point[1] + dy * t)
                        self.lasso_points.append((interp_x, interp_y))
                        
                    self.lasso_points.append(self.lasso_start_point)
                self.process_lasso_selection()
            
            self.is_dragging = False
            self.last_position = None
        
        # Zoom with mouse wheel - Fix for different platforms
        elif event == cv2.EVENT_MOUSEWHEEL:
            # For Windows
            delta = np.sign(flags >> 16)  # Extract vertical wheel motion
            self.zoom += delta * self.zoom_sensitivity
            self.zoom = max(0.1, min(2.0, self.zoom))
            self.view_control.set_zoom(self.zoom)
            self.update_mesh_visualization()
        elif event == cv2.EVENT_MOUSEHWHEEL:  
            # For macOS
            delta = -np.sign(flags)  # Different direction on macOS
            self.zoom += delta * self.zoom_sensitivity
            self.zoom = max(0.1, min(2.0, self.zoom))
            self.view_control.set_zoom(self.zoom)
            self.update_mesh_visualization()
    
    def update_view_from_rotation(self):
        """Update the view based on current rotation angles."""
        # Convert Euler angles to direction vector
        pitch, yaw, _ = [math.radians(angle) for angle in self.rotation]
        
        # Calculate front direction vector - natural orbit control
        # When yaw increases, we rotate rightward around the object
        # When pitch increases, we rotate upward around the object
        x = math.sin(yaw) * math.cos(pitch)
        y = math.sin(pitch)
        z = math.cos(yaw) * math.cos(pitch)
        
        # Update view
        self.view_control.set_front([x, y, z])
        
        # Update visualization
        self.update_mesh_visualization()
    
    def process_lasso_selection(self):
        """Process the lasso selection to select vertices."""
        if not self.lasso_points or len(self.lasso_points) < 3:
            return
        
        # Create a mask for the lasso region
        h, w, _ = 720, 1280, 3  # Frame dimensions
        mask = np.zeros((h, w), dtype=np.uint8)
        
        # Convert lasso points to numpy array
        points = np.array(self.lasso_points, dtype=np.int32)
        
        # Fill the lasso region
        cv2.fillPoly(mask, [points], 255)
        
        # Get rendered image with vertices
        render_img = self.vis.capture_screen_float_buffer(True)
        if render_img is None:
            print("Error: Could not capture screen buffer")
            return
        
        render_img = np.asarray(render_img)
        # Convert to CV2 format (0-255 RGB)
        render_img = (render_img * 255).astype(np.uint8)
        render_img = cv2.cvtColor(render_img, cv2.COLOR_RGB2BGR)
        
        # Project all mesh vertices to screen space
        vertices = np.asarray(self.mesh.vertices)
        selected_indices = []
        
        # Get camera parameters
        param = self.view_control.convert_to_pinhole_camera_parameters()
        
        for idx, vertex in enumerate(vertices):
            # Project vertex to screen coordinates
            point_2d = self.project_point_to_screen(vertex, param, w, h)
            
            # Check if point is inside the lasso
            if 0 <= point_2d[0] < w and 0 <= point_2d[1] < h:
                if mask[int(point_2d[1]), int(point_2d[0])] > 0:
                    selected_indices.append(idx)
        
        # Update selected vertices
        self.selected_vertices = selected_indices
        print(f"Selected {len(selected_indices)} vertices")
        
        # Reset lasso points after selection is complete
        self.lasso_points = []
        self.lasso_start_point = None
        
        # Update mesh visualization to highlight selected vertices
        self.update_mesh_visualization()
    
    def project_point_to_screen(self, point_3d, camera_param, width, height):
        """Project a 3D point to screen coordinates using camera parameters."""
        # Extract camera parameters
        extrinsic = np.array(camera_param.extrinsic)
        intrinsic = np.array(camera_param.intrinsic.intrinsic_matrix)
        
        # Convert point to homogeneous coordinates
        point_h = np.append(point_3d, 1)
        
        # Transform point to camera space
        point_camera = extrinsic @ point_h
        
        # Handle points behind the camera
        if point_camera[2] <= 0:
            return [-1000, -1000]  # Off-screen
        
        # Project to normalized device coordinates
        point_ndc = intrinsic @ point_camera[:3]
        
        # Convert to screen coordinates
        x = (point_ndc[0] / point_ndc[2])
        y = (point_ndc[1] / point_ndc[2])
        
        return [x, y]
    
    def process_hand_gesture(self, hand_landmarks, frame, results=None):
        """Process hand gestures for mesh editing."""
        if self.mode != "edit" or not hand_landmarks:
            return
            
        # Check if this is the active hand we want to use
        is_right = self.detect_right_hand(hand_landmarks, results)
        is_active_hand = (is_right and self.active_hand == "right") or (not is_right and self.active_hand == "left")
        
        # Only process gestures for the active hand
        if not is_active_hand:
            # If previously pinching with active hand and now using inactive hand, end the pinch
            if self.is_pinching:
                self.end_pinch()
            return
        
        # Get thumb and index finger positions
        thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
        index_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.INDEX_FINGER_TIP]
        
        # Calculate distance for pinch detection
        distance = math.sqrt(
            (thumb_tip.x - index_tip.x) ** 2 + 
            (thumb_tip.y - index_tip.y) ** 2
        )
        
        # Check for pinch gesture - increased threshold for better detection
        is_pinching = distance < 0.15  # Increased for better detection
        
        # Track movement of pinch point
        pinch_center = [(thumb_tip.x + index_tip.x) / 2, 
                        (thumb_tip.y + index_tip.y) / 2, 
                        (thumb_tip.z + index_tip.z) / 2]
        
        # Store initial pinch position when starting a pinch
        if is_pinching and not self.is_pinching:
            self.initial_pinch_pos = pinch_center
        
        # Update the is_pinching state
        self.is_pinching = is_pinching
        
        # If pinching, move selected vertices based on hand movement from initial position
        if self.is_pinching and self.selected_vertices and hasattr(self, 'initial_pinch_pos'):
            # Calculate how much the hand has moved since starting the pinch
            delta_x = pinch_center[0] - self.initial_pinch_pos[0]
            delta_y = pinch_center[1] - self.initial_pinch_pos[1]
            
            # Apply scaling factor for more responsive movement
            movement_scale = 25.0  # Increased for faster response
            
            # Get current view parameters
            param = self.view_control.convert_to_pinhole_camera_parameters()
            extrinsic = np.array(param.extrinsic)
            
            # Get the camera's view vectors
            front_dir = extrinsic[:3, 2]  # Camera's Z axis (looking direction)
            right_dir = extrinsic[:3, 0]  # Camera's X axis (right direction)
            up_dir = extrinsic[:3, 1]     # Camera's Y axis (up direction)
            
            # Create a corrected screen-aligned movement vector
            screen_movement = np.array([delta_x, delta_y, 0]) * movement_scale
            
            # Transform screen movement to world space
            world_movement = np.zeros(3)
            # Right/left movement corresponds to camera's right vector
            world_movement += right_dir * screen_movement[0]
            # Up/down movement corresponds to camera's up vector (note: we keep delta_y positive for correct movement)
            world_movement -= up_dir * screen_movement[1]  # Negative to fix the inverted controls
            
            # Scale by sensitivity factor
            movement = world_movement * self.movement_sensitivity
            
            # Apply movement to selected vertices
            vertices = np.asarray(self.mesh.vertices)
            before_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            
            for idx in self.selected_vertices:
                vertices[idx] += movement
            
            # Save positions for undo history
            after_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            self.vertex_movement_history.append((before_positions, after_positions))
            
            # Update mesh
            self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
            self.mesh.compute_vertex_normals()
            self.mesh_modified = True
            
            # Update visualization
            self.update_mesh_visualization()
            
            # Update initial position more frequently for smoother tracking
            self.initial_pinch_pos = [
                self.initial_pinch_pos[0] + (pinch_center[0] - self.initial_pinch_pos[0]) * 0.8,
                self.initial_pinch_pos[1] + (pinch_center[1] - self.initial_pinch_pos[1]) * 0.8,
                self.initial_pinch_pos[2] + (pinch_center[2] - self.initial_pinch_pos[2]) * 0.8
            ]
    
    def run(self):
        """Main application loop."""
        print("Starting modern mesh editor...")
        print("Controls:")
        print("  ESC: Exit")
        print("  r: Reset mesh")
        print("  s: Save mesh")
        print("  v: View mode")
        print("  l: Lasso select mode")
        print("  e: Edit mode (toggle between Lasso/Edit)")
        print("  f: Front view")
        print("  t: Top view")
        print("  d: Side view")
        print("  z: Undo last movement")
        print("  +/-: Zoom in/out")
        print("  Mouse wheel: Zoom in/out")
        print("  h: Toggle help overlay")
        print("  Left mouse button: Rotate in view mode, draw lasso in lasso mode")
        print("  NOTE: Modern interface with improved lasso tool and natural orbit controls")
        
        while self.running:
            try:
                # Capture frame from webcam
                success, frame = self.cap.read()
                if not success:
                    print("Failed to capture frame from webcam")
                    time.sleep(0.1)
                    continue
                
                # Flip the webcam frame horizontally (mirror effect)
                frame = cv2.flip(frame, 1)
                
                # Process frame for hand tracking
                frame.flags.writeable = False
                frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                results = self.hands.process(frame_rgb)
                
                # Make frame writeable again
                frame.flags.writeable = True
                
                # Get the rendered mesh image
                color_image = self.vis.capture_screen_float_buffer(True)
                if color_image is None:
                    continue
                
                # Convert to numpy array and then to CV2 format
                mesh_img = np.asarray(color_image)
                mesh_img = (mesh_img * 255).astype(np.uint8)
                mesh_img = cv2.cvtColor(mesh_img, cv2.COLOR_RGB2BGR)
                
                # Resize webcam frame to match mesh image
                frame = cv2.resize(frame, (mesh_img.shape[1], mesh_img.shape[0]))
                
                # Create a blended view if in edit mode, otherwise show mesh
                if self.mode == "edit":
                    # Blend mesh image with webcam frame
                    alpha = 0.7  # Mesh visibility
                    beta = 0.3   # Webcam visibility
                    blended_img = cv2.addWeighted(mesh_img, alpha, frame, beta, 0)
                    display_img = blended_img
                else:
                    display_img = mesh_img.copy()
                
                # Process and draw hand landmarks
                if results.multi_hand_landmarks:
                    for hand_landmarks in results.multi_hand_landmarks:
                        self.hand_landmarks = hand_landmarks
                        self.draw_hand_landmarks(display_img, hand_landmarks, results)
                        self.process_hand_gesture(hand_landmarks, frame, results)
                else:
                    self.hand_landmarks = None
                    # If hand disappears during pinch, end the pinch
                    if self.is_pinching:
                        self.end_pinch()
                
                # Draw modern UI elements
                self.draw_modern_ui(display_img)
                
                # Show the integrated view
                cv2.imshow("Mesh Editor", display_img)
                
                # Process keyboard shortcuts
                key = cv2.waitKey(1) & 0xFF
                if key == 27:  # ESC key
                    break
                elif key == ord('r'):
                    self.reset_mesh()
                elif key == ord('s'):
                    self.save_mesh()
                elif key == ord('v'):
                    self.set_view_mode()
                elif key == ord('l'):
                    self.set_lasso_mode()
                elif key == ord('e'):
                    # Toggle between Lasso and Edit modes with 'e'
                    if self.mode == "lasso":
                        self.set_edit_mode()
                    elif self.mode == "edit":
                        self.set_lasso_mode()
                    else:
                        # If in view mode, go to lasso first for selection
                        self.set_lasso_mode()
                elif key == ord('f'):
                    self.set_front_view()
                elif key == ord('t'):
                    self.set_top_view()
                elif key == ord('d'):
                    self.set_side_view()
                elif key == ord('h'):
                    self.toggle_help()
                elif key == ord('z') and self.vertex_movement_history:
                    # Undo last movement
                    before_positions, _ = self.vertex_movement_history.pop()
                    vertices = np.asarray(self.mesh.vertices)
                    for idx, pos in before_positions.items():
                        vertices[idx] = pos
                    self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
                    self.mesh.compute_vertex_normals()
                    self.update_mesh_visualization()
                elif key == ord('+') or key == ord('='):  # Plus key
                    self.zoom += self.zoom_sensitivity
                    self.zoom = min(2.0, self.zoom)
                    self.view_control.set_zoom(self.zoom)
                    self.update_mesh_visualization()
                elif key == ord('-') or key == ord('_'):  # Minus key
                    self.zoom -= self.zoom_sensitivity
                    self.zoom = max(0.1, self.zoom)
                    self.view_control.set_zoom(self.zoom)
                    self.update_mesh_visualization()
                # Close help overlay on any key press if it's open
                elif self.show_help:
                    self.show_help = False
                
                # Slight delay to reduce CPU usage
                time.sleep(0.01)
                
            except Exception as e:
                print(f"Error in main loop: {e}")
                time.sleep(0.1)
        
        # Clean up resources
        self.cap.release()
        cv2.destroyAllWindows()
        self.vis.destroy_window()
        print("Application closed")

def main():
    """Main entry point for the application."""
    print("Starting Mesh Editor application...")
    try:
        editor = IntegratedMeshEditor("cylinder.stl")
        editor.run()
    except Exception as e:
        print(f"Application error: {e}")

if __name__ == "__main__":
    main()