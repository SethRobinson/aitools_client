/*
 
 Name:  Cut and paste without changing shit
 Release Date:  2/25/2020
 Version: 1.0
 Credits: Written by Seth A. Robinson except where otherwise noted
 License: No rights reserved, do whatever with it

 Description:

 In the Unity editor, if you drag gameobjects around in the hierarchy, their local position and rotation will be modified so they end up in
 the same final rotation/world position as they had before.  This adds an option so you can do a "pure" cut and paste without that silliness.

 To use:

 Make a folder called "Editor" somewhere in your assets folder (or a subfolder of it) and put this file in it.

 If you right click a gameobject in the editor hierarchy, you should now see two new options "Cut without changing shit" and
 "Paste without changing shit".  Using those you can move an object without Unity modifying its local transform like it normally does.

 Notes:

- No multi-select, only works on a single object (which can contain sub-objects)
- Nothing actually happens until you paste a gameobject (it isn't actually moved until then)
- If you paste without an object selected, it will be moved to the hierarchy root
- Undo doesn't work for this cut and paste
- (barely) Tested with Unity 2019.3.2f1

 www.rtsoft.com
 www.codedojo.com

*/

using UnityEditor;
using UnityEngine;

public class NoBSCutAndPaste
{
    static GameObject _tempObj;

    [MenuItem("GameObject/Cut without changing shit (Shift-Ctrl-X) %#x", false, 0)]
    static void CutWithoutChangingShit()
    {
        var go = Selection.activeTransform;

        if (go == null)
        {
            EditorUtility.DisplayDialog("Woah!", "First click on a gameobject in the hierarchy!", "Ok");
            return;
        }

        var s = EditorWindow.focusedWindow.ToString();

        if (EditorWindow.focusedWindow.ToString() != " (UnityEditor.SceneHierarchyWindow)")
        {
            EditorUtility.DisplayDialog("Woah!", "Don't use the 3D window, click on the gameobject in the hierarchy tree instead before doing cut/paste.", "Ok");
            _tempObj = null;
            return;
        }

        _tempObj = go.gameObject;

        Debug.Log("Cutting" + _tempObj.name+ ", now choose Paste without changing shit");

    }

    //This part by Jlpeebles taken from https://answers.unity.com/questions/656869/foldunfold-gameobject-from-code.html
    public static void SetExpandedRecursive(GameObject go, bool expand)
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        var methodInfo = type.GetMethod("SetExpandedRecursive");

        var window = EditorWindow.focusedWindow;

        methodInfo.Invoke(window, new object[] { go.GetInstanceID(), expand });
    }

    [MenuItem("GameObject/Paste without changing shit (Shift-Ctrl-V) %#v", false, 0)]
    static void PasteWithoutChangingShit()
    {

        if (_tempObj == null)
        {
            EditorUtility.DisplayDialog("Woah!", "Nothing to paste.  Highlight an object, right click, and choose 'Paste without changing shit' first.", "Ok");
            return;
        }

        if (EditorWindow.focusedWindow.ToString() != " (UnityEditor.SceneHierarchyWindow)")
        {
            EditorUtility.DisplayDialog("Woah!", "Don't use the 3D window, click on objects in the hierarchy tree instead before doing cut/paste.", "Ok");
            _tempObj = null;
            return;
        }

        var go = Selection.activeTransform;
        if (go == null || go.gameObject == null)
        {
            Debug.Log("Pasting " + _tempObj.name + " without changing its local transform stuff.  (Pasted to root as a gameobject wasn't highlighted to parent it to)");

            //Move the object to the root
            _tempObj.transform.SetParent(null, false);
            _tempObj = null;
            return;
        }

        Debug.Log("Pasting " + _tempObj.name + " under "+go.gameObject.name+" without changing its local transform stuff.");
        _tempObj.transform.SetParent(go.transform, false);
        _tempObj = null;

        SetExpandedRecursive(go.gameObject, true);
    }


    /*
     //In theory this would grey out the paste option when it wasn't valid, but due to Unity weirdness it only works in the "GameObject" drop down, not the right
     //click context menu on the hierarchy.  Better to not have it on as it just looks like it doesn't work when using from there.

    // Note that we pass the same path, and also pass "true" to the second argument.
    [MenuItem("GameObject/Paste without changing shit (Shift-Ctrl-V) %#v", true)]
    static bool PasteWithoutChangingShitValidation()
    {
        // This returns true when the selected object is a Texture2D (the menu item will be disabled otherwise).
        return _tempObj != null;
    }

    */

}
 