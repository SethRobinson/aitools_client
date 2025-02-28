using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using B83.Win32;

public class DragAndDropHandler : MonoBehaviour
{

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