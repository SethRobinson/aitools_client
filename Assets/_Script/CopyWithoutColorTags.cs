using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.ComponentModel.Design;

public class CopyWithoutColorTags : MonoBehaviour
{
    public TMPro.TextMeshProUGUI textObject;

    private void Start()
    {
   
    }

    public void CopySelectedText()
    {
        //we could 
        GUIUtility.systemCopyBuffer = RTUtil.RemoveColorAndFontTags(textObject.text); //we could use GetParsedText() but I want to use the same thing everywhere
    }

}

