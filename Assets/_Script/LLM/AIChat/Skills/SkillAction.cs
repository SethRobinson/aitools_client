using System.Collections.Generic;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// One parsed <c>&lt;aitools_action ... /&gt;</c> tag from an LLM stream. Built by
    /// <see cref="SkillActionParser"/>; consumed by the executor.
    ///
    /// Common attributes are surfaced as direct properties for convenience; everything
    /// else is also available in <see cref="Args"/> for forward-compat with future
    /// skills that need extra fields without parser changes.
    /// </summary>
    public class SkillAction
    {
        public string SkillId;

        /// <summary>
        /// All attributes from the XML tag, lowercase keys. Includes the same values that
        /// are surfaced as named properties (e.g. "preset", "prompt", "attachment", "gpu",
        /// "llm", "id"). Useful for forward-compat / debugging.
        /// </summary>
        public readonly Dictionary<string, string> Args = new Dictionary<string, string>();

        public string Preset => GetArg("preset");
        public string Prompt => GetArg("prompt");
        public string NegativePrompt => GetArg("negative_prompt");

        /// <summary>1-based attachment index from the LLM, or null if unspecified.</summary>
        public int? AttachmentIndex
        {
            get
            {
                if (!Args.TryGetValue("attachment", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>
        /// 1-based chat-image bubble index from the LLM (chat_image="N"), or null.
        /// Lets img2img / img2vid skills reuse a previously-generated image still
        /// visible in the chat as their input - "edit the image you just made".
        /// </summary>
        public int? ChatImageIndex
        {
            get
            {
                if (!Args.TryGetValue("chat_image", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>Optional GPU id hint (Config.GetGPUInfo index), or null.</summary>
        public int? GpuId
        {
            get
            {
                if (!Args.TryGetValue("gpu", out var v)) return null;
                if (int.TryParse(v, out int n) && n >= 0) return n;
                return null;
            }
        }

        /// <summary>Optional LLM instance id hint, or null.</summary>
        public int? LlmInstanceId
        {
            get
            {
                if (!Args.TryGetValue("llm", out var v)) return null;
                if (int.TryParse(v, out int n) && n >= 0) return n;
                return null;
            }
        }

        /// <summary>Used by <c>read_skill</c>: which skill id to load full body for.</summary>
        public string TargetSkillId => GetArg("id");

        /// <summary>
        /// True when the action carries <c>chain="true"</c> (or "1" / "yes"). A chained
        /// action does NOT spawn a fresh Pic; it stacks its workflow onto the most recent
        /// Pic spawned this turn, reusing the existing chat bubble. The prior step's
        /// output is inherited automatically via the preset's <c>@upload|image1|input1|</c>
        /// modifier - chained actions therefore must NOT also set attachment / chat_image.
        /// </summary>
        public bool Chain
        {
            get
            {
                if (!Args.TryGetValue("chain", out var v) || string.IsNullOrEmpty(v)) return false;
                v = v.Trim().ToLowerInvariant();
                return v == "true" || v == "1" || v == "yes";
            }
        }

        public string GetArg(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Args.TryGetValue(key.ToLowerInvariant(), out var v);
            return v;
        }

        public override string ToString()
        {
            var parts = new List<string> { "skill=" + SkillId };
            foreach (var kv in Args)
            {
                if (kv.Key == "skill") continue;
                string v = kv.Value ?? "";
                if (v.Length > 60) v = v.Substring(0, 57) + "...";
                parts.Add(kv.Key + "=\"" + v + "\"");
            }
            return "<aitools_action " + string.Join(" ", parts) + "/>";
        }
    }
}
