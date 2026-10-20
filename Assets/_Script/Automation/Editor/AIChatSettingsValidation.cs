using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AITools.AIChat.Skills;
using AITools.AIChat.UI;
using SimpleJSON;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated settings-content checks. No desktop input, captures or real prompt-file access.</summary>
internal static class AIChatSettingsValidation
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Fields).GetValue(target);

    public static string Run()
    {
        var result = new JSONObject();
        var checks = new JSONArray();
        result["checks"] = checks;
        string fixtures = Path.Combine(Path.GetDirectoryName(Application.dataPath), "build",
            "aichat-settings-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        result["fixtures"] = fixtures;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
        }

        // SaveChanges persists these keys, even when their values were not edited.
        // Preserve missing keys too, and keep all values out of the validation report.
        string[] intKeys = {
            "aichat_keep_last_n_media", "aichat_attachment_max_edge", "aichat_compact_keep_n",
            "aichat_image_context_limit", "aichat_keep_old_tool_calls_in_prompt",
            "aichat_auto_caption_generated_images", "aichat_show_debug_stuff",
            "aichat_normalize_clip_audio", "aichat_allow_actions_in_think_blocks"
        };
        var previousInts = intKeys.ToDictionary(k => k, k => PlayerPrefs.GetInt(k));
        var existingKeys = new HashSet<string>(intKeys.Where(PlayerPrefs.HasKey));
        const string prefixKey = "aichat_preset_prefix";
        bool hadPrefix = PlayerPrefs.HasKey(prefixKey);
        string previousPrefix = PlayerPrefs.GetString(prefixKey);
        string previousPostMessage = AIChatPanel.GetUserPostMessage();
        GameObject root = null;
        try
        {
            var manager = new SkillManager();
            // Redirect every prompt path before Reload. The user's active prompt,
            // experimental overrides and skills are never read or written.
            foreach (string property in new[] { "PrePromptPath", "MainPromptPath", "PostPromptPath", "CaptionPromptPath",
                "TestPrePromptPath", "TestMainPromptPath", "TestPostPromptPath", "TestCaptionPromptPath" })
            {
                string filename = property.StartsWith("Test", StringComparison.Ordinal)
                    ? "override_" + property.Substring(4) + ".txt" : property + ".txt";
                typeof(SkillManager).GetProperty(property).SetValue(manager, Path.Combine(fixtures, filename));
            }
            string skills = Path.Combine(fixtures, "skills");
            Directory.CreateDirectory(skills);
            typeof(SkillManager).GetProperty("SkillsDirectory").SetValue(manager, skills);
            File.WriteAllText(Path.Combine(skills, "fixture.md"),
                "---\nid: fixture\nsummary: Synthetic settings fixture.\ninputs: none\n---\nFixture body.");
            File.WriteAllText(manager.MainPromptPath, "Production fixture.");
            File.WriteAllText(manager.TestMainPromptPath, "Override fixture.");
            PlayerPrefs.SetString(prefixKey, "test_");
            manager.Reload();

            // Disabled world-space canvas: exercise real TMP/layout components without
            // displaying a window, taking screenshots, or touching the user's panels.
            root = new GameObject("AIChatSettingsValidation", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.enabled = false;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root.transform, false);
            content.sizeDelta = new Vector2(900, 660);
            var actions = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actions.SetParent(root.transform, false);
            actions.sizeDelta = new Vector2(900, 48);
            int saved = 0;
            var page = AIChatSettingsPanel.CreateEmbedded(content, actions, TMP_Settings.defaultFontAsset, manager, () => saved++);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();

            Check(page.GetComponentsInChildren<Canvas>(true).Length == 0, "Page uses its host canvas without a second window");
            Check(page.transform.parent == content, "Entire settings page is embedded in host content");
            var prompt = Field<TMP_InputField>(page, "_mainPromptField");
            Check(prompt.text == "Override fixture.", "Active prompt override loads from isolated fixture");
            Check(prompt.verticalScrollbar != null && prompt.textViewport != null, "Prompt editor retains its scrollable viewport");
            Check(Field<RectTransform>(page, "_skillsContent").childCount == 1, "Loaded skills list is present");

            foreach (string name in new[] { "_mainPromptField", "_maxEdgeField", "_keepLastNField", "_presetPrefixField",
                "_compactKeepNField", "_imageContextLimitField", "_userPostMessageField" })
            {
                var input = Field<TMP_InputField>(page, name);
                Check(input != null && input.transform.IsChildOf(content), name + " is available in the embedded page");
                var corners = new Vector3[4];
                input.GetComponent<RectTransform>().GetWorldCorners(corners);
                Rect bounds = content.rect;
                Check(corners.All(c => {
                    Vector3 local = content.InverseTransformPoint(c);
                    return local.x >= bounds.xMin - 1 && local.x <= bounds.xMax + 1
                        && local.y >= bounds.yMin - 1 && local.y <= bounds.yMax + 1;
                }), name + " fits within the content area");
            }
            foreach (string name in new[] { "_keepOldToolCallsToggle", "_autoCaptionGeneratedImagesToggle",
                "_showDebugStuffToggle", "_normalizeClipAudioToggle", "_allowActionsInThinkBlocksToggle" })
                Check(Field<Toggle>(page, name) != null, name + " is available");
            foreach (string name in new[] { "Reload skills", "Open aichat folder", "Save & Close" })
                Check(actions.GetComponentsInChildren<Button>().Any(b => b.name == "Btn_" + name), name + " uses the host action bar");
            Check(page.GetComponentsInChildren<Button>().Count(b => b.name == "Btn_Truncate" || b.name == "Btn_Summarize") == 2,
                "Both Compact actions remain available");

            DateTime untouched = File.GetLastWriteTimeUtc(manager.TestMainPromptPath);
            Check(page.SaveChanges() && saved == 1, "Saving notifies the host");
            Check(File.GetLastWriteTimeUtc(manager.TestMainPromptPath) == untouched, "Opening and saving leaves an unchanged prompt file untouched");
            prompt.text = "Updated override fixture.";
            Field<TMP_InputField>(page, "_presetPrefixField").text = "fixture_";
            Field<TMP_InputField>(page, "_userPostMessageField").text = "Session fixture.";
            Field<Toggle>(page, "_allowActionsInThinkBlocksToggle").isOn = true;
            actions.GetComponentsInChildren<Button>().Single(b => b.name == "Btn_Reload skills").onClick.Invoke();
            Check(File.ReadAllText(manager.TestMainPromptPath) == "Updated override fixture.", "Reload saves a pending prompt edit to its original loaded path");
            Check(File.ReadAllText(manager.MainPromptPath) == "Production fixture.", "Changing prefix does not overwrite the other prompt");
            Check(prompt.text == "Production fixture.", "Reload displays the newly selected prompt after a prefix change");
            Check(AIChatPanel.GetUserPostMessage() == "Session fixture." && AIChatPanel.GetAllowActionsInThinkBlocks(),
                "Session text and thinking-controls preference survive saving");
            result["ok"] = true;
        }
        catch (Exception ex)
        {
            result["ok"] = false;
            result["error"] = ex.Message;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (string key in intKeys)
            {
                if (existingKeys.Contains(key)) PlayerPrefs.SetInt(key, previousInts[key]);
                else PlayerPrefs.DeleteKey(key);
            }
            if (hadPrefix) PlayerPrefs.SetString(prefixKey, previousPrefix);
            else PlayerPrefs.DeleteKey(prefixKey);
            AIChatPanel.SetUserPostMessage(previousPostMessage);
            PlayerPrefs.Save();
        }
        return result.ToString();
    }
}
