using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using B83.Win32;


public class DragAndDropHandler : MonoBehaviour
{
     
    void OnEnable ()
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
        string file = "";
        // scan through dropped files and filter out supported image types
        foreach(var f in aFiles)
        {
            var fi = new System.IO.FileInfo(f);
            var ext = fi.Extension.ToLower();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                file = f;
                break;
            }
        }
        // If the user dropped a supported file, create a DropInfo
        if (file != "")
        {
            Debug.Log("Dropped " + file + " at " + new Vector2(aPos.x, aPos.y));
            ImageGenerator.Get().AddImageByFileName(file);
        }
    }

}
