using System.Text;
using AITools.AIChat.Skills;

namespace AITools.AIChat.Context
{
    /// <summary>
    /// Assembles the dynamic system prompt sent at the start of every chat turn.
    /// Order:
    ///   1. main_prompt.txt body (user-editable persona + house rules)
    ///   2. GPU snapshot (current state of every configured ComfyUI GPU/server)
    ///   3. LLM snapshot (current state of every configured LLM instance)
    ///   4. Skill summaries (one line per skill; full body via read_skill action)
    ///   5. Action protocol footer (re-iterates the XML invocation rules)
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
        /// Builds the complete system prompt string. <paramref name="callingInstanceId"/>
        /// is the LLM instance currently handling the chat (so the snapshot can mark it
        /// "&lt;-- you"); pass -1 if running through the legacy single-provider settings
        /// path with no instance id. <paramref name="reachableChatImageCount"/> is the
        /// number of previously-generated chat images currently available to be reused
        /// via chat_image="N" (1-based, in spawn order). Pass 0 if none.
        /// </summary>
        public string Build(int callingInstanceId, int reachableChatImageCount = 0)
        {
            var sb = new StringBuilder();

            // 1. Main prompt body (user-editable).
            string main = _skills?.MainPrompt ?? "";
            if (!string.IsNullOrEmpty(main))
            {
                sb.AppendLine(main.TrimEnd());
                sb.AppendLine();
            }

            // 2. GPUs.
            sb.Append(GpuSnapshot.BuildBlock());
            sb.AppendLine();

            // 3. LLMs.
            sb.Append(LLMSnapshot.BuildBlock(callingInstanceId));
            sb.AppendLine();

            // 4. Reachable chat images (for chat_image="N" reuse).
            if (reachableChatImageCount > 0)
            {
                sb.Append("CHAT IMAGES (still in chat, reusable as input via chat_image=\"N\"): ");
                sb.Append("Image #1");
                if (reachableChatImageCount > 1) sb.Append(" .. Image #").Append(reachableChatImageCount);
                sb.AppendLine();
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("CHAT IMAGES: none yet (a chat_image=\"N\" reference would fail right now).");
                sb.AppendLine();
            }

            // 5. Skill summaries.
            if (_skills != null)
            {
                sb.Append(_skills.BuildSkillSummariesBlock());
                sb.AppendLine();
            }

            // 6. Action protocol footer. Kept short on purpose - the per-skill Template
            // lines above already show the EXACT call syntax with required attributes.
            sb.AppendLine("ACTION PROTOCOL:");
            sb.AppendLine("- To call a skill, copy its Template line above EXACTLY, then change");
            sb.AppendLine("  the prompt (and the chat_image / attachment number where relevant).");
            sb.AppendLine("- Don't drop required attributes like preset - they are in the Template");
            sb.AppendLine("  for a reason. Don't invent attributes that aren't shown.");
            sb.AppendLine("- Optional add-ons not in any template:");
            sb.AppendLine("  - gpu=\"N\" : pin generation to a specific GPU id (see GPUS list).");
            sb.AppendLine("  - llm=\"N\" : pin a delegated LLM call to a specific instance.");
            sb.AppendLine("- chat_image=\"N\" references the Nth image bubble already in the chat");
            sb.AppendLine("  (matches the \"Image #N\" / \"Movie #N\" labels). attachment=\"N\"");
            sb.AppendLine("  references the Nth image the user pasted THIS turn.");
            sb.AppendLine("- Each action goes on its own line, self-closing, never inside a code fence.");
            sb.AppendLine("- Previous assistant messages may contain old executed <aitools_action .../>");
            sb.AppendLine("  commands. Treat them as examples/history; don't repeat an old action unless");
            sb.AppendLine("  the user asks for another result like it.");
            sb.AppendLine("- Built-in: <aitools_action skill=\"read_skill\" id=\"<skill_id>\"/> loads");
            sb.AppendLine("  a skill's full body if the Template above isn't enough.");

            // 7. User's post-prompt overrides go LAST so they have the strongest "recency"
            // effect on the model. Lets the user dynamically tweak behavior via
            // aichat/post_prompt.txt (or aichat/test_post_prompt.txt) without editing any
            // code or skill file. When the test override is active we add a banner so the
            // LLM (and a curious dev reading the prompt) knows the experiment is live.
            string post = _skills?.PostPrompt ?? "";
            if (!string.IsNullOrEmpty(post))
            {
                sb.AppendLine();
                if (_skills.PostPromptIsTestOverride)
                    sb.AppendLine("[TEST POST-PROMPT OVERRIDE - aichat/test_post_prompt.txt]");
                else
                    sb.AppendLine("[POST-PROMPT - aichat/post_prompt.txt]");
                sb.AppendLine(post.TrimEnd());
            }

            return sb.ToString();
        }
    }
}
