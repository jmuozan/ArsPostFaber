# Improved Mesh Editor Usage Guide

## Key Changes Made

1. **Fixed Error Messages**:
   - Added missing `end_pinch()` method implementation
   - Fixed attribute references

2. **Improved Webcam Display**:
   - Webcam image is now flipped horizontally (mirror effect) for more intuitive hand tracking

3. **Enhanced Mesh Display**:
   - Vertices are now larger (8px instead of 5px) and more visible
   - Added edge visualization (wireframe) with black color
   - Face color is light gray to contrast with blue vertices
   - Selected vertices are highlighted in red

4. **Improved Control Sensitivity**:
   - Rotation sensitivity reduced to 0.2 (from 0.5) for finer control
   - Zoom sensitivity reduced to 0.03 (from 0.05) for smoother zooming
   - Movement sensitivity reduced to 0.005 (from 0.01) for more precise edits

## Controls

- **ESC**: Exit application
- **r**: Reset mesh to original state
- **s**: Save modified mesh
- **v**: Switch to View mode (rotate/zoom)
- **l**: Switch to Lasso Select mode (select vertices)
- **e**: Switch to Edit mode (manipulate selected vertices with hand gestures)
- **f**: Set Front view
- **t**: Set Top view
- **d**: Set Side view
- **z**: Undo last movement
- **+/-**: Zoom in/out
- **Mouse wheel**: Zoom in/out
- **Left mouse button**: Rotate in view mode, draw lasso in lasso mode

## Tips for Best Results

1. **If you can't see vertices clearly**:
   - The larger point size (8px) should make vertices more visible
   - The blue color of vertices should contrast with the light gray faces
   - The black edges should help define the mesh structure

2. **For better hand tracking**:
   - Ensure good lighting
   - Keep your hand within the camera frame
   - Move slowly when using pinch gestures

3. **If movement is too fast/slow**:
   - The sensitivity values can be adjusted in the code
   - Look for `rotation_sensitivity`, `zoom_sensitivity`, and `movement_sensitivity` in `__init__`

4. **For MacOS users**:
   - Mouse wheel zoom is implemented for both Windows and MacOS standards
   - Alternative + and - key zooming is also available

5. **Common errors**:
   - If you get Open3D version compatibility errors, check your Open3D version
   - OpenCV and MediaPipe warning messages can usually be ignored