using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using B83.Win32;

public class DragAndDropHandler : MonoBehaviour
{
    /// <summary>
    /// Optional intercept hooks for panels (e.g. AIChatPanel, AdventureInput) that want
    /// to claim file drops landing over their UI rect before the default "open as new pic"
    /// behavior runs. Each handler returns true to consume the drop, false to let it fall
    /// through to the next claimant (or the default handler). Multiple panels can register
    /// independently; the first one to return true wins.
    /// </summary>
    public static readonly List<System.Func<List<string>, POINT, bool>> ClaimHandlers
        = new List<System.Func<List<string>, POINT, bool>>();

    void OnEnable()
    {
        UnityDragAndDropHook.InstallHook();
        UnityDragAndDropHook.OnDroppedFiles += OnFiles;

    }
    void OnDisable()
    {
        UnityDragAndDropHook.UninstallHook();
    }

    void OnFiles(List<string> aFiles, POINT aPos)
    {
        // Give any interested panel a chance to claim this drop first. Snapshot the list
        // so a claim handler can safely (de)register itself during iteration.
        if (ClaimHandlers.Count > 0)
        {
            var snapshot = ClaimHandlers.ToArray();
            foreach (var handler in snapshot)
            {
                if (handler == null) continue;
                try
                {
                    if (handler(aFiles, aPos))
                        return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("DragAndDropHandler claim handler threw: " + ex);
                }
            }
        }

        // List to collect all supported files
        List<string> validFiles = new List<string>();

        // Scan through dropped files and filter out supported image types
        foreach (var f in aFiles)
        {
            var fi = new System.IO.FileInfo(f);
            var ext = fi.Extension.ToLower();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".mp4" || ext == ".avi" || ext == ".mov")
            {
                validFiles.Add(f);
            }
            else
            {
                RTQuickMessageManager.Get().ShowMessage("Unknown image format: " + fi.Name);
            }
        }

        // Process all valid files
        if (validFiles.Count > 0)
        {
            Debug.Log("Dropped " + validFiles.Count + " valid files at " + new Vector2(aPos.x, aPos.y));
            RTQuickMessageManager.Get().ShowMessage("Opening " + validFiles.Count + " files");

            // Add a small delay for each file to ensure proper loading sequence
            for (int i = 0; i < validFiles.Count; i++)
            {
                string file = validFiles[i];
                // Schedule with increasing delays to avoid overwhelming the system
                float delay = 0.1f + (i * 0.05f);
                RTMessageManager.Get().Schedule(delay, ImageGenerator.Get().AddImageByFileNameNoReturn, file);
            }
        }
    }
}