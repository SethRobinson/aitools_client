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
        private readonly CaptureSettings _settings;
        private Point _start;
        private PixelRect _selection;
        private bool _dragging;

        internal SelectionOverlay(Screen screen, CaptureSettings settings, Action<Screen, PixelRect> selected, Action cancel)
        {
            _screen = screen; _settings = settings; _selected = selected; _cancel = cancel;
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
            OnMouseMove(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging && !_settings.fixedSize) return;
            Point end = PointToScreen(e.Location);
            Rectangle b = _screen.Bounds;
            var monitor = new PixelRect(b.X, b.Y, b.Width, b.Height);
            _selection = _settings.fixedSize
                ? CaptureGeometry.Move(monitor, new PixelRect(0, 0, _settings.fixedWidth, _settings.fixedHeight), end.X - _settings.fixedWidth / 2, end.Y - _settings.fixedHeight / 2)
                : CaptureGeometry.Select(monitor, _start.X, _start.Y, end.X, end.Y, _settings.aspectRatio);
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
                e.Graphics.DrawString(_settings.fixedSize
                    ? (_settings.fixedWidth > _screen.Bounds.Width || _settings.fixedHeight > _screen.Bounds.Height
                        ? "Fixed size does not fit this monitor. Choose another monitor, or Esc to change the size."
                        : "Position the fixed box and release or click to place it. Esc cancels.")
                    : "Drag an area to record. Esc cancels.", SystemFonts.MessageBoxFont, brush, 24, 24);
            if (_selection.Width <= 0) return;
            Rectangle b = _screen.Bounds;
            using (var pen = new Pen(Color.White, 3))
                e.Graphics.DrawRectangle(pen, _selection.X - b.X, _selection.Y - b.Y, _selection.Width, _selection.Height);
            RegionLabel.Draw(e.Graphics, Font, _selection, b, b.Location);
        }
    }

    internal sealed class RegionBorder : Form
    {
        private readonly PixelRect _monitor;
        private readonly Action<PixelRect> _changed;
        private readonly string _aspectRatio;
        private PixelRect _rect, _dragRect;
        private Point _dragStart;
        private bool _dragging, _locked;
        private int _corner = -1;
        internal bool Recording, FixedSize;
        private int HandleSize => Math.Max(8, 8 * DeviceDpi / 96);
        protected override bool ShowWithoutActivation => true;
        internal RegionBorder(PixelRect monitor, PixelRect rect, bool fixedSize, string aspectRatio, Action<PixelRect> changed)
        {
            _monitor = monitor; _rect = rect; FixedSize = fixedSize; _aspectRatio = aspectRatio; _changed = changed;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            UpdateRegionBounds();
        }
        internal void SetState(bool locked, bool recording)
        {
            if (_locked == locked && Recording == recording) return;
            _locked = locked; Recording = recording;
            if (locked) { _dragging = false; Capture = false; }
            // WS_EX_TRANSPARENT on a layered window passes clicks to other processes too.
            if (IsHandleCreated)
            {
                long style = Native.GetWindowLongPtr(Handle, -20).ToInt64();
                Native.SetWindowLongPtr(Handle, -20, new IntPtr(locked ? style | 0x20 : style & ~0x20));
            }
            Invalidate();
        }
        private Rectangle MonitorBounds => new Rectangle(_monitor.X, _monitor.Y, _monitor.Width, _monitor.Height);
        private void UpdateRegionBounds()
        {
            Rectangle bounds = new Rectangle(_rect.X, _rect.Y, _rect.Width, _rect.Height);
            bounds.Inflate(HandleSize, HandleSize);
            bounds = Rectangle.Union(bounds, RegionLabel.Bounds(Font, _rect, MonitorBounds));
            Bounds = bounds; Invalidate();
        }
        private Point[] Corners() => new[] { new Point(_rect.X, _rect.Y), new Point(_rect.X + _rect.Width, _rect.Y), new Point(_rect.X, _rect.Y + _rect.Height), new Point(_rect.X + _rect.Width, _rect.Y + _rect.Height) };
        private int HitCorner(Point point)
        {
            Point[] corners = Corners();
            for (int i = 0; i < corners.Length; i++)
                if (Math.Abs(point.X - corners[i].X) <= HandleSize && Math.Abs(point.Y - corners[i].Y) <= HandleSize) return i;
            return -1;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_locked || e.Button != MouseButtons.Left) return;
            _dragStart = PointToScreen(e.Location); _dragRect = _rect;
            _corner = FixedSize ? -1 : HitCorner(_dragStart); _dragging = true; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point point = PointToScreen(e.Location);
            int corner = _dragging ? _corner : FixedSize ? -1 : HitCorner(point);
            Cursor = _locked ? Cursors.Default : corner < 0 ? Cursors.SizeAll : corner == 0 || corner == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW;
            if (!_dragging || _locked) return;
            _rect = _corner < 0
                ? CaptureGeometry.Move(_monitor, _dragRect, _dragRect.X + point.X - _dragStart.X, _dragRect.Y + point.Y - _dragStart.Y)
                : CaptureGeometry.Resize(_monitor, _dragRect, (_corner & 1) == 0, _corner < 2,
                    ((_corner & 1) == 0 ? _dragRect.X : _dragRect.X + _dragRect.Width) + point.X - _dragStart.X,
                    (_corner < 2 ? _dragRect.Y : _dragRect.Y + _dragRect.Height) + point.Y - _dragStart.Y, _aspectRatio);
            UpdateRegionBounds(); _changed(_rect);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { OnMouseMove(e); _dragging = false; Capture = false; } }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragging = false; base.OnMouseCaptureChanged(e); }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | (_locked ? 0x20 : 0); return p; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084 && _locked) { message.Result = new IntPtr(-1); return; }
            base.WndProc(ref message);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.Exclude(this); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var color = Recording ? Color.Red : Color.DeepSkyBlue;
            // Draw outside the captured pixels. TransparencyKey leaves the content clickable.
            using (var pen = new Pen(color, 4))
                e.Graphics.DrawRectangle(pen, _rect.X - Left - 2, _rect.Y - Top - 2, _rect.Width + 4, _rect.Height + 4);
            if (!_locked)
                using (var brush = new SolidBrush(color))
                    foreach (Point p in Corners()) e.Graphics.FillRectangle(brush, p.X - Left - HandleSize / 2, p.Y - Top - HandleSize / 2, HandleSize, HandleSize);
            RegionLabel.Draw(e.Graphics, Font, _rect, MonitorBounds, Location);
        }
    }

    internal static class RegionLabel
    {
        internal static Rectangle Bounds(Font font, PixelRect rect, Rectangle monitor)
        {
            Size size = TextRenderer.MeasureText(CaptureGeometry.Describe(rect), font);
            size.Width += 12; size.Height += 8;
            int x = Math.Max(monitor.Left, Math.Min(rect.X + rect.Width - size.Width, monitor.Right - size.Width));
            int y = rect.Y + rect.Height + 8;
            if (y + size.Height > monitor.Bottom) y = Math.Max(monitor.Top, rect.Y + rect.Height - size.Height - 8);
            return new Rectangle(x, y, size.Width, size.Height);
        }
        internal static void Draw(Graphics graphics, Font font, PixelRect rect, Rectangle monitor, Point origin)
        {
            Rectangle label = Bounds(font, rect, monitor); label.Offset(-origin.X, -origin.Y);
            graphics.FillRectangle(Brushes.Black, label);
            TextRenderer.DrawText(graphics, CaptureGeometry.Describe(rect), font, label, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
