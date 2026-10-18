using System;
using System.Drawing;
using System.Windows.Forms;

namespace AITools.ScreenCapture.Helper
{
    internal sealed class SelectionOverlay : Form
    {
        private readonly Screen _screen;
        private readonly Action<Screen, PixelRect> _selected;
        private readonly Action _cancel;
        private Point _start;
        private PixelRect _selection;
        private bool _dragging;

        internal SelectionOverlay(Screen screen, Action<Screen, PixelRect> selected, Action cancel)
        {
            _screen = screen; _selected = selected; _cancel = cancel;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            BackColor = Color.Black; Opacity = 0.35; Cursor = Cursors.Cross;
            KeyPreview = true;
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.Exclude(this); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) _cancel(); base.OnKeyDown(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { _cancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            _start = PointToScreen(e.Location); _dragging = true; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            Point end = PointToScreen(e.Location);
            Rectangle b = _screen.Bounds;
            _selection = CaptureGeometry.Select(new PixelRect(b.X, b.Y, b.Width, b.Height), _start.X, _start.Y, end.X, end.Y);
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            OnMouseMove(e); _dragging = false; Capture = false;
            if (_selection.Width >= 2 && _selection.Height >= 2) _selected(_screen, _selection);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.DrawString("Drag an area to record. Esc cancels.", SystemFonts.MessageBoxFont, brush, 24, 24);
            if (_selection.Width <= 0) return;
            Rectangle b = _screen.Bounds;
            using (var pen = new Pen(Color.White, 3))
                e.Graphics.DrawRectangle(pen, _selection.X - b.X, _selection.Y - b.Y, _selection.Width, _selection.Height);
            e.Graphics.DrawString(_selection.Width + " x " + _selection.Height, SystemFonts.MessageBoxFont, Brushes.White,
                _selection.X - b.X + 8, _selection.Y - b.Y + 8);
        }
    }

    internal sealed class RegionBorder : Form
    {
        internal bool Recording;
        protected override bool ShowWithoutActivation => true;
        internal RegionBorder(PixelRect rect)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
            BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true;
        }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x20 | 0x08000000 | 0x80; return p; }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.Exclude(this); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Recording ? Color.Red : Color.DeepSkyBlue, 2)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }
}
