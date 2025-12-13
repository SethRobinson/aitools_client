using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// A simple, code-only message dialog that can be created without prefabs.
/// Usage: RTSimpleMessageDialog.Show("Title", "Body text", "OK");
/// For clickable links, use: RTSimpleMessageDialog.ShowWithLink("Title", "Body", "Link Text", "https://url.com");
/// </summary>
public class RTSimpleMessageDialog : MonoBehaviour
{
    private const string DIALOG_NAME = "RTSimpleMessageDialog";
    
    /// <summary>
    /// Shows a simple message dialog with a title, body text, and OK button.
    /// </summary>
    public static RTSimpleMessageDialog Show(string title, string bodyText, string buttonText = "OK", Action onClose = null)
    {
        return ShowWithLink(title, bodyText, null, null, buttonText, onClose);
    }

    /// <summary>
    /// Shows a message dialog with a clickable link at the bottom.
    /// </summary>
    public static RTSimpleMessageDialog ShowWithLink(string title, string bodyText, string linkText, string linkUrl, string buttonText = "OK", Action onClose = null)
    {
        // Check if dialog already exists to prevent duplicates
        var existing = GameObject.Find(DIALOG_NAME);
        if (existing != null)
            GameObject.Destroy(existing);

        // Create dialog container
        GameObject dialogObj = new GameObject(DIALOG_NAME);
        
        // Find canvas
        Canvas canvas = null;
        var mainCanvas = RTUtil.FindIncludingInactive("MainCanvas");
        if (mainCanvas != null)
            canvas = mainCanvas.GetComponent<Canvas>();
        
        if (canvas == null)
        {
            Debug.LogError("RTSimpleMessageDialog: Could not find MainCanvas");
            GameObject.Destroy(dialogObj);
            return null;
        }
        
        dialogObj.transform.SetParent(canvas.transform, false);
        
        // Add the component
        RTSimpleMessageDialog dialog = dialogObj.AddComponent<RTSimpleMessageDialog>();
        dialog.CreateDialog(title, bodyText, linkText, linkUrl, buttonText, onClose);
        
        return dialog;
    }

    private Action m_onCloseCallback;
    private string m_linkUrl;

    private void CreateDialog(string title, string bodyText, string linkText, string linkUrl, string buttonText, Action onClose)
    {
        m_onCloseCallback = onClose;
        m_linkUrl = linkUrl;

        // Calculate height based on content
        int baseHeight = 280;
        int linkHeight = !string.IsNullOrEmpty(linkText) ? 30 : 0;
        int totalHeight = baseHeight + linkHeight;

        // Add background panel
        RectTransform dialogRect = gameObject.AddComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.sizeDelta = new Vector2(500, totalHeight);
        dialogRect.anchoredPosition = Vector2.zero;

        // Add dark background image
        Image bgImage = gameObject.AddComponent<Image>();
        bgImage.color = new Color(0.12f, 0.12f, 0.14f, 0.98f);

        // Add outline
        Outline outline = gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.55f, 0.95f, 1f);
        outline.effectDistance = new Vector2(2, 2);

        // Create title
        CreateTitle(title);

        // Create body with proper text wrapping
        CreateBody(bodyText, linkHeight);

        // Create link if provided
        if (!string.IsNullOrEmpty(linkText) && !string.IsNullOrEmpty(linkUrl))
        {
            CreateLink(linkText, linkUrl);
        }

        // Create button
        CreateButton(buttonText);
    }

    private void CreateTitle(string title)
    {
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(transform, false);
        
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -12);
        titleRect.sizeDelta = new Vector2(-30, 30);

        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = title;
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(0.4f, 0.7f, 1f, 1f);
        titleText.enableWordWrapping = true;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void CreateBody(string bodyText, int linkHeight)
    {
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(transform, false);
        
        RectTransform bodyRect = bodyObj.AddComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0, 0);
        bodyRect.anchorMax = new Vector2(1, 1);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.offsetMin = new Vector2(20, 55 + linkHeight); // left, bottom
        bodyRect.offsetMax = new Vector2(-20, -45); // right, top

        TextMeshProUGUI bodyTextComp = bodyObj.AddComponent<TextMeshProUGUI>();
        bodyTextComp.text = bodyText;
        bodyTextComp.fontSize = 15;
        bodyTextComp.alignment = TextAlignmentOptions.TopLeft;
        bodyTextComp.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        bodyTextComp.richText = true;
        bodyTextComp.enableWordWrapping = true;
        bodyTextComp.overflowMode = TextOverflowModes.Truncate;
    }

    private void CreateLink(string linkText, string linkUrl)
    {
        GameObject linkObj = new GameObject("Link");
        linkObj.transform.SetParent(transform, false);
        
        RectTransform linkRect = linkObj.AddComponent<RectTransform>();
        linkRect.anchorMin = new Vector2(0, 0);
        linkRect.anchorMax = new Vector2(1, 0);
        linkRect.pivot = new Vector2(0.5f, 0);
        linkRect.anchoredPosition = new Vector2(0, 50);
        linkRect.sizeDelta = new Vector2(-40, 25);

        TextMeshProUGUI linkTextComp = linkObj.AddComponent<TextMeshProUGUI>();
        linkTextComp.text = $"<link=\"{linkUrl}\"><color=#6699CC><u>{linkText}</u></color></link>";
        linkTextComp.fontSize = 13;
        linkTextComp.alignment = TextAlignmentOptions.Center;
        linkTextComp.color = new Color(0.5f, 0.65f, 0.85f, 1f);
        linkTextComp.richText = true;
        linkTextComp.enableWordWrapping = true;

        // Add click handler for the link
        var linkHandler = linkObj.AddComponent<RTSimpleMessageDialogLinkHandler>();
        linkHandler.Setup(linkTextComp, linkUrl);
    }

    private void CreateButton(string buttonText)
    {
        GameObject buttonObj = new GameObject("Button");
        buttonObj.transform.SetParent(transform, false);
        
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0);
        buttonRect.anchorMax = new Vector2(0.5f, 0);
        buttonRect.pivot = new Vector2(0.5f, 0);
        buttonRect.anchoredPosition = new Vector2(0, 12);
        buttonRect.sizeDelta = new Vector2(90, 32);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.25f, 0.45f, 0.75f, 1f);

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        
        var colors = button.colors;
        colors.normalColor = new Color(0.25f, 0.45f, 0.75f, 1f);
        colors.highlightedColor = new Color(0.35f, 0.55f, 0.85f, 1f);
        colors.pressedColor = new Color(0.2f, 0.35f, 0.6f, 1f);
        button.colors = colors;

        button.onClick.AddListener(OnButtonClicked);

        // Button text
        GameObject buttonTextObj = new GameObject("Text");
        buttonTextObj.transform.SetParent(buttonObj.transform, false);
        
        RectTransform buttonTextRect = buttonTextObj.AddComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        TextMeshProUGUI buttonTextComp = buttonTextObj.AddComponent<TextMeshProUGUI>();
        buttonTextComp.text = buttonText;
        buttonTextComp.fontSize = 16;
        buttonTextComp.fontStyle = FontStyles.Bold;
        buttonTextComp.alignment = TextAlignmentOptions.Center;
        buttonTextComp.color = Color.white;
    }

    private void OnButtonClicked()
    {
        m_onCloseCallback?.Invoke();
        GameObject.Destroy(gameObject);
    }

    /// <summary>
    /// Close the dialog programmatically.
    /// </summary>
    public void Close()
    {
        OnButtonClicked();
    }
}

/// <summary>
/// Helper component to handle link clicks in TextMeshPro text.
/// </summary>
public class RTSimpleMessageDialogLinkHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
{
    private TextMeshProUGUI m_textComponent;
    private string m_url;

    public void Setup(TextMeshProUGUI textComponent, string url)
    {
        m_textComponent = textComponent;
        m_url = url;
    }

    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (m_textComponent == null) return;

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_textComponent, eventData.position, null);
        if (linkIndex != -1)
        {
            TMP_LinkInfo linkInfo = m_textComponent.textInfo.linkInfo[linkIndex];
            string url = linkInfo.GetLinkID();
            if (!string.IsNullOrEmpty(url))
            {
                Application.OpenURL(url);
            }
        }
        else if (!string.IsNullOrEmpty(m_url))
        {
            // Fallback: open URL if clicked anywhere on the text
            Application.OpenURL(m_url);
        }
    }
}

