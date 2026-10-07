using System;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>Fixed-scope bulk cleanup. Invalid roots and linked paths fail closed.</summary>
public static class RTSafeFileSystem
{
    public static string GetAppRoot(string unityDataPath)
    {
        string dataPath = RequireAbsoluteDirectory(unityDataPath);
        var parent = Directory.GetParent(dataPath);
        if (parent == null) throw new IOException("Missing application root.");
        return RequireAbsoluteDirectory(parent.FullName);
    }

    public static void DeleteTempCache(string appRoot)
    {
        appRoot = RequireAbsoluteDirectory(appRoot);
        string cache = Path.Combine(appRoot, "tempCache");
        AssertNoLinkedAncestors(cache);
        if (!Directory.Exists(cache)) return;
        AssertNoLinksInTree(cache);
        Directory.Delete(Path.Combine(appRoot, "tempCache"), true);
    }

    public static void DeleteYtDlpPartials(string appRoot, string outputDirectory, string stem)
    {
        appRoot = RequireAbsoluteDirectory(appRoot);
        // A missing or wildcard-bearing stem must never expand into an all-files cleanup.
        if (stem == null || !Regex.IsMatch(stem, @"\Aytdlp_[0-9a-f]{8}\z"))
            throw new IOException("Invalid yt-dlp cleanup stem.");
        string expected = Path.Combine(appRoot, "tempCache", "aichat_web_videos");
        string actual = RequireAbsoluteDirectory(outputDirectory);
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(expected, actual, comparison))
            throw new IOException("Refusing yt-dlp cleanup outside its cache directory.");
        AssertNoLinkedAncestors(expected);
        if (!Directory.Exists(expected)) return;
        // Keep the literal prefix and extension boundary even after validating the ID.
        string pattern = "ytdlp_" + stem.Substring("ytdlp_".Length) + ".*";
        string[] files = Directory.GetFiles(expected, pattern, SearchOption.TopDirectoryOnly);
        foreach (string file in files) AssertNoLinkedAncestors(file);
        foreach (string file in files)
            File.Delete(Path.Combine(appRoot, "tempCache", "aichat_web_videos", Path.GetFileName(file)));
    }

    private static string RequireAbsoluteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.IndexOfAny(new[] { '*', '?' }) >= 0)
            throw new IOException("Cleanup requires a nonblank absolute directory.");
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (segment == "..") throw new IOException("Refusing traversal in a cleanup root.");
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full);
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing cleanup relative to a filesystem root.");
        return full;
    }

    private static void AssertNoLinkedAncestors(string path)
    {
        for (string cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            try { AssertNotLink(cursor); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void AssertNotLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing cleanup through a link or junction: " + path);
    }

    private static void AssertNoLinksInTree(string directory)
    {
        foreach (string entry in Directory.GetFileSystemEntries(directory))
        {
            AssertNotLink(entry);
            if (Directory.Exists(entry)) AssertNoLinksInTree(entry);
        }
    }
}
