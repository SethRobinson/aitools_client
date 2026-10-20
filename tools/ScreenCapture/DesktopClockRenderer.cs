using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace AITools.ScreenCapture.Helper
{
    // Draws our own artwork only. The same renderer is exercised by bitmap checks.
    internal sealed class DesktopClockRenderer : IDisposable
    {
        private Bitmap _dial;
        private bool _digital;
        private static readonly Color Face = Color.FromArgb(29, 32, 37);
        private static readonly Color Ivory = Color.FromArgb(243, 238, 222);
        private static readonly Color Muted = Color.FromArgb(169, 173, 177);
        private static readonly Color Orange = Color.FromArgb(255, 155, 73);
        internal const int ButtonCount = 5;
        internal static RectangleF ButtonRect(int index) => new RectangleF(10 + index * 40, 8, 38, 29);
        internal static int HitButton(float x, float y)
        {
            for (int i = 0; i < ButtonCount; i++) if (ButtonRect(i).Contains(x, y)) return i;
            return -1;
        }
        internal static bool HitGrip(float x, float y, bool digital) =>
            x >= 196 && y >= 220 * DesktopClockGeometry.HeightRatio(digital) - 24;

        internal void Draw(Bitmap target, DesktopClockSettings settings, double seconds,
            bool running, bool synchronized, float controls, int focusedButton = -1)
        {
            if (_dial == null || _dial.Size != target.Size || _digital != settings.digital)
            {
                _dial?.Dispose();
                _dial = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
                _digital = settings.digital;
                using (Graphics g = Graphics.FromImage(_dial)) DrawDial(g, target.Width, settings.digital);
            }
            using (Graphics g = Graphics.FromImage(target))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_dial, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(target.Width / 220f, target.Width / 220f);
                Hand(g, DesktopClockTimer.HourAngle(seconds), 49, 5, Ivory, 0);
                Hand(g, DesktopClockTimer.MinuteAngle(seconds), 73, 3.5f, Ivory, 0);
                Hand(g, DesktopClockTimer.SecondAngle(seconds), 83, Math.Max(1.8f, 440f / target.Width), Orange, 17);
                using (var brush = new SolidBrush(Orange)) g.FillEllipse(brush, 105.5f, 105.5f, 9, 9);
                using (var brush = new SolidBrush(Face)) g.FillEllipse(brush, 108, 108, 4, 4);
                if (settings.digital)
                {
                    string label = settings.stopwatch ? (running ? "Elapsed" : "Elapsed  /  Paused") : "Local time";
                    if (settings.stopwatch && synchronized) label = "Elapsed  /  Recording";
                    Text(g, label, 10, Muted, new RectangleF(8, 216, 204, 18));
                    string value = settings.stopwatch ? DesktopClockTimer.FormatElapsed(seconds) :
                        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
                    Text(g, value, value.Length > 9 ? 17 : 21, Ivory, new RectangleF(8, 235, 204, 29));
                }
                // When the digital readout is hidden, keep the timer's paused state explicit.
                else if (settings.stopwatch && !running)
                    Text(g, "Paused", 10, Muted, new RectangleF(70, 155, 80, 18));
                if (controls > 0) DrawControls(g, settings, running, synchronized, controls, focusedButton);
            }
        }

        private static void DrawDial(Graphics g, int width, bool digital)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(width / 220f, width / 220f);
            float height = (float)(220 * DesktopClockGeometry.HeightRatio(digital));
            using (GraphicsPath outline = RoundRect(new RectangleF(1, 1, 218, height - 2), 18))
            using (var fill = new SolidBrush(Face))
            using (var edge = new Pen(Color.FromArgb(68, 73, 80), 1))
            { g.FillPath(fill, outline); g.DrawPath(edge, outline); }
            using (var rim = new Pen(Color.FromArgb(60, 65, 72), 1))
                g.DrawEllipse(rim, 13, 13, 194, 194);
            for (int tick = 0; tick < 60; tick++)
            {
                bool major = tick % 5 == 0;
                PointF a = Point(tick * 6, major ? 82 : 88);
                PointF b = Point(tick * 6, 93);
                using (var pen = new Pen(major ? Ivory : Muted, major ? 2 : 0.8f))
                    g.DrawLine(pen, a, b);
            }
            foreach (int hour in new[] { 12, 3, 6, 9 })
            {
                PointF p = Point(hour * 30, 68);
                Text(g, hour.ToString(CultureInfo.InvariantCulture), 15, Ivory,
                    new RectangleF(p.X - 16, p.Y - 12, 32, 24));
            }
        }

        private static void DrawControls(Graphics g, DesktopClockSettings settings, bool running,
            bool synchronized, float opacity, int focused)
        {
            int alpha = (int)(255 * Math.Max(0, Math.Min(1, opacity)));
            using (var back = new SolidBrush(Color.FromArgb(alpha, 45, 50, 57)))
            using (var path = RoundRect(new RectangleF(7, 5, 206, 35), 9)) g.FillPath(back, path);
            for (int i = 0; i < ButtonCount; i++)
            {
                RectangleF b = ButtonRect(i);
                bool disabled = (i == 1 || i == 2) && (!settings.stopwatch || synchronized);
                Color color = Color.FromArgb(disabled ? alpha / 3 : alpha, Ivory);
                using (var pen = new Pen(color, 1.6f))
                using (var brush = new SolidBrush(color))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    float cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
                    if (i == 0)
                    {
                        g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                        g.DrawLine(pen, cx, cy, cx, cy - 4);
                        g.DrawLine(pen, cx, cy, cx + 4, cy + 2);
                        if (settings.stopwatch) g.DrawLine(pen, cx - 2, cy - 10, cx + 2, cy - 10);
                    }
                    else if (i == 1 && running)
                    { g.FillRectangle(brush, cx - 5, cy - 6, 3, 12); g.FillRectangle(brush, cx + 2, cy - 6, 3, 12); }
                    else if (i == 1)
                        g.FillPolygon(brush, new[] { new PointF(cx - 4, cy - 7), new PointF(cx + 6, cy), new PointF(cx - 4, cy + 7) });
                    else if (i == 2)
                    {
                        g.DrawArc(pen, cx - 6, cy - 6, 12, 12, -60, 290);
                        g.DrawLines(pen, new[] { new PointF(cx - 8, cy - 1), new PointF(cx - 6, cy + 5), new PointF(cx - 1, cy + 2) });
                    }
                    else if (i == 3)
                    { for (int dot = -1; dot <= 1; dot++) g.FillEllipse(brush, cx + dot * 5 - 1, cy - 1, 2, 2); }
                    else
                    { g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4); g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4); }
                    if (focused == i) g.DrawRectangle(pen, b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4);
                }
            }
            if (!settings.locked)
            {
                float bottom = (float)(220 * DesktopClockGeometry.HeightRatio(settings.digital));
                using (var pen = new Pen(Color.FromArgb(alpha, Muted), 1.5f))
                { g.DrawLine(pen, 202, bottom - 8, 212, bottom - 18); g.DrawLine(pen, 208, bottom - 8, 212, bottom - 12); }
            }
        }
        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        private static PointF Point(double degrees, float length)
        {
            double angle = degrees * Math.PI / 180;
            return new PointF(110 + (float)Math.Sin(angle) * length, 110 - (float)Math.Cos(angle) * length);
        }
        private static void Hand(Graphics g, double angle, float length, float width, Color color, float tail)
        {
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, Point(angle, -tail), Point(angle, length));
        }
        private static void Text(Graphics g, string value, float size, Color color, RectangleF rect)
        {
            // The dial scales freely, but its labels must remain readable at 140px.
            float scale;
            using (var transform = g.Transform) scale = transform.Elements[0];
            size = Math.Max(size, 9.5f / scale);
            using (var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(value, font, brush, rect, format);
        }
        public void Dispose() { _dial?.Dispose(); _dial = null; }
    }
}
