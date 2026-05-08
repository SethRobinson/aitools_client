using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Installs broader TextMeshPro emoji fallbacks at runtime.
/// </summary>
public static class TMPEmojiFallbackInstaller
{
    private const int EmojiAtlasSize = 2048;
    private const int EmojiPointSize = 90;
    private const int EmojiPadding = 1;

    private static bool _installed;
    private static TMP_FontAsset _runtimeEmojiFontAsset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallOnLoad()
    {
        EnsureInstalled();
    }

    public static void EnsureInstalled()
    {
        if (_installed)
            return;

        _installed = true;

        AddDefaultSpriteAssetFallback();
        AddRuntimeColorEmojiFontFallback();
    }

    private static void AddDefaultSpriteAssetFallback()
    {
        if (TMP_Settings.defaultSpriteAsset != null)
            AddEmojiFallback(TMP_Settings.defaultSpriteAsset);
    }

    private static void AddRuntimeColorEmojiFontFallback()
    {
        string fontPath = FindColorEmojiFontPath();
        if (string.IsNullOrEmpty(fontPath))
            return;

        try
        {
            _runtimeEmojiFontAsset = TMP_FontAsset.CreateFontAsset(
                fontPath,
                0,
                EmojiPointSize,
                EmojiPadding,
                GlyphRenderMode.COLOR,
                EmojiAtlasSize,
                EmojiAtlasSize);

            if (_runtimeEmojiFontAsset == null)
                return;

            _runtimeEmojiFontAsset.name = "Runtime Color Emoji Fallback";
            if (_runtimeEmojiFontAsset.material != null)
                _runtimeEmojiFontAsset.material.name = _runtimeEmojiFontAsset.name + " Material";
            AddEmojiFallback(_runtimeEmojiFontAsset);
            AddGeneralFallback(_runtimeEmojiFontAsset);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Unable to install TMP color emoji fallback from " + fontPath + ": " + ex.Message);
        }
    }

    private static void AddEmojiFallback(TMP_Asset asset)
    {
        if (asset == null)
            return;

        List<TMP_Asset> fallbacks = TMP_Settings.emojiFallbackTextAssets;
        if (fallbacks == null)
        {
            fallbacks = new List<TMP_Asset>();
            TMP_Settings.emojiFallbackTextAssets = fallbacks;
        }

        if (!fallbacks.Contains(asset))
            fallbacks.Add(asset);
    }

    private static void AddGeneralFallback(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
            return;

        List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
        if (fallbacks == null)
        {
            fallbacks = new List<TMP_FontAsset>();
            TMP_Settings.fallbackFontAssets = fallbacks;
        }

        if (!fallbacks.Contains(fontAsset))
            fallbacks.Add(fontAsset);
    }

    private static string FindColorEmojiFontPath()
    {
        string knownPath = FindKnownPlatformEmojiFontPath();
        if (!string.IsNullOrEmpty(knownPath))
            return knownPath;

        try
        {
            string[] osFontPaths = Font.GetPathsToOSFonts();
            if (osFontPaths == null)
                return null;

            foreach (string path in osFontPaths)
            {
                string fileName = Path.GetFileName(path);
                if (IsPreferredEmojiFontFileName(fileName) && File.Exists(path))
                    return path;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Unable to scan OS fonts for TMP emoji fallback: " + ex.Message);
        }

        return null;
    }

    private static string FindKnownPlatformEmojiFontPath()
    {
        foreach (string path in GetKnownPlatformEmojiFontPaths())
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string> GetKnownPlatformEmojiFontPaths()
    {
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windowsDir))
            yield return Path.Combine(windowsDir, "Fonts", "seguiemj.ttf");

        yield return "/System/Library/Fonts/Apple Color Emoji.ttc";
        yield return "/Library/Fonts/Apple Color Emoji.ttc";
        yield return "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf";
        yield return "/usr/share/fonts/truetype/noto/Noto Color Emoji.ttf";
        yield return "/usr/share/fonts/google-noto-emoji/NotoColorEmoji.ttf";
        yield return "/usr/share/fonts/noto/NotoColorEmoji.ttf";
    }

    private static bool IsPreferredEmojiFontFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        return fileName.Equals("seguiemj.ttf", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Apple Color Emoji.ttc", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("NotoColorEmoji.ttf", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Noto Color Emoji.ttf", StringComparison.OrdinalIgnoreCase);
    }
}
