using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Runtime-created startup splash. Release builds always show it; editor/dev
/// builds opt in through GameLogic so day-to-day Play Mode stays quiet.
/// </summary>
public class StartupSplashPanel : MonoBehaviour
{
    private const float PanelWidth = 760f;
    private const float PanelHeight = 620f;
    private const float HeaderHeight = 56f;
    private const float BannerHeight = 210f;
    private const float FooterHeight = 70f;
    private const string ArtworkResourcePath = "Splash/seths_ai_tools_splash";
    private const string GithubUrl = "https://github.com/SethRobinson/aitools_client";
    private const string PrefsDontShowAgain = "StartupSplashPanel.DontShowAgain";

    private static StartupSplashPanel _instance;

    private TMP_FontAsset _font;
    private Sprite _runtimeBannerSprite;

    private static readonly Color PanelBg = new Color(0.94f, 0.95f, 0.96f, 1f);
    private static readonly Color HeaderBg = new Color(0.10f, 0.12f, 0.15f, 1f);
    private static readonly Color TextDark = new Color(0.08f, 0.09f, 0.10f, 1f);
    private static readonly Color PrimaryButton = new Color(0.09f, 0.48f, 0.55f, 1f);
    private static readonly Color SecondaryButton = new Color(1f, 1f, 1f, 1f);

    public static void ShowIfNeeded(bool showInEditor)
    {
        if (PlayerPrefs.GetInt(PrefsDontShowAgain, 0) != 0)
            return;

#if RT_RELEASE
        Show();
#else
        if (showInEditor)
            Show();
#endif
    }

    public static void Show()
    {
        if (_instance != null)
        {
            _instance.gameObject.SetActive(true);
            return;
        }

        var root = new GameObject("StartupSplashPanel");
        _instance = root.AddComponent<StartupSplashPanel>();
        _instance.CreateUI();
    }

    private void OnDestroy()
    {
        if (_runtimeBannerSprite != null)
        {
            Destroy(_runtimeBannerSprite);
            _runtimeBannerSprite = null;
        }

        if (_instance == this)
            _instance = null;
    }

    private TMP_FontAsset FindFont()
    {
        var existing = FindAnyObjectByType<TextMeshProUGUI>();
        return existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
    }

    private void CreateUI()
    {
        _font = FindFont();

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        var panel = CreateRect("MainPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = PanelBg;

        CreateHeader(panel);
        CreateBanner(panel);
        CreateBody(panel);
        CreateFooter(panel);
    }

    private void CreateHeader(RectTransform panel)
    {
        var header = CreateRect("Header", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        header.sizeDelta = new Vector2(0f, HeaderHeight);
        header.anchoredPosition = Vector2.zero;
        var headerImage = header.gameObject.AddComponent<Image>();
        headerImage.color = HeaderBg;
        var dragHandler = header.gameObject.AddComponent<PanelDragHandler>();
        dragHandler.SetTarget(panel, HeaderHeight);

        var title = CreateText("Title", header, "Seth's AI Tools", 30f, Color.white, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.anchorMin = new Vector2(0f, 0f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(24f, 0f);
        title.rectTransform.offsetMax = new Vector2(-320f, 0f);
        title.fontStyle = FontStyles.Bold;

        var version = CreateText("Version", header, GetBuildInfoText(), 12.5f, new Color(0.79f, 0.84f, 0.88f, 1f), TextAlignmentOptions.MidlineRight);
        version.rectTransform.anchorMin = new Vector2(0.50f, 0f);
        version.rectTransform.anchorMax = new Vector2(1f, 1f);
        version.rectTransform.offsetMin = new Vector2(0f, 0f);
        version.rectTransform.offsetMax = new Vector2(-78f, 0f);

        RTWindowChrome.CreateCloseButton(header, DestroySplash, anchoredPosition: new Vector2(-10f, 0f));
    }

    private void CreateBanner(RectTransform panel)
    {
        var bannerFrame = CreateRect("BannerFrame", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        bannerFrame.anchoredPosition = new Vector2(0f, -HeaderHeight);
        bannerFrame.sizeDelta = new Vector2(0f, BannerHeight);

        var banner = CreateRect("Banner", bannerFrame, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        banner.offsetMin = new Vector2(18f, 16f);
        banner.offsetMax = new Vector2(-18f, -16f);
        var bannerImage = banner.gameObject.AddComponent<Image>();
        bannerImage.color = new Color(0.13f, 0.16f, 0.18f, 1f);

        Texture2D texture = Resources.Load<Texture2D>(ArtworkResourcePath);
        if (texture != null)
        {
            _runtimeBannerSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            bannerImage.sprite = _runtimeBannerSprite;
            bannerImage.preserveAspect = true;
            bannerImage.color = Color.white;
        }
        else
        {
            var fallback = CreateText("FallbackText", banner, "AI image, video, workflows, and chat", 20f, Color.white, TextAlignmentOptions.Center);
            fallback.rectTransform.anchorMin = Vector2.zero;
            fallback.rectTransform.anchorMax = Vector2.one;
            fallback.rectTransform.offsetMin = Vector2.zero;
            fallback.rectTransform.offsetMax = Vector2.zero;
        }
    }

    private void CreateBody(RectTransform panel)
    {
        var bodyPanel = CreateRect("BodyPanel", panel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        bodyPanel.offsetMin = new Vector2(24f, FooterHeight + 8f);
        bodyPanel.offsetMax = new Vector2(-24f, -(HeaderHeight + BannerHeight + 4f));
        var bodyImage = bodyPanel.gameObject.AddComponent<Image>();
        bodyImage.color = new Color(1f, 1f, 1f, 0.78f);

        string body =
            "<b><color=#0F899A>First setup</color></b>\n" +
            "Add at least one ComfyUI server with Configuration. Add your LLMs in LLM Settings.\n\n" +
            "<b><color=#0F899A>Best first stop</color></b>\n" +
            "Open AI Chat and ask for images, edits, posters, comics, movies, or workflow help in plain language. It can drive most of the app for you.\n\n" +
            "<b><color=#0F899A>Direct controls</color></b>\n" +
            "You can still use presets and Generate directly when you know exactly what workflow you want.\n\n" +
            "<b><color=#0F899A>Navigation</color></b>\n" +
            "Right-drag pans, mouse wheel zooms, middle-drag moves pics, and U undoes the last image change.\n\n" +
            "<b><color=#0F899A>ComfyUI issues</color></b>\n" +
            "Open the workflow in ComfyUI and install missing nodes or models. Once it works there, it should work here.";

        var bodyText = CreateText("BodyText", bodyPanel, body, 15f, TextDark, TextAlignmentOptions.TopLeft);
        bodyText.rectTransform.anchorMin = Vector2.zero;
        bodyText.rectTransform.anchorMax = Vector2.one;
        bodyText.rectTransform.offsetMin = new Vector2(18f, 14f);
        bodyText.rectTransform.offsetMax = new Vector2(-18f, -14f);
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 11f;
        bodyText.fontSizeMax = 15f;
        bodyText.lineSpacing = 2f;
        bodyText.richText = true;
    }

    private void CreateFooter(RectTransform panel)
    {
        var footer = CreateRect("Footer", panel, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        footer.sizeDelta = new Vector2(0f, FooterHeight);
        footer.anchoredPosition = Vector2.zero;
        var footerImage = footer.gameObject.AddComponent<Image>();
        footerImage.color = new Color(0.86f, 0.88f, 0.90f, 1f);

        float y = 0f;
        CreateButton(footer, "Configuration", "Configuration", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(22f, y), new Vector2(126f, 36f), OpenConfiguration, false);
        CreateButton(footer, "LLMSettings", "LLM Settings", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(154f, y), new Vector2(116f, 36f), OpenLLMSettings, false);
        CreateButton(footer, "AIChat", "AI Chat", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(276f, y), new Vector2(92f, 36f), OpenAIChat, true);
        CreateDontShowAgainToggle(footer, new Vector2(386f, y), new Vector2(132f, 36f));
        CreateButton(footer, "GitHub", "GitHub", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(-150f, y), new Vector2(86f, 36f), OpenGithub, false);
        CreateButton(footer, "Continue", "Continue", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(-24f, y), new Vector2(112f, 36f), DestroySplash, true);
    }

    private Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 size, UnityAction onClick, bool primary)
    {
        var go = new GameObject("Btn_" + name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        var image = go.AddComponent<Image>();
        image.color = primary ? PrimaryButton : SecondaryButton;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.94f, 0.96f, 0.97f, 1f),
            pressedColor = new Color(0.78f, 0.82f, 0.84f, 1f),
            selectedColor = new Color(0.94f, 0.96f, 0.97f, 1f),
            disabledColor = new Color(0.78f, 0.82f, 0.84f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        var text = CreateText("Text", rt, label, 14f, primary ? Color.white : TextDark, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        text.fontStyle = FontStyles.Bold;

        return button;
    }

    private Toggle CreateDontShowAgainToggle(RectTransform parent, Vector2 anchoredPosition, Vector2 size)
    {
        var go = new GameObject("DontShowAgainToggle");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0.001f);

        var box = CreateRect("Box", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        box.anchoredPosition = new Vector2(0f, 0f);
        box.sizeDelta = new Vector2(18f, 18f);
        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.color = SecondaryButton;

        var check = CreateText("Check", box, "X", 14f, TextDark, TextAlignmentOptions.Center);
        check.rectTransform.anchorMin = Vector2.zero;
        check.rectTransform.anchorMax = Vector2.one;
        check.rectTransform.offsetMin = Vector2.zero;
        check.rectTransform.offsetMax = Vector2.zero;
        check.fontStyle = FontStyles.Bold;

        var label = CreateText("Label", rt, "Don't show again", 12.5f, TextDark, TextAlignmentOptions.MidlineLeft);
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.offsetMin = new Vector2(24f, 0f);
        label.rectTransform.offsetMax = Vector2.zero;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = check;
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.isOn = PlayerPrefs.GetInt(PrefsDontShowAgain, 0) != 0;
        toggle.onValueChanged.AddListener(OnDontShowAgainChanged);
        return toggle;
    }

    private TextMeshProUGUI CreateText(string name, RectTransform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    private string GetBuildInfoText()
    {
        string version = Config.Get() != null ? Config.Get().GetVersionString() : Application.version;
        return "V" + version + " | Compiled " + RTBuildInfo.Timestamp;
    }

    private void OpenConfiguration()
    {
        GameLogic.Get()?.OnConfigButton();
    }

    private void OpenLLMSettings()
    {
        LLMSettingsPanel.Show();
    }

    private void OpenAIChat()
    {
        AIChatPanel.Show();
    }

    private void OpenGithub()
    {
        RTUtil.PopupUnblockOpenURL(GithubUrl);
    }

    private void DestroySplash()
    {
        Destroy(gameObject);
    }

    private void OnDontShowAgainChanged(bool value)
    {
        PlayerPrefs.SetInt(PrefsDontShowAgain, value ? 1 : 0);
        PlayerPrefs.Save();
    }
}
