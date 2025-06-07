using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Eto.Drawing;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Preview window for mesh visualization with orbit, pan, and zoom.
    /// </summary>
    internal class MeshPreviewForm : Form
    {
        private readonly List<Point3d> _vertices;
        private readonly List<int[]> _faces;
        private float _yaw = MathF.PI / 4f;
        private float _pitch = MathF.PI / 6f;
        private float _zoom;
        private float _panX, _panY;
        private Point3d _center;
        private readonly Drawable _canvas;
        private PointF _lastMouse;
        private bool _rotating, _panning;

        public MeshPreviewForm(Mesh mesh)
        {
            Title = "Mesh Preview";
            ClientSize = new Size(800, 600);
            // Duplicate mesh to avoid modifying original
            var m = mesh.DuplicateMesh();
            // Extract vertices
            _vertices = m.Vertices.ToPoint3dArray().ToList();
            // Extract faces (triangles and quads)
            _faces = new List<int[]>();
            for (int i = 0; i < m.Faces.Count; i++)
            {
                var f = m.Faces[i];
                if (f.IsQuad)
                    _faces.Add(new[] { f.A, f.B, f.C, f.D });
                else
                    _faces.Add(new[] { f.A, f.B, f.C });
            }
            // Compute initial view parameters
            ComputeBounds();

            // Set up drawing canvas
            _canvas = new Drawable { BackgroundColor = Colors.White };
            _canvas.Paint += (s, e) => Draw(e.Graphics);
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;

            // Reset view button
            var resetButton = new Button { Text = "Reset View" };
            resetButton.Click += (s, e) => { ResetView(); _canvas.Invalidate(); };

            // Layout
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(5) };
            layout.BeginVertical();
            layout.Add(resetButton);
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
            var cx = cw / 2f + _panX;
            var cy = ch / 2f + _panY;
            // Draw faces
            using var brush = new SolidBrush(new Color(0.8f, 0.8f, 0.8f));
            using var pen = new Pen(Colors.Black, 1);
            foreach (var face in _faces)
            {
                var pts2d = face.Select(idx => Project(_vertices[idx], cx, cy)).ToArray();
                g.FillPolygon(brush, pts2d);
                g.DrawPolygon(pen, pts2d);
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

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _lastMouse = e.Location;
            if (e.Buttons == MouseButtons.Primary)
                _rotating = true;
            else if (e.Buttons == MouseButtons.Middle || e.Buttons == MouseButtons.Alternate)
                _panning = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var delta = new PointF(e.Location.X - _lastMouse.X, e.Location.Y - _lastMouse.Y);
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
            _lastMouse = e.Location;
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            _rotating = false;
            _panning = false;
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta.Height > 0) _zoom *= 1.1f;
            else if (e.Delta.Height < 0) _zoom /= 1.1f;
            _canvas.Invalidate();
        }
    }
}