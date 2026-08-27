using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Reads FILE references off the Windows clipboard so "paste" can accept videos and
/// other media that arrive as files rather than bitmaps - an Explorer Ctrl+C, or the
/// Snipping Tool's Copy button after a screen recording. Bitmap/PNG clipboard content
/// is NOT handled here; that stays with utils\RTClip.exe's image mode.
///
/// Two layers:
///   1. In-process CF_HDROP / FileNameW via Win32 (instant, covers real file lists).
///   2. utils\RTClip.exe "files" mode for OLE virtual files (FileGroupDescriptorW +
///      FileContents), which need an STA COM reader; the helper extracts them to
///      tempCache\pasted_media and hands back the paths via winclip_files.txt.
///
/// Callers that keep referencing a pasted file's path long-term (movie pics stream
/// from disk) should first snapshot it with <see cref="CopyToPasteCache"/>: clipboard
/// sources are often transient (Snipping Tool recordings live in its TempState).
/// </summary>
public static class RTClipboardFileList
{
    const string PasteCacheFolder = "pasted_media";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    const uint CF_HDROP = 15;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr hMem);
#endif

    /// <summary>
    /// Files currently referenced by the Windows clipboard, or an empty list. Paths are
    /// verified to exist. Never throws. Main thread only (resolves the app root).
    /// </summary>
    public static List<string> GetFiles()
    {
        var files = new List<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            bool virtualFilesPresent = false;
            if (TryOpenClipboard())
            {
                try
                {
                    ReadHDropFiles(files);
                    if (files.Count == 0)
                        ReadFileNameW(files);
                    if (files.Count == 0)
                        virtualFilesPresent = IsClipboardFormatAvailable(RegisterClipboardFormat("FileGroupDescriptorW"));
                }
                finally { CloseClipboard(); }
            }

            // OLE virtual files (no real path on the clipboard): let the STA helper
            // extract them to the paste cache. Spawned only when the format is present,
            // so the common paths never pay the helper's process startup.
            if (files.Count == 0 && virtualFilesPresent)
                ReadVirtualFilesViaHelper(files);

            for (int i = files.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(files[i]) || !File.Exists(files[i]))
                    files.RemoveAt(i);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("RTClipboardFileList: clipboard file read failed: " + ex.Message);
        }
#endif
        return files;
    }

    /// <summary>
    /// True when the clipboard carries bitmap/PNG image data the RTClip image path could
    /// read. Lets paste callers avoid creating an empty pic for a text-only clipboard.
    /// Errs on true when uncertain (the RTClip path then decides for real).
    /// </summary>
    public static bool HasImage()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            const uint CF_BITMAP = 2, CF_DIB = 8, CF_DIBV5 = 17;
            if (IsClipboardFormatAvailable(CF_BITMAP)
                || IsClipboardFormatAvailable(CF_DIB)
                || IsClipboardFormatAvailable(CF_DIBV5))
                return true;
            uint png = RegisterClipboardFormat("PNG");
            return png != 0 && IsClipboardFormatAvailable(png);
        }
        catch { return true; }
#else
        return true;
#endif
    }

    /// <summary>
    /// Copy a pasted file into tempCache\pasted_media so the app owns a stable copy
    /// (Snipping Tool etc. may clean up their temp originals later). Returns the copy's
    /// path, the input unchanged if it already lives in the paste cache, or null on
    /// failure. Callers that only read the bytes immediately don't need this.
    /// </summary>
    public static string CopyToPasteCache(string sourcePath)
    {
        try
        {
            string dir = GetPasteCacheDir();
            string fullSource = Path.GetFullPath(sourcePath);
            if (fullSource.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                return sourcePath; // already ours (e.g. helper-extracted virtual file)

            string stem = "paste";
            string ext = "";
            try
            {
                string fileStem = Path.GetFileNameWithoutExtension(sourcePath);
                if (!string.IsNullOrWhiteSpace(fileStem)) stem = SanitizeFileStem(fileStem);
                ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            }
            catch { }

            string dest = Path.Combine(dir, stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
            File.Copy(fullSource, dest, true);
            return dest;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("RTClipboardFileList: could not cache pasted file " + sourcePath + ": " + ex.Message);
            return null;
        }
    }

    static string GetAppRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath.Replace('/', '\\'), ".."));
    }

    static string GetPasteCacheDir()
    {
        string dir = Path.Combine(GetAppRoot(), "tempCache", PasteCacheFolder);
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(dir);
    }

    static string SanitizeFileStem(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    static bool TryOpenClipboard()
    {
        // Another app can hold the clipboard for a moment right after a copy.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            System.Threading.Thread.Sleep(15);
        }
        return false;
    }

    static void ReadHDropFiles(List<string> files)
    {
        if (!IsClipboardFormatAvailable(CF_HDROP)) return;
        IntPtr hDrop = GetClipboardData(CF_HDROP);
        if (hDrop == IntPtr.Zero) return;

        uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        for (uint i = 0; i < count; i++)
        {
            uint len = DragQueryFile(hDrop, i, null, 0);
            if (len == 0) continue;
            var sb = new StringBuilder((int)len + 1);
            if (DragQueryFile(hDrop, i, sb, (uint)sb.Capacity) > 0)
                files.Add(sb.ToString());
        }
    }

    static void ReadFileNameW(List<string> files)
    {
        // Single-file alternative some shell operations publish without CF_HDROP.
        uint fmt = RegisterClipboardFormat("FileNameW");
        if (fmt == 0 || !IsClipboardFormatAvailable(fmt)) return;
        IntPtr hMem = GetClipboardData(fmt);
        if (hMem == IntPtr.Zero) return;
        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) return;
        try
        {
            string path = Marshal.PtrToStringUni(ptr);
            if (!string.IsNullOrWhiteSpace(path))
                files.Add(path);
        }
        finally { GlobalUnlock(hMem); }
    }

    static void ReadVirtualFilesViaHelper(List<string> files)
    {
        string root = GetAppRoot();
        string exe = Path.Combine(root, "utils", "RTClip.exe");
        if (!File.Exists(exe)) return; // this build may not ship RTClip.exe

        string listFile = Path.Combine(root, "winclip_files.txt");
        RTUtil.DeleteFileIfItExists(listFile);

        var psi = new System.Diagnostics.ProcessStartInfo(exe, "files \"" + GetPasteCacheDir() + "\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return;
        proc.WaitForExit();
        proc.Close();

        if (!File.Exists(listFile)) return;
        try
        {
            foreach (var line in File.ReadAllLines(listFile, Encoding.UTF8))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    files.Add(line.Trim());
            }
        }
        finally { RTUtil.DeleteFileIfItExists(listFile); }
    }
#endif
}
