using System.Collections.Generic;
using System.Text;
using AITools.AIChat.Skills;

namespace AITools.AIChat.Context
{
    /// <summary>
    /// Assembles the two pieces of per-turn context for the chat LLM:
    ///
    /// Build() - the system prompt. Deliberately STABLE from turn to turn so
    /// server-side prompt caching (llama.cpp slot KV reuse, OpenAI/Anthropic
    /// prefix caching) can skip re-prefilling it AND the conversation history that
    /// follows it. It only changes when the user edits the prompt/skill files.
    /// Order:
    ///   1. main_prompt.txt body (user-editable persona + house rules)
    ///   2. Skill summaries (one line per skill; full bodies are injected separately
    ///      by AIChatPanel when an autoload trigger appears)
    ///   3. Action protocol footer (re-iterates the XML invocation rules)
    ///   4. post_prompt.txt body (user's "last word" overrides)
    ///
    /// BuildCurrentStateBlock() - everything that CHANGES between turns (GPU
    /// busy/idle state, the numbered chat-image list with its async captions).
    /// AIChatPanel appends it to the OUTGOING copy of the latest user message at
    /// send time - never to stored history - so state churn only re-prefills the
    /// tail of the request instead of invalidating the cached prefix at the top.
    ///
    /// (LLM snapshot intentionally omitted from both - we don't share the OTHER
    /// LLMS roster with the chat LLM. Runtime still honours an explicit llm="N" on
    /// a delegation skill, but we no longer advertise the available ids.)
    ///
    /// Plain class - no Unity dependencies of its own; defers to the snapshot helpers
    /// which read Config / LLMInstanceManager directly.
    /// </summary>
    public class ChatContextBuilder
    {
        private readonly SkillManager _skills;

        public ChatContextBuilder(SkillManager skills)
        {
            _skills = skills;
        }

        /// <summary>
        /// Builds the stable system prompt string. Keep volatile data OUT of here -
        /// anything that varies turn-to-turn belongs in
        /// <see cref="BuildCurrentStateBlock"/>, because a change this early in the
        /// request invalidates the server's prompt cache for the whole conversation.
        /// </summary>
        public string Build()
        {
            var sb = new StringBuilder();

            // 1. Main prompt body (user-editable).
            string main = _skills?.MainPrompt ?? "";
            if (!string.IsNullOrEmpty(main))
            {
                sb.AppendLine(main.TrimEnd());
                sb.AppendLine();
            }

            // 2. Skill summaries.
            if (_skills != null)
            {
                sb.Append(_skills.BuildSkillSummariesBlock());
                sb.AppendLine();
            }

            // 3. Action protocol footer. Kept short on purpose - the per-skill Template
            // lines above already show the EXACT call syntax with required attributes.
            sb.AppendLine("ACTION PROTOCOL:");
            sb.AppendLine("- To call a skill, copy its Template line above EXACTLY, then change");
            sb.AppendLine("  the prompt (and the chat_image / attachment number where relevant).");
            sb.AppendLine("- Don't drop required attributes like preset - they are in the Template");
            sb.AppendLine("  for a reason. Don't invent attributes that aren't shown.");
            sb.AppendLine("- Optional add-ons not in any template:");
            sb.AppendLine("  - gpu=\"N\" : pin generation to a specific GPU id (see the GPUS list in");
            sb.AppendLine("    the CURRENT STATE block attached to the latest message).");
            sb.AppendLine("- chat_image=\"N\" references the Nth image bubble already in the chat");
            sb.AppendLine("  (matches the \"Image #N\" / \"Movie #N\" labels). attachment=\"N\"");
            sb.AppendLine("  references the Nth image the user pasted THIS turn.");
            sb.AppendLine("- Each action goes on its own line, self-closing, never inside a code fence.");
            sb.AppendLine("- Previous assistant messages may contain old executed <aitools_action .../>");
            sb.AppendLine("  commands. Treat them as examples/history; don't repeat an old action unless");
            sb.AppendLine("  the user asks for another result like it.");
            sb.AppendLine("- Built-in: <aitools_action skill=\"read_skill\" id=\"<skill_id>\"/> loads");
            sb.AppendLine("  a skill's full body for the NEXT assistant turn if the Template above isn't enough.");

            // 4. User's post-prompt overrides go LAST so they have the strongest "recency"
            // effect on the model. Lets the user dynamically tweak behavior via
            // aichat/post_prompt.txt (or aichat/test_post_prompt.txt) without editing any
            // code or skill file. No banner or filename is emitted - the model sees only
            // the instructions, never an internal path. Which file is live (and whether
            // it's the test override) is surfaced locally instead, via the [TEST PROMPT]
            // status pill and the "active config" notice on Clear/init/preset change.
            string post = _skills?.PostPrompt ?? "";
            if (!string.IsNullOrEmpty(post))
            {
                sb.AppendLine();
                sb.AppendLine(post.TrimEnd());
            }

            // Final pass: substitute every {{Preset Name.txt}} sentinel with
            // <prefix>Preset Name.txt (prefix from PlayerPrefs, empty by default).
            // Done at the end so it covers main_prompt body, per-skill Templates,
            // and the action protocol footer in one shot.
            return SkillManager.ApplyPresetPrefix(sb.ToString());
        }

        /// <summary>
        /// Builds the volatile CURRENT STATE block: the GPU roster (with live
        /// busy/idle status) and the numbered chat-image list (captions fill in
        /// asynchronously). <paramref name="chatImageSlotCount"/> is the number of
        /// numbered chat image slots available via chat_image="N" (1-based, matching
        /// the visible Image #N / Movie #N labels); pass 0 if none. AIChatPanel
        /// appends this to the outgoing user message each turn - ephemerally, so a
        /// GPU flipping busy or a caption arriving never rewrites earlier request
        /// content the server already cached.
        /// </summary>
        public string BuildCurrentStateBlock(int chatImageSlotCount = 0, IReadOnlyList<string> chatImageCaptions = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CURRENT STATE - attached automatically to the newest message; the user did not type this. Earlier messages had their copies removed to save space.]");

            sb.Append(GpuSnapshot.BuildBlock());
            sb.AppendLine();

            // Reachable chat images (for chat_image="N" reuse). When captions are
            // available we list each image with its short auto-generated description so
            // the LLM can resolve descriptive references ("the one with grandma") to
            // the right chat_image="N" without relying solely on visual recall.
            if (chatImageSlotCount > 0)
            {
                sb.AppendLine("CHAT IMAGES (numbered slots reusable as input via chat_image=\"N\"):");
                for (int i = 0; i < chatImageSlotCount; i++)
                {
                    string caption = (chatImageCaptions != null && i < chatImageCaptions.Count)
                        ? (chatImageCaptions[i] ?? "")
                        : "";
                    if (string.IsNullOrEmpty(caption)) caption = "(captioning...)";
                    sb.Append("- Image #").Append(i + 1).Append(": ").AppendLine(caption);
                }
            }
            else
            {
                sb.AppendLine("CHAT IMAGES: none yet (a chat_image=\"N\" reference would fail right now).");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
