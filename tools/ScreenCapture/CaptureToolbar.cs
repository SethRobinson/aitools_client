using System;
using System.Drawing;
using System.Windows.Forms;

namespace AITools.ScreenCapture.Helper
{
    internal sealed class CaptureToolbar : Form
    {
        internal event Action SelectRequested, RecordRequested, CloseRequested, CancelRequested, SettingsChanged;
        private readonly Button _select = new Button { Text = "Select area", AutoSize = true };
        private readonly Button _record = new Button { Text = "Record", AutoSize = true };
        private readonly CheckBox _audio = new CheckBox { Text = "System audio", AutoSize = true };
        private readonly CheckBox _cursor = new CheckBox { Text = "Include cursor", AutoSize = true };
        private readonly ComboBox _fps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 86 };
        private readonly Button _settings = new Button { Text = "Settings", AutoSize = true };
        private readonly ComboBox _sizeMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 86 };
        private readonly NumericUpDown _width = DimensionInput(1280), _height = DimensionInput(720);
        private readonly FlowLayoutPanel _dimensions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        private readonly ComboBox _aspectRatio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 94, MaxDropDownItems = 18, IntegralHeight = false, DropDownHeight = 320 };
        private readonly FlowLayoutPanel _aspectControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        private readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Padding = new Padding(4) };
        private bool _updating;
        private double _countdownSeconds = 3;
        internal bool HasValidFrameRate => CaptureSettings.TryParseFrameRate(_fps.Text, out _);
        internal bool AllowClose;

        internal CaptureToolbar()
        {
            Text = "AI Tools screen recording";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(8) };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.AddRange(new Control[] { _select, _record, _audio, _cursor, Caption("FPS"), _fps, _settings });
            var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            _sizeMode.Items.AddRange(new object[] { "Custom", "Fixed" });
            _dimensions.Controls.AddRange(new Control[] { _width, Caption("x"), _height, Caption("pixels") });
            foreach (var ratio in CaptureAspectRatio.Options) _aspectRatio.Items.Add(ratio.Name);
            _aspectControls.Controls.AddRange(new Control[] { Caption("Aspect ratio"), _aspectRatio });
            sizeRow.Controls.AddRange(new Control[] { Caption("Size"), _sizeMode, _dimensions, _aspectControls });
            layout.Controls.Add(row); layout.Controls.Add(sizeRow); layout.Controls.Add(_status); Controls.Add(layout);
            foreach (int rate in CaptureSettings.FrameRates) _fps.Items.Add(rate);
            _select.Click += (s, e) => SelectRequested?.Invoke();
            _record.Click += (s, e) => RecordRequested?.Invoke();
            _audio.CheckedChanged += Changed; _cursor.CheckedChanged += Changed; _fps.TextChanged += Changed;
            _sizeMode.SelectedIndexChanged += Changed;
            _aspectRatio.SelectedIndexChanged += Changed;
            _width.ValueChanged += Changed; _height.ValueChanged += Changed;
            _settings.Click += (s, e) =>
            {
                using (var dialog = new CaptureSettingsDialog(_countdownSeconds))
                    if (dialog.ShowDialog(this) == DialogResult.OK) { _countdownSeconds = dialog.CountdownSeconds; Changed(this, EventArgs.Empty); }
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; CancelRequested?.Invoke(); } };
            FormClosing += (s, e) => { if (!AllowClose) { e.Cancel = true; CloseRequested?.Invoke(); } };
        }

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        private static NumericUpDown DimensionInput(int value) => new NumericUpDown { Minimum = 2, Maximum = 16384, Increment = 2, Value = value, Width = 76 };
        private void Changed(object sender, EventArgs e)
        {
            _dimensions.Visible = _sizeMode.SelectedIndex == 1;
            _aspectControls.Visible = _sizeMode.SelectedIndex == 0;
            if (!_updating)
            {
                // H.264 4:2:0 requires even dimensions. Show the actual stored value.
                _updating = true;
                _width.Value = Math.Floor(_width.Value / 2) * 2; _height.Value = Math.Floor(_height.Value / 2) * 2;
                _updating = false;
                SettingsChanged?.Invoke();
            }
        }
        internal void Apply(CaptureSettings settings)
        {
            _updating = true;
            _audio.Checked = settings.audio; _cursor.Checked = settings.cursor; _fps.Text = settings.fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _countdownSeconds = settings.countdownSeconds; _sizeMode.SelectedIndex = settings.fixedSize ? 1 : 0;
            _width.Value = settings.fixedWidth; _height.Value = settings.fixedHeight;
            _aspectRatio.SelectedItem = settings.aspectRatio;
            _updating = false;
        }
        internal void ReadSettings(CaptureSettings settings)
        {
            settings.audio = _audio.Checked; settings.cursor = _cursor.Checked;
            // Retain the saved rate while an entry is incomplete; Record is disabled.
            if (CaptureSettings.TryParseFrameRate(_fps.Text, out int value)) settings.fps = value;
            settings.countdownSeconds = _countdownSeconds; settings.fixedSize = _sizeMode.SelectedIndex == 1;
            settings.fixedWidth = (int)_width.Value; settings.fixedHeight = (int)_height.Value;
            settings.aspectRatio = _aspectRatio.SelectedItem as string ?? "Any";
        }
        internal void UpdateState(CaptureStage stage, string text)
        {
            bool busy = stage == CaptureStage.Countdown || stage == CaptureStage.Starting || stage == CaptureStage.Recording || stage == CaptureStage.Finishing;
            _select.Enabled = !busy;
            _audio.Enabled = _cursor.Enabled = _fps.Enabled = !busy;
            _settings.Enabled = _sizeMode.Enabled = _dimensions.Enabled = _aspectControls.Enabled = !busy;
            _record.Text = stage == CaptureStage.Countdown ? "Cancel" : busy ? "Stop" : "Record";
            _record.Enabled = (stage == CaptureStage.Ready && HasValidFrameRate) || stage == CaptureStage.Countdown || stage == CaptureStage.Recording || stage == CaptureStage.Starting;
            _fps.BackColor = HasValidFrameRate ? SystemColors.Window : Color.MistyRose;
            _status.Text = !busy && !HasValidFrameRate ? "FPS must be a positive whole number, for example 25, 50 or 144." : text;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Exclude(this); }
    }

    internal sealed class CaptureSettingsDialog : Form
    {
        private readonly NumericUpDown _countdown = new NumericUpDown { Minimum = 0, Maximum = 3600, DecimalPlaces = 2, Increment = 0.1m, Width = 95 };
        internal double CountdownSeconds => (double)_countdown.Value;
        internal CaptureSettingsDialog(double countdown)
        {
            Text = "Recording settings"; TopMost = true; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _countdown.Value = (decimal)countdown;
            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(12) };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.AddRange(new Control[] { new Label { Text = "Countdown (seconds)", AutoSize = true, Padding = new Padding(0, 5, 8, 0) }, _countdown });
            layout.Controls.Add(row);
            layout.Controls.Add(new Label { Text = "0 starts immediately. Fractions such as 0.3 are supported.", AutoSize = true, Padding = new Padding(0, 6, 0, 8) });
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { save, cancel }); layout.Controls.Add(buttons); Controls.Add(layout);
            AcceptButton = save; CancelButton = cancel;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Exclude(this); }
    }
}
