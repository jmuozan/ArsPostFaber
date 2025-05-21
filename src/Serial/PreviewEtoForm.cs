using System;
using System.Collections.Generic;
using Eto.Forms;
using Eto.Drawing;
using System.Linq;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Cross-platform preview window for G-code path using Eto.Forms.
    /// Supports view (orbit/pan/zoom) mode.
    /// </summary>
    internal class PreviewEtoForm : Form
    {
        /// <summary>Editable segments with endpoints and color.</summary>
        private readonly List<Segment> _segments;
        // Internal segment data class
        private class Segment
        {
            public Point3d A;
            public Point3d B;
            public System.Drawing.Color Color;
        }
        private readonly List<Point3d> _points;
        private readonly List<Point3d> _samplePoints;
        // Edited upcoming path points with command indices
        private readonly List<Tuple<int, Point3d>> _editSamples;
        // Bounding box region for constraint
        private Point3d _bboxMin;
        private Point3d _bboxMax;
        // Group drag state for selected sample points
        private bool _isGroupDragging = false;
        private Point3d _dragAnchorWorld;
        private double _dragRefZ;
        private List<Point3d> _initialSelectedPoints;
        private float _yaw = MathF.PI / 4f;
        private float _pitch = MathF.PI / 6f;
        private float _zoom;
        private float _panX, _panY;
        private PointF _lastMouse;
        private bool _rotating, _panning;
        private Point3d _center;
        private readonly Drawable _canvas;
        // Editing state
        private bool _isEditing = false;
        private bool _isDragging = false;
        private int _selectedSegIdx;
        private bool _selectedIsEnd;
        private double _origDragZ;
        // Selection mode state
        private enum Mode { Edit, Select }
        private Mode _mode = Mode.Edit;
        private bool _isLassoing = false;
        private List<PointF> _lassoPath = new List<PointF>();
        private List<int> _selectedSampleIndices = new List<int>();

        public PreviewEtoForm(List<Tuple<Point3d, Point3d, System.Drawing.Color>> segments)
        {
            Title = "G-code Path Preview";
            ClientSize = new Size(800, 600);
            // Initialize editable segments from input tuples
            _segments = new List<Segment>();
            if (segments != null)
            {
                foreach (var seg in segments)
                {
                    _segments.Add(new Segment { A = seg.Item1, B = seg.Item2, Color = seg.Item3 });
                }
            }
            _points = new List<Point3d>();
            // Initialize editable samples
            _editSamples = new List<Tuple<int, Point3d>>();
            // Collect all segment endpoints for bounding box
            foreach (var seg in _segments)
            {
                _points.Add(seg.A);
                _points.Add(seg.B);
            }
            _samplePoints = new List<Point3d>();
            ComputeBounds();
            // Capture bounding box region from segments (includes printed bbox)
            var bbAll = new BoundingBox(_points);
            _bboxMin = bbAll.Min;
            _bboxMax = bbAll.Max;
            _canvas = new Drawable { BackgroundColor = Colors.White };
            _canvas.Paint += (s, e) => Draw(e.Graphics);
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;
            // Toolbar: Reset view button and mode selector
            var resetButton = new Button { Text = "Reset View" };
            resetButton.Click += (s, e) =>
            {
                _yaw = MathF.PI / 4f;
                _pitch = MathF.PI / 6f;
                _panX = 0;
                _panY = 0;
                ComputeBounds();
                _canvas.Invalidate();
            };
            var modeSelector = new ComboBox { Items = { "Edit", "Select" }, SelectedIndex = 0 };
            modeSelector.SelectedIndexChanged += (s, e) =>
            {
                _mode = modeSelector.SelectedIndex == 0 ? Mode.Edit : Mode.Select;
                _isLassoing = false;
                _lassoPath.Clear();
                if (_mode == Mode.Select)
                    _selectedSampleIndices.Clear();
                _canvas.Invalidate();
            };
            // Save edits and close preview, triggering component update
            var saveButton = new Button { Text = "Save" };
            saveButton.Click += (s, e) =>
            {
                // Commit edited sample points back to editSamples before closing
                int count = Math.Min(_editSamples.Count, _samplePoints.Count);
                for (int i = 0; i < count; i++)
                {
                    var cmdIdx = _editSamples[i].Item1;
                    var pt = _samplePoints[i];
                    _editSamples[i] = Tuple.Create(cmdIdx, pt);
                }
                Close();
            };
            // Layout: toolbar and canvas in a vertical DynamicLayout
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(5) };
            layout.BeginVertical();
            // Toolbar row: Reset, Mode, Save
            layout.Add(new TableLayout
            {
                Rows = { new TableRow(resetButton, modeSelector, saveButton) }
            });
            layout.Add(_canvas, yscale: true);
            layout.EndVertical();
            Content = layout;
        }
        /// <summary>
        /// Constructor with editable sample points for preview (with command indices).
        /// </summary>
        public PreviewEtoForm(List<Tuple<Point3d, Point3d, System.Drawing.Color>> segments, List<Tuple<int, Point3d>> editSamples)
            : this(segments)
        {
            _editSamples = editSamples ?? new List<Tuple<int, Point3d>>();
            _samplePoints.Clear();
            foreach (var tup in _editSamples)
            {
                _samplePoints.Add(tup.Item2);
                _points.Add(tup.Item2);
            }
            ComputeBounds();
        }

        private void ComputeBounds()
        {
            if (_points.Count == 0)
            {
                _center = new Point3d();
                _zoom = 1f;
                return;
            }
            var bb = new BoundingBox(_points);
            _center = (bb.Min + bb.Max) * 0.5;
            var ext = new Vector3d(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z);
            var span = Math.Max(Math.Max(ext.X, ext.Y), ext.Z);
            if (span <= 0) span = 1;
            var w = ClientSize.Width * 0.8f;
            var h = ClientSize.Height * 0.8f;
            _zoom = Math.Min(w, h) / (float)span;
        }

        private void Draw(Graphics g)
        {
            var cw = ClientSize.Width;
            var ch = ClientSize.Height;
            var cx = cw / 2f + _panX;
            var cy = ch / 2f + _panY;
            // Draw segments (skip upcoming translation segments when in edit preview)
            foreach (var seg in _segments)
            {
                // Skip original blue (unexecuted translation) segments if edited path exists
                if (_samplePoints.Count > 1 && seg.Color == System.Drawing.Color.Blue)
                    continue;
                var a = Project(seg.A, cx, cy);
                var b = Project(seg.B, cx, cy);
                using var pen = new Pen(ConvertColor(seg.Color), 2);
                g.DrawLine(pen, a, b);
            }
            // Draw edited upcoming path via sample points
            if (_samplePoints.Count > 1)
            {
                using var penPath = new Pen(new Color(0f, 0f, 1f), 2);
                var pts2d = _samplePoints.Select(p => Project(p, cx, cy)).ToArray();
                g.DrawLines(penPath, pts2d);
            }
            // Draw sample points, highlighting selections
            if (_samplePoints != null)
            {
                using var brushNorm = new SolidBrush(ConvertColor(System.Drawing.Color.LimeGreen));
                using var brushSel = new SolidBrush(ConvertColor(System.Drawing.Color.Red));
                for (int i = 0; i < _samplePoints.Count; i++)
                {
                    var proj = Project(_samplePoints[i], cx, cy);
                    var brush = _selectedSampleIndices.Contains(i) ? brushSel : brushNorm;
                    g.FillEllipse(brush, proj.X - 3, proj.Y - 3, 6, 6);
                }
            }
            // Draw lasso polygon in select mode
            if (_mode == Mode.Select && _lassoPath.Count > 1)
            {
                using var penLasso = new Pen(Colors.Blue, 1);
                g.DrawPolygon(penLasso, _lassoPath.ToArray());
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

        private Color ConvertColor(System.Drawing.Color c) => new Color(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
        /// <summary>
        /// Unproject a screen point onto the world XY plane at the given Z.
        /// </summary>
        private Point3d Unproject(PointF sp, double origZ)
        {
            // Screen center with pan
            float cx = ClientSize.Width * 0.5f + _panX;
            float cy = ClientSize.Height * 0.5f + _panY;
            // Normalize to rotated coordinates
            float x1 = (sp.X - cx) / _zoom;
            float y2 = -(sp.Y - cy) / _zoom;
            // Z after center offset
            float z1 = (float)(origZ - _center.Z);
            // Invert pitch
            float spch = MathF.Sin(_pitch);
            float cpch = MathF.Cos(_pitch);
            float y1 = (y2 + z1 * spch) / cpch;
            // Invert yaw
            float sy = MathF.Sin(_yaw);
            float cyaw = MathF.Cos(_yaw);
            float Xc = x1 * cyaw + y1 * sy;
            float Yc = -x1 * sy + y1 * cyaw;
            // World coordinates
            double wx = Xc + _center.X;
            double wy = Yc + _center.Y;
            return new Point3d(wx, wy, origZ);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _lastMouse = e.Location;
            // Mode-dependent mouse down
            if (_mode == Mode.Edit)
            {
                // Group-drag selected sample points
                if (e.Buttons == MouseButtons.Primary && _selectedSampleIndices.Count > 0)
                {
                    // Check if clicked near any selected sample point
                    const float hitRadius = 6f;
                    float bestDistSq = hitRadius * hitRadius;
                    int hitIndex = -1;
                    float cx = ClientSize.Width * 0.5f + _panX;
                    float cy = ClientSize.Height * 0.5f + _panY;
                    foreach (var idx in _selectedSampleIndices)
                    {
                        var sp = _samplePoints[idx];
                        var proj = Project(sp, cx, cy);
                        float dx = proj.X - e.Location.X;
                        float dy = proj.Y - e.Location.Y;
                        float dsq = dx * dx + dy * dy;
                        if (dsq < bestDistSq)
                        {
                            bestDistSq = dsq;
                            hitIndex = idx;
                        }
                    }
                    if (hitIndex >= 0)
                    {
                        // Begin group drag
                        _initialSelectedPoints = _selectedSampleIndices.Select(i => _samplePoints[i]).ToList();
                        _dragRefZ = _initialSelectedPoints.Average(p => p.Z);
                        _dragAnchorWorld = Unproject(e.Location, _dragRefZ);
                        _isGroupDragging = true;
                        return;
                    }
                }
                // Begin endpoint drag
                if (e.Buttons == MouseButtons.Alternate)
                {
                    BeginDrag(e.Location);
                }
                else if (e.Buttons == MouseButtons.Primary)
                {
                    _rotating = true;
                }
            }
            else if (_mode == Mode.Select)
            {
                if (e.Buttons == MouseButtons.Primary)
                {
                    _isLassoing = true;
                    _lassoPath.Clear();
                    _lassoPath.Add(e.Location);
                }
            }
        }

        /// <summary>
        /// Initialize dragging of the nearest endpoint.
        /// </summary>
        private void BeginDrag(PointF mousePt)
        {
            const float hitThreshold = 10f;
            float best = hitThreshold * hitThreshold;
            int bestIdx = -1;
            bool bestEnd = false;
            // Screen center for projection
            float cx = ClientSize.Width * 0.5f + _panX;
            float cy = ClientSize.Height * 0.5f + _panY;
            // Find closest segment endpoint
            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                var pa = Project(seg.A, cx, cy);
                var pb = Project(seg.B, cx, cy);
                float da = (pa.X - mousePt.X) * (pa.X - mousePt.X) + (pa.Y - mousePt.Y) * (pa.Y - mousePt.Y);
                if (da < best)
                {
                    best = da; bestIdx = i; bestEnd = false;
                }
                float db = (pb.X - mousePt.X) * (pb.X - mousePt.X) + (pb.Y - mousePt.Y) * (pb.Y - mousePt.Y);
                if (db < best)
                {
                    best = db; bestIdx = i; bestEnd = true;
                }
            }
            if (bestIdx >= 0)
            {
                _isDragging = true;
                _selectedSegIdx = bestIdx;
                _selectedIsEnd = bestEnd;
                _origDragZ = bestEnd ? _segments[bestIdx].B.Z : _segments[bestIdx].A.Z;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Handle group drag in edit mode
            if (_mode == Mode.Edit && _isGroupDragging)
            {
                // Project new anchor on reference plane and compute delta
                var newAnchor = Unproject(e.Location, _dragRefZ);
                var delta = newAnchor - _dragAnchorWorld;
                // Move each selected sample point with clamping
                for (int k = 0; k < _selectedSampleIndices.Count; k++)
                {
                    int idx = _selectedSampleIndices[k];
                    var orig = _initialSelectedPoints[k];
                    var moved = orig + delta;
                    moved.X = Math.Max(_bboxMin.X, Math.Min(_bboxMax.X, moved.X));
                    moved.Y = Math.Max(_bboxMin.Y, Math.Min(_bboxMax.Y, moved.Y));
                    moved.Z = Math.Max(_bboxMin.Z, Math.Min(_bboxMax.Z, moved.Z));
                    _samplePoints[idx] = moved;
                }
                _canvas.Invalidate();
                _lastMouse = e.Location;
                return;
            }
            // Handle lasso drawing in select mode
            if (_mode == Mode.Select && _isLassoing)
            {
                _lassoPath.Add(e.Location);
                _canvas.Invalidate();
                _lastMouse = e.Location;
                return;
            }
            // Handle dragging in edit mode
            if (_mode == Mode.Edit && _isDragging)
            {
                // Compute new world point at constant Z
                var newPt = Unproject(e.Location, _origDragZ);
                var seg = _segments[_selectedSegIdx];
                if (_selectedIsEnd)
                    seg.B = newPt;
                else
                    seg.A = newPt;
                _canvas.Invalidate();
                _lastMouse = e.Location;
                return;
            }
            if (_mode == Mode.Edit)
            {
                var dx = e.Location.X - _lastMouse.X;
                var dy = e.Location.Y - _lastMouse.Y;
                if (_rotating)
            {
                _yaw += dx * 0.01f;
                _pitch += dy * 0.01f;
                // Clamp pitch to [-pi/2+ε, pi/2-ε]
                var max = (float)(Math.PI / 2 - 0.01);
                var min = (float)(-Math.PI / 2 + 0.01);
                if (_pitch > max) _pitch = max;
                if (_pitch < min) _pitch = min;
                _canvas.Invalidate();
            }
            else if (_panning)
            {
                _panX += dx;
                _panY += dy;
                _canvas.Invalidate();
            }
                _lastMouse = e.Location;
            }
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            // End group drag in edit mode
            if (_mode == Mode.Edit && _isGroupDragging)
            {
                _isGroupDragging = false;
                return;
            }
            // Handle end of lasso in select mode
            if (_mode == Mode.Select && _isLassoing)
            {
                _isLassoing = false;
                PerformLassoSelection();
                _canvas.Invalidate();
            }
            // Stop dragging or panning/rotating in edit mode
            else if (_mode == Mode.Edit)
            {
                if (_isDragging)
                    _isDragging = false;
                else
                    _rotating = _panning = false;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            // Use vertical wheel delta
            var dz = e.Delta.Height;
            _zoom *= dz > 0 ? 1.1f : 1 / 1.1f;
            _canvas.Invalidate();
        }
        /// <summary>
        /// Select sample points inside the lasso polygon.
        /// </summary>
        private void PerformLassoSelection()
        {
            if (_lassoPath.Count < 3 || _samplePoints == null || _samplePoints.Count == 0)
                return;
            var poly = _lassoPath.ToArray();
            _selectedSampleIndices.Clear();
            float cx = ClientSize.Width * 0.5f + _panX;
            float cy = ClientSize.Height * 0.5f + _panY;
            for (int i = 0; i < _samplePoints.Count; i++)
            {
                var sp = _samplePoints[i];
                var pt2d = Project(sp, cx, cy);
                if (PointInPolygon(poly, pt2d))
                    _selectedSampleIndices.Add(i);
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
                {
                    result = !result;
                }
                j = i;
            }
            return result;
        }
        /// <summary>
        /// Edited sample points with their original command indices.
        /// </summary>
        public List<Tuple<int, Point3d>> EditedSamples => _editSamples;
    }
}