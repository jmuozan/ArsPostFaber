using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Eto.Drawing;
using Rhino.Geometry;
using System.Diagnostics;
using System.Threading;
using System.IO;

namespace crft
{
    /// <summary>
    /// Mesh editor window with vertex selection and movement.
    /// </summary>
    internal class MeshEditForm : Form
    {
        private List<Point3d> _vertices;
        private readonly List<int[]> _faces;
        private enum Mode { View, Edit, Hands }
        private Mode _mode = Mode.View;
        private readonly ComboBox _modeSelector;
        private readonly ComboBox _viewSelector;
        private readonly ComboBox _toolSelector;
        private Process _handProcess;
        private Thread _handTrackingThread;
        private readonly PointF[] _handPoints = new PointF[21];
        private bool _handDragging;
        // Default camera resolution for hand tracking overlay
        private const int CamWidth = 640;
        private const int CamHeight = 480;
        private readonly float _pinchThreshold = 60f;
        private bool _isLassoing = false;
        private List<PointF> _lassoPath = new List<PointF>();
        private List<int> _selectedVertices = new List<int>();
        private bool _isGroupDragging = false;
        private List<Point3d> _initialSelected;
        private Point3d _dragAnchorWorld;
        private double _dragRefZ;
        private float _yaw = MathF.PI / 4f;
        private float _pitch = MathF.PI / 6f;
        private float _zoom;
        private float _panX, _panY;
        private Point3d _center;
        private readonly Drawable _canvas;
        private PointF _lastMouse;
        private bool _rotating, _panning;


        /// <summary>
        /// The modified mesh after editing.
        /// </summary>
        public Mesh EditedMesh { get; private set; }

        public MeshEditForm(Mesh mesh)
        {
            Title = "Mesh Edit";
            ClientSize = new Size(800, 600);
            // Duplicate mesh to avoid modifying original
            var m = mesh.DuplicateMesh();
            // Extract vertices and faces
            _vertices = m.Vertices.ToPoint3dArray().ToList();
            _faces = new List<int[]>();
            foreach (var f in m.Faces)
            {
                if (f.IsQuad)
                    _faces.Add(new[] { f.A, f.B, f.C, f.D });
                else
                    _faces.Add(new[] { f.A, f.B, f.C });
            }
            ComputeBounds();

            _canvas = new Drawable { BackgroundColor = Colors.White };
            _canvas.Paint += (s, e) => Draw(e.Graphics);
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;

            var resetButton = new Button { Text = "Reset View" };
            resetButton.Click += (s, e) => { ResetView(); _canvas.Invalidate(); };
            var saveButton = new Button { Text = "Save" };
            saveButton.Click += (s, e) =>
            {
                // Construct the new mesh from edited vertices and faces
                var newMesh = new Mesh();
                foreach (var v in _vertices)
                    newMesh.Vertices.Add(v);
                foreach (var face in _faces)
                {
                    if (face.Length == 4)
                        newMesh.Faces.AddFace(face[0], face[1], face[2], face[3]);
                    else
                        newMesh.Faces.AddFace(face[0], face[1], face[2]);
                }
                newMesh.Normals.ComputeNormals();
                newMesh.Compact();
                EditedMesh = newMesh;
                Close();
            };

            // Mode selector (View/Edit)
            _modeSelector = new ComboBox { Items = { "View", "Edit" }, SelectedIndex = 0 };
            _modeSelector.SelectedIndexChanged += (s, e) =>
            {
                _mode = _modeSelector.SelectedIndex switch
                {
                    0 => Mode.View,
                    1 => Mode.Edit,
                    2 => Mode.Hands
                };
                _isLassoing = false;
                _isGroupDragging = false;
                _lassoPath.Clear();
                _selectedVertices.Clear();
                _canvas.Invalidate();
            };
            // View selector (Standard views)
            _viewSelector = new ComboBox { Items = { "Custom", "Front", "Right", "Back", "Left", "Top", "Bottom" }, SelectedIndex = 0 };
            _viewSelector.SelectedIndexChanged += (s, e) =>
            {
                switch (_viewSelector.SelectedIndex)
                {
                    case 1: // Front
                        _yaw = 0; _pitch = 0; break;
                    case 2: // Right
                        _yaw = MathF.PI / 2f; _pitch = 0; break;
                    case 3: // Back
                        _yaw = MathF.PI; _pitch = 0; break;
                    case 4: // Left
                        _yaw = -MathF.PI / 2f; _pitch = 0; break;
                    case 5: // Top
                        _yaw = 0; _pitch = MathF.PI / 2f; break;
                    case 6: // Bottom
                        _yaw = 0; _pitch = -MathF.PI / 2f; break;
                    default:
                        break;
                }
                ComputeBounds();
                _canvas.Invalidate();
            };
            // Tool selector (Mouse/Hand)
            _toolSelector = new ComboBox { Items = { "Mouse", "Hand" }, SelectedIndex = 0 };
            _toolSelector.SelectedIndexChanged += (s, e) =>
            {
                if (_toolSelector.SelectedIndex == 1) StartHandTracking(); else StopHandTracking();
                _canvas.Invalidate();
            };
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(5) };
            layout.BeginVertical();
            layout.Add(new TableLayout
            {
                Rows = { new TableRow(resetButton, _modeSelector, _viewSelector, _toolSelector, saveButton) }
            });
            layout.Add(_canvas, yscale: true);
            layout.EndVertical();
            Content = layout;
        }

        private void ComputeBounds()
        {
            if (_vertices.Count == 0)
            {
                _center = new Point3d();
                _zoom = 1f;
                return;
            }
            var bb = new BoundingBox(_vertices);
            _center = (bb.Min + bb.Max) * 0.5;
            var ext = new Vector3d(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z);
            var span = Math.Max(Math.Max(ext.X, ext.Y), ext.Z);
            if (span <= 0) span = 1;
            var w = ClientSize.Width * 0.8f;
            var h = ClientSize.Height * 0.8f;
            _zoom = Math.Min(w, h) / (float)span;
        }

        private void ResetView()
        {
            _yaw = MathF.PI / 4f;
            _pitch = MathF.PI / 6f;
            _panX = 0;
            _panY = 0;
            ComputeBounds();
        }

        private void Draw(Graphics g)
        {
            var cw = ClientSize.Width;
            var ch = ClientSize.Height;
            // Hand landmark overlay in Hand mode
            if (_toolSelector.SelectedIndex == 1)
            {
                float fx = cw / CamWidth;
                float fy = ch / CamHeight;
                using (var b = new SolidBrush(Colors.Lime))
                {
                    foreach (var p in _handPoints)
                        g.FillEllipse(b, p.X * fx - 3, p.Y * fy - 3, 6, 6);
                }
                using (var pen = new Pen(Colors.Lime, 2))
                {
                    var p4 = _handPoints[4];
                    var p8 = _handPoints[8];
                    g.DrawLine(pen, p4.X * fx, p4.Y * fy, p8.X * fx, p8.Y * fy);
                }
            }
            var cx = cw / 2f + _panX;
            var cy = ch / 2f + _panY;
            // Compute view direction for shading
            var viewDir = new Vector3d(
                Math.Sin(_pitch) * Math.Sin(_yaw),
                Math.Sin(_pitch) * Math.Cos(_yaw),
                Math.Cos(_pitch)
            );
            // Draw back faces first
            foreach (var face in _faces)
            {
                var a = _vertices[face[0]];
                var b = _vertices[face[1]];
                var c = _vertices[face[2]];
                var normal = Vector3d.CrossProduct(b - a, c - a);
                normal.Unitize();
                var dot = Vector3d.Multiply(normal, viewDir);
                if (dot < 0)
                {
                    var pts2d = face.Select(idx => Project(_vertices[idx], cx, cy)).ToArray();
                    using var brush = new SolidBrush(new Color(0.6f, 0.6f, 0.6f));
                    using var pen = new Pen(Colors.Black, 1);
                    g.FillPolygon(brush, pts2d);
                    g.DrawPolygon(pen, pts2d);
                }
            }
            // Draw front faces
            foreach (var face in _faces)
            {
                var a = _vertices[face[0]];
                var b = _vertices[face[1]];
                var c = _vertices[face[2]];
                var normal = Vector3d.CrossProduct(b - a, c - a);
                normal.Unitize();
                var dot = Vector3d.Multiply(normal, viewDir);
                if (dot >= 0)
                {
                    var pts2d = face.Select(idx => Project(_vertices[idx], cx, cy)).ToArray();
                    using var brush = new SolidBrush(new Color(0.8f, 0.8f, 0.8f));
                    using var pen = new Pen(Colors.Black, 1);
                    g.FillPolygon(brush, pts2d);
                    g.DrawPolygon(pen, pts2d);
                }
            }
            // Draw lasso path in edit mode
            if (_mode == Mode.Edit && _isLassoing && _lassoPath.Count > 1)
            {
                using var penL = new Pen(Colors.Blue, 1);
                g.DrawPolygon(penL, _lassoPath.ToArray());
            }
            // Highlight selected vertices
            if (_mode == Mode.Edit && _selectedVertices.Count > 0)
            {
                using var brush = new SolidBrush(Colors.Red);
                foreach (var idx in _selectedVertices)
                {
                    var p2 = Project(_vertices[idx], cx, cy);
                    g.FillEllipse(brush, p2.X - 4, p2.Y - 4, 8, 8);
                }
            }
        }

        private PointF Project(Point3d p, float cx, float cy)
        {
            var v = p - _center;
            var c = MathF.Cos(_yaw);
            var s = MathF.Sin(_yaw);
            var x1 = (float)(v.X * c - v.Y * s);
            var y1 = (float)(v.X * s + v.Y * c);
            var z1 = (float)v.Z;
            var cp = MathF.Cos(_pitch);
            var sp = MathF.Sin(_pitch);
            var y2 = y1 * cp - z1 * sp;
            return new PointF(x1 * _zoom + cx, -y2 * _zoom + cy);
        }

        private Point3d Unproject(PointF sp, double origZ)
        {
            float cx = ClientSize.Width * 0.5f + _panX;
            float cy = ClientSize.Height * 0.5f + _panY;
            float x1 = (sp.X - cx) / _zoom;
            float y2 = -(sp.Y - cy) / _zoom;
            float z1 = (float)(origZ - _center.Z);
            var spch = MathF.Sin(_pitch);
            var cpch = MathF.Cos(_pitch);
            float y1 = (y2 + z1 * spch) / cpch;
            var sy = MathF.Sin(_yaw);
            var cyaw = MathF.Cos(_yaw);
            float Xc = x1 * cyaw + y1 * sy;
            float Yc = -x1 * sy + y1 * cyaw;
            double wx = Xc + _center.X;
            double wy = Yc + _center.Y;
            return new Point3d(wx, wy, origZ);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _lastMouse = e.Location;
            if (_mode == Mode.View)
            {
                if (e.Buttons == MouseButtons.Primary)
                    _rotating = true;
                else if (e.Buttons == MouseButtons.Middle || e.Buttons == MouseButtons.Alternate)
                    _panning = true;
            }
            else // Edit mode
            {
                if (e.Buttons == MouseButtons.Primary)
                {
                    // Group drag if clicking on selected vertex
                    const float tol = 6f;
                    float best = tol * tol;
                    int hitSel = -1;
                    float cx = ClientSize.Width * 0.5f + _panX;
                    float cy = ClientSize.Height * 0.5f + _panY;
                    foreach (var idx in _selectedVertices)
                    {
                        var proj = Project(_vertices[idx], cx, cy);
                        var dx = proj.X - e.Location.X;
                        var dy = proj.Y - e.Location.Y;
                        var dsq = dx * dx + dy * dy;
                        if (dsq < best)
                        {
                            best = dsq;
                            hitSel = idx;
                        }
                    }
                    if (hitSel >= 0)
                    {
                        _isGroupDragging = true;
                        _initialSelected = _selectedVertices.Select(i => _vertices[i]).ToList();
                        _dragRefZ = _initialSelected.Average(p => p.Z);
                        _dragAnchorWorld = Unproject(e.Location, _dragRefZ);
                        return;
                    }
                    // Start lasso selection
                    _isLassoing = true;
                    _lassoPath.Clear();
                    _lassoPath.Add(e.Location);
                }
                else if (e.Buttons == MouseButtons.Middle || e.Buttons == MouseButtons.Alternate)
                {
                    _panning = true;
                }
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var delta = new PointF(e.Location.X - _lastMouse.X, e.Location.Y - _lastMouse.Y);
            if (_mode == Mode.View)
            {
                if (_rotating)
                {
                    _yaw += delta.X * 0.01f;
                    _pitch += delta.Y * 0.01f;
                    _pitch = Math.Clamp(_pitch, -MathF.PI / 2f, MathF.PI / 2f);
                    _canvas.Invalidate();
                }
                else if (_panning)
                {
                    _panX += delta.X;
                    _panY += delta.Y;
                    _canvas.Invalidate();
                }
            }
            else // Edit mode
            {
                if (_isGroupDragging)
                {
                    var currWorld = Unproject(e.Location, _dragRefZ);
                    var deltaWorld = currWorld - _dragAnchorWorld;
                    for (int i = 0; i < _selectedVertices.Count; i++)
                        _vertices[_selectedVertices[i]] = _initialSelected[i] + deltaWorld;
                    _canvas.Invalidate();
                }
                else if (_isLassoing)
                {
                    _lassoPath.Add(e.Location);
                    _canvas.Invalidate();
                }
                else if (_panning)
                {
                    _panX += delta.X;
                    _panY += delta.Y;
                    _canvas.Invalidate();
                }
            }
            _lastMouse = e.Location;
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_mode == Mode.View)
            {
                _rotating = false;
                _panning = false;
            }
            else // Edit mode
            {
                if (_isLassoing)
                {
                    _isLassoing = false;
                    PerformLassoSelection();
                    _canvas.Invalidate();
                }
                if (_isGroupDragging)
                    _isGroupDragging = false;
                if (_panning)
                    _panning = false;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta.Height > 0) _zoom *= 1.1f;
            else if (e.Delta.Height < 0) _zoom /= 1.1f;
            _canvas.Invalidate();
        }
        /// <summary>
        /// Select vertices inside the lasso polygon.
        /// </summary>
        private void PerformLassoSelection()
        {
            if (_lassoPath.Count < 3 || _vertices.Count == 0)
                return;
            var poly = _lassoPath.ToArray();
            _selectedVertices.Clear();
            float cx = ClientSize.Width * 0.5f + _panX;
            float cy = ClientSize.Height * 0.5f + _panY;
            for (int i = 0; i < _vertices.Count; i++)
            {
                var pt3 = _vertices[i];
                var pt2 = Project(pt3, cx, cy);
                if (PointInPolygon(poly, pt2))
                    _selectedVertices.Add(i);
            }
        }
        /// <summary>
        /// Point-in-polygon test (ray-casting).
        /// </summary>
        private bool PointInPolygon(PointF[] polygon, PointF test)
        {
            bool result = false;
            int j = polygon.Length - 1;
            for (int i = 0; i < polygon.Length; i++)
            {
                if ((polygon[i].Y < test.Y && polygon[j].Y >= test.Y || polygon[j].Y < test.Y && polygon[i].Y >= test.Y) &&
                    (polygon[i].X + (test.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) * (polygon[j].X - polygon[i].X) < test.X))
                    result = !result;
                j = i;
            }
            return result;
        }

        private void StartHandTracking()
        {
            StopHandTracking();
            try
            {
                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "hands.py");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    // -E ignores PYTHONHOME and PYTHONPATH to use clean stdlib
                    Arguments = $"-E \"{scriptPath}\" --headless",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                // Prevent any PYTHONHOME/PYTHONPATH from interfering
                startInfo.Environment.Remove("PYTHONHOME");
                startInfo.Environment.Remove("PYTHONPATH");
                _handProcess = Process.Start(startInfo);
                if (_handProcess == null)
                {
                    MessageBox.Show(this, "Could not start Python hand tracking script. Ensure python3 is installed and in PATH.",
                        MessageBoxButtons.OK, MessageBoxType.Error);
                    return;
                }
                // capture stderr to show errors
                _handProcess.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    // Ignore Python environment diagnostic prints and harmless warnings
                    var line = e.Data.Trim();
                    var ignore = new[] { "INFO:", "WARNING:", "objc[", "Python path configuration:", "PYTHONHOME", "PYTHONPATH", "program name", "isolated", "environment", "safe_path", "user site", "import site", "import: \"site\"", "is in build tree", "stdlib dir", "sys." };
                    if (ignore.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return;
                    Application.Instance.Invoke(() =>
                    {
                        var msg = line;
                        if (line.Contains("No module named", StringComparison.Ordinal))
                            msg += "\n\nPlease install required Python packages with:\n    pip3 install mediapipe opencv-python numpy";
                        MessageBox.Show(this, $"Hand tracking error:\n{msg}", MessageBoxButtons.OK, MessageBoxType.Error);
                    });
                };
                _handProcess.BeginErrorReadLine();
                // notify user that script launched
                MessageBox.Show(this, "Hand tracking process launched.",
                    MessageBoxButtons.OK, MessageBoxType.Information);
                _handTrackingThread = new Thread(() =>
                {
                    bool pinched = false;
                    string line;
                    while (_handProcess != null && !_handProcess.HasExited && (line = _handProcess.StandardOutput.ReadLine()) != null)
                    {
                        var parts = line.Split(' ');
                        if (parts.Length < 5) continue;
                        if (!float.TryParse(parts[0], out var tx)) continue;
                        if (!float.TryParse(parts[1], out var ty)) continue;
                        if (!float.TryParse(parts[2], out var ix)) continue;
                        if (!float.TryParse(parts[3], out var iy)) continue;
                        if (!float.TryParse(parts[4], out var dist)) continue;
                        Application.Instance.Invoke(() =>
                        {
                            var cx = ClientSize.Width;
                            var cy = ClientSize.Height;
                            var mx = (tx + ix) * 0.5f * cx;
                            var my = (ty + iy) * 0.5f * cy;
                            var loc = new PointF(mx, my);
                            var downArgs = new MouseEventArgs(MouseButtons.Primary, Keys.None, loc, null, 1f);
                            var moveArgs = new MouseEventArgs(MouseButtons.Primary, Keys.None, loc, null, 1f);
                            var upArgs = new MouseEventArgs(MouseButtons.Primary, Keys.None, loc, null, 1f);
                            if (!pinched && dist < 0.08f)
                            {
                                pinched = true;
                                if (_mode == Mode.Edit && _selectedVertices.Count > 0)
                                {
                                    _isGroupDragging = true;
                                    _initialSelected = _selectedVertices.Select(i => _vertices[i]).ToList();
                                    _dragRefZ = _initialSelected.Average(p => p.Z);
                                    _dragAnchorWorld = Unproject(loc, _dragRefZ);
                                }
                            }
                            else if (pinched && dist < 0.08f)
                            {
                                Canvas_MouseMove(_canvas, moveArgs);
                            }
                            else if (pinched && dist >= 0.08f)
                            {
                                Canvas_MouseUp(_canvas, upArgs);
                                pinched = false;
                            }
                        });
                    }
                }) { IsBackground = true };
                _handTrackingThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start hand tracking process: {ex}");
            }
        }

        private void StopHandTracking()
        {
            // TODO: stop hand tracking process
        if (_handProcess != null)
        {
            try { _handProcess.Kill(); } catch { }
        }
        if (_handTrackingThread != null)
        {
            _handTrackingThread.Join();
            _handTrackingThread = null;
        }
        if (_handProcess != null)
        {
            try { _handProcess.Dispose(); } catch { }
            _handProcess = null;
        }
        }
    }
}