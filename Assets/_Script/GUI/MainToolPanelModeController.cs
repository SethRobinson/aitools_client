using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggles the scene-authored main tool panel groups. The actual layout lives
/// under CompactToolControls and ManualToolControls so it can be edited in Unity.
/// </summary>
public class MainToolPanelModeController : MonoBehaviour
{
    private const string PrefsKey = "aitools_main_tool_panel_manual_mode";
    private const string CompactControlsName = "CompactToolControls";
    private const string ManualControlsName = "ManualToolControls";
    private const string ManualButtonName = "ManualModeButton";
    private const string ShowManualText = "Show Manual Controls";
    private const string ShowSimpleText = "Show Simple Controls";
    private const float CompactPanelPadding = 6f;

    private RectTransform _panel;
    private RectTransform _compactControls;
    private RectTransform _manualControls;
    private RectLayoutSnapshot _manualPanelLayout;
    private TMP_Text _manualButtonText;
    private bool _initialized;
    private bool _isManualMode;

    public bool IsManualMode => _isManualMode;

    public static MainToolPanelModeController Install()
    {
        GameObject toolsCanvas = RTUtil.FindIncludingInactive("ToolsCanvas");
        if (toolsCanvas == null)
        {
            Debug.LogWarning("MainToolPanelModeController: ToolsCanvas not found.");
            return null;
        }

        Transform panelTransform = toolsCanvas.transform.Find("Panel");
        if (panelTransform == null)
        {
            Debug.LogWarning("MainToolPanelModeController: main Panel not found under ToolsCanvas.");
            return null;
        }

        var panel = panelTransform as RectTransform;
        if (panel == null)
        {
            Debug.LogWarning("MainToolPanelModeController: ToolsCanvas/Panel has no RectTransform.");
            return null;
        }

        var controller = panel.gameObject.GetComponent<MainToolPanelModeController>();
        if (controller == null)
            controller = panel.gameObject.AddComponent<MainToolPanelModeController>();

        controller.Initialize(panel);
        return controller;
    }

    public void Initialize(RectTransform panel)
    {
        if (_initialized) return;
        if (panel == null) return;

        _panel = panel;
        _manualPanelLayout = RectLayoutSnapshot.Capture(_panel);
        _compactControls = FindDirectChildRect(panel, CompactControlsName);
        _manualControls = FindDirectChildRect(panel, ManualControlsName);

        if (_compactControls == null || _manualControls == null)
        {
            Debug.LogWarning("MainToolPanelModeController: CompactToolControls/ManualToolControls scene groups are missing.");
            return;
        }

        BindManualButton();

        _initialized = true;
        bool savedManualMode = PlayerPrefs.GetInt(PrefsKey, 0) != 0;
        ApplyMode(savedManualMode, false);
    }

    public void ToggleManualMode()
    {
        SetManualMode(!_isManualMode);
    }

    public void SetManualMode(bool enabled)
    {
        ApplyMode(enabled, true);
    }

    private void ApplyMode(bool manualMode, bool savePreference)
    {
        if (!_initialized) return;

        _isManualMode = manualMode;

        if (savePreference)
        {
            PlayerPrefs.SetInt(PrefsKey, manualMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        _compactControls.gameObject.SetActive(true);
        _manualControls.gameObject.SetActive(manualMode);

        if (manualMode)
            _manualPanelLayout.ApplyTo(_panel);
        else
            FitPanelToCompactControls();

        UpdateManualButtonText();
    }

    private void BindManualButton()
    {
        Transform manualButtonTransform = _compactControls.Find(ManualButtonName);
        if (manualButtonTransform == null)
        {
            Debug.LogWarning("MainToolPanelModeController: ManualModeButton not found under CompactToolControls.");
            return;
        }

        _manualButtonText = manualButtonTransform.GetComponentInChildren<TMP_Text>(true);

        var button = manualButtonTransform.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning("MainToolPanelModeController: ManualModeButton has no Button component.");
            return;
        }

        button.onClick.RemoveListener(ToggleManualMode);
        button.onClick.AddListener(ToggleManualMode);
    }

    private void UpdateManualButtonText()
    {
        if (_manualButtonText != null)
            _manualButtonText.text = _isManualMode ? ShowSimpleText : ShowManualText;
    }

    private void FitPanelToCompactControls()
    {
        if (_panel == null || _compactControls == null) return;

        bool hasBounds = false;
        float right = 0f;
        float bottom = 0f;

        float compactLeft = _compactControls.anchoredPosition.x - _compactControls.sizeDelta.x * _compactControls.pivot.x;
        float compactTop = -_compactControls.anchoredPosition.y - _compactControls.sizeDelta.y * (1f - _compactControls.pivot.y);

        foreach (Transform child in _compactControls)
        {
            if (!child.gameObject.activeSelf) continue;
            var childRect = child as RectTransform;
            if (childRect == null) continue;

            float childLeft = compactLeft + childRect.anchoredPosition.x - childRect.sizeDelta.x * childRect.pivot.x;
            float childTop = compactTop - childRect.anchoredPosition.y - childRect.sizeDelta.y * (1f - childRect.pivot.y);
            float childRight = childLeft + childRect.sizeDelta.x;
            float childBottom = childTop + childRect.sizeDelta.y;

            right = hasBounds ? Mathf.Max(right, childRight) : childRight;
            bottom = hasBounds ? Mathf.Max(bottom, childBottom) : childBottom;
            hasBounds = true;
        }

        if (!hasBounds)
        {
            right = _compactControls.anchoredPosition.x + _compactControls.sizeDelta.x * (1f - _compactControls.pivot.x);
            bottom = -_compactControls.anchoredPosition.y + _compactControls.sizeDelta.y * _compactControls.pivot.y;
        }

        float width = Mathf.Ceil(Mathf.Max(right + CompactPanelPadding, 1f));
        float height = Mathf.Ceil(Mathf.Max(bottom + CompactPanelPadding, 1f));

        _panel.anchorMin = new Vector2(0f, 1f);
        _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(width, height);
        _panel.anchoredPosition = new Vector2(width * 0.5f, -height * 0.5f);
    }

    private static RectTransform FindDirectChildRect(RectTransform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        Transform child = parent.Find(childName);
        return child as RectTransform;
    }

    private struct RectLayoutSnapshot
    {
        private Vector2 _anchorMin;
        private Vector2 _anchorMax;
        private Vector2 _anchoredPosition;
        private Vector2 _sizeDelta;
        private Vector2 _pivot;

        public static RectLayoutSnapshot Capture(RectTransform rect)
        {
            return new RectLayoutSnapshot
            {
                _anchorMin = rect.anchorMin,
                _anchorMax = rect.anchorMax,
                _anchoredPosition = rect.anchoredPosition,
                _sizeDelta = rect.sizeDelta,
                _pivot = rect.pivot
            };
        }

        public void ApplyTo(RectTransform rect)
        {
            if (rect == null) return;

            rect.anchorMin = _anchorMin;
            rect.anchorMax = _anchorMax;
            rect.pivot = _pivot;
            rect.sizeDelta = _sizeDelta;
            rect.anchoredPosition = _anchoredPosition;
        }
    }
}
