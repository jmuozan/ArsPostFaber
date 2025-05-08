using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace crft
{
    /// <summary>
    /// Adds a clickable button beneath the component for custom actions.
    /// </summary>
    internal class ComponentButton : GH_ComponentAttributes
    {
        private readonly GH_Component _owner;
        private readonly Func<string> _labelProvider;
        private readonly Action _action;
        private RectangleF _buttonBounds;
        private bool _mouseDown;

        public ComponentButton(GH_Component owner, Func<string> labelProvider, Action action)
            : base(owner)
        {
            _owner = owner;
            _labelProvider = labelProvider;
            _action = action;
        }

        protected override void Layout()
        {
            base.Layout();
            const int margin = 3;
            var bounds = GH_Convert.ToRectangle(Bounds);
            var button = bounds;
            button.X += margin;
            button.Width -= margin * 2;
            button.Y = bounds.Bottom;
            button.Height = 18;
            bounds.Height += button.Height + margin;
            Bounds = bounds;
            _buttonBounds = button;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel == GH_CanvasChannel.Objects)
            {
                var prototype = GH_FontServer.StandardAdjusted;
                var font = GH_FontServer.NewFont(prototype, 6f / GH_GraphicsUtil.UiScale);
                var radius = 3;
                var highlight = !_mouseDown ? 8 : 0;
                using var capsule = GH_Capsule.CreateTextCapsule(_buttonBounds, _buttonBounds, GH_Palette.Black, _labelProvider(), font, radius, highlight);
                capsule.Render(graphics, false, _owner.Locked, false);
            }
        }

        private void SetMouseDown(bool value, GH_Canvas canvas, GH_CanvasMouseEvent e, bool doAction = true)
        {
            if (_owner.Locked || _mouseDown == value)
                return;
            if (value && e.Button != MouseButtons.Left)
                return;
            if (!_buttonBounds.Contains(e.CanvasLocation))
                return;
            if (_mouseDown && !value && doAction)
                _action();
            _mouseDown = value;
            canvas.Invalidate();
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            SetMouseDown(true, sender, e);
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            SetMouseDown(false, sender, e);
            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            SetMouseDown(false, sender, e, false);
            return base.RespondToMouseMove(sender, e);
        }
    }
}