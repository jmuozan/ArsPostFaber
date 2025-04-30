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
        self.selected_vertices = []
        self.hand_landmarks = None
        self.running = True
        self.mesh_modified = False
        
        # Initialize MediaPipe hands
        print("Initializing MediaPipe...")
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=0.7,
            min_tracking_confidence=0.5
        )
        
        # Create or load the mesh
        self.ensure_mesh_exists(mesh_path)
        print(f"Loading mesh from {mesh_path}...")
        self.mesh = o3d.io.read_triangle_mesh(mesh_path)
        if not self.mesh.has_vertex_normals():
            self.mesh.compute_vertex_normals()
        self.mesh.paint_uniform_color([0.7, 0.7, 0.7])
        self.mesh.translate(-self.mesh.get_center())
        self.original_mesh = copy.deepcopy(self.mesh)
        
        # Initialize webcam
        print("Opening webcam...")
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            print("Warning: Could not open webcam!")
        
        # Create the integrated interface window
        cv2.namedWindow("Integrated Mesh Editor", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("Integrated Mesh Editor", 1280, 720)
        cv2.setMouseCallback("Integrated Mesh Editor", self.mouse_callback)
        
        # Initialize Open3D offscreen renderer for mesh visualization
        print("Setting up Open3D renderer...")
        self.setup_renderer()
        
        # Create control buttons
        self.setup_ui_controls()
        
        # Last mouse position
        self.last_position = None
        self.is_dragging = False
        self.rotation = [0, 0, 0]  # Euler angles for rotation
        self.translation = [0, 0, 0]
        self.zoom = 0.8
        
        # Movement history for mesh vertices
        self.vertex_movement_history = deque(maxlen=10)  # Store last 10 movements
    
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
        
        # Set initial camera view
        self.view_control = self.vis.get_view_control()
        self.view_control.set_zoom(0.8)
        self.set_front_view()
    
    def setup_ui_controls(self):
        """Create UI controls for the integrated interface."""
        self.button_height = 40
        self.button_margin = 10
        self.button_width = 150
        
        # Define buttons: [name, color, x, y, width, height]
        self.buttons = [
            {"name": "View Mode", "color": (0, 128, 255), "x": 10, "y": 10, 
             "width": self.button_width, "height": self.button_height, "active": True},
            
            {"name": "Lasso Select", "color": (0, 255, 0), "x": 10 + self.button_width + self.button_margin, "y": 10, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Edit Mode", "color": (255, 0, 0), "x": 10 + 2 * (self.button_width + self.button_margin), "y": 10, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Reset Mesh", "color": (128, 128, 128), "x": 10 + 3 * (self.button_width + self.button_margin), "y": 10, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Front View", "color": (50, 50, 200), "x": 10, "y": 10 + self.button_height + self.button_margin, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Top View", "color": (50, 50, 200), "x": 10 + self.button_width + self.button_margin, 
             "y": 10 + self.button_height + self.button_margin, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Side View", "color": (50, 50, 200), "x": 10 + 2 * (self.button_width + self.button_margin), 
             "y": 10 + self.button_height + self.button_margin, 
             "width": self.button_width, "height": self.button_height, "active": False},
            
            {"name": "Save Mesh", "color": (0, 200, 0), "x": 10 + 3 * (self.button_width + self.button_margin), 
             "y": 10 + self.button_height + self.button_margin, 
             "width": self.button_width, "height": self.button_height, "active": False}
        ]
    
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
    
    def reset_mesh(self):
        """Reset mesh to original state."""
        self.mesh = copy.deepcopy(self.original_mesh)
        self.selected_vertices = []
        self.lasso_points = []
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
        # Highlight selected vertices
        vertex_colors = np.asarray(self.mesh.vertex_colors)
        if len(vertex_colors) == 0:
            vertex_colors = np.ones((len(self.mesh.vertices), 3)) * [0.7, 0.7, 0.7]
        
        # Reset all vertices to default color
        vertex_colors[:] = [0.7, 0.7, 0.7]
        
        # Highlight selected vertices in red
        for idx in self.selected_vertices:
            if 0 <= idx < len(vertex_colors):
                vertex_colors[idx] = [1.0, 0.0, 0.0]
        
        self.mesh.vertex_colors = o3d.utility.Vector3dVector(vertex_colors)
        
        # Update mesh in visualizer
        self.vis.update_geometry(self.mesh)
        self.vis.poll_events()
        self.vis.update_renderer()
    
    def draw_ui(self, frame):
        """Draw the UI elements on the frame."""
        # Draw mode indicator background
        cv2.rectangle(frame, (0, 0), (frame.shape[1], 100), (40, 40, 40), -1)
        
        # Draw buttons
        for button in self.buttons:
            color = (button["color"][0] + 50, button["color"][1] + 50, button["color"][2] + 50) if button["active"] else button["color"]
            cv2.rectangle(frame, 
                         (button["x"], button["y"]), 
                         (button["x"] + button["width"], button["y"] + button["height"]), 
                         color, -1)
            
            cv2.rectangle(frame, 
                         (button["x"], button["y"]), 
                         (button["x"] + button["width"], button["y"] + button["height"]), 
                         (255, 255, 255), 1)
            
            text_size = cv2.getTextSize(button["name"], cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)[0]
            text_x = button["x"] + (button["width"] - text_size[0]) // 2
            text_y = button["y"] + (button["height"] + text_size[1]) // 2
            
            cv2.putText(frame, button["name"], (text_x, text_y), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        # Draw current mode and stats
        cv2.putText(frame, f"Mode: {self.mode.upper()}", (10, 80), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)
        
        cv2.putText(frame, f"Selected vertices: {len(self.selected_vertices)}", (250, 80), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)
        
        # Draw lasso if in lasso mode
        if self.mode == "lasso" and len(self.lasso_points) > 1:
            pts = np.array(self.lasso_points, np.int32)
            pts = pts.reshape((-1, 1, 2))
            cv2.polylines(frame, [pts], False, (0, 255, 0), 2)
    
    def draw_hand_landmarks(self, frame, hand_landmarks):
        """Draw hand landmarks on the frame using MediaPipe."""
        if hand_landmarks:
            # Draw hand landmarks
            self.mp_drawing.draw_landmarks(
                frame,
                hand_landmarks,
                self.mp_hands.HAND_CONNECTIONS,
                self.mp_drawing_styles.get_default_hand_landmarks_style(),
                self.mp_drawing_styles.get_default_hand_connections_style()
            )
            
            # If in edit mode, draw a line from index to thumb
            if self.mode == "edit":
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
                
                # Draw line between index and thumb
                color = (0, 0, 255) if distance < 0.08 else (0, 255, 0)
                cv2.line(frame, thumb_pos, index_pos, color, 2)
                
                # Draw a circle at midpoint
                mid_x = (thumb_pos[0] + index_pos[0]) // 2
                mid_y = (thumb_pos[1] + index_pos[1]) // 2
                cv2.circle(frame, (mid_x, mid_y), 5, color, -1)
    
    def mouse_callback(self, event, x, y, flags, param):
        """Handle mouse events."""
        # Check if clicking on any button
        if event == cv2.EVENT_LBUTTONDOWN:
            for button in self.buttons:
                if (button["x"] <= x <= button["x"] + button["width"] and 
                    button["y"] <= y <= button["y"] + button["height"]):
                    self.handle_button_click(button["name"])
                    return
            
            # Start lasso selection if in lasso mode
            if self.mode == "lasso":
                self.lasso_points = [(x, y)]
            
            # Start rotation if in view mode
            if self.mode == "view":
                self.last_position = (x, y)
                self.is_dragging = True
        
        # Continue lasso selection if in lasso mode
        elif event == cv2.EVENT_MOUSEMOVE:
            if self.mode == "lasso" and len(self.lasso_points) > 0:
                self.lasso_points.append((x, y))
            
            # Rotate mesh if dragging in view mode
            if self.mode == "view" and self.is_dragging and self.last_position:
                dx = x - self.last_position[0]
                dy = y - self.last_position[1]
                
                # Update rotation based on mouse movement
                self.rotation[1] += dx * 0.5  # Yaw
                self.rotation[0] += dy * 0.5  # Pitch
                
                # Convert to rotation matrix
                self.update_view_from_rotation()
                
                self.last_position = (x, y)
        
        # End lasso selection or rotation
        elif event == cv2.EVENT_LBUTTONUP:
            if self.mode == "lasso" and len(self.lasso_points) > 2:
                self.process_lasso_selection()
            
            self.is_dragging = False
            self.last_position = None
        
        # Zoom with mouse wheel
        elif event == cv2.EVENT_MOUSEWHEEL:
            delta = flags > 0 and 1 or -1
            self.zoom += delta * 0.05
            self.zoom = max(0.1, min(2.0, self.zoom))
            self.view_control.set_zoom(self.zoom)
            self.update_mesh_visualization()
    
    def update_view_from_rotation(self):
        """Update the view based on current rotation angles."""
        # Convert Euler angles to direction vector
        pitch, yaw, _ = [math.radians(angle) for angle in self.rotation]
        
        # Calculate front direction vector
        x = math.sin(yaw) * math.cos(pitch)
        y = math.sin(pitch)
        z = math.cos(yaw) * math.cos(pitch)
        
        # Update view
        self.view_control.set_front([x, y, z])
        
        # Update visualization
        self.update_mesh_visualization()
    
    def handle_button_click(self, button_name):
        """Handle button clicks."""
        print(f"Button clicked: {button_name}")
        
        # Update button active states
        for button in self.buttons:
            if button["name"] == button_name and button_name in ["View Mode", "Lasso Select", "Edit Mode"]:
                button["active"] = True
            elif button["name"] in ["View Mode", "Lasso Select", "Edit Mode"]:
                button["active"] = False
        
        # Handle specific button actions
        if button_name == "View Mode":
            self.mode = "view"
            self.lasso_points = []
        
        elif button_name == "Lasso Select":
            self.mode = "lasso"
            self.lasso_points = []
        
        elif button_name == "Edit Mode":
            if len(self.selected_vertices) > 0:
                self.mode = "edit"
                self.lasso_points = []
        
        elif button_name == "Reset Mesh":
            self.reset_mesh()
        
        elif button_name == "Front View":
            self.set_front_view()
        
        elif button_name == "Top View":
            self.set_top_view()
        
        elif button_name == "Side View":
            self.set_side_view()
        
        elif button_name == "Save Mesh":
            self.save_mesh()
    
    def process_lasso_selection(self):
        """Process the lasso selection to select vertices."""
        if not self.lasso_points:
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
    
    def process_hand_gesture(self, hand_landmarks, frame):
        """Process hand gestures for mesh editing."""
        if self.mode != "edit" or not hand_landmarks:
            return
        
        # Get thumb and index finger positions
        thumb_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.THUMB_TIP]
        index_tip = hand_landmarks.landmark[self.mp_hands.HandLandmark.INDEX_FINGER_TIP]
        
        # Calculate distance for pinch detection
        distance = math.sqrt(
            (thumb_tip.x - index_tip.x) ** 2 + 
            (thumb_tip.y - index_tip.y) ** 2
        )
        
        # Check for pinch gesture
        is_pinching = distance < 0.08
        
        # If pinching, move selected vertices
        if is_pinching and self.selected_vertices:
            # Calculate 3D movement vector based on hand position
            h, w, _ = frame.shape
            
            # Get hand position in normalized screen coordinates
            hand_x = (thumb_tip.x + index_tip.x) / 2
            hand_y = (thumb_tip.y + index_tip.y) / 2
            hand_z = (thumb_tip.z + index_tip.z) / 2
            
            # Convert to -1 to 1 range
            hand_x = (hand_x - 0.5) * 2
            hand_y = (hand_y - 0.5) * 2
            hand_z = hand_z * 2
            
            # Create movement vector
            movement = np.array([hand_x, -hand_y, hand_z]) * 0.01
            
            # Save current position for history
            vertices = np.asarray(self.mesh.vertices)
            before_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            
            # Apply movement to selected vertices
            for idx in self.selected_vertices:
                vertices[idx] += movement
            
            # Save after positions for history
            after_positions = {idx: vertices[idx].copy() for idx in self.selected_vertices}
            self.vertex_movement_history.append((before_positions, after_positions))
            
            # Update mesh
            self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
            self.mesh.compute_vertex_normals()
            self.mesh_modified = True
            
            # Update visualization
            self.update_mesh_visualization()
    
    def run(self):
        """Main application loop."""
        print("Starting integrated mesh editor...")
        
        while self.running:
            try:
                # Capture frame from webcam
                success, frame = self.cap.read()
                if not success:
                    print("Failed to capture frame from webcam")
                    time.sleep(0.1)
                    continue
                
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
                    display_img = mesh_img
                
                # Process and draw hand landmarks
                if results.multi_hand_landmarks:
                    for hand_landmarks in results.multi_hand_landmarks:
                        self.hand_landmarks = hand_landmarks
                        self.draw_hand_landmarks(display_img, hand_landmarks)
                        self.process_hand_gesture(hand_landmarks, frame)
                else:
                    self.hand_landmarks = None
                
                # Draw UI elements
                self.draw_ui(display_img)
                
                # Show the integrated view
                cv2.imshow("Integrated Mesh Editor", display_img)
                
                # Check for key presses
                key = cv2.waitKey(1) & 0xFF
                if key == 27:  # ESC key
                    break
                elif key == ord('r'):
                    self.reset_mesh()
                elif key == ord('s'):
                    self.save_mesh()
                elif key == ord('v'):
                    self.handle_button_click("View Mode")
                elif key == ord('l'):
                    self.handle_button_click("Lasso Select")
                elif key == ord('e'):
                    self.handle_button_click("Edit Mode")
                elif key == ord('f'):
                    self.set_front_view()
                elif key == ord('t'):
                    self.set_top_view()
                elif key == ord('d'):
                    self.set_side_view()
                elif key == ord('z') and self.vertex_movement_history:
                    # Undo last movement
                    before_positions, _ = self.vertex_movement_history.pop()
                    vertices = np.asarray(self.mesh.vertices)
                    for idx, pos in before_positions.items():
                        vertices[idx] = pos
                    self.mesh.vertices = o3d.utility.Vector3dVector(vertices)
                    self.mesh.compute_vertex_normals()
                    self.update_mesh_visualization()
                
            except Exception as e:
                print(f"Error in main loop: {e}")
                time.sleep(0.1)
        
        # Clean up resources
        self.cap.release()
        cv2.destroyAllWindows()
        self.vis.destroy_window()
        print("Application closed")

if __name__ == "__main__":
    editor = IntegratedMeshEditor("cylinder.stl")
    editor.run()