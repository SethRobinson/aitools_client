using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AITools.ScreenCapture.Helper
{
    internal sealed class DesktopClockWindow : Form
    {
        private readonly DesktopClockTimer _clock;
        private readonly Func<bool> _captureBusy;
        private readonly Action<string> _error;
        private readonly DesktopClockRenderer _renderer = new DesktopClockRenderer();
        private readonly Timer _timer = new Timer { Interval = 33 };
        private readonly ToolTip _tip = new ToolTip();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private ToolStripMenuItem _syncItem, _toggleItem, _resetItem;
        private DesktopClockSettings _settings;
        private Bitmap _surface;
        private bool _dragging, _resizing, _keyboardControls, _placing, _closing;
        private Point _dragStart;
        private Rectangle _dragBounds;
        private double _lastHover, _lastMonitorCheck;
        private float _controls;
        private int _focusedButton, _pressedButton = -1, _hoverButton = -2;
        internal event Action<DesktopClockSettings> SettingsChanged;

        internal DesktopClockWindow(DesktopClockSettings settings, DesktopClockTimer clock,
            Func<bool> captureBusy, Action<string> error)
        {
            _settings = settings.Copy(); _clock = clock; _captureBusy = captureBusy; _error = error;
            Text = "AI Tools clock";
            AccessibleName = "Floating clock and stopwatch";
            AccessibleDescription = "Drag to move, drag lower right to resize. Tab selects controls; Space starts or pauses; R resets; M switches mode; F10 opens the menu; Escape hides.";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            _timer.Tick += (s, e) => Guard(Tick);
            _menu.Closed += (s, e) => _lastHover = DesktopClockTimer.Timestamp;
        }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x80; return p; }
        }
        // No Native.Exclude: the clock is deliberately part of desktop recordings.
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Guard(Render); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_closing) return;
            if (Visible) _timer.Start();
            else
            {
                _timer.Stop();
                _dragging = false; _pressedButton = -1; _keyboardControls = false;
                Capture = false;
                _tip.Hide(this); _menu.Close();
            }
        }
        internal void Apply(DesktopClockSettings settings)
        {
            _settings = settings.Copy();
            if (Visible) Guard(() => { Place(); Render(); });
        }
        internal void Reveal()
        {
            Guard(() =>
            {
                Place(); _controls = 1; _lastHover = DesktopClockTimer.Timestamp;
                // A top-level window with no Unity owner remains visible when Unity minimizes.
                if (!Visible) Show();
                Activate(); Native.SetForegroundWindow(Handle);
                Render(); Save();
            });
        }
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                _timer.Stop(); Hide();
                _error("Clock: " + ex.Message);
            }
        }
        private void Place()
        {
            Screen screen = _settings.positionSaved
                ? Screen.FromRectangle(new Rectangle(_settings.x, _settings.y, Math.Max(1, Width), Math.Max(1, Height)))
                : Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            Rectangle work = screen.WorkingArea;
            int x = _settings.positionSaved ? _settings.x : work.Right - (int)(_settings.widthDip * DeviceDpi / 96.0) - 24;
            int y = _settings.positionSaved ? _settings.y : work.Top + 24;
            SetFittedBounds(work, x, y, _settings.widthDip, DeviceDpi);
        }
        private void SetFittedBounds(Rectangle work, int x, int y, double widthDip, int dpi)
        {
            var rect = DesktopClockGeometry.Fit(new PixelRect(work.X, work.Y, work.Width, work.Height), x, y, widthDip, dpi, _settings.digital);
            _placing = true;
            try { Bounds = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height); }
            finally { _placing = false; }
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // Layered artwork is explicitly sized in physical pixels. Let this handler
            // apply the monitor's DPI instead of WinForms scaling the bounds again.
            e.Cancel = true;
            base.OnDpiChanged(e);
            if (_placing) return;
            Guard(() =>
            {
                Rectangle suggested = e.SuggestedRectangle;
                Rectangle work = Screen.FromRectangle(suggested).WorkingArea;
                SetFittedBounds(work, suggested.X, suggested.Y, _settings.widthDip, e.DeviceDpiNew);
                if (_dragging)
                {
                    _dragStart = System.Windows.Forms.Cursor.Position;
                    _dragBounds = Bounds;
                }
                Render(); Save();
            });
        }
        private void Save()
        {
            _settings.positionSaved = true; _settings.x = Left; _settings.y = Top;
            SettingsChanged?.Invoke(_settings.Copy());
        }
        private void Tick()
        {
            double now = DesktopClockTimer.Timestamp;
            bool hovering = ClientRectangle.Contains(PointToClient(System.Windows.Forms.Cursor.Position));
            if (hovering || _dragging || _menu.Visible || (_keyboardControls && ContainsFocus)) _lastHover = now;
            if (_menu.Visible)
            {
                _syncItem.Enabled = !_captureBusy();
                _toggleItem.Enabled = _resetItem.Enabled = _settings.stopwatch && !_clock.Synchronized;
            }
            _controls = (float)Math.Max(0, Math.Min(1, 1 - (now - _lastHover - 1) / .2));
            if (!_dragging && now - _lastMonitorCheck >= 1)
            {
                _lastMonitorCheck = now;
                Rectangle previous = Bounds;
                Place();
                if (previous != Bounds) Save();
            }
            Render();
        }
        private void Render()
        {
            if (!IsHandleCreated || Width < 1 || Height < 1) return;
            if (_surface == null || _surface.Size != Size)
            {
                _surface?.Dispose();
                _surface = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            double seconds = _settings.stopwatch ? _clock.Elapsed : DateTime.Now.TimeOfDay.TotalSeconds;
            _renderer.Draw(_surface, _settings, seconds, _clock.Running, _clock.Synchronized,
                _controls, _keyboardControls && ContainsFocus ? _focusedButton : -1);
            Native.PresentLayer(this, _surface);
        }
        private PointF Logical(Point point) => new PointF(point.X * 220f / Width, point.Y * 220f / Width);
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Guard(() =>
            {
                _keyboardControls = false;
                if (e.Button == MouseButtons.Right) { OpenMenu(); return; }
                if (e.Button != MouseButtons.Left) return;
                Focus(); _lastHover = DesktopClockTimer.Timestamp; _controls = 1;
                PointF p = Logical(e.Location);
                _pressedButton = DesktopClockRenderer.HitButton(p.X, p.Y);
                if (_pressedButton >= 0) { Capture = true; return; }
                if (_settings.locked) return;
                _dragStart = PointToScreen(e.Location); _dragBounds = Bounds;
                _dragging = true;
                _resizing = DesktopClockRenderer.HitGrip(p.X, p.Y, _settings.digital);
                Capture = true;
            });
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Guard(() =>
            {
                _keyboardControls = false;
                PointF p = Logical(e.Location);
                int hit = DesktopClockRenderer.HitButton(p.X, p.Y);
                if (hit != _hoverButton)
                {
                    _hoverButton = hit;
                    string[] tips = { "Switch Clock / Stopwatch (M)", "Start / Pause (Space)", "Reset and pause (R)", "Clock options (F10)", "Hide clock (Esc)" };
                    _tip.SetToolTip(this, hit >= 0 ? tips[hit] : "Drag face to move; drag lower-right corner to resize.");
                }
                Cursor = !_settings.locked && DesktopClockRenderer.HitGrip(p.X, p.Y, _settings.digital)
                    ? Cursors.SizeNWSE : hit >= 0 ? Cursors.Hand : !_settings.locked ? Cursors.SizeAll : Cursors.Default;
                if (!_dragging) return;
                Point point = PointToScreen(e.Location);
                int dx = point.X - _dragStart.X, dy = point.Y - _dragStart.Y;
                if (_resizing)
                {
                    double delta = Math.Max(dx, dy / DesktopClockGeometry.HeightRatio(_settings.digital));
                    _settings.widthDip = Math.Max(140, Math.Min(640, (_dragBounds.Width + delta) * 96.0 / DeviceDpi));
                    SetFittedBounds(Screen.FromRectangle(_dragBounds).WorkingArea, _dragBounds.X, _dragBounds.Y, _settings.widthDip, DeviceDpi);
                }
                else
                {
                    // Allow crossing monitor boundaries while dragging; clamp on release.
                    Location = new Point(_dragBounds.X + dx, _dragBounds.Y + dy);
                }
                Render();
            });
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Guard(() =>
            {
                int pressed = _pressedButton; _pressedButton = -1;
                bool dragged = _dragging; _dragging = false; Capture = false;
                if (dragged) { Save(); Place(); Save(); }
                PointF p = Logical(e.Location);
                if (pressed >= 0 && pressed == DesktopClockRenderer.HitButton(p.X, p.Y)) Perform(pressed);
            });
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _dragging)
            {
                _dragging = false; _pressedButton = -1;
                Guard(() => { Save(); Place(); Save(); });
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Tab)
            {
                _keyboardControls = true;
                _focusedButton = (_focusedButton + ((keyData & Keys.Shift) != 0 ? 4 : 1)) % 5;
                Guard(Render); return true;
            }
            int action = key == Keys.M ? 0 : key == Keys.Space ? 1 : key == Keys.R ? 2 :
                key == Keys.F10 || key == Keys.Apps ? 3 : key == Keys.Escape ? 4 :
                key == Keys.Enter ? _focusedButton : -1;
            if (action < 0) return base.ProcessCmdKey(ref msg, keyData);
            _keyboardControls = true; Guard(() => Perform(action)); return true;
        }
        private void Perform(int button)
        {
            switch (button)
            {
                case 0: _settings.stopwatch = !_settings.stopwatch; Save(); break;
                case 1: if (_settings.stopwatch) _clock.Toggle(); break;
                case 2: if (_settings.stopwatch) _clock.Reset(); break;
                case 3: OpenMenu(); break;
                case 4: Hide(); break;
            }
            Render();
        }
        private void OpenMenu()
        {
            for (int i = _menu.Items.Count - 1; i >= 0; i--) _menu.Items[i].Dispose();
            _menu.Items.Clear();
            Add("Clock", _settings.stopwatch == false, () => { _settings.stopwatch = false; Save(); });
            Add("Stopwatch", _settings.stopwatch, () => { _settings.stopwatch = true; Save(); });
            _menu.Items.Add(new ToolStripSeparator());
            _toggleItem = Add(_clock.Running ? "Pause" : "Start / Resume", false, () => _clock.Toggle(), _settings.stopwatch && !_clock.Synchronized);
            _resetItem = Add("Reset and pause", false, () => _clock.Reset(), _settings.stopwatch && !_clock.Synchronized);
            _menu.Items.Add(new ToolStripSeparator());
            Add("Digital readout", _settings.digital, () => { _settings.digital = !_settings.digital; Place(); Save(); });
            _syncItem = Add("Sync with recorder", _settings.sync, () =>
            {
                // The global recording shortcut may have started capture since menu open.
                if (!_captureBusy()) { _settings.sync = !_settings.sync; Save(); }
            }, !_captureBusy());
            Add("Lock position and size", _settings.locked, () => { _settings.locked = !_settings.locked; Save(); });
            Add("Reset position and size", false, () => { _settings.positionSaved = false; _settings.widthDip = 220; Place(); Save(); });
            _menu.Items.Add(new ToolStripSeparator());
            Add("Close clock", false, Hide);
            _menu.Show(this, new Point(Math.Min(Width - 1, (int)(130 * Width / 220f)), (int)(40 * Width / 220f)));
        }
        private ToolStripMenuItem Add(string label, bool check, Action action, bool enabled = true)
        {
            var item = new ToolStripMenuItem(label) { Checked = check, Enabled = enabled };
            item.Click += (s, e) => Guard(() => { action(); Render(); });
            _menu.Items.Add(item);
            return item;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                _timer.Dispose(); _tip.Dispose(); _menu.Dispose(); _surface?.Dispose(); _renderer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
