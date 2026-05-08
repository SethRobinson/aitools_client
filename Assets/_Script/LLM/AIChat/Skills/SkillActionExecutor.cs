using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// Dispatches parsed <see cref="SkillAction"/>s to the rest of the app.
    ///
    /// Design notes:
    /// <list type="bullet">
    /// <item>Image / movie skills reuse the unmodified <c>PicMain.RunPresetByName</c>
    /// + <c>UpdateJobs()</c> pipeline. We just spawn a Pic, optionally seed its main
    /// image from a chat attachment, then hand it off. No new render path.</item>
    /// <item>read_skill is fully in-process: looks up the skill by id and injects its
    /// full markdown body back into the chat as a system-role message so the LLM sees
    /// it on its next turn.</item>
    /// <item>summarize_with_small_llm fires a one-shot chat-completion against a small
    /// LLM instance picked by <see cref="LLMInstanceManager"/>. When the result lands,
    /// it's surfaced as both a chat bubble and a system-role injection.</item>
    /// </list>
    /// </summary>
    public class SkillActionExecutor
    {
        private readonly SkillManager _skills;
        private readonly IChatHost _host;

        public SkillActionExecutor(SkillManager skills, IChatHost host)
        {
            _skills = skills;
            _host = host;
        }

        public void Execute(SkillAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.SkillId))
            {
                _host?.AddInfoBubble("Skill error: empty or malformed action tag.");
                return;
            }

            switch (action.SkillId.ToLowerInvariant())
            {
                case BuiltInSkillIds.GenerateImage:
                case BuiltInSkillIds.GenerateMovie:
                    ExecuteGenerate(action, useAttachment: false);
                    break;

                case BuiltInSkillIds.ImageToImage:
                case BuiltInSkillIds.ImageToMovie:
                    ExecuteGenerate(action, useAttachment: true);
                    break;

                case BuiltInSkillIds.ReadSkill:
                    ExecuteReadSkill(action);
                    break;

                case BuiltInSkillIds.SummarizeWithSmallLlm:
                    ExecuteSummarizeWithSmallLlm(action);
                    break;

                case BuiltInSkillIds.DescribeImage:
                    // No-op skill - documents that the LLM should describe images itself.
                    _host?.AddInfoBubble("(describe_image is a documentation-only skill - I'll answer in chat directly.)");
                    break;

                default:
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}' is not recognized. Use one of: " +
                        string.Join(", ", GetKnownSkillIds()));
                    break;
            }
        }

        // ---------- Generate (image or movie) ----------

        private void ExecuteGenerate(SkillAction action, bool useAttachment)
        {
            // Generate-class skills with an empty prompt produce a workflow that runs
            // against whatever GameLogic.GetModifiedGlobalPrompt() happens to return -
            // typically the main GUI's prompt field, which has nothing to do with the
            // chat. Surface this loudly so it doesn't ship silent "no prompt" videos.
            // Checked for both chained AND non-chained paths, so the fast-fail catches
            // the common LLM mistake of emitting an action tag with no prompt attribute.
            if (string.IsNullOrWhiteSpace(action.Prompt))
            {
                var skill = _skills?.GetById(action.SkillId);
                string template = skill != null && !string.IsNullOrEmpty(skill.Template)
                    ? skill.Template
                    : $"<aitools_action skill=\"{action.SkillId}\" preset=\"...\" prompt=\"...\"/>";
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' was emitted without a prompt attribute (or it was empty). " +
                    "Generate-class skills must carry a non-empty prompt - the chat does NOT inherit prompt text " +
                    "from the main GUI. Re-emit with the prompt filled in. Template:\n  " + template);
                return;
            }

            // Stacking onto the previous Pic in this turn: stack the new workflow onto
            // the last spawned Pic instead of creating a fresh one. Lets the LLM compose
            // multi-stage Pics directly (e.g. generate_image -> image_to_movie on one
            // Pic, one updating chat bubble) without going through a sub-LLM preset.
            //
            // Safety-net fallback: img2X actions (image_to_movie / image_to_image) that
            // arrive with NO source attribute AND no chain="true" get auto-promoted to
            // chained mode IF a chain target exists this turn. This catches a common LLM
            // miss - it generates an image then forgets to add chain="true" to the
            // follow-up. The original explicit-only design erred out and lost the work;
            // the rescue stacks correctly and surfaces an info bubble so it's visible.
            bool autoChain = false;
            if (!action.Chain
                && useAttachment
                && !action.AttachmentIndex.HasValue
                && !action.ChatImageIndex.HasValue
                && (_host?.GetTurnAttachmentCount() ?? 0) == 0
                && _host?.GetLastSpawnedPicForTurn() != null)
            {
                autoChain = true;
                _host?.AddInfoBubble(
                    $"(Stacked '{action.SkillId}' onto the most recent unchained Pic this turn - chain=\"true\" inferred.)");
            }

            if (action.Chain || autoChain)
            {
                ExecuteChainedGenerate(action);
                return;
            }

            string preset = action.Preset;
            string prompt = action.Prompt;

            // Source image for img2img / img2vid skills can come from EITHER:
            //   - chat_image="N"  -> snapshot the texture of the Nth chat-image bubble
            //                        spawned this session (lets the LLM say "edit the
            //                        image you just made"). Spawns a fresh Pic so the
            //                        original bubble is untouched.
            //   - attachment="N"  -> the Nth image the user pasted/dragged THIS turn.
            // chat_image takes precedence so the LLM can mix recent generations with
            // fresh user pastes in the same turn if it ever wants to.
            byte[] attachmentBytes = null;
            if (useAttachment)
            {
                int chatN = action.ChatImageIndex ?? -1;
                int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
                int chatImageCount = _host?.GetChatImageCount() ?? 0;

                if (chatN > 0)
                {
                    attachmentBytes = _host?.GetChatImagePngBytes(chatN);
                    if (attachmentBytes == null)
                    {
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}': chat_image=\"{chatN}\" is not available. " +
                            $"There are {chatImageCount} reachable chat image(s) this session. " +
                            $"Use a smaller index, ask the user to paste an image, or use generate_image instead.");
                        return;
                    }
                }
                else if (turnAttachCount > 0)
                {
                    int idx = action.AttachmentIndex ?? 1;
                    attachmentBytes = _host?.GetTurnAttachmentBytes(idx);
                    if (attachmentBytes == null)
                    {
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}' wanted attachment={idx} but the user only attached {turnAttachCount} image(s) this turn. " +
                            $"Use attachment=\"1\" to reference the first one.");
                        return;
                    }
                }
                else if (chatImageCount > 0)
                {
                    bool hasChainTarget = _host?.GetLastSpawnedPicForTurn() != null;
                    if (hasChainTarget)
                    {
                        // Same-reply pair: the LLM emitted (e.g.) generate_image then a
                        // bare image_to_movie without chain="true". We can't safely
                        // auto-pick a chat_image because the just-spawned Pic isn't a
                        // numbered bubble yet - point the LLM at chain="true" and let it
                        // re-roll on the next turn.
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}' has no input image. " +
                            "If you meant to stack this onto the image you JUST generated earlier in this same reply, add chain=\"true\" instead (do not also pass chat_image / attachment with chain=\"true\"). " +
                            $"Otherwise reference an existing chat bubble via chat_image=\"N\" (1..{chatImageCount}).");
                        return;
                    }

                    // Standalone reply (e.g. follow-up "turn it into a video") - the LLM
                    // forgot chat_image="N" but there's only one reasonable target: the
                    // most recent chat image. Fall back to it instead of erroring; this
                    // is the single most common LLM omission with smaller models, and
                    // failing strictly here breaks the user's flow for no real benefit.
                    int implicitIdx = chatImageCount;
                    attachmentBytes = _host?.GetChatImagePngBytes(implicitIdx);
                    if (attachmentBytes == null)
                    {
                        // Race: pic was destroyed between count and fetch. Fall through
                        // to the same explicit-error message the chat_image="N" path uses.
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}': implicit chat_image=\"{implicitIdx}\" is no longer available (the world Pic may have been deleted). " +
                            $"Use a smaller chat_image=\"N\" index, or ask the user to paste a new image.");
                        return;
                    }
                    _host?.AddInfoBubble($"(auto-picked chat_image=\"{implicitIdx}\" - the latest image - as the source for {action.SkillId})");
                }
                else
                {
                    bool hasChainTarget = _host?.GetLastSpawnedPicForTurn() != null;
                    string chainHint = hasChainTarget
                        ? "If you meant to stack this onto the image you JUST generated earlier in this same reply, add chain=\"true\" (do not also pass chat_image / attachment with chain=\"true\"). "
                        : "";
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}' needs the user to paste an image into the chat first " +
                        $"(or you can reference an earlier chat image once one exists, via chat_image=\"N\"). " +
                        chainHint +
                        "There are 0 reachable images right now.");
                    return;
                }
            }

            // Resolve optional 2nd-input image (attachment2 / chat_image2). Used by
            // 2-input presets like Image To Image Klein Edit 2 Input. Returns null when
            // the LLM didn't ask for a second image; returns null + emits a bubble when
            // it asked for one that isn't reachable (caller bails).
            byte[] attachmentBytes2 = ResolveSecondInputBytes(action, out bool secondInputErrored);
            if (secondInputErrored) return;

            if (string.IsNullOrEmpty(preset))
            {
                // The system prompt's SKILLS block shows a Template line per skill with
                // every required attribute filled in - the LLM literally just has to copy
                // it. So we hard-fail here (no auto-default that masks the bug) and the
                // LLM has the right info to fix it on its next turn.
                var skill = _skills?.GetById(action.SkillId);
                string template = skill != null && !string.IsNullOrEmpty(skill.Template)
                    ? skill.Template
                    : $"<aitools_action skill=\"{action.SkillId}\" preset=\"...\" prompt=\"...\"/>";
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' is missing the required preset attribute. " +
                    $"Copy the Template line from the SKILLS block and only change the prompt:\n" +
                    $"  {template}");
                return;
            }

            // Resolve the preset filename robustly (case-insensitive, with/without .txt).
            string resolved = ResolvePresetName(preset);
            if (resolved == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}': preset '{preset}' was not found in Presets/. " +
                    "Re-pick from the list shown in your skill description.");
                return;
            }

            var imageGen = ImageGenerator.Get();
            if (imageGen == null)
            {
                _host?.AddInfoBubble("Skill error: ImageGenerator not initialized yet.");
                return;
            }

            GameObject picGO = imageGen.CreateNewPic();
            if (picGO == null)
            {
                _host?.AddInfoBubble("Skill error: failed to spawn a Pic.");
                return;
            }
            var picMain = picGO.GetComponent<PicMain>();
            if (picMain == null)
            {
                _host?.AddInfoBubble("Skill error: spawned object has no PicMain.");
                return;
            }

            // Seed main image for img2img / img2vid presets (which expect "image1" via
            // @upload|image1|input1|).
            int srcW = 0, srcH = 0;
            if (useAttachment && attachmentBytes != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(attachmentBytes))
                {
                    srcW = tex.width;
                    srcH = tex.height;
                    picMain.SetImage(tex, false);
                }
                else
                {
                    UnityEngine.Object.Destroy(tex);
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}': could not decode attachment {action.AttachmentIndex ?? 1} as an image.");
                    return;
                }
            }

            // Optional 2nd input - feeds the workflow's "image2" upload slot. The Pic
            // takes ownership of the texture and uploads it (no display, no mask).
            if (attachmentBytes2 != null)
            {
                var tex2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex2.LoadImage(attachmentBytes2))
                {
                    picMain.SetImage2(tex2);
                }
                else
                {
                    UnityEngine.Object.Destroy(tex2);
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}': could not decode the 2nd input image.");
                    return;
                }
            }

            // Aspect-aware dimension override for img2X presets. Explicit width/height
            // attributes from the LLM win; otherwise fall back to "match the source's
            // aspect at the preset's pixel budget" so a 1024x1024 source no longer
            // gets center-cropped into LTX's default 960x544. Standalone generate_*
            // (no source image) and presets that don't follow the standard %width%/
            // %height% @replace pattern are unaffected - PicMain's helper no-ops.
            if (action.Width.HasValue && action.Height.HasValue
                && action.Width.Value > 0 && action.Height.Value > 0)
            {
                picMain.SetWorkflowDimensionOverride(action.Width.Value, action.Height.Value);
            }
            else if (useAttachment && srcW > 0 && srcH > 0)
            {
                picMain.SetWorkflowAspectSource(srcW, srcH);
            }

            // Optional GPU hint - reuses the existing per-server "wait for this server"
            // slot on PicMain (see PicMain.UpdateJobs around m_requestedServerID).
            if (action.GpuId.HasValue)
            {
                int gpu = action.GpuId.Value;
                if (gpu >= 0 && gpu < Config.Get().GetGPUCount())
                    picMain.m_requestedServerID = gpu;
            }

            // Pull the preset's default_negative_prompt so AI Chat matches the normal UI
            // "Load preset" behavior. Without this, RunPresetByName falls back to whatever
            // negative prompt the main GUI was last set to, ignoring the value the preset
            // author wrote into the file. (We deliberately do NOT mirror default_pre_prompt
            // / default_post_prompt - those are GameLogic globals that the workflow JSON
            // does NOT consume directly via <AITOOLS_PROMPT> substitution, so writing them
            // would just churn the main GUI's fields with no effect on the chat run.)
            string negFromPreset = ReadPresetDefaultNegativePrompt(resolved);

            try
            {
                picMain.RunPresetByName(resolved, prompt, negFromPreset);
                picMain.UpdateJobs();
            }
            catch (Exception ex)
            {
                Debug.LogError("SkillActionExecutor: RunPresetByName threw: " + ex);
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}': failed to start preset '{resolved}'. See Unity console.");
                return;
            }

            // Insert the chat-side image bubble (live mirror of the spawned Pic).
            _host?.AppendImageBubbleForPic(action, picMain);

            // Record this Pic as the chain target for any subsequent chain="true" actions
            // in this same turn. Chained steps deliberately do NOT update this - a 3-step
            // chain (base -> chain -> chain) all stacks onto the same root Pic.
            _host?.SetLastSpawnedPicForTurn(picMain);
        }

        // ---------- Chained generate (chain="true") ----------

        /// <summary>
        /// Stack a follow-up workflow onto the most recently spawned Pic this turn.
        /// Uses no fresh attachment - the chained workflow inherits the prior step's
        /// output via the preset's own <c>@upload|image1|input1|</c> modifier (every
        /// img2X preset starts with this). The existing chat bubble keeps mirroring
        /// the same Pic, so it visibly transitions from still -> video as each stage
        /// finishes.
        /// </summary>
        private void ExecuteChainedGenerate(SkillAction action)
        {
            // FIFO match: pop the OLDEST unchained Pic from the queue (or fall back to
            // the most-recent if the queue is empty - keeps 3+ step chains working).
            // The previous design used GetLastSpawnedPicForTurn directly, which made
            // every chain pile onto the most-recent Pic - so a "grouped" reply like
            // gen_image, gen_image, img_to_movie, img_to_movie produced two LTX videos
            // stacked on the second image instead of one each.
            var prevPic = _host?.ConsumeChainTarget();
            if (prevPic == null)
            {
                // Common LLM mistake: chain="true" emitted on a fresh turn, intending
                // to operate on an image from the previous turn. chain only works
                // within a SINGLE reply, but the model's intent is clear when there's
                // a recent chat image - translate to chat_image="<latest>" and run
                // through the standard non-chain path. Smaller models (Qwen, etc) at
                // low temperature persistently emit chain="true" by reflex even with
                // explicit prompt warnings; this rescue keeps the user's flow alive.
                int chatImageCount = _host?.GetChatImageCount() ?? 0;
                if (chatImageCount > 0)
                {
                    action.Args["chat_image"] = chatImageCount.ToString();
                    action.Args.Remove("chain");
                    _host?.AddInfoBubble(
                        $"(translated chain=\"true\" -> chat_image=\"{chatImageCount}\" - chain only works within the SAME reply; using the latest chat image instead)");
                    // Both image_to_image and image_to_movie are useAttachment=true in
                    // the dispatcher above (lines 50-52); chain rescue only applies to
                    // those two skills, so always pass true here.
                    ExecuteGenerate(action, useAttachment: true);
                    return;
                }

                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' was called with chain=\"true\" but no Pic was spawned earlier in this turn. " +
                    "Either drop chain=\"true\" or emit a base generate_image / image_to_image action first.");
                return;
            }

            if (action.AttachmentIndex.HasValue || action.ChatImageIndex.HasValue)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' with chain=\"true\" must NOT also set attachment / chat_image. " +
                    "A chained step automatically uses the previous step's output as its input. " +
                    "Drop the attachment/chat_image attribute and re-emit.");
                return;
            }

            string preset = action.Preset;
            if (string.IsNullOrEmpty(preset))
            {
                var skill = _skills?.GetById(action.SkillId);
                string template = skill != null && !string.IsNullOrEmpty(skill.Template)
                    ? skill.Template
                    : $"<aitools_action skill=\"{action.SkillId}\" preset=\"...\" prompt=\"...\" chain=\"true\"/>";
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' (chain=\"true\") is missing the required preset attribute. " +
                    $"Copy the Template line from the SKILLS block and add chain=\"true\":\n  {template}");
                return;
            }

            string resolved = ResolvePresetName(preset);
            if (resolved == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' (chain=\"true\"): preset '{preset}' was not found in Presets/. " +
                    "Re-pick from the list shown in your skill description.");
                return;
            }

            // Optional 2nd input - chain inherits image1 from the prior step, but the
            // LLM can still bring a separate image2 reference in via attachment2 /
            // chat_image2 for 2-input presets.
            byte[] chainBytes2 = ResolveSecondInputBytes(action, out bool chainSecondErrored);
            if (chainSecondErrored) return;
            if (chainBytes2 != null)
            {
                var tex2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex2.LoadImage(chainBytes2))
                {
                    prevPic.SetImage2(tex2);
                }
                else
                {
                    UnityEngine.Object.Destroy(tex2);
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}' (chain=\"true\"): could not decode the 2nd input image.");
                    return;
                }
            }

            // Same per-Pic negative-prompt extraction as the non-chained path so the
            // chained workflow inherits the preset author's negative prompt instead of
            // whatever GameLogic's global state happens to hold.
            string negFromPreset = ReadPresetDefaultNegativePrompt(resolved);

            // Aspect-aware dimension override: explicit width/height from the LLM win;
            // otherwise inherit the prior step's actual texture dimensions if any are
            // already on the Pic, else fall back to the prior step's last queued
            // workflow dimensions (best-effort) - this keeps a Z-Image -> LTX chain
            // running at the Z-Image source's aspect even though the texture isn't
            // rendered yet at queue time.
            if (action.Width.HasValue && action.Height.HasValue
                && action.Width.Value > 0 && action.Height.Value > 0)
            {
                prevPic.SetWorkflowDimensionOverride(action.Width.Value, action.Height.Value);
            }
            else
            {
                int chainSrcW = 0, chainSrcH = 0;
                if (prevPic.TryGetCurrentTexture(out var prevTex) && prevTex != null)
                {
                    chainSrcW = prevTex.width;
                    chainSrcH = prevTex.height;
                }
                // Texture not rendered yet (typical for generate_image -> image_to_movie
                // chain in one reply): fall back to the prior step's queued dimensions
                // so e.g. a Z-Image 1024x1024 prior step propagates "square" to LTX.
                if ((chainSrcW <= 0 || chainSrcH <= 0)
                    && prevPic.LastQueuedWorkflowWidth > 0 && prevPic.LastQueuedWorkflowHeight > 0)
                {
                    chainSrcW = prevPic.LastQueuedWorkflowWidth;
                    chainSrcH = prevPic.LastQueuedWorkflowHeight;
                }
                if (chainSrcW > 0 && chainSrcH > 0)
                    prevPic.SetWorkflowAspectSource(chainSrcW, chainSrcH);
            }

            try
            {
                prevPic.AppendPresetJobs(resolved, action.Prompt, negFromPreset);
            }
            catch (Exception ex)
            {
                Debug.LogError("SkillActionExecutor: AppendPresetJobs threw: " + ex);
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' (chain=\"true\"): failed to append preset '{resolved}'. See Unity console.");
                return;
            }

            // No new chat bubble: the chained step shares the existing bubble (which
            // mirrors prevPic via ChatPicMirror and will transition still -> video as
            // each stage finishes). Also do NOT update SetLastSpawnedPicForTurn -
            // multi-step chains stay anchored to the original Pic.
        }

        /// <summary>
        /// Read the preset's <c>default_negative_prompt</c> block (if any) without
        /// applying any other global side-effects. Mirrors the snippet of
        /// <c>PresetManager.LoadPresetAndApply</c> that pulls negative prompt out of
        /// the file - just for negative-prompt only, since that's per-PicJob and we want
        /// the chained step to honor the preset author's choice rather than inherit
        /// whatever the main GUI last had.
        /// Returns null when the preset has no block (so callers can fall back to
        /// GameLogic's value via the existing <c>RunPresetByName</c> null-coalesce).
        /// </summary>
        private static string ReadPresetDefaultNegativePrompt(string resolvedPresetName)
        {
            try
            {
                var pm = PresetManager.Get();
                if (pm == null) return null;
                var extractor = new PresetFileConfigExtractor();
                pm.LoadPreset(resolvedPresetName, extractor);
                return extractor.default_negative_prompt?.Trim();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SkillActionExecutor.ReadPresetDefaultNegativePrompt: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Resolve the optional 2nd input image (attachment2 / chat_image2) to PNG bytes.
        /// Returns null when the LLM didn't ask for a second image. Returns null and sets
        /// <paramref name="errored"/>=true (after emitting a system-injection bubble) when
        /// it asked for one that's not reachable; callers should bail in that case.
        /// chat_image2 wins over attachment2 if both are set, mirroring the precedence on
        /// the primary slot.
        /// </summary>
        private byte[] ResolveSecondInputBytes(SkillAction action, out bool errored)
        {
            errored = false;
            int chatN2 = action.ChatImageIndex2 ?? -1;
            int attachN2 = action.AttachmentIndex2 ?? -1;
            if (chatN2 <= 0 && attachN2 <= 0) return null;

            if (chatN2 > 0)
            {
                byte[] bytes = _host?.GetChatImagePngBytes(chatN2);
                if (bytes == null)
                {
                    int chatImageCount = _host?.GetChatImageCount() ?? 0;
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}': chat_image2=\"{chatN2}\" is not available. " +
                        $"There are {chatImageCount} reachable chat image(s) this session. " +
                        $"Use a smaller index, or drop chat_image2 if a 2nd image isn't needed.");
                    errored = true;
                    return null;
                }
                return bytes;
            }

            // attachN2 > 0
            int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
            byte[] aBytes = _host?.GetTurnAttachmentBytes(attachN2);
            if (aBytes == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' wanted attachment2=\"{attachN2}\" but the user only attached {turnAttachCount} image(s) this turn. " +
                    "Use a smaller index, or drop attachment2 if a 2nd image isn't needed.");
                errored = true;
                return null;
            }
            return aBytes;
        }

        private static string ResolvePresetName(string requested)
        {
            if (string.IsNullOrEmpty(requested)) return null;
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            string presetsDir = System.IO.Path.Combine(projectRoot, "Presets");
            if (!System.IO.Directory.Exists(presetsDir)) return null;

            string requestedFile = requested.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? requested : requested + ".txt";

            // Exact match first.
            string exact = System.IO.Path.Combine(presetsDir, requestedFile);
            if (System.IO.File.Exists(exact))
                return requestedFile;

            // Case-insensitive fallback.
            foreach (var path in System.IO.Directory.GetFiles(presetsDir, "*.txt"))
            {
                string name = System.IO.Path.GetFileName(path);
                if (string.Equals(name, requestedFile, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        // ---------- read_skill ----------

        private void ExecuteReadSkill(SkillAction action)
        {
            string targetId = action.TargetSkillId;
            if (string.IsNullOrEmpty(targetId))
            {
                _host?.AddSystemInjectionAndBubble(
                    "read_skill needs an id attribute. Example: <aitools_action skill=\"read_skill\" id=\"generate_movie\"/>");
                return;
            }

            var skill = _skills?.GetById(targetId);
            if (skill == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"read_skill: '{targetId}' is not a loaded skill. Known: " +
                    string.Join(", ", GetKnownSkillIds()));
                return;
            }

            string body = skill.RawMarkdown ?? "(skill body is empty)";
            // Apply the same {{...}} -> <prefix>... substitution the system prompt uses,
            // so a read_skill echo stays consistent with the Template line the LLM saw
            // in the main prompt.
            body = SkillManager.ApplyPresetPrefix(body);
            _host?.AddSystemInjectionAndBubble(
                "Full content of skill '" + skill.Id + "':\n" + body);
        }

        // ---------- summarize_with_small_llm ----------

        /// <summary>
        /// Fire a one-shot chat-completion against a small LLM instance; on completion,
        /// inject the result back into the main chat as both a system-role interaction
        /// (so the next turn sees it) and an info bubble (so the user sees it).
        /// Asynchronous: returns immediately, the request runs as a coroutine on the
        /// host's MonoBehaviour.
        /// </summary>
        private void ExecuteSummarizeWithSmallLlm(SkillAction action)
        {
            string prompt = action.Prompt;
            if (string.IsNullOrEmpty(prompt))
            {
                _host?.AddSystemInjectionAndBubble(
                    "summarize_with_small_llm needs a prompt attribute (the small LLM cannot see the chat).");
                return;
            }

            var instanceMgr = LLMInstanceManager.Get();
            if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
            {
                _host?.AddSystemInjectionAndBubble("No LLM instances are configured - cannot delegate.");
                return;
            }

            int targetId = -1;
            int replicaIndex = 0;
            if (action.LlmInstanceId.HasValue)
            {
                var hint = instanceMgr.GetInstance(action.LlmInstanceId.Value);
                if (hint != null && hint.HasCapacity())
                    targetId = hint.instanceID;
            }
            if (targetId < 0)
                targetId = instanceMgr.GetFreeLLM(isSmallJob: true, isVisionJob: false, out replicaIndex);
            if (targetId < 0)
                targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: true, isVisionJob: false, out replicaIndex);
            if (targetId < 0)
            {
                _host?.AddSystemInjectionAndBubble("No small-job-capable LLM is available to delegate to.");
                return;
            }

            var inst = instanceMgr.GetInstance(targetId);
            if (inst == null || inst.settings == null)
            {
                _host?.AddSystemInjectionAndBubble("Picked LLM instance has no settings - cannot delegate.");
                return;
            }

            // Reserve a slot on the small LLM so the rest of the system sees it as busy.
            instanceMgr.SetLLMBusy(targetId, replicaIndex, true);
            _host?.AddInfoBubble($"Delegating to LLM #{targetId} ({inst.providerType} {inst.settings.selectedModel})...");

            var runner = _host?.CoroutineRunner;
            if (runner == null)
            {
                instanceMgr.SetLLMBusy(targetId, replicaIndex, false);
                _host?.AddSystemInjectionAndBubble("No coroutine runner available to dispatch delegated request.");
                return;
            }

            // Build a tiny one-shot prompt: a single user line. No chat history is shared.
            var lines = new Queue<GTPChatLine>();
            lines.Enqueue(new GTPChatLine("system", "You are a focused helper. Do exactly what is asked, briefly."));
            lines.Enqueue(new GTPChatLine("user", prompt));

            int capturedTargetId = targetId;
            int capturedReplicaIndex = replicaIndex;
            string callerLabel = $"LLM #{capturedTargetId}";

            Action<RTDB, JSONObject, string> onDone = (db, json, text) =>
            {
                instanceMgr.SetLLMBusy(capturedTargetId, capturedReplicaIndex, false);

                string clean = (text ?? "").Trim();
                if (string.IsNullOrEmpty(clean) && json != null)
                {
                    try { clean = json["choices"][0]["message"]["content"]; } catch { /* no-op */ }
                }
                if (string.IsNullOrEmpty(clean))
                {
                    _host?.AddSystemInjectionAndBubble($"{callerLabel} returned no content.");
                    return;
                }

                _host?.AddSystemInjectionAndBubble(
                    $"Result from delegated {callerLabel} ({inst.providerType} {inst.settings.selectedModel}):\n{clean}");
            };

            DispatchOneShot(runner, inst, lines, onDone, callerLabel);
        }

        /// <summary>
        /// Fire-and-forget chat completion for delegated one-shot calls (used by both
        /// <see cref="ExecuteSummarizeWithSmallLlm"/> and AIChatPanel's image-caption job).
        /// Supports the OpenAI-compatible / Ollama / LlamaCpp / OpenAI flow (which is
        /// where small/local models almost always live in this app). Other providers fall
        /// back to a clear error so the LLM can pick a different instance next turn.
        /// Image data carried by lines (via GTPChatLine.AddImage) is preserved through the
        /// OpenAI-compatible / LlamaCpp providers' multipart serializers.
        /// </summary>
        public static void DispatchOneShot(
            MonoBehaviour runner,
            LLMInstanceInfo inst,
            Queue<GTPChatLine> lines,
            Action<RTDB, JSONObject, string> onDone,
            string callerLabel)
        {
            var settings = inst.settings;
            var db = new RTDB();
            string apiKey = settings.apiKey ?? "";

            switch (inst.providerType)
            {
                case LLMProvider.Ollama:
                {
                    var mgr = runner.gameObject.AddComponent<TexGenWebUITextCompletionManager>();
                    string serverAddress = settings.endpoint ?? "";
                    string suggestedEndpoint;
                    string json = mgr.BuildForInstructJSON(lines, out suggestedEndpoint, 1024, 0.4f, "chat-instruct", false, null, true, false);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, serverAddress, suggestedEndpoint, null, false, apiKey);
                    break;
                }
                case LLMProvider.LlamaCpp:
                {
                    var mgr = runner.gameObject.AddComponent<TexGenWebUITextCompletionManager>();
                    string serverAddress = settings.endpoint ?? "";
                    string suggestedEndpoint;
                    var llmParms = BuildLLMParmsForInstance(inst);
                    string json = mgr.BuildForInstructJSON(lines, out suggestedEndpoint, 1024, 0.4f, "chat-instruct", false, llmParms, false, true);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, serverAddress, suggestedEndpoint, null, false, apiKey);
                    break;
                }
                case LLMProvider.OpenAICompatible:
                {
                    var mgr = runner.gameObject.AddComponent<OpenAITextCompletionManager>();
                    string serverAddress = settings.endpoint ?? "";
                    string endpoint = serverAddress.TrimEnd('/') + "/v1/chat/completions";
                    string model = settings.selectedModel ?? "";
                    bool isDeepSeek = LLMRequestProfile.IsDeepSeekModel(model);
                    var effort = isDeepSeek
                        ? settings.GetReasoningEffort()
                        : (settings.enableThinking ? LLMReasoningEffort.High : LLMReasoningEffort.Off);
                    float temp = isDeepSeek ? LLMRequestProfile.GetRecommendedTemperature(model, effort, 0.4f) : 0.4f;
                    float? topP = isDeepSeek ? (float?)LLMRequestProfile.GetRecommendedTopP(model, effort, 1.0f) : null;
                    int maxTokens = isDeepSeek ? LLMRequestProfile.GetRecommendedMaxTokens(model, effort, 1024) : 1024;
                    string reasoningEffortParam = isDeepSeek ? LLMReasoningEffortUtil.ToConfigValue(effort) : null;
                    string json = mgr.BuildChatCompleteJSON(lines, maxTokens, temp, model, false,
                        enableThinking: isDeepSeek ? effort != LLMReasoningEffort.Off : settings.enableThinking,
                        topP: topP,
                        customReasoningEffort: reasoningEffortParam);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, endpoint, null, false);
                    break;
                }
                case LLMProvider.OpenAI:
                {
                    var mgr = runner.gameObject.AddComponent<OpenAITextCompletionManager>();
                    string model = string.IsNullOrEmpty(settings.selectedModel) ? "gpt-4o-mini" : settings.selectedModel;
                    string endpoint = "https://api.openai.com/v1/chat/completions";
                    string json = mgr.BuildChatCompleteJSON(lines, 1024, 0.4f, model, false);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, endpoint, null, false);
                    break;
                }
                default:
                    onDone?.Invoke(db, null,
                        $"({callerLabel}) Provider {inst.providerType} is not supported by summarize_with_small_llm yet. " +
                        "Use a small Ollama / OpenAICompatible / LlamaCpp / OpenAI instance instead.");
                    break;
            }
        }

        // ---------- Helpers ----------

        private static List<LLMParm> BuildLLMParmsForInstance(LLMInstanceInfo inst)
        {
            var result = new List<LLMParm>();
            if (inst == null || inst.settings == null) return result;

            if (!string.IsNullOrEmpty(inst.settings.selectedModel))
                result.Add(new LLMParm { _key = "model", _value = inst.settings.selectedModel });

            bool isDeepSeek = LLMRequestProfile.IsDeepSeekModel(inst.settings.selectedModel);
            if (isDeepSeek)
            {
                var effort = inst.settings.GetReasoningEffort();
                result.Add(new LLMParm { _key = "reasoning_effort", _value = LLMReasoningEffortUtil.ToConfigValue(effort) });
                result.Add(new LLMParm { _key = "enable_thinking", _value = effort != LLMReasoningEffort.Off ? "true" : "false" });
            }
            else
            {
                result.Add(new LLMParm { _key = "enable_thinking", _value = inst.settings.enableThinking ? "true" : "false" });
            }

            return result;
        }

        private IEnumerable<string> GetKnownSkillIds()
        {
            if (_skills == null) yield break;
            foreach (var s in _skills.GetSkills()) yield return s.Id;
        }

    }
}
