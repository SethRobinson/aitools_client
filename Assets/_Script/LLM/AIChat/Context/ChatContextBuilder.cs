using System.Collections.Generic;
using System.Text;
using AITools.AIChat.Skills;

namespace AITools.AIChat.Context
{
    public class ChatImageState
    {
        public int Index;
        public bool IsUserAttachment;
        public bool IsMovie;
        public bool IsReusable = true;
        public bool IncludeCaption;
        public string Kind;
        public string AnchorName;
        public string Dimensions;
        public string Caption;
        public string Provenance;
        public bool HasCleanBase;
    }

    /// <summary>
    /// Assembles the two pieces of per-turn context for the chat LLM:
    ///
    /// Build() - the system prompt. Deliberately STABLE from turn to turn so
    /// server-side prompt caching (llama.cpp slot KV reuse, OpenAI/Anthropic
    /// prefix caching) can skip re-prefilling it AND the conversation history that
    /// follows it. It only changes when the user edits the prompt/skill files.
    /// Order:
    ///   1. pre_prompt.txt body (optional top-of-system-prompt framing)
    ///   2. main_prompt.txt body (user-editable persona + house rules)
    ///   3. Skill summaries (one line per skill; full bodies are injected separately
    ///      by AIChatPanel when an autoload trigger appears)
    ///   4. Action protocol footer (re-iterates the XML invocation rules)
    ///   5. post_prompt.txt body (user's "last word" overrides)
    ///
    /// BuildCurrentStateBlock() - everything that CHANGES between turns (GPU
    /// busy/idle state, the numbered chat-image list with compact provenance and
    /// optional captions).
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
        public string Build(bool keepOldToolCallsInPrompt = true)
        {
            var sb = new StringBuilder();

            // 1. Pre-prompt body. It intentionally comes before the normal main prompt.
            string pre = _skills?.PrePrompt ?? "";
            if (!string.IsNullOrEmpty(pre))
            {
                sb.AppendLine(pre.TrimEnd());
                sb.AppendLine();
            }

            // 2. Main prompt body (user-editable).
            string main = _skills?.MainPrompt ?? "";
            if (!string.IsNullOrEmpty(main))
            {
                sb.AppendLine(main.TrimEnd());
                sb.AppendLine();
            }

            // 3. Skill summaries.
            if (_skills != null)
            {
                sb.Append(_skills.BuildSkillSummariesBlock());
                sb.AppendLine();
            }

            // 4. Action protocol footer. Kept short on purpose - the per-skill Template
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
            if (keepOldToolCallsInPrompt)
            {
                sb.AppendLine("- Previous assistant messages may contain old executed <aitools_action .../>");
                sb.AppendLine("  commands. Treat them as examples/history; don't repeat an old action unless");
                sb.AppendLine("  the user asks for another result like it.");
            }
            else
            {
                sb.AppendLine("- Old executed action XML is hidden from your prompt by default. Use the");
                sb.AppendLine("  CHAT IMAGES provenance list for prior generated/edit history.");
            }
            sb.AppendLine("- Built-in: <aitools_action skill=\"read_skill\" id=\"<skill_id>\"/> loads");
            sb.AppendLine("  a skill's full body, then the host automatically gives you one");
            sb.AppendLine("  synthetic continue turn so you can use it. If you call read_skill,");
            sb.AppendLine("  do not ask the user to press Send/Continue and do not call it again.");

            // 5. User's post-prompt overrides go LAST so they have the strongest "recency"
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
            // Done at the end so it covers prompt bodies, per-skill Templates, and
            // the action protocol footer in one shot.
            return SkillManager.ApplyPresetPrefix(sb.ToString());
        }

        /// <summary>
        /// Builds the volatile CURRENT STATE block: the GPU roster (with live
        /// busy/idle status) and a bounded recent-window numbered chat-image list.
        /// <paramref name="chatImageSlotCount"/> is the total number of numbered
        /// slots available via chat_image="N"; <paramref name="chatImages"/> is
        /// the subset to actually list in the prompt. AIChatPanel appends this
        /// to the outgoing user message each turn on the prompt copy. Visible
        /// history stays clean, while prior sent copies can remain byte-identical
        /// for server prompt caches.
        /// </summary>
        public string BuildCurrentStateBlock(int chatImageSlotCount = 0, IReadOnlyList<ChatImageState> chatImages = null, string anchorsLine = null, int imageContextLimit = 40)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CURRENT STATE - attached automatically to this user turn; the user did not type this. Use the newest CURRENT STATE block for live status; older copies are historical and may remain for prompt-cache reuse.]");

            sb.Append(GpuSnapshot.BuildBlock());
            sb.AppendLine();

            // Named character anchors (recurring cast). Printed ABOVE the raw chat-image
            // list so the model reaches for stable names ("Bob") instead of slot numbers
            // that shift when the media column trims. The host resolves each name to its
            // CURRENT slot every turn, so this stays correct across renumbers.
            if (!string.IsNullOrEmpty(anchorsLine))
                sb.AppendLine(anchorsLine);

            // Reachable chat images (for chat_image="N" reuse). Generated images are
            // represented by compact provenance instead of repeated visual captions by
            // default; user attachments still carry their one-shot caption because the
            // app did not create them from a prompt.
            if (chatImageSlotCount > 0)
            {
                int listedCount = chatImages != null ? chatImages.Count : 0;
                int firstListedIndex = listedCount > 0 && chatImages[0] != null && chatImages[0].Index > 0
                    ? chatImages[0].Index
                    : (listedCount > 0 ? chatImageSlotCount - listedCount + 1 : 0);
                int lastListedIndex = listedCount > 0 && chatImages[listedCount - 1] != null && chatImages[listedCount - 1].Index > 0
                    ? chatImages[listedCount - 1].Index
                    : chatImageSlotCount;

                sb.Append("CHAT IMAGES (").Append(chatImageSlotCount)
                  .Append(" current slot").Append(chatImageSlotCount == 1 ? "" : "s")
                  .Append("; ");
                if (listedCount <= 0)
                {
                    sb.Append("showing none because image context limit is ").Append(imageContextLimit)
                      .Append("; next new bubble will be #").Append(chatImageSlotCount + 1)
                      .AppendLine(". Use existing numbers only; for same-reply follow-ups use chain=\"true\" or anchors, not guessed future numbers):");
                    sb.Append("All existing chat images #1-#").Append(chatImageSlotCount)
                      .AppendLine(" omitted from prompt; use named anchors or ask the user if an omitted old image is needed.");
                }
                else if (listedCount < chatImageSlotCount)
                {
                    sb.Append("showing #").Append(firstListedIndex).Append("-#").Append(lastListedIndex)
                      .Append(" only, latest ").Append(listedCount)
                      .Append(" by image context limit ").Append(imageContextLimit)
                      .Append("; next new bubble will be #").Append(chatImageSlotCount + 1)
                      .AppendLine(". Use existing numbers only; for same-reply follow-ups use chain=\"true\" or anchors, not guessed future numbers):");
                    if (firstListedIndex > 1)
                    {
                        sb.Append("Older chat images #1-#").Append(firstListedIndex - 1)
                          .AppendLine(" omitted from prompt; use named anchors or ask the user if an omitted old image is needed.");
                    }
                }
                else
                {
                    sb.Append("showing all; next new bubble will be #").Append(chatImageSlotCount + 1)
                      .AppendLine(". Use existing numbers only; for same-reply follow-ups use chain=\"true\" or anchors, not guessed future numbers):");
                }

                sb.AppendLine("If a composed image has clean_base=available and you need to redo/change text, labels, borders, or speech bubbles, use chat_image=\"N\" clean_base=\"true\" on the FIRST replacement draw_shape/draw_text/add_border step so you do not draw over baked-in old overlays.");
                for (int i = 0; i < listedCount; i++)
                {
                    ChatImageState state = (chatImages != null && i < chatImages.Count) ? chatImages[i] : null;
                    int index = state != null && state.Index > 0 ? state.Index : i + 1;
                    string kind = state != null && !string.IsNullOrEmpty(state.Kind)
                        ? state.Kind
                        : (state != null && state.IsMovie ? "movie" : "image");

                    sb.Append("- #").Append(index).Append(": ").Append(kind);
                    if (state != null)
                    {
                        if (!state.IsReusable)
                            sb.Append(", not reusable");
                        if (!string.IsNullOrEmpty(state.AnchorName))
                            sb.Append(", anchor=\"").Append(state.AnchorName).Append('"');
                        if (!string.IsNullOrEmpty(state.Dimensions))
                            sb.Append(", ").Append(state.Dimensions);
                        if (state.HasCleanBase)
                            sb.Append(", clean_base=available");
                        if (!string.IsNullOrEmpty(state.Provenance))
                            sb.Append(", ").Append(state.Provenance);
                        if (state.IncludeCaption && !string.IsNullOrEmpty(state.Caption))
                            sb.Append(", caption: ").Append(state.Caption);
                    }
                    sb.AppendLine();
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
