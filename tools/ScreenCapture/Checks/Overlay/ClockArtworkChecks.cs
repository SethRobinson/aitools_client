using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
using AITools.ScreenCapture;
using AITools.ScreenCapture.Helper;

internal static class ClockArtworkChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        string evidence = root == null ? null : Path.Combine(root, "build", "clock-preview-" + Guid.NewGuid().ToString("N"));
        if (evidence != null) Directory.CreateDirectory(evidence);
        var serializer = new JavaScriptSerializer();
        var roundTrip = serializer.Deserialize<CaptureEvent>(serializer.Serialize(new CaptureEvent
        {
            type = "clock_settings", clockSettings = new DesktopClockSettings { x = -200, widthDip = 333, sync = true }
        }));
        if (roundTrip.clockSettings.x != -200 || roundTrip.clockSettings.widthDip != 333 || !roundTrip.clockSettings.sync)
            throw new Exception("Helper clock settings serialization failed.");
        checks++;
        using (var renderer = new DesktopClockRenderer())
        foreach (int width in new[] { 140, 220, 640 })
        foreach (bool digital in new[] { true, false })
        foreach (bool controls in new[] { false, true })
        {
            var settings = new DesktopClockSettings { stopwatch = true, digital = digital };
            using (var bitmap = new Bitmap(width, (int)Math.Round(width * DesktopClockGeometry.HeightRatio(digital)), PixelFormat.Format32bppPArgb))
            {
                renderer.Draw(bitmap, settings, 10 * 3600 + 8 * 60 + 15.5, false, false, controls ? 1 : 0, controls ? 1 : -1);
                if (evidence != null) bitmap.Save(Path.Combine(evidence, "clock-" + width + (digital ? "-digital" : "-analog") + (controls ? "-controls" : "") + ".png"));
                if (bitmap.GetPixel(0, 0).A != 0) throw new Exception("Clock corners must be transparent.");
                if (bitmap.GetPixel(width / 2, width / 2).A != 255) throw new Exception("Clock face must be opaque.");
                int orange = 0;
                for (int y = 0; y < width; y++)
                for (int x = 0; x < width; x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    if (c.R > 230 && c.G > 100 && c.G < 190 && c.B < 110) orange++;
                }
                if (orange < width / 3) throw new Exception("Second hand is missing or too faint: width=" + width + ", orange pixels=" + orange + ". Evidence: " + evidence);
                checks += 3;
                // Render with a different time into the same buffer to exercise cached dial reuse.
                renderer.Draw(bitmap, settings, 30, true, true, 0);
                Color tip = bitmap.GetPixel(width / 2, (int)(188 * width / 220f));
                if (tip.R < 200 || tip.G < 100) throw new Exception("Second hand did not move to the new time.");
                checks++;
            }
        }
        for (int i = 0; i < DesktopClockRenderer.ButtonCount; i++)
        {
            var r = DesktopClockRenderer.ButtonRect(i);
            if (DesktopClockRenderer.HitButton(r.X + r.Width / 2, r.Y + r.Height / 2) != i)
                throw new Exception("Clock control hit region disagrees with artwork.");
            checks++;
        }
        if (DesktopClockRenderer.HitButton(110, 110) != -1) throw new Exception("Clock face should remain draggable.");
        checks++;
        Console.WriteLine("PASS: " + checks + " synthetic clock artwork checks; no windows opened." + (evidence == null ? "" : " Evidence: " + evidence));
        return checks;
    }
}
