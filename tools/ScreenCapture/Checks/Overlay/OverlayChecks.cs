using System;
using System.Drawing;
using System.Drawing.Imaging;
using AITools.ScreenCapture;
using AITools.ScreenCapture.Helper;

// Synthetic bitmap rendering only. No Form, desktop DC, capture, or input is used.
internal static class OverlayChecks
{
    private static int Main(string[] args)
    {
        try
        {
            int checks = ClockArtworkChecks.Run(args.Length > 0 ? PathRoot(args[0]) : null);
            foreach (Point origin in new[] { Point.Empty, new Point(-2560, -400), new Point(1920, 0) })
            using (var bitmap = new Bitmap(800, 600, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                Rectangle monitor = new Rectangle(origin, bitmap.Size);
                var rect = new PixelRect(origin.X + 100, origin.Y + 100, 400, 300);
                SelectionArtwork.Draw(g, bitmap.Size, rect, monitor, SystemFonts.MessageBoxFont, "Drag an area", false);
                if (bitmap.GetPixel(300, 250).A != 0) throw new Exception("Selected interior must be completely clear while dragging.");
                if (bitmap.GetPixel(10, 10).ToArgb() != Color.FromArgb(90, 0, 0, 0).ToArgb()) throw new Exception("Outside must have only the intended black dimming.");
                if (bitmap.GetPixel(99, 250).A != 255) throw new Exception("Outline must remain readable.");
                checks += 3;
                SelectionArtwork.Draw(g, bitmap.Size, rect, monitor, SystemFonts.MessageBoxFont, "Position the box", true);
                if (bitmap.GetPixel(300, 250).A != 1) throw new Exception("Fixed hover must intercept clicks with minimum opacity.");
                checks++;
                // Erase the old hole when the rectangle moves. No stale transparent pixels.
                rect = new PixelRect(origin.X + 520, origin.Y + 100, 200, 300);
                SelectionArtwork.Draw(g, bitmap.Size, rect, monitor, SystemFonts.MessageBoxFont, "Drag an area", false);
                if (bitmap.GetPixel(300, 250).A != 90 || bitmap.GetPixel(600, 250).A != 0) throw new Exception("Moved hole left stale dimming.");
                checks++;
                rect = new PixelRect(origin.X, origin.Y, 800, 600);
                SelectionArtwork.Draw(g, bitmap.Size, rect, monitor, SystemFonts.MessageBoxFont, "Hidden instructions", false);
                if (bitmap.GetPixel(30, 30).A != 0 || bitmap.GetPixel(400, 300).A != 0) throw new Exception("Full-screen interior must stay clear, including the instruction area.");
                checks++;
            }
            Console.WriteLine("PASS: " + checks + " synthetic overlay checks; no windows opened.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static string PathRoot(string path) => System.IO.Path.GetFullPath(path);
}
