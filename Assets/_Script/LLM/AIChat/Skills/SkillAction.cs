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

        /// <summary>
        /// Optional prompt text to show in tool logs/provenance when local code rewrites
        /// <see cref="Prompt"/> before execution. Kept out of <see cref="Args"/> so it
        /// never becomes a synthetic action attribute.
        /// </summary>
        public string PromptForLogsOverride;

        public string Preset => GetArg("preset");
        public string Prompt => GetArg("prompt");
        public string PromptForLogs => string.IsNullOrEmpty(PromptForLogsOverride) ? Prompt : PromptForLogsOverride;
        public string NegativePrompt => GetArg("negative_prompt");

        public IReadOnlyDictionary<string, string> GetArgsForToolLog()
        {
            const string hiddenPreApplyStyleArg = "pre_applystyle_prompt";
            bool hasHiddenArg = Args.ContainsKey(hiddenPreApplyStyleArg);
            if (string.IsNullOrEmpty(PromptForLogsOverride) && !hasHiddenArg)
                return Args;

            var sanitized = new Dictionary<string, string>(Args);
            sanitized.Remove(hiddenPreApplyStyleArg);
            if (!string.IsNullOrEmpty(PromptForLogsOverride))
                sanitized["prompt"] = PromptForLogsOverride;
            return sanitized;
        }

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

        /// <summary>
        /// Optional 1-based attachment index for the SECOND input slot (2-input
        /// presets such as Image To Image Klein Edit 2 Input). Mirrors
        /// <see cref="AttachmentIndex"/> but feeds the workflow's image2 slot.
        /// </summary>
        public int? AttachmentIndex2
        {
            get
            {
                if (!Args.TryGetValue("attachment2", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>
        /// Optional 1-based chat-image index for the SECOND input slot (2-input
        /// presets). Wins over <see cref="AttachmentIndex2"/> when both are set,
        /// matching the precedence rule on the primary slot.
        /// </summary>
        public int? ChatImageIndex2
        {
            get
            {
                if (!Args.TryGetValue("chat_image2", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>
        /// Generic accessor for the 1-based attachment index of an extra input slot
        /// (slot 2..5). Used by N-input presets (Image To Image Klein Edit 3/4/5 Input)
        /// to pull additional anchor / reference images. Returns null if the LLM did
        /// not provide attachment{slot}.
        /// </summary>
        public int? GetExtraAttachmentIndex(int slot)
        {
            if (slot < 2 || slot > 5) return null;
            string key = "attachment" + slot;
            if (!Args.TryGetValue(key, out var v)) return null;
            if (int.TryParse(v, out int n) && n > 0) return n;
            return null;
        }

        /// <summary>
        /// Generic accessor for the 1-based chat-image index of an extra input slot
        /// (slot 2..5). Wins over <see cref="GetExtraAttachmentIndex"/> at the same
        /// slot when both are set, matching the precedence rule on the primary slot.
        /// </summary>
        public int? GetExtraChatImageIndex(int slot)
        {
            if (slot < 2 || slot > 5) return null;
            string key = "chat_image" + slot;
            if (!Args.TryGetValue(key, out var v)) return null;
            if (int.TryParse(v, out int n) && n > 0) return n;
            return null;
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

        /// <summary>
        /// Optional explicit output-width override for image/video skills. When both
        /// width and height are set (and > 0), they bypass the host's auto-aspect
        /// match and force the workflow to run at the specified dimensions. Useful
        /// for portrait video from a square source, or any time the LLM needs a
        /// specific aspect that doesn't match the source image. Returns null if
        /// missing or unparseable.
        /// </summary>
        public int? Width
        {
            get
            {
                if (!Args.TryGetValue("width", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>Optional explicit output-height override - see <see cref="Width"/>.</summary>
        public int? Height
        {
            get
            {
                if (!Args.TryGetValue("height", out var v)) return null;
                if (int.TryParse(v, out int n) && n > 0) return n;
                return null;
            }
        }

        /// <summary>Used by <c>read_skill</c>: which skill id to load full body for.</summary>
        public string TargetSkillId => GetArg("id");

        /// <summary>
        /// Optional character-anchor name (<c>anchor="Bob"</c>). When a generate_image /
        /// image_to_image action carries it, the host registers (or re-points) that name to
        /// the spawned or chained Pic, so later turns can reference the character by name via
        /// <c>chat_image="Bob"</c> instead of a numeric slot that shifts when the media
        /// list trims. Re-using an existing name on a new image is the "update the look"
        /// path. Null/empty when the action does not declare an anchor.
        /// </summary>
        public string AnchorName
        {
            get
            {
                string v = GetArg("anchor");
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
        }

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

        /// <summary>
        /// Optional "resume=true" flag for async sidecar skills. When present on
        /// inspect_image, the host automatically gives the main chat model one
        /// follow-up turn after the inspection result is available.
        /// </summary>
        public bool Resume
        {
            get
            {
                if (!Args.TryGetValue("resume", out var v) || string.IsNullOrEmpty(v)) return false;
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
