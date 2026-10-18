// Only the small, pure Unity API surface used by FfmpegTool. The production source
// is linked into these checks so real FFmpeg argument building and export run offline.
using System;
namespace UnityEngine
{
    public static class Application { public static string dataPath; }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp01(float v) => Math.Max(0, Math.Min(1, v));
        public static float Clamp(float v, float a, float b) => Math.Max(a, Math.Min(b, v));
        public static int Clamp(int v, int a, int b) => Math.Max(a, Math.Min(b, v));
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }
}
