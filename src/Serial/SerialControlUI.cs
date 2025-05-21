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
    /// <summary>
    /// Toolbar with Play/Pause and Edit buttons under the component.
    /// </summary>
    internal class ComponentToolbar : GH_ComponentAttributes
    {
        private readonly GH_Component _owner;
        private readonly Func<string> _playLabel;
        private readonly Action _playAction;
        private readonly Func<string> _editLabel;
        private readonly Action _editAction;
        private RectangleF _playBounds, _editBounds;
        private bool _playDown, _editDown;

        public ComponentToolbar(GH_Component owner,
                                Func<string> playLabel, Action playAction,
                                Func<string> editLabel, Action editAction)
            : base(owner)
        {
            _owner = owner;
            _playLabel = playLabel;
            _playAction = playAction;
            _editLabel = editLabel;
            _editAction = editAction;
        }

        protected override void Layout()
        {
            base.Layout();
            const int margin = 3;
            // Use RectangleF for precise layout
            var bounds = Bounds;
            // Define button row
            float y = bounds.Bottom;
            float height = 18f;
            float totalWidth = bounds.Width - margin * 3;
            float btnWidth = totalWidth / 2f;
            _playBounds = new RectangleF(bounds.Left + margin, y, btnWidth, height);
            _editBounds = new RectangleF(_playBounds.Right + margin, y, btnWidth, height);
            bounds.Height += height + margin;
            Bounds = bounds;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel == GH_CanvasChannel.Objects)
            {
                var font = GH_FontServer.NewFont(GH_FontServer.StandardAdjusted, 6f / GH_GraphicsUtil.UiScale);
                const int radius = 3;
                using (var cap = GH_Capsule.CreateTextCapsule(_playBounds, _playBounds, GH_Palette.Black, _playLabel(), font, radius, _playDown ? 0 : 8))
                {
                    cap.Render(graphics, false, _owner.Locked, false);
                }
                using (var cap = GH_Capsule.CreateTextCapsule(_editBounds, _editBounds, GH_Palette.Black, _editLabel(), font, radius, _editDown ? 0 : 8))
                {
                    cap.Render(graphics, false, _owner.Locked, false);
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked) return base.RespondToMouseDown(sender, e);
            if (e.Button == MouseButtons.Left)
            {
                if (_playBounds.Contains(e.CanvasLocation))
                {
                    _playDown = true;
                    return GH_ObjectResponse.Capture;
                }
                if (_editBounds.Contains(e.CanvasLocation))
                {
                    _editDown = true;
                    return GH_ObjectResponse.Capture;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked) return base.RespondToMouseUp(sender, e);
            if (e.Button == MouseButtons.Left)
            {
                if (_playDown)
                {
                    _playDown = false;
                    if (_playBounds.Contains(e.CanvasLocation)) _playAction();
                    sender.Invalidate();
                    return GH_ObjectResponse.Release;
                }
                if (_editDown)
                {
                    _editDown = false;
                    if (_editBounds.Contains(e.CanvasLocation)) _editAction();
                    sender.Invalidate();
                    return GH_ObjectResponse.Release;
                }
            }
            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // Reset down flags when moving out
            if (_playDown && !_playBounds.Contains(e.CanvasLocation)) _playDown = false;
            if (_editDown && !_editBounds.Contains(e.CanvasLocation)) _editDown = false;
            return base.RespondToMouseMove(sender, e);
        }
    }
}