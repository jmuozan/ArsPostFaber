using System;
using System.Collections.Generic;
using Eto.Forms;
using Eto.Drawing;
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
            // Collect all segment endpoints for bounding box
            foreach (var seg in _segments)
            {
                _points.Add(seg.A);
                _points.Add(seg.B);
            }
            _samplePoints = new List<Point3d>();
            ComputeBounds();
            _canvas = new Drawable { BackgroundColor = Colors.White };
            _canvas.Paint += (s, e) => Draw(e.Graphics);
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;
            // Edit mode toggle
            var editToggle = new CheckBox { Text = "Edit Mode" };
            editToggle.CheckedChanged += (s, e) => { _isEditing = editToggle.Checked == true; };
            // Layout: toolbar and canvas in a vertical DynamicLayout
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(5) };
            layout.BeginVertical();
            layout.Add(editToggle);
            layout.Add(_canvas, yscale: true);
            layout.EndVertical();
            Content = layout;
        }
        /// <summary>
        /// Constructor with sample points for preview.
        /// </summary>
        public PreviewEtoForm(List<Tuple<Point3d, Point3d, System.Drawing.Color>> segments, List<Point3d> samplePoints)
            : this(segments)
        {
            _samplePoints = samplePoints ?? new List<Point3d>();
            if (_samplePoints.Count > 0)
            {
                _points.AddRange(_samplePoints);
                ComputeBounds();
            }
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
            // Draw segments
            foreach (var seg in _segments)
            {
                var a = Project(seg.A, cx, cy);
                var b = Project(seg.B, cx, cy);
                using var pen = new Pen(ConvertColor(seg.Color), 2);
                g.DrawLine(pen, a, b);
            }
            // Draw sample points
            if (_samplePoints != null)
            {
                using var brush = new SolidBrush(ConvertColor(System.Drawing.Color.LimeGreen));
                foreach (var sp in _samplePoints)
                {
                    var proj = Project(sp, cx, cy);
                    g.FillEllipse(brush, proj.X - 3, proj.Y - 3, 6, 6);
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
            // Begin drag in edit mode with right-click
            if (_isEditing && e.Buttons == MouseButtons.Alternate)
            {
                // Attempt to select endpoint
                BeginDrag(e.Location);
            }
            else if (!_isEditing)
            {
                if (e.Buttons == MouseButtons.Primary)
                    _rotating = true;
                else if (e.Buttons == MouseButtons.Alternate)
                    _panning = true;
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
            // Handle dragging in edit mode
            if (_isDragging)
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

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            // Stop dragging or panning/rotating
            if (_isDragging)
            {
                _isDragging = false;
            }
            else
            {
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
    }
}