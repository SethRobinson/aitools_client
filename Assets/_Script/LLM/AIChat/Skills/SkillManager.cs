using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// Loads skill definitions from <c>aichat/skills/*.md</c> at the project root.
    /// Each skill has a short YAML-ish front matter (id, summary, inputs) plus a free-
    /// form markdown body. By default only the per-skill summaries are folded into the
    /// LLM's system prompt; the full body is loaded on demand via the built-in
    /// <c>read_skill</c> action.
    ///
    /// Plain class (not a MonoBehaviour) so it can be created on-demand from any panel
    /// without scene-side wiring. Lifetime is tied to whoever owns it (e.g. AIChatPanel).
    /// </summary>
    public class SkillManager
    {
        private readonly List<Skill> _skills = new List<Skill>();
        private readonly Dictionary<string, Skill> _byId = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _lastReloadAddedSkillIds = new List<string>();
        private readonly List<string> _lastReloadRemovedSkillIds = new List<string>();
        private bool _hasCompletedReload;

        public event Action<IReadOnlyList<string>, IReadOnlyList<string>> OnSkillListChanged;

        /// <summary>
        /// Absolute path to the aichat/skills/ folder this manager loads from.
        /// </summary>
        public string SkillsDirectory { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/pre_prompt.txt file (prepended at the START of
        /// the system prompt before main_prompt.txt). Lets the user add top-priority
        /// framing without editing the main prompt or any skill file.
        /// </summary>
        public string PrePromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/test_pre_prompt.txt override. While the preset
        /// prefix is "test_" and this file exists it COMPLETELY REPLACES
        /// <see cref="PrePromptPath"/>'s contents. Falls back to pre_prompt.txt otherwise.
        /// </summary>
        public string TestPrePromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/main_prompt.txt file.
        /// </summary>
        public string MainPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/test_main_prompt.txt override. Used in place of
        /// <see cref="MainPromptPath"/> for BOTH loading and saving while the preset
        /// prefix is "test_", so the production system prompt can be experimented with
        /// as part of the "test_" preset family without ever clobbering main_prompt.txt.
        /// </summary>
        public string TestMainPromptPath { get; private set; }

        /// <summary>
        /// The main-prompt file currently active for editing and saving:
        /// test_main_prompt.txt while the preset prefix is "test_", otherwise the normal
        /// main_prompt.txt. The settings panel writes here so a test prompt is never
        /// saved over the real one.
        /// </summary>
        public string ActiveMainPromptPath =>
            IsTestPresetPrefixActive() ? TestMainPromptPath : MainPromptPath;

        /// <summary>
        /// Absolute path to the aichat/post_prompt.txt file (appended at the END of the
        /// system prompt - the "last word" the LLM reads). Lets the user tack on quick
        /// behavioral tweaks without editing main_prompt or any skill file.
        /// </summary>
        public string PostPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/test_post_prompt.txt override. While the preset
        /// prefix is "test_" and this file exists it COMPLETELY REPLACES
        /// <see cref="PostPromptPath"/>'s contents - useful for experimenting as part of
        /// the "test_" family without touching the real post_prompt.txt. Falls back to
        /// post_prompt.txt otherwise.
        /// </summary>
        public string TestPostPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/caption_prompt.txt file. This is the user-side
        /// prompt the host sends with each one-shot vision call when captioning a
        /// chat image. Editable without recompiling the client.
        /// </summary>
        public string CaptionPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/test_caption_prompt.txt override. While the preset
        /// prefix is "test_" and this file exists it COMPLETELY REPLACES
        /// <see cref="CaptionPromptPath"/>'s contents - useful for experimenting with
        /// stricter / looser caption instructions (e.g. enabling explicit-content fields)
        /// without touching the default caption_prompt.txt. Falls back to caption_prompt.txt
        /// otherwise.
        /// </summary>
        public string TestCaptionPromptPath { get; private set; }

        /// <summary>
        /// Cached body of either aichat/test_pre_prompt.txt (if the preset prefix is
        /// "test_" and it exists) or aichat/pre_prompt.txt. Empty when neither file exists.
        /// </summary>
        public string PrePrompt { get; private set; } = "";

        /// <summary>
        /// True if the test override (aichat/test_pre_prompt.txt) was actually used
        /// for the most recent <see cref="Reload"/>.
        /// </summary>
        public bool PrePromptIsTestOverride { get; private set; }

        /// <summary>
        /// Cached body of either aichat/test_main_prompt.txt (if the preset prefix is
        /// "test_" and that file exists) or aichat/main_prompt.txt (refreshed each
        /// <see cref="Reload"/> call).
        /// </summary>
        public string MainPrompt { get; private set; } = "";

        /// <summary>
        /// True if the test override (aichat/test_main_prompt.txt) was actually read for
        /// the most recent <see cref="Reload"/> - i.e. the preset prefix is "test_" and
        /// the file exists. Surfaced so the chat can flag that an experimental system
        /// prompt is live, same as <see cref="PostPromptIsTestOverride"/>.
        /// </summary>
        public bool MainPromptIsTestOverride { get; private set; }

        /// <summary>
        /// Cached body of either aichat/test_post_prompt.txt (if the preset prefix is
        /// "test_" and it exists) or aichat/post_prompt.txt. Empty when neither file exists.
        /// </summary>
        public string PostPrompt { get; private set; } = "";

        /// <summary>
        /// True if the test override (aichat/test_post_prompt.txt) was actually used
        /// for the most recent <see cref="Reload"/>. Surfaced so the panel can show a
        /// visible "TEST PROMPT ACTIVE" indicator and the user doesn't forget.
        /// </summary>
        public bool PostPromptIsTestOverride { get; private set; }

        /// <summary>
        /// Cached body of either aichat/test_caption_prompt.txt (if the preset prefix is
        /// "test_" and it exists) or aichat/caption_prompt.txt. Empty when neither file
        /// exists - the host then falls back to a hardcoded default so captioning still works.
        /// </summary>
        public string CaptionPrompt { get; private set; } = "";

        /// <summary>
        /// True if the test caption override was actually used for the most recent
        /// <see cref="Reload"/>. The host can surface this in logs or UI so it's
        /// obvious which prompt is producing the captions.
        /// </summary>
        public bool CaptionPromptIsTestOverride { get; private set; }

        public SkillManager()
        {
            string root = GetAIChatRoot();
            SkillsDirectory = Path.Combine(root, "skills");
            PrePromptPath = Path.Combine(root, "pre_prompt.txt");
            TestPrePromptPath = Path.Combine(root, "test_pre_prompt.txt");
            MainPromptPath = Path.Combine(root, "main_prompt.txt");
            TestMainPromptPath = Path.Combine(root, "test_main_prompt.txt");
            PostPromptPath = Path.Combine(root, "post_prompt.txt");
            TestPostPromptPath = Path.Combine(root, "test_post_prompt.txt");
            CaptionPromptPath = Path.Combine(root, "caption_prompt.txt");
            TestCaptionPromptPath = Path.Combine(root, "test_caption_prompt.txt");
        }

        public IReadOnlyList<Skill> GetSkills() => _skills;

        public IReadOnlyList<string> LastReloadAddedSkillIds => _lastReloadAddedSkillIds;
        public IReadOnlyList<string> LastReloadRemovedSkillIds => _lastReloadRemovedSkillIds;
        public bool LastReloadHadSkillListChange =>
            _lastReloadAddedSkillIds.Count > 0 || _lastReloadRemovedSkillIds.Count > 0;

        public Skill GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _byId.TryGetValue(id, out var skill);
            return skill;
        }

        public bool HasSkill(string id) => GetById(id) != null;

        /// <summary>
        /// (Re)load prompt files and every <c>*.md</c> under aichat/skills/. Logs (but
        /// does not throw) on individual file errors so one bad prompt/skill doesn't
        /// kill the rest. Safe to call repeatedly.
        /// </summary>
        public void Reload()
        {
            var previousSkillIds = new HashSet<string>(_byId.Keys, StringComparer.OrdinalIgnoreCase);
            bool shouldReportSkillListChanges = _hasCompletedReload;
            _lastReloadAddedSkillIds.Clear();
            _lastReloadRemovedSkillIds.Clear();

            _skills.Clear();
            _byId.Clear();
            PrePrompt = "";
            PrePromptIsTestOverride = false;
            MainPrompt = "";
            MainPromptIsTestOverride = false;
            PostPrompt = "";
            PostPromptIsTestOverride = false;
            CaptionPrompt = "";
            CaptionPromptIsTestOverride = false;

            EnsureDirectoryExists();

            // Pre-prompt. Same test_ override behavior as post_prompt, but inserted at
            // the very top of the stable system prompt before main_prompt.txt.
            try
            {
                if (IsTestPresetPrefixActive() && File.Exists(TestPrePromptPath))
                {
                    PrePrompt = File.ReadAllText(TestPrePromptPath);
                    PrePromptIsTestOverride = true;
                }
                else if (File.Exists(PrePromptPath))
                {
                    PrePrompt = File.ReadAllText(PrePromptPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to read pre_prompt: " + ex.Message);
            }

            // Main prompt. While the preset prefix is "test_" we prefer test_main_prompt.txt
            // so the whole "test_" family (presets + system prompt) swaps in as a unit.
            // Falls back to main_prompt.txt when the test file doesn't exist yet, so the
            // chat always has a system prompt and the editor opens seeded from the real one.
            try
            {
                if (IsTestPresetPrefixActive() && File.Exists(TestMainPromptPath))
                {
                    MainPrompt = File.ReadAllText(TestMainPromptPath);
                    MainPromptIsTestOverride = true;
                }
                else if (File.Exists(MainPromptPath))
                {
                    MainPrompt = File.ReadAllText(MainPromptPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to read main_prompt: " + ex.Message);
            }

            // Post-prompt: while the preset prefix is "test_" the test override wins (so the
            // user can experiment with prompt tweaks as part of the "test_" family without
            // losing their real one). Falls back to post_prompt.txt otherwise.
            try
            {
                if (IsTestPresetPrefixActive() && File.Exists(TestPostPromptPath))
                {
                    PostPrompt = File.ReadAllText(TestPostPromptPath);
                    PostPromptIsTestOverride = true;
                }
                else if (File.Exists(PostPromptPath))
                {
                    PostPrompt = File.ReadAllText(PostPromptPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to read post_prompt: " + ex.Message);
            }

            // Caption prompt: same "test_" prefix gating as post_prompt. Empty body when
            // neither file is present is a valid state - the host has a hardcoded fallback
            // so captioning still works after a fresh checkout.
            try
            {
                if (IsTestPresetPrefixActive() && File.Exists(TestCaptionPromptPath))
                {
                    CaptionPrompt = File.ReadAllText(TestCaptionPromptPath);
                    CaptionPromptIsTestOverride = true;
                }
                else if (File.Exists(CaptionPromptPath))
                {
                    CaptionPrompt = File.ReadAllText(CaptionPromptPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to read caption_prompt: " + ex.Message);
            }

            try
            {
                if (Directory.Exists(SkillsDirectory))
                {
                    string[] files = Directory.GetFiles(SkillsDirectory, "*.md");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in files)
                    {
                        try
                        {
                            var skill = LoadSkillFile(file);
                            if (skill == null || !skill.IsValid) continue;
                            if (_byId.ContainsKey(skill.Id))
                            {
                                Debug.LogWarning($"SkillManager: duplicate skill id '{skill.Id}' in {file} (already loaded). Skipping.");
                                continue;
                            }
                            _skills.Add(skill);
                            _byId[skill.Id] = skill;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"SkillManager: failed to load skill '{file}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to enumerate skills: " + ex.Message);
            }

            RecordSkillListChanges(previousSkillIds);
            _hasCompletedReload = true;
            if (shouldReportSkillListChanges && LastReloadHadSkillListChange)
                OnSkillListChanged?.Invoke(_lastReloadAddedSkillIds.AsReadOnly(), _lastReloadRemovedSkillIds.AsReadOnly());
        }

        /// <summary>
        /// Returns the section of system-prompt text that lists every loaded skill with
        /// its summary AND a copy-pasteable Template line so the LLM has the exact call
        /// syntax (with required attributes already in place) without having to call
        /// read_skill first. Bodies are still loaded on demand via the read_skill action.
        /// </summary>
        public string BuildSkillSummariesBlock()
        {
            if (_skills.Count == 0)
                return "SKILLS: (none loaded)\n";

            var sb = new StringBuilder();
            sb.AppendLine("SKILLS (copy the Template line exactly, change the prompt etc; call read_skill for more detail; read_skill auto-continues after loading):");
            foreach (var s in _skills)
            {
                sb.Append("- ").Append(s.Id).Append(": ").AppendLine(s.Summary);
                if (!string.IsNullOrEmpty(s.Template))
                    sb.Append("    Template: ").AppendLine(s.Template);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns full skill bodies for skills that opted into same-turn preload and
        /// whose trigger terms match the latest user message. Trigger terms live in
        /// skill front matter so adding a new autoloaded skill does not require code.
        /// </summary>
        public string BuildAutoloadSkillBodiesBlock(string latestUserMessage)
        {
            return BuildSkillReferenceMaterialBlock(GetAutoloadSkillsForMessage(latestUserMessage));
        }

        public string BuildSkillReferenceMaterialBlock(IEnumerable<Skill> skills)
        {
            var sb = new StringBuilder();
            bool wroteAny = false;
            sb.AppendLine("AUTO-LOADED SKILL REFERENCE MATERIAL:");
            sb.AppendLine("The following skill bodies were loaded because a trigger word appeared in the conversation. Use them directly in this and later replies; do NOT call read_skill for these skills.");
            foreach (var skill in skills ?? Array.Empty<Skill>())
            {
                if (skill == null || string.IsNullOrEmpty(skill.Id))
                    continue;
                wroteAny = true;
                sb.AppendLine();
                sb.Append("## ").AppendLine(skill.Id);
                // Skip the Summary: and Template: lines here - the SKILLS section in the
                // base system prompt already shows both, so re-emitting them wastes ~600
                // tokens per auto-loaded skill (a measurable chunk of the context window
                // for a 27B model). The body below is the only non-duplicate content.
                sb.AppendLine();
                string body = ApplyPresetPrefix(skill.RawMarkdown ?? "");
                sb.AppendLine(body.TrimEnd());
            }
            sb.AppendLine();
            return wroteAny ? sb.ToString() : "";
        }

        public List<Skill> GetAutoloadSkillsForMessage(string latestUserMessage)
        {
            var result = new List<Skill>();
            if (string.IsNullOrWhiteSpace(latestUserMessage))
                return result;

            foreach (var skill in _skills)
            {
                if (skill == null || !skill.Autoload || skill.Triggers == null || skill.Triggers.Count == 0)
                    continue;

                bool excluded = false;
                if (skill.ExcludeTriggers != null)
                {
                    foreach (string exclude in skill.ExcludeTriggers)
                    {
                        if (TriggerMatches(latestUserMessage, exclude))
                        {
                            excluded = true;
                            break;
                        }
                    }
                }
                if (excluded)
                    continue;

                foreach (string trigger in skill.Triggers)
                {
                    if (TriggerMatches(latestUserMessage, trigger))
                    {
                        result.Add(skill);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// PlayerPrefs key for the global preset prefix. Empty string = no prefix.
        /// </summary>
        public const string PresetPrefixPrefsKey = "aichat_preset_prefix";

        /// <summary>
        /// True when the global preset prefix is exactly "test_" (case-insensitive) - the
        /// marker that swaps in the parallel "test_" family of presets and prompt files.
        /// </summary>
        public static bool IsTestPresetPrefixActive()
        {
            string prefix = PlayerPrefs.GetString(PresetPrefixPrefsKey, "");
            return prefix != null && prefix.Trim().Equals("test_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A short, human-readable summary of the active preset prefix and exactly which
        /// prompt files are in effect after the most recent <see cref="Reload"/> (resolving
        /// each test_ override vs its fallback). Intended purely for a local UI notice so
        /// the user can confirm a renamed / "test_" prompt was actually picked up - it is
        /// never added to chat history or sent to an LLM.
        /// </summary>
        public string BuildActivePromptStatus()
        {
            string prefix = PlayerPrefs.GetString(PresetPrefixPrefsKey, "");
            if (string.IsNullOrEmpty(prefix))
                return "Preset prefix: none - using the default prompt files (pre_prompt.txt, main_prompt.txt, etc.).";

            string preFile = PrePromptIsTestOverride
                ? Path.GetFileName(TestPrePromptPath)
                : (string.IsNullOrEmpty(PrePrompt) ? "(none)" : Path.GetFileName(PrePromptPath));
            string mainFile = MainPromptIsTestOverride
                ? Path.GetFileName(TestMainPromptPath)
                : (File.Exists(MainPromptPath) ? Path.GetFileName(MainPromptPath) : "(none)");
            string postFile = PostPromptIsTestOverride
                ? Path.GetFileName(TestPostPromptPath)
                : (string.IsNullOrEmpty(PostPrompt) ? "(none)" : Path.GetFileName(PostPromptPath));
            string captionFile = CaptionPromptIsTestOverride
                ? Path.GetFileName(TestCaptionPromptPath)
                : (string.IsNullOrEmpty(CaptionPrompt) ? "built-in default" : Path.GetFileName(CaptionPromptPath));

            // No angle-bracket placeholder here - the bubble renders through TMP rich
            // text, which would swallow a literal <name> as an unknown tag.
            return $"Preset prefix '{prefix}' active - preset names are prefixed with '{prefix}'. " +
                   $"Prompt files in use: pre={preFile}, system={mainFile}, post={postFile}, caption={captionFile}.";
        }

        // <c>{{Preset Name.txt}}</c> sentinel - inner text excludes braces. Compiled
        // once for the lifetime of the AppDomain.
        private static readonly Regex PresetTokenRx = new Regex(
            @"\{\{([^{}]+)\}\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Replaces every <c>{{Preset Name.txt}}</c> sentinel with
        /// <c>&lt;prefix&gt;Preset Name.txt</c>, where the prefix is read live from
        /// <see cref="PresetPrefixPrefsKey"/>. Empty prefix is a strip-only pass - the
        /// sentinel itself is always removed so raw <c>{{...}}</c> never reaches the LLM.
        /// </summary>
        public static string ApplyPresetPrefix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string prefix = PlayerPrefs.GetString(PresetPrefixPrefsKey, "");
            return PresetTokenRx.Replace(text, m => prefix + m.Groups[1].Value);
        }

        // ---------- Internals ----------

        private void RecordSkillListChanges(HashSet<string> previousSkillIds)
        {
            var currentSkillIds = new HashSet<string>(_byId.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var id in currentSkillIds)
            {
                if (!previousSkillIds.Contains(id))
                    _lastReloadAddedSkillIds.Add(id);
            }

            foreach (var id in previousSkillIds)
            {
                if (!currentSkillIds.Contains(id))
                    _lastReloadRemovedSkillIds.Add(id);
            }

            _lastReloadAddedSkillIds.Sort(StringComparer.OrdinalIgnoreCase);
            _lastReloadRemovedSkillIds.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Locates the aichat/ folder relative to the running app. Editor and standalone
        /// builds both put Application.dataPath at "...&lt;project&gt;/Assets" or
        /// "...&lt;app&gt;/&lt;Game&gt;_Data", so the parent of dataPath is the right root
        /// in both cases (matches Presets/, AIGuide/, etc.).
        /// </summary>
        private static string GetAIChatRoot()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "aichat");
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(SkillsDirectory))
                    Directory.CreateDirectory(SkillsDirectory);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: could not create aichat/skills directory: " + ex.Message);
            }
        }

        private static Skill LoadSkillFile(string filePath)
        {
            string text = File.ReadAllText(filePath);
            if (string.IsNullOrEmpty(text)) return null;

            string id = "";
            string summary = "";
            string template = "";
            SkillInputs inputs = SkillInputs.None;
            List<string> triggers = new List<string>();
            List<string> excludeTriggers = new List<string>();
            bool autoload = false;
            string body = text;

            // Front matter: --- ... ---  (only if file starts with ---)
            if (text.StartsWith("---"))
            {
                int endMarker = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (endMarker > 0)
                {
                    string fm = text.Substring(3, endMarker - 3);
                    body = text.Substring(endMarker + 4).TrimStart('\r', '\n');

                    foreach (var rawLine in fm.Split('\n'))
                    {
                        string line = rawLine.Trim().TrimEnd('\r');
                        if (line.Length == 0 || line.StartsWith("#")) continue;

                        // 'template:' values contain '"' chars and many embedded ':' (well,
                        // one in xmlns-style if we ever add them). We only split on the FIRST
                        // colon so the value can contain anything afterwards verbatim.
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                        string value = line.Substring(colon + 1).Trim();
                        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                            value = value.Substring(1, value.Length - 2);

                        switch (key)
                        {
                            case "id":       id = value; break;
                            case "summary":  summary = value; break;
                            case "inputs":   inputs = ParseInputs(value); break;
                            case "template": template = value; break;
                            case "trigger":
                            case "triggers": triggers = ParseTriggerList(value); break;
                            case "exclude_trigger":
                            case "exclude_triggers":
                            case "autoload_exclude":
                            case "autoload_excludes": excludeTriggers = ParseTriggerList(value); break;
                            case "autoload": autoload = ParseBool(value); break;
                        }
                    }
                }
            }

            // Sensible fallbacks so a malformed file still works.
            if (string.IsNullOrEmpty(id))
                id = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(summary))
                summary = "(no summary - read the skill file for details)";

            return new Skill(id, summary, inputs, template, triggers, excludeTriggers, autoload, body, filePath);
        }

        private static List<string> ParseTriggerList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value))
                return result;

            foreach (string raw in value.Split(','))
            {
                string t = raw.Trim();
                if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2)
                    t = t.Substring(1, t.Length - 2).Trim();
                if (t.Length > 0)
                    result.Add(t);
            }
            return result;
        }

        private static bool ParseBool(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return v == "true" || v == "1" || v == "yes" || v == "y" || v == "on";
        }

        private static bool TriggerMatches(string text, string trigger)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(trigger))
                return false;

            string pattern = @"(?<![A-Za-z0-9])" + Regex.Escape(trigger.Trim()) + @"(?![A-Za-z0-9])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static SkillInputs ParseInputs(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "attachment":          return SkillInputs.Attachment;
                case "attachment_optional": return SkillInputs.AttachmentOptional;
                case "":
                case "none":
                default:                    return SkillInputs.None;
            }
        }
    }
}
