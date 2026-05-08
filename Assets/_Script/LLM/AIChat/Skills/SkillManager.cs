using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        /// <summary>
        /// Absolute path to the aichat/skills/ folder this manager loads from.
        /// </summary>
        public string SkillsDirectory { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/main_prompt.txt file.
        /// </summary>
        public string MainPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/post_prompt.txt file (appended at the END of the
        /// system prompt - the "last word" the LLM reads). Lets the user tack on quick
        /// behavioral tweaks without editing main_prompt or any skill file.
        /// </summary>
        public string PostPromptPath { get; private set; }

        /// <summary>
        /// Absolute path to the aichat/test_post_prompt.txt override. If this file exists
        /// it COMPLETELY REPLACES <see cref="PostPromptPath"/>'s contents - useful for
        /// experimenting without touching the real post_prompt.txt.
        /// </summary>
        public string TestPostPromptPath { get; private set; }

        /// <summary>
        /// Cached body of aichat/main_prompt.txt (refreshed each <see cref="Reload"/> call).
        /// </summary>
        public string MainPrompt { get; private set; } = "";

        /// <summary>
        /// Cached body of either aichat/test_post_prompt.txt (if it exists) or
        /// aichat/post_prompt.txt. Empty when neither file exists.
        /// </summary>
        public string PostPrompt { get; private set; } = "";

        /// <summary>
        /// True if the test override (aichat/test_post_prompt.txt) was actually used
        /// for the most recent <see cref="Reload"/>. Surfaced so the panel can show a
        /// visible "TEST PROMPT ACTIVE" indicator and the user doesn't forget.
        /// </summary>
        public bool PostPromptIsTestOverride { get; private set; }

        public SkillManager()
        {
            string root = GetAIChatRoot();
            SkillsDirectory = Path.Combine(root, "skills");
            MainPromptPath = Path.Combine(root, "main_prompt.txt");
            PostPromptPath = Path.Combine(root, "post_prompt.txt");
            TestPostPromptPath = Path.Combine(root, "test_post_prompt.txt");
        }

        public IReadOnlyList<Skill> GetSkills() => _skills;

        public Skill GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _byId.TryGetValue(id, out var skill);
            return skill;
        }

        public bool HasSkill(string id) => GetById(id) != null;

        /// <summary>
        /// (Re)load main_prompt.txt and every <c>*.md</c> under aichat/skills/. Logs (but
        /// does not throw) on individual file errors so one bad skill doesn't kill the
        /// rest. Safe to call repeatedly.
        /// </summary>
        public void Reload()
        {
            _skills.Clear();
            _byId.Clear();
            MainPrompt = "";
            PostPrompt = "";
            PostPromptIsTestOverride = false;

            EnsureDirectoryExists();

            try
            {
                if (File.Exists(MainPromptPath))
                    MainPrompt = File.ReadAllText(MainPromptPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillManager: failed to read main_prompt.txt: " + ex.Message);
            }

            // Post-prompt: test override takes precedence over the real file when present
            // so the user can experiment with prompt tweaks without losing their main one.
            try
            {
                if (File.Exists(TestPostPromptPath))
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
            sb.AppendLine("SKILLS (copy the Template line exactly, change the prompt etc; call read_skill for more detail):");
            foreach (var s in _skills)
            {
                sb.Append("- ").Append(s.Id).Append(": ").AppendLine(s.Summary);
                if (!string.IsNullOrEmpty(s.Template))
                    sb.Append("    Template: ").AppendLine(s.Template);
            }
            return sb.ToString();
        }

        // ---------- Internals ----------

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
                        }
                    }
                }
            }

            // Sensible fallbacks so a malformed file still works.
            if (string.IsNullOrEmpty(id))
                id = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(summary))
                summary = "(no summary - read the skill file for details)";

            return new Skill(id, summary, inputs, template, body, filePath);
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
