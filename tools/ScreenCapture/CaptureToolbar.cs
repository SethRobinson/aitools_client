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
        private readonly ComboBox _fps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 66 };
        private readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Padding = new Padding(4) };
        private bool _updating;
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
            row.Controls.AddRange(new Control[] { _select, _record, _audio, _cursor, new Label { Text = "FPS", AutoSize = true, Padding = new Padding(4, 6, 0, 0) }, _fps });
            layout.Controls.Add(row); layout.Controls.Add(_status); Controls.Add(layout);
            foreach (int rate in CaptureSettings.FrameRates) _fps.Items.Add(rate);
            _select.Click += (s, e) => SelectRequested?.Invoke();
            _record.Click += (s, e) => RecordRequested?.Invoke();
            _audio.CheckedChanged += Changed; _cursor.CheckedChanged += Changed; _fps.SelectedIndexChanged += Changed;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; CancelRequested?.Invoke(); } };
            FormClosing += (s, e) => { if (!AllowClose) { e.Cancel = true; CloseRequested?.Invoke(); } };
        }

        private void Changed(object sender, EventArgs e) { if (!_updating) SettingsChanged?.Invoke(); }
        internal void Apply(CaptureSettings settings)
        {
            _updating = true;
            _audio.Checked = settings.audio; _cursor.Checked = settings.cursor; _fps.SelectedItem = settings.fps;
            _updating = false;
        }
        internal void ReadSettings(CaptureSettings settings)
        {
            settings.audio = _audio.Checked; settings.cursor = _cursor.Checked;
            settings.fps = _fps.SelectedItem is int value ? value : 30;
        }
        internal void UpdateState(CaptureStage stage, string text)
        {
            bool busy = stage == CaptureStage.Countdown || stage == CaptureStage.Starting || stage == CaptureStage.Recording || stage == CaptureStage.Finishing;
            _select.Enabled = !busy;
            _audio.Enabled = _cursor.Enabled = _fps.Enabled = !busy;
            _record.Text = stage == CaptureStage.Countdown ? "Cancel" : busy ? "Stop" : "Record";
            _record.Enabled = stage == CaptureStage.Ready || stage == CaptureStage.Countdown || stage == CaptureStage.Recording || stage == CaptureStage.Starting;
            _status.Text = text;
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.Exclude(this); }
    }
}
