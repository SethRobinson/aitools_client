using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;
using TMPro;
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
        private int _lastLocalOpOutputChatImageIndex = -1;
        private PicMain _lastLocalOpOutputPic;
        private readonly HashSet<SkillAction> _reloadAttemptedActions = new HashSet<SkillAction>();
        private const float ChatImageReloadPollSeconds = 0.2f;
        // Anchor GPU renders routinely exceed the old fixed 12s deadline. We now
        // wait as long as the referenced chat-image Pic is still generating, with
        // a generous absolute safety cap and a short grace for "not busy yet"
        // (job queued but no GPU server has picked it up).
        private const float ChatImageReloadAbsoluteCapSeconds = 600f;
        private const float ChatImageNotYetBusyGraceSeconds = 20f;

        // ----- Per-turn serial action scheduler -----
        // Skill action tags stream from the LLM and were historically executed
        // synchronously in arrival order. When an action defers (waits for an
        // anchor image to finish rendering), later actions used to keep running
        // immediately and chain="true" steps landed on the wrong Pic (the raw
        // anchor) instead of the not-yet-spawned page. This queue enforces
        // strict ordering: once an action defers, every following action stays
        // queued until the deferred one completes. All on the Unity main thread,
        // so no locking is needed.
        private readonly Queue<SkillAction> _actionQueue = new Queue<SkillAction>();
        private enum PumpState { Idle, Running, Blocked }
        private PumpState _pumpState = PumpState.Idle;
        // True while inside the synchronous drain loop - a nested Execute() call
        // (from the deferred coroutine or the chain-rescue re-dispatch) must run
        // its one action without starting a second drain.
        private bool _draining = false;
        // Set deep in the call stack (TryDeferActionUntilChatImageReady) to tell
        // the pump the action it just ran has parked itself on a coroutine.
        private bool _lastActionDeferred = false;
        // The action currently blocking the pump (diagnostics + resume match).
        private SkillAction _blockingAction = null;
        // Incremented every turn reset. A deferred coroutine captures the epoch
        // at start and bails if it changed, so a previous turn's book page can
        // never spawn into a new turn.
        private int _turnEpoch = 0;

        // Preset filenames the chat successfully resolved this SESSION, most-recent first
        // (capped). Used as the tiebreaker when a fuzzy preset match is otherwise
        // ambiguous: the model almost always re-typos a name it JUST used correctly (e.g.
        // used "...Klein Edit", then asks for "...Edit"), and several real presets can be
        // equally close by raw edit distance ("Klein Edit" vs "Qwen Edit"). NOT reset per
        // turn - cross-turn usage is exactly the disambiguation signal we want.
        private readonly List<string> _recentlyResolvedPresets = new List<string>();
        private const int RecentPresetCap = 12;

        private void RecordResolvedPreset(string onDiskName)
        {
            if (string.IsNullOrEmpty(onDiskName)) return;
            _recentlyResolvedPresets.RemoveAll(p => string.Equals(p, onDiskName, StringComparison.OrdinalIgnoreCase));
            _recentlyResolvedPresets.Insert(0, onDiskName);
            if (_recentlyResolvedPresets.Count > RecentPresetCap)
                _recentlyResolvedPresets.RemoveAt(_recentlyResolvedPresets.Count - 1);
        }

        public SkillActionExecutor(SkillManager skills, IChatHost host)
        {
            _skills = skills;
            _host = host;
        }

        /// <summary>
        /// Enqueue a parsed action for in-order execution. The pump drains the
        /// queue synchronously; if an action defers, the pump parks and the rest
        /// of the turn's actions wait behind it.
        /// </summary>
        public void EnqueueAction(SkillAction action)
        {
            _actionQueue.Enqueue(action);
            PumpQueue();
        }

        private void PumpQueue()
        {
            if (_pumpState == PumpState.Blocked) return; // parked on a deferred action
            if (_draining) return;                       // re-entrant; outer loop continues

            _draining = true;
            _pumpState = PumpState.Running;
            try
            {
                while (_actionQueue.Count > 0)
                {
                    var action = _actionQueue.Peek(); // keep at head until it completes
                    _lastActionDeferred = false;
                    try
                    {
                        ExecuteInternal(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("SkillActionExecutor: ExecuteInternal threw: " + ex);
                        _host?.AddInfoBubble("Skill error: " + ex.Message);
                        // Swallow: treat as completed so one bad action can't wedge
                        // the whole queue (matches the old per-action isolation).
                    }

                    if (_lastActionDeferred)
                    {
                        // Action parked itself on a coroutine. Leave it at the
                        // head and stop draining; the coroutine resumes us.
                        _blockingAction = action;
                        _pumpState = PumpState.Blocked;
                        return;
                    }

                    _actionQueue.Dequeue();
                }
                _pumpState = PumpState.Idle;
            }
            finally
            {
                _draining = false;
            }
        }

        /// <summary>
        /// Called by the deferred-action coroutine once its resource is ready
        /// (or it gave up). Drops the finished action from the head and resumes
        /// the pump so queued followers run - now correctly onto the page Pic
        /// the deferred action just spawned.
        /// </summary>
        private void ResumePumpAfterDeferredComplete(SkillAction completed)
        {
            if (_actionQueue.Count > 0 && ReferenceEquals(_actionQueue.Peek(), completed))
                _actionQueue.Dequeue();
            _blockingAction = null;
            if (_pumpState == PumpState.Blocked)
                _pumpState = PumpState.Idle;
            PumpQueue();
        }

        /// <summary>
        /// Reset all per-turn scheduler state. Called in lockstep with the
        /// host's chain-target reset on send / clear / stop. Bumps the turn
        /// epoch so any still-alive deferred coroutine from the previous turn
        /// bails instead of spawning a stale page.
        /// </summary>
        public void ResetForNewTurn()
        {
            _actionQueue.Clear();
            _reloadAttemptedActions.Clear();
            _pumpState = PumpState.Idle;
            _draining = false;
            _lastActionDeferred = false;
            _blockingAction = null;
            _lastLocalOpOutputChatImageIndex = -1;
            _lastLocalOpOutputPic = null;
            _turnEpoch++;
        }

        /// <summary>
        /// Run a single action synchronously, end to end. This is the legacy /
        /// recursive entry point: the deferred-action coroutine re-runs its
        /// action through here, and the chain-rescue path re-dispatches through
        /// here. It deliberately does NOT touch the queue or pump - ordering for
        /// those callers is handled by <see cref="ResumePumpAfterDeferredComplete"/>.
        /// </summary>
        public void Execute(SkillAction action)
        {
            ExecuteInternal(action);
        }

        private void ExecuteInternal(SkillAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.SkillId))
            {
                _host?.AddInfoBubble("Skill error: empty or malformed action tag.");
                return;
            }

            if (_host?.GetLastSpawnedPicForTurn() == null)
            {
                _lastLocalOpOutputChatImageIndex = -1;
                _lastLocalOpOutputPic = null;
            }

            // Normalize common short-form / alias names the LLM tends to invent
            // (e.g. "paste" instead of "paste_image", "border" instead of
            // "add_border"). The dispatcher below is exact-match on the canonical
            // ids; without aliasing, every invented short name dies with "Skill X
            // is not recognized" and wastes the user's turn. The mapping is small
            // and one-way - canonical ids always win when they're already correct.
            string normalizedId = NormalizeSkillId(action.SkillId);
            if (normalizedId != action.SkillId)
            {
                action.SkillId = normalizedId;
            }

            // Rewrite any chat_image="<name>" into chat_image="<number>" using the host's
            // character-anchor registry, BEFORE the slot logic below (which parses those
            // attributes as integers). Idempotent, so the deferred re-execution path is
            // safe. Done here in the dispatcher so every skill that reads chat_image*
            // benefits, not just image_to_image.
            NormalizeAnchorRefs(action);

            // Editor-only: record the raw tool call (skill id + every attribute) so
            // the AI Chat log shows exactly what the model emitted - including the
            // full generate_image prompt (where poster/book text sometimes gets
            // baked in instead of being laid out with draw_text).
            AIChatLog.Action(action.SkillId, action.Args);

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

                // ----- Composition primitives (C#-side image ops, no GPU). -----
                // Wrapped in a per-skill try so a buggy or malformed composition tag
                // surfaces a useful error in the chat (and a full stack to the Unity
                // console) instead of taking down the whole assistant turn with the
                // generic "Skill error: ..." catch in AIChatPanel.OnSkillActionParsed.
                case BuiltInSkillIds.DrawText:
                    SafelyRunCompositionSkill(action, ExecuteDrawText);
                    break;
                case BuiltInSkillIds.AddBorder:
                    SafelyRunCompositionSkill(action, ExecuteAddBorder);
                    break;
                case BuiltInSkillIds.PasteImage:
                    SafelyRunCompositionSkill(action, ExecutePasteImage);
                    break;
                case BuiltInSkillIds.NewCanvas:
                    SafelyRunCompositionSkill(action, ExecuteNewCanvas);
                    break;
                case BuiltInSkillIds.CropResize:
                    SafelyRunCompositionSkill(action, ExecuteCropResize);
                    break;
                case BuiltInSkillIds.DrawShape:
                    SafelyRunCompositionSkill(action, ExecuteDrawShape);
                    break;

                default:
                    // Rescue: the LLM emitted a RECIPE skill id directly (e.g.
                    // skill="ideo" or skill="books") instead of that skill's actual
                    // template (read_skill, or generate_image with a specific preset).
                    // Easy mistake - the SKILLS summary block lists recipe ids right
                    // next to the executable ones. Treat it as read_skill for that id:
                    // the full body lands in the LLM's context with "act on this next
                    // turn" framing, so the turn isn't wasted on a dead-end error.
                    if (_skills?.GetById(action.SkillId) != null)
                    {
                        _host?.AddSystemInjectionSilent(
                            $"'{action.SkillId}' is a recipe/knowledge skill, not a directly executable action - " +
                            "never emit it as skill=\"...\". Its body has been loaded below; on your NEXT turn, " +
                            "follow the Invocation section in it (typically generate_image / image_to_image " +
                            "with a specific preset) to fulfill the user's request.");
                        action.Args["id"] = action.SkillId;
                        ExecuteReadSkill(action);
                        break;
                    }
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}' is not recognized. Use one of: " +
                        string.Join(", ", GetKnownSkillIds()));
                    break;
            }
        }

        // ---------- Generate (image or movie) ----------

        private void ExecuteGenerate(SkillAction action, bool useAttachment)
        {
            _lastLocalOpOutputChatImageIndex = -1;
            _lastLocalOpOutputPic = null;

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

            // Past here this is a fresh, unchained spawn. Mark the chain target stale so that
            // if this spawn FAILS below (preset not found, unresolved source, decode error), a
            // following chain="true" decorator errors cleanly instead of stacking onto - and
            // corrupting - the previous page's Pic. The successful spawn clears it via
            // SetLastSpawnedPicForTurn; a deferred spawn re-runs and clears it on completion.
            _host?.MarkChainTargetStale();

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
                    if (!TryResolveChatImageBytesOrDefer(action, action.SkillId, "chat_image", chatN, out attachmentBytes, out bool deferred))
                    {
                        if (deferred) return;
                        byte[] fallbackBytes = TryFallbackChatImageBytes(action, action.SkillId, chatN, chatImageCount);
                        if (fallbackBytes == null)
                        {
                            _host?.AddSystemInjectionAndBubble(
                                $"Skill '{action.SkillId}': chat_image=\"{chatN}\" is not available. " +
                                $"There are {chatImageCount} numbered chat image slot(s) this session. " +
                                $"Use a smaller index, ask the user to paste an image, or use generate_image instead.");
                            return;
                        }
                        attachmentBytes = fallbackBytes;
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
                    int implicitIdx = _host?.GetLatestChatImageIndex() ?? 0;
                    if (implicitIdx <= 0)
                    {
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}' needs the user to paste an image into the chat first " +
                            "(or you can reference an earlier chat image once one exists, via chat_image=\"N\"). " +
                            "There are no live chat images right now.");
                        return;
                    }

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
                    if (!TryResolveChatImageBytesOrDefer(action, action.SkillId, "implicit chat_image", implicitIdx, out attachmentBytes, out bool deferred))
                    {
                        if (deferred) return;
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
                        "There are no chat images right now.");
                    return;
                }
            }

            // Resolve optional extra input images (slots 2..5). Used by N-input presets
            // (Image To Image Klein Edit 2/3/4/5 Input). Each returns null when the LLM
            // didn't ask for that slot; returns null + emits a bubble when it asked for
            // one that isn't available (caller bails).
            byte[] attachmentBytes2 = ResolveExtraInputBytes(action, 2, out bool errored2, out bool deferred2);
            if (errored2 || deferred2) return;
            byte[] attachmentBytes3 = ResolveExtraInputBytes(action, 3, out bool errored3, out bool deferred3);
            if (errored3 || deferred3) return;
            byte[] attachmentBytes4 = ResolveExtraInputBytes(action, 4, out bool errored4, out bool deferred4);
            if (errored4 || deferred4) return;
            byte[] attachmentBytes5 = ResolveExtraInputBytes(action, 5, out bool errored5, out bool deferred5);
            if (errored5 || deferred5) return;

            // Auto-downgrade preset name when fewer inputs were wired than the preset
            // expects. The LLM frequently picks a "5 Input" preset but only supplies 4
            // anchors (or picks "3 Input" with only one anchor) - without this rescue,
            // PicMain's @upload|imageN|inputN| step aborts at runtime with "Need imageN
            // image first!" and the failure NEVER reaches the LLM, so it can't learn.
            // Rewriting the preset to match the actual input count avoids the dead-end.
            int wiredInputCount = (useAttachment && attachmentBytes != null ? 1 : 0)
                + (attachmentBytes2 != null ? 1 : 0)
                + (attachmentBytes3 != null ? 1 : 0)
                + (attachmentBytes4 != null ? 1 : 0)
                + (attachmentBytes5 != null ? 1 : 0);
            preset = DowngradePresetToInputCount(preset, wiredInputCount, action.SkillId);

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
            string resolved = ResolvePresetName(preset, _recentlyResolvedPresets, out bool presetFuzzy);
            if (resolved == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}': preset '{preset}' was not found in Presets/. " +
                    "Re-pick from the list shown in your skill description.");
                return;
            }
            if (presetFuzzy)
                _host?.AddInfoBubble(
                    $"(preset '{preset}' wasn't found - used the closest match '{resolved}' instead. Use that exact name next time.)");
            RecordResolvedPreset(resolved);

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

            // Optional extra inputs (slots 2..5) - feed the workflow's "image2".."image5"
            // upload slots. The Pic takes ownership of each texture and uploads it (no
            // display, no mask).
            if (!TryWireExtraInput(picMain, attachmentBytes2, 2, action.SkillId)) return;
            if (!TryWireExtraInput(picMain, attachmentBytes3, 3, action.SkillId)) return;
            if (!TryWireExtraInput(picMain, attachmentBytes4, 4, action.SkillId)) return;
            if (!TryWireExtraInput(picMain, attachmentBytes5, 5, action.SkillId)) return;

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
            // slot on PicMain (see PicMain.UpdateJobs around m_requestedServerID), but as a
            // SOFT preference: if the chosen GPU is busy when this pic is ready to run, the
            // scheduler falls back to any free GPU instead of waiting. The LLM picks GPUs
            // from a snapshot frozen at turn-start and routinely collides (e.g. pins 4 movies
            // to gpus 0,2,0,2), so a hard pin would deadlock half the batch on busy GPUs
            // while others sit idle. Soft fallback also keeps the main_prompt's promise that
            // a specified-but-busy gpu falls back automatically.
            if (action.GpuId.HasValue)
            {
                int gpu = action.GpuId.Value;
                if (gpu >= 0 && gpu < Config.Get().GetGPUCount())
                {
                    picMain.m_requestedServerID = gpu;
                    picMain.m_requestedServerIsPreference = true;
                }
            }

            // Install a workflow-error reporter so PicMain's runtime aborts (e.g.
            // "Need image5 image first!" when an @upload step can't find its source)
            // surface back to the AI Chat as a system injection. Otherwise those errors
            // only land in the Pic's status text and the LLM has no idea anything went
            // wrong - it just keeps emitting the same broken action on subsequent turns.
            WireWorkflowErrorReporter(picMain, action.SkillId, resolved);

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
            // LIFO match: pop the MOST-RECENT unchained Pic from the stack so a paired
            // "gen A, gen B, mov, mov" reply animates mov1->B, mov2->A (each chained
            // generate gets a distinct source). Falls back to GetLastSpawnedPicForTurn (the
            // head) when the stack is empty, so a 3+ step chain on one root Pic still works.
            // The previous design used GetLastSpawnedPicForTurn directly, which made every
            // chain pile onto the most-recent Pic - so the grouped reply above produced two
            // LTX videos stacked on the second image instead of one each. NOTE: this CONSUME
            // (pop) is correct for chained GENERATES; chained LOCAL composition ops use
            // PeekChainTarget() (non-consuming) so border+text+number all decorate one image.
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
                int latestChatImageIndex = _host?.GetLatestChatImageIndex() ?? 0;
                if (latestChatImageIndex > 0)
                {
                    action.Args["chat_image"] = latestChatImageIndex.ToString();
                    action.Args.Remove("chain");
                    _host?.AddInfoBubble(
                        $"(translated chain=\"true\" -> chat_image=\"{latestChatImageIndex}\" - chain only works within the SAME reply; using the latest chat image instead)");
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

            // Tolerate the very common small-model slip of pairing chain="true" with a
            // (usually self-predicted) primary chat_image / attachment - e.g. a multi-beat
            // reply where the model both chains the movie onto its just-made composite AND
            // redundantly points chat_image at that same composite's guessed slot number.
            // We only reach here when a real same-reply chain target exists (prevPic was
            // non-null above), so chain is the correct intent: silently drop the stray
            // primary input and proceed instead of erroring and throwing away the render.
            // Extra slots chat_image2..5 / attachment2..5 are left intact - those are
            // legitimate multi-input references resolved below.
            if (action.AttachmentIndex.HasValue || action.ChatImageIndex.HasValue)
            {
                action.Args.Remove("chat_image");
                action.Args.Remove("attachment");
                Debug.Log($"SkillActionExecutor: dropped stray primary chat_image/attachment on chained '{action.SkillId}' - chain=\"true\" already supplies input1.");
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

            // Optional extra inputs (slots 2..5) - chain inherits image1 from the prior
            // step, but the LLM can still bring separate image2..image5 references in via
            // attachment{N} / chat_image{N} for N-input presets.
            byte[] chainBytes2 = ResolveExtraInputBytes(action, 2, out bool chainErr2, out bool chainDef2);
            if (chainErr2 || chainDef2) return;
            byte[] chainBytes3 = ResolveExtraInputBytes(action, 3, out bool chainErr3, out bool chainDef3);
            if (chainErr3 || chainDef3) return;
            byte[] chainBytes4 = ResolveExtraInputBytes(action, 4, out bool chainErr4, out bool chainDef4);
            if (chainErr4 || chainDef4) return;
            byte[] chainBytes5 = ResolveExtraInputBytes(action, 5, out bool chainErr5, out bool chainDef5);
            if (chainErr5 || chainDef5) return;

            // Auto-downgrade preset to match wired input count (see ExecuteGenerate for
            // the rationale). Chain always provides image1 from the prior step's output,
            // so wired = 1 + (non-null extras). Done BEFORE ResolvePresetName so the
            // resolver sees the corrected filename.
            int chainWiredInputCount = 1
                + (chainBytes2 != null ? 1 : 0)
                + (chainBytes3 != null ? 1 : 0)
                + (chainBytes4 != null ? 1 : 0)
                + (chainBytes5 != null ? 1 : 0);
            preset = DowngradePresetToInputCount(preset, chainWiredInputCount, action.SkillId);

            string resolved = ResolvePresetName(preset, _recentlyResolvedPresets, out bool presetFuzzy);
            if (resolved == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' (chain=\"true\"): preset '{preset}' was not found in Presets/. " +
                    "Re-pick from the list shown in your skill description.");
                return;
            }
            if (presetFuzzy)
                _host?.AddInfoBubble(
                    $"(preset '{preset}' wasn't found - used the closest match '{resolved}' instead. Use that exact name next time.)");
            RecordResolvedPreset(resolved);

            if (!TryWireExtraInput(prevPic, chainBytes2, 2, action.SkillId)) return;
            if (!TryWireExtraInput(prevPic, chainBytes3, 3, action.SkillId)) return;
            if (!TryWireExtraInput(prevPic, chainBytes4, 4, action.SkillId)) return;
            if (!TryWireExtraInput(prevPic, chainBytes5, 5, action.SkillId)) return;

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

            // Same workflow-error reporter as the non-chained path: surface PicMain
            // runtime aborts back to the LLM as a system injection.
            WireWorkflowErrorReporter(prevPic, action.SkillId, resolved);

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
        /// Thin wrapper over <see cref="ResolveExtraInputBytes"/> for backwards-compat call sites.
        /// </summary>
        private byte[] ResolveSecondInputBytes(SkillAction action, out bool errored, out bool deferred)
        {
            return ResolveExtraInputBytes(action, 2, out errored, out deferred);
        }

        /// <summary>
        /// Resolve an optional extra input image (slot 2..5) to PNG bytes. Reads
        /// attachment{slot} / chat_image{slot} from the action args. Returns null when
        /// the LLM didn't ask for an image at that slot; returns null and sets
        /// <paramref name="errored"/>=true (after emitting a system-injection bubble)
        /// when it asked for one that isn't available; callers should bail in that case.
        /// chat_image{slot} wins over attachment{slot} if both are set, matching the
        /// precedence rule on the primary slot.
        /// </summary>
        private byte[] ResolveExtraInputBytes(SkillAction action, int slot, out bool errored, out bool deferred)
        {
            errored = false;
            deferred = false;
            if (slot < 2 || slot > 5) return null;

            int chatN = action.GetExtraChatImageIndex(slot) ?? -1;
            int attachN = action.GetExtraAttachmentIndex(slot) ?? -1;
            if (chatN <= 0 && attachN <= 0) return null;

            string chatKey = "chat_image" + slot;
            string attachKey = "attachment" + slot;

            if (chatN > 0)
            {
                if (!TryResolveChatImageBytesOrDefer(action, action.SkillId, chatKey, chatN, out byte[] bytes, out deferred))
                {
                    if (deferred) return null;
                    int chatImageCount = _host?.GetChatImageCount() ?? 0;
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}': {chatKey}=\"{chatN}\" is not available. " +
                        $"There are {chatImageCount} numbered chat image slot(s) this session. " +
                        $"Use a smaller index, or drop {chatKey} if an input at slot {slot} isn't needed.");
                    errored = true;
                    return null;
                }
                return bytes;
            }

            // attachN > 0
            int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
            byte[] aBytes = _host?.GetTurnAttachmentBytes(attachN);
            if (aBytes == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' wanted {attachKey}=\"{attachN}\" but the user only attached {turnAttachCount} image(s) this turn. " +
                    $"Use a smaller index, or drop {attachKey} if an input at slot {slot} isn't needed.");
                errored = true;
                return null;
            }
            return aBytes;
        }

        /// <summary>
        /// Decode <paramref name="bytes"/> as a texture and attach it to <paramref name="pic"/>'s
        /// image{slot} input slot (slot 2..5). Used by N-input presets. Returns false (after
        /// emitting a system-injection bubble) when bytes are present but undecodable, so the
        /// caller can bail before queuing the workflow.
        /// </summary>
        private bool TryWireExtraInput(PicMain pic, byte[] bytes, int slot, string skillId)
        {
            if (bytes == null) return true;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}': could not decode input image at slot {slot}.");
                return false;
            }
            switch (slot)
            {
                case 2: pic.SetImage2(tex); break;
                case 3: pic.SetImage3(tex); break;
                case 4: pic.SetImage4(tex); break;
                case 5: pic.SetImage5(tex); break;
                default:
                    UnityEngine.Object.Destroy(tex);
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}': internal error - unsupported input slot {slot}.");
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Install a callback on <paramref name="pic"/> that surfaces workflow-runtime
        /// aborts back to the chat as a system injection (so the LLM sees the failure
        /// on its next turn). The callback also tags the message with the spawning
        /// skill + preset for context, since the abort itself fires deep inside
        /// PicMain's job queue with no knowledge of either. <see cref="IChatHost"/>
        /// captured into a local so the callback survives even if <c>_host</c> is
        /// later replaced. Idempotent per Pic - PicMain self-nulls the field after
        /// invoking it (see <c>ReportWorkflowAbortOnce</c>).
        /// </summary>
        private void WireWorkflowErrorReporter(PicMain pic, string skillId, string presetName)
        {
            if (pic == null) return;
            IChatHost capturedHost = _host;
            if (capturedHost == null) return;
            pic.m_workflowErrorReporter = (msg) =>
            {
                capturedHost.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}' (preset '{presetName}'): {msg}");
            };
        }

        /// <summary>
        /// If <paramref name="preset"/> names an "N Input" multi-input variant (e.g.
        /// "Image To Image Klein Edit 5 Input.txt") and the actual <paramref name="wiredCount"/>
        /// of inputs is smaller than N, return the smaller-N variant instead (or the
        /// suffix-less base name when wired==1). Returns <paramref name="preset"/>
        /// unchanged when no rewrite applies. Emits an info bubble whenever a rewrite
        /// happens so the user (and the LLM, on its next turn via the bubble) sees the
        /// switch. <paramref name="wiredCount"/> ≤ 0 disables the downgrade (used for
        /// generate-only skills where no source image is wired).
        /// </summary>
        private string DowngradePresetToInputCount(string preset, int wiredCount, string skillId)
        {
            if (string.IsNullOrEmpty(preset) || wiredCount <= 0) return preset;

            // Strip the .txt extension if present so we can pattern-match on the stem,
            // then re-attach it at the end. Match is case-insensitive to forgive LLM
            // capitalization drift ("5 input" / "5 Input" / "5 INPUT").
            bool hadTxt = preset.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            string stem = hadTxt ? preset.Substring(0, preset.Length - 4) : preset;

            // Look for " <N> Input" at the END of the stem (N in 2..5). Don't match
            // mid-string variants - that would corrupt presets that legitimately have
            // "Input" elsewhere in their name.
            var match = System.Text.RegularExpressions.Regex.Match(
                stem, @"\s([2-5])\s+Input\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return preset;

            int presetN = int.Parse(match.Groups[1].Value);
            if (wiredCount >= presetN) return preset; // already matches or oversupplied

            string stemBase = stem.Substring(0, match.Index); // drop the " N Input" suffix
            string newStem = wiredCount == 1
                ? stemBase
                : stemBase + " " + wiredCount + " Input";
            string newPreset = hadTxt ? newStem + ".txt" : newStem;

            _host?.AddInfoBubble(
                $"(auto-downgraded preset '{preset}' -> '{newPreset}' for {skillId} - only {wiredCount} input(s) were wired, but the preset wanted {presetN}.)");
            return newPreset;
        }

        private static string ResolvePresetName(string requested, IReadOnlyList<string> recentPresets, out bool fuzzyCorrected)
        {
            fuzzyCorrected = false;
            if (string.IsNullOrEmpty(requested)) return null;
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            string presetsDir = System.IO.Path.Combine(projectRoot, "Presets");
            if (!System.IO.Directory.Exists(presetsDir)) return null;

            string requestedFile = requested.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? requested : requested + ".txt";

            string found = FindPresetFile(presetsDir, requestedFile);
            if (found != null)
                return found;

            string prefix = PlayerPrefs.GetString(SkillManager.PresetPrefixPrefsKey, "");
            if (!string.IsNullOrEmpty(prefix))
            {
                string withoutLeadingUnderscore = requestedFile.TrimStart('_');
                if (!requestedFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    found = FindPresetFile(presetsDir, prefix + withoutLeadingUnderscore);
                    if (found != null)
                        return found;
                }
                else
                {
                    // Reverse fallback: the system prompt shows every {{...}} preset
                    // sentinel WITH the prefix applied (ApplyPresetPrefix), so the LLM
                    // faithfully asks for e.g. "test_Prompt To Image (Ideogram 4).txt"
                    // even when no test_ variant of that particular preset exists on
                    // disk. Fall back to the unprefixed file instead of dead-ending.
                    found = FindPresetFile(presetsDir, requestedFile.Substring(prefix.Length));
                    if (found != null)
                        return found;
                }
            }

            if (requestedFile.StartsWith("_", StringComparison.Ordinal))
            {
                found = FindPresetFile(presetsDir, requestedFile.TrimStart('_'));
                if (found != null)
                    return found;
            }

            // Fuzzy last resort: the LLM often drops or garbles a word in a long preset
            // name ("Image To Image Edit" / "Image To Image Klein" for "Image To Image
            // Klein Edit"). If exactly one preset is a close, unambiguous match, use it
            // instead of dead-ending - which otherwise costs the model a whole turn.
            string fuzzy = FindClosestPresetFile(presetsDir, requestedFile, prefix, recentPresets);
            if (fuzzy != null)
            {
                fuzzyCorrected = true;
                return fuzzy;
            }

            return null;
        }

        private static string FindPresetFile(string presetsDir, string requestedFile)
        {
            if (string.IsNullOrEmpty(requestedFile))
                return null;

            string exact = System.IO.Path.Combine(presetsDir, requestedFile);
            if (System.IO.File.Exists(exact))
                return requestedFile;

            foreach (var path in System.IO.Directory.GetFiles(presetsDir, "*.txt"))
            {
                string name = System.IO.Path.GetFileName(path);
                if (string.Equals(name, requestedFile, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            return null;
        }

        /// <summary>
        /// Last-resort fuzzy resolver: return the on-disk preset whose normalized name is
        /// the CLOSEST match to <paramref name="requestedFile"/>, but ONLY when that match
        /// is both close and clearly unambiguous. Catches the common LLM slip of dropping
        /// or garbling a word in a long preset name ("Image To Image Edit" ->
        /// "Image To Image Klein Edit"). Returns the on-disk filename, or null when no
        /// confident match exists (caller then errors as before). Deliberately conservative
        /// so two similarly-named presets never silently resolve to the wrong one.
        /// </summary>
        private static string FindClosestPresetFile(string presetsDir, string requestedFile, string prefix, IReadOnlyList<string> recentPresets)
        {
            string reqNorm = NormalizePresetName(requestedFile, prefix);
            if (reqNorm.Length < 4) return null; // too short to disambiguate safely
            int maxAllowed = Math.Min(8, Math.Max(2, reqNorm.Length / 3));

            // Anchor on the FIRST WORD and trust it: the model reliably gets the leading
            // token right, and that token is where any preset PREFIX lives ("custom_Image",
            // "hirez_Image", "test_Image", ...). Only presets sharing that EXACT first word
            // are candidates, so fuzzy matching can fix a dropped/garbled LATER word but can
            // never jump across prefixes or onto an unrelated preset family.
            string reqFirst = FirstWord(requestedFile);
            if (reqFirst.Length == 0) return null;

            // Group surviving files by normalized name so any same-name duplicates collapse
            // to one candidate (they share the first word, so the same prefix too).
            var byNorm = new Dictionary<string, List<string>>();
            var dist = new Dictionary<string, int>();
            string bestNorm = null;
            int best = int.MaxValue, second = int.MaxValue;
            foreach (var path in System.IO.Directory.GetFiles(presetsDir, "*.txt"))
            {
                string name = System.IO.Path.GetFileName(path);
                if (!string.Equals(FirstWord(name), reqFirst, StringComparison.OrdinalIgnoreCase))
                    continue; // different first word (incl. a different prefix) - never substitute
                string nm = NormalizePresetName(name, prefix);
                if (!byNorm.TryGetValue(nm, out var list))
                {
                    list = new List<string>();
                    byNorm[nm] = list;
                    int d = LevenshteinDistance(reqNorm, nm);
                    dist[nm] = d;
                    if (d < best) { second = best; best = d; bestNorm = nm; }
                    else if (d < second) { second = d; }
                }
                list.Add(name);
            }
            if (bestNorm == null || best > maxAllowed) return null;

            // When the closest match isn't strictly unique (a near-tie within 1 edit - e.g.
            // "...Edit" is equally close to "...Klein Edit" AND "...Qwen Edit"), break the
            // tie with RECENT successful usage: the model overwhelmingly re-typos a name it
            // just used. Prefer the closest in-range preset that was recently resolved.
            if (second - best < 2)
            {
                string recentPick = null;
                int recentBest = int.MaxValue;
                if (recentPresets != null)
                {
                    foreach (string recent in recentPresets)
                    {
                        string rn = NormalizePresetName(recent, prefix);
                        if (dist.TryGetValue(rn, out int rd) && rd <= maxAllowed && rd < recentBest)
                        {
                            recentBest = rd;
                            recentPick = rn;
                        }
                    }
                }
                if (recentPick == null) return null; // genuinely ambiguous, no usage hint - don't guess
                bestNorm = recentPick;
            }

            // All candidates share the first word (hence the same prefix), so just return
            // the winning preset's on-disk file.
            return byNorm[bestNorm][0];
        }

        /// <summary>
        /// The first whitespace-delimited token of a preset filename (".txt" dropped, any
        /// prefix kept, lowercased). Fuzzy matching requires this to match EXACTLY, so a
        /// prefix baked into the first token ("custom_Image") is never silently swapped for
        /// another ("hirez_Image" / "Image") and the model's leading word is taken as truth.
        /// </summary>
        private static string FirstWord(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string s = fileName.Trim();
            if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        }

        /// <summary>
        /// Normalize a preset filename for fuzzy comparison: drop ".txt" and the active
        /// preset prefix / leading underscore, lowercase, and collapse whitespace runs.
        /// "test_Image To Image  Klein Edit.txt" -> "image to image klein edit".
        /// </summary>
        private static string NormalizePresetName(string fileName, string prefix)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string s = fileName.Trim();
            if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            if (!string.IsNullOrEmpty(prefix) && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(prefix.Length);
            s = s.TrimStart('_').Trim().ToLowerInvariant();
            var sb = new System.Text.StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (char c in s)
            {
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace) { if (!prevSpace) sb.Append(' '); }
                else sb.Append(c);
                prevSpace = isSpace;
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Iterative two-row Levenshtein edit distance. Inputs are short preset names, so
        /// the per-call allocation is negligible.
        /// </summary>
        private static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[m];
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

            // Inject the body INTO THE LLM's context (so it sees it on its next turn)
            // but DON'T splash the entire markdown body into the chat as a bubble. That
            // pattern was loud, and the LLM can't act on the body within the current
            // stream anyway (mid-stream skill output doesn't reach the model). Show a
            // small info indicator so the user knows the load happened, plus a system
            // injection telling the LLM that the body it just received is reference
            // material - it should USE that knowledge on the very next user turn,
            // not call read_skill again or wait for confirmation.
            _host?.AddSystemInjectionSilent(
                "Reference material for skill '" + skill.Id + "' (the full body of " +
                "aichat/skills/" + skill.Id + ".md). Use this knowledge directly on " +
                "the next assistant turn - do NOT call read_skill again for this id, " +
                "and do NOT ask the user 'should I proceed' - just act on what they " +
                "already requested using the patterns documented below.\n\n" + body);
            _host?.AddInfoBubble(
                "(loaded skill '" + skill.Id + "' - the LLM will use it on its next reply. " +
                "read_skill cannot influence the CURRENT reply because mid-stream injections " +
                "don't reach the model.)");
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
                    try { clean = OpenAITextCompletionManager.ExtractTextFromResponseJSON(json); } catch { /* no-op */ }
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
        /// Supports OpenAI-compatible / Ollama / LlamaCpp / OpenAI / Anthropic / Gemini.
        /// Other providers fall back to a clear error so the LLM can pick a different
        /// instance next turn. Image data carried by lines (via GTPChatLine.AddImage) is
        /// preserved through the OpenAI-compatible / LlamaCpp / Anthropic / Gemini
        /// serializers, so this path covers the vision-caption sidecar as well as plain
        /// text summarization.
        /// </summary>
        public static void DispatchOneShot(
            MonoBehaviour runner,
            LLMInstanceInfo inst,
            Queue<GTPChatLine> lines,
            Action<RTDB, JSONObject, string> onDone,
            string callerLabel,
            string sentJsonFilename = "text_completion_sent.json")
        {
            var settings = inst.settings;
            var db = new RTDB();
            string apiKey = settings.apiKey ?? "";

            // Editor-only: log this sidecar's reply to the AI Chat log under its
            // caller label (e.g. "ImageCaption"). The replies arrive async, outside
            // the request scope below, so wrap onDone to capture them here.
            var realOnDone = onDone;
            onDone = (rtdb, jn, str) =>
            {
                try { AIChatLog.Response(callerLabel, !string.IsNullOrEmpty(str) ? str : (jn != null ? jn.ToString() : "")); } catch { }
                realOnDone?.Invoke(rtdb, jn, str);
            };

            // Forward the request body to the AI Chat log tagged with the caller
            // label. Managers call LogRequest synchronously before their first
            // yield, so this scope is still active when the dispatch below fires.
            using (LLMDebugLog.PurposeScope(callerLabel))
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
                    }, db, serverAddress, suggestedEndpoint, null, false, apiKey, sentJsonFilename);
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
                    }, db, serverAddress, suggestedEndpoint, null, false, apiKey, sentJsonFilename);
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
                    }, db, apiKey, endpoint, null, false, sentJsonFilename);
                    break;
                }
                case LLMProvider.OpenAI:
                {
                    var mgr = runner.gameObject.AddComponent<OpenAITextCompletionManager>();
                    string model = string.IsNullOrEmpty(settings.selectedModel) ? "gpt-4o-mini" : settings.selectedModel;
                    var profile = OpenAIRequestProfileResolver.Resolve(model, settings, 0);
                    string json = mgr.BuildChatCompleteJSON(lines, 1024, 0.4f, model, false,
                        profile.useResponsesAPI, profile.isReasoningModel, profile.includeTemperature,
                        profile.reasoningEffort, profile.enableThinking);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, profile.endpoint, null, false, sentJsonFilename);
                    break;
                }
                case LLMProvider.Anthropic:
                {
                    var mgr = runner.gameObject.AddComponent<AnthropicAITextCompletionManager>();
                    string model = string.IsNullOrEmpty(settings.selectedModel)
                        ? Config.Get().GetAnthropicAI_APIModel()
                        : settings.selectedModel;
                    string endpoint = string.IsNullOrEmpty(settings.endpoint)
                        ? Config.Get().GetAnthropicAI_APIEndpoint()
                        : settings.endpoint;
                    string anthropicKey = string.IsNullOrEmpty(apiKey) ? Config.Get().GetAnthropicAI_APIKey() : apiKey;
                    // Non-streaming: simpler for one-shots, mirrors the OpenAI/Ollama path
                    // above. Anthropic returns content as a typed-block array; we pull text
                    // out via ExtractTextFromResponseJSON so callers see plain text in `str`.
                    string json = mgr.BuildChatCompleteJSON(lines, 1024, 0.4f, model, false);
                    mgr.SpawnChatCompletionRequest(json, (rtdb, jn, str) =>
                    {
                        try
                        {
                            string extracted = str;
                            if (string.IsNullOrEmpty(extracted) && jn != null)
                            {
                                try { extracted = AnthropicAITextCompletionManager.ExtractTextFromResponseJSON(jn); }
                                catch { /* leave empty so caller can report nothing-returned */ }
                            }
                            int extractedLen = extracted == null ? 0 : extracted.Length;
                            Debug.Log($"DispatchOneShot[Anthropic/{callerLabel}]: extracted {extractedLen} chars" +
                                      (extractedLen > 0 ? " preview: " + extracted.Substring(0, System.Math.Min(120, extractedLen)) : ""));
                            onDone(rtdb, jn, extracted ?? "");
                        }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, anthropicKey, endpoint, null, false, sentJsonFilename);
                    break;
                }
                case LLMProvider.Gemini:
                {
                    var mgr = runner.gameObject.AddComponent<GeminiTextCompletionManager>();
                    string model = string.IsNullOrEmpty(settings.selectedModel) ? "gemini-2.5-pro" : settings.selectedModel;
                    string baseEndpoint = string.IsNullOrEmpty(settings.endpoint)
                        ? "https://generativelanguage.googleapis.com/v1beta/models"
                        : settings.endpoint;
                    // Non-streaming one-shot: GeminiTextCompletionManager hands the
                    // already-extracted response text back as `str`. Images carried
                    // by `lines` (via GTPChatLine.AddImage) are serialized as
                    // inlineData parts, so this path covers the vision-caption
                    // sidecar as well as plain text summarization.
                    string endpoint = GeminiTextCompletionManager.BuildEndpointUrl(baseEndpoint, model, false);
                    string json = mgr.BuildChatCompleteJSON(lines, 1024, 0.4f, model, false, settings.enableThinking);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str ?? ""); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, endpoint, null, false);
                    break;
                }
                default:
                    onDone?.Invoke(db, null,
                        $"({callerLabel}) Provider {inst.providerType} is not supported by summarize_with_small_llm yet. " +
                        "Use a small Ollama / OpenAICompatible / LlamaCpp / OpenAI / Anthropic / Gemini instance instead.");
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

        // chat_image slot attributes that may carry a character-anchor NAME instead of
        // a number. Resolved to live slot numbers in NormalizeAnchorRefs so the rest of
        // the executor (which int-parses these) needs no changes.
        private static readonly string[] AnchorRefArgKeys =
            { "chat_image", "chat_image2", "chat_image3", "chat_image4", "chat_image5" };

        /// <summary>
        /// Rewrite any chat_image* attribute whose value is a character-anchor NAME
        /// (e.g. <c>chat_image="Bob"</c>) into its current numeric slot using the host's
        /// anchor registry. Numeric values are left untouched. A name that resolves to no
        /// live slot is dropped (with a help bubble) so the downstream integer parse
        /// doesn't silently treat it as "missing" and the LLM learns to use a valid
        /// anchor or number. Safe to call more than once on the same action (numbers
        /// pass straight through), which the deferred re-execution path relies on.
        /// </summary>
        private void NormalizeAnchorRefs(SkillAction action)
        {
            if (action == null || _host == null) return;

            foreach (string key in AnchorRefArgKeys)
            {
                if (!action.Args.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw))
                    continue;

                string val = raw.Trim();
                if (int.TryParse(val, out _))
                    continue; // already a slot number

                int resolved = _host.ResolveAnchorToIndex(val);
                if (resolved > 0)
                {
                    action.Args[key] = resolved.ToString();
                }
                else
                {
                    action.Args.Remove(key);
                    _host.AddSystemInjectionAndBubble(
                        $"{key}=\"{val}\" did not match any known character anchor. Known anchors are " +
                        "listed in the ANCHORS line of CURRENT STATE - reference one of those by name, " +
                        "or use a numeric chat_image=\"N\".");
                }
            }
        }

        /// <summary>
        /// Map common short-form / abbreviated skill ids the LLM tends to invent
        /// to their canonical names. Returns the input unchanged when no alias
        /// applies. Aliases are intentionally one-way (no canonical -> short
        /// rewrite) so the dispatcher's exact-match switch keeps working as is.
        /// </summary>
        private static string NormalizeSkillId(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            string lower = id.Trim().ToLowerInvariant();
            switch (lower)
            {
                // paste / past / paste_img -> paste_image
                case "paste":
                case "past":
                case "paste_img":
                case "pasteimage":
                    return BuiltInSkillIds.PasteImage;

                // border / addborder -> add_border
                case "border":
                case "addborder":
                    return BuiltInSkillIds.AddBorder;

                // text / drawtext / write / write_text -> draw_text
                case "text":
                case "drawtext":
                case "write":
                case "write_text":
                    return BuiltInSkillIds.DrawText;

                // shape / drawshape / draw_rect / draw_circle -> draw_shape
                case "shape":
                case "drawshape":
                case "draw_rect":
                case "draw_circle":
                case "rect":
                case "circle":
                    return BuiltInSkillIds.DrawShape;

                // canvas / blank / blank_canvas / newcanvas -> new_canvas
                case "canvas":
                case "blank":
                case "blank_canvas":
                case "newcanvas":
                case "create_canvas":
                    return BuiltInSkillIds.NewCanvas;

                // crop / resize / scale -> crop_resize
                case "crop":
                case "resize":
                case "scale":
                case "cropresize":
                    return BuiltInSkillIds.CropResize;

                // generate / gen / image -> generate_image
                case "generate":
                case "gen":
                case "image":
                case "generateimage":
                    return BuiltInSkillIds.GenerateImage;

                // movie / video / generatemovie -> generate_movie
                case "movie":
                case "video":
                case "generatemovie":
                case "generatevideo":
                case "generate_video":
                    return BuiltInSkillIds.GenerateMovie;

                // edit / img2img / imagetoimage -> image_to_image
                case "edit":
                case "img2img":
                case "imagetoimage":
                    return BuiltInSkillIds.ImageToImage;

                // animate / img2vid / img2movie / imagetomovie -> image_to_movie
                case "animate":
                case "img2vid":
                case "img2movie":
                case "imagetomovie":
                case "image_to_video":
                    return BuiltInSkillIds.ImageToMovie;

                default:
                    return id;
            }
        }

        /// <summary>
        /// Run one of the composition Execute* methods, catching any synchronous
        /// exception and surfacing a user-friendly error in the chat (with the
        /// skill id and exception type) instead of letting it bubble up to the
        /// generic AIChatPanel catch. The full stack trace still goes to the
        /// Unity console so we can debug.
        /// </summary>
        private void SafelyRunCompositionSkill(SkillAction action, Action<SkillAction> impl)
        {
            try
            {
                impl(action);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SkillActionExecutor: '{action?.SkillId}' threw {ex.GetType().Name}: {ex}");
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action?.SkillId}' failed with {ex.GetType().Name}: {ex.Message}. " +
                    "See Unity console for the full stack trace. " +
                    "Try simplifying the call (smaller dimensions, fewer attributes, or omit font_name) " +
                    $"and re-emit. Re-read the skill body via <aitools_action skill=\"read_skill\" id=\"{action?.SkillId}\"/> if you're unsure of the attribute syntax.");
            }
        }

        // ===========================================================================
        //   Composition primitives (draw_text / add_border / paste_image /
        //   new_canvas / crop_resize / draw_shape).
        //
        //   All non-canvas ops resolve a "canvas" image the same way image_to_image
        //   does - chat_image="N", attachment="N", or chain="true". On a non-chained
        //   call we spawn a fresh Pic seeded with that image (so the original bubble
        //   is preserved); on chain="true" we stack the local op onto the most-recent
        //   unchained Pic this turn via PicMain.AppendLocalOp so it runs after any
        //   prior workflow steps land. new_canvas is the only skill that takes no
        //   source image (and therefore can't chain).
        // ===========================================================================

        private void ExecuteDrawText(SkillAction action)
        {
            string text = action.GetArg("text") ?? "";
            if (string.IsNullOrEmpty(text))
            {
                _host?.AddSystemInjectionAndBubble(
                    "draw_text needs a non-empty text=\"...\" attribute.");
                return;
            }

            byte[] canvasBytes = ResolveCanvasBytes(action, "draw_text", out bool errored, out bool deferred, allowMissing: false);
            if (errored || deferred) return;

            Func<PicMain, IEnumerator> op = (pic) => DrawTextCoroutine(pic, action, text);
            RunOrChainLocalOp(action, "draw_text", canvasBytes, op);
        }

        private IEnumerator DrawTextCoroutine(PicMain pic, SkillAction action, string text)
        {
            var sprite = pic != null ? pic.m_pic?.sprite : null;
            var dst = sprite != null ? sprite.texture as Texture2D : null;
            if (dst == null)
            {
                Debug.LogWarning("draw_text: Pic has no texture to draw on.");
                yield break;
            }

            int srcW = dst.width;
            int srcH = dst.height;

            int x = ParsePixelOrPercent(action.GetArg("x"), srcW) ?? 0;
            int y = ParsePixelOrPercent(action.GetArg("y"), srcH) ?? 0;
            int w = ParsePixelOrPercent(action.GetArg("width"), srcW) ?? srcW;
            int h = ParsePixelOrPercent(action.GetArg("height"), srcH) ?? srcH;
            int fontSize = ParsePixelOrPercent(action.GetArg("font_size"), srcH) ?? Mathf.Max(16, srcH / 16);
            // Optional auto-size lower bound. When omitted, TMP uses its built-in
            // default (18). Set higher to guarantee body text stays readable even
            // when long; TMP will OVERFLOW the rect rather than shrink below this.
            int minFontSize = ParsePixelOrPercent(action.GetArg("min_font_size"), srcH) ?? 0;

            Color color = ParseColor(action.GetArg("color"), Color.white);
            Color? bgColor = ParseColorOpt(action.GetArg("bg_color"));
            Color? outlineColor = ParseColorOpt(action.GetArg("outline_color"));
            int outlineWidth = ParsePixelOrPercent(action.GetArg("outline_width"), srcH) ?? 0;

            bool bold = ParseBool(action.GetArg("bold"), false);
            bool italic = ParseBool(action.GetArg("italic"), false);
            // Default auto_size=true: we use OUR OWN fit logic (not TMP's built-in
            // enableAutoSizing, which behaves unpredictably in the synchronous
            // render-to-texture path). We measure the text's preferred bounds at a
            // reference fontSize, then scale to fit the rect, clamped by font_size
            // (max) and min_font_size (floor). Result: predictable text that
            // actually fills the rect, regardless of canvas size or text length.
            // Pass auto_size="false" to use font_size as the exact value with no
            // fitting (useful for matching a specific design spec).
            bool autoSize = ParseBool(action.GetArg("auto_size"), true);
            bool wrap = ParseBool(action.GetArg("wrap"), true);
            string align = (action.GetArg("align") ?? "center").Trim().ToLowerInvariant();
            string valign = (action.GetArg("valign") ?? "middle").Trim().ToLowerInvariant();
            TextAlignmentOptions tmpAlign = ResolveTmpAlignment(align, valign);

            FontStyles styles = 0;
            if (bold) styles |= FontStyles.Bold;
            if (italic) styles |= FontStyles.Italic;

            TMP_FontAsset font = ResolveFontByName(action.GetArg("font_name"));
            if (font == null)
            {
                // No font was resolvable at all - TMP would crash internally with an
                // IndexOutOfRange when trying to render. Surface this cleanly instead.
                _host?.AddSystemInjectionAndBubble(
                    "draw_text: no TMP font is available (AIGuideManager font array is empty " +
                    "AND TMP_Settings.defaultFontAsset is null). Open the AI Guide popup once " +
                    "to initialize fonts, or check the project's TMP setup.");
                yield break;
            }

            // Optional background fill behind the text rect (drawn first so text sits on top).
            if (bgColor.HasValue)
            {
                dst.DrawFilledRect(x, y, w, h, bgColor.Value, 0);
                yield return null;
            }

            int textW = Mathf.Max(1, w);
            int textH = Mathf.Max(1, h);

            // ---- World-unit vs pixel reconciliation -------------------------
            // The fit/measure helpers below use TMP's GetPreferredValues, which
            // reports text size in TMP WORLD UNITS. But RTUtil.RenderTextToTexture2D
            // rasterizes through an orthographic camera whose orthographicSize is
            // min(rectW,rectH)/2 - so one world unit maps to a NON-1:1 number of
            // pixels that depends on the rect's shape, and TMP fontSize maps to
            // world units by a per-font-asset ratio (~0.25 for the bundled fonts).
            // The old code compared raw world units straight against the pixel
            // rect and fed the LLM's pixel font_size in as a TMP fontSize cap.
            // Net effect: poster titles capped ~4x too small, and the error
            // changed with rect shape / resolved font - exactly the "sometimes
            // fine, sometimes tiny" bug. Reconcile everything into one space so
            // the result is exact regardless of canvas size or font asset.
            float pxPerWorld = (float)textH / Mathf.Max(1, Mathf.Min(textW, textH));
            // World units produced per 1 unit of TMP fontSize for THIS font.
            float worldPerFont = MeasurePreferredHeight("Hg", font, 100, 100000, styles, tmpAlign, false) / 100f;
            if (worldPerFont <= 0.0001f) worldPerFont = 0.25f; // TMP fallback if the probe failed
            float pxPerFont = worldPerFont * pxPerWorld;       // 1 TMP fontSize unit -> rendered pixels

            // font_size / min_font_size arrive as PIXEL heights (the skill docs
            // and the LLM treat them that way). Convert to the TMP-fontSize
            // cap/floor the search and renderer actually consume.
            int PxToTmpFont(int px) =>
                px <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(px / pxPerFont), 1, 4000);
            int tmpMaxFont = fontSize > 0 ? PxToTmpFont(fontSize) : 0;
            int tmpMinFont = PxToTmpFont(minFontSize); // the LLM's requested floor (logged below; now a soft hint)
            // BOX ALWAYS WINS. The auto-fitter is allowed to shrink text all the way
            // down to this tiny absolute floor so it ALWAYS fits the rect, instead of
            // honoring the requested min_font_size when that floor is bigger than the
            // box (the old behavior - it forced poster titles / book body text to
            // overflow the band, overlap the next line, and clip at the canvas edge).
            // min_font_size is therefore a soft hint now: when the box can hold it the
            // fitted size comes out >= it anyway; when it can't, the box wins and the
            // text shrinks to fit. MIN_FIT_PX just keeps text from collapsing to 0.
            const int MIN_FIT_PX = 6;
            int tmpFitFloor = Mathf.Max(1, PxToTmpFont(MIN_FIT_PX));
            // The fit helpers measure in world units, so hand them the rect in
            // world units too (pixels / pxPerWorld). Returned value is a TMP
            // fontSize, which is exactly what the renderer wants.
            int worldRectW = Mathf.Max(1, Mathf.RoundToInt(textW / pxPerWorld));
            int worldRectH = Mathf.Max(1, Mathf.RoundToInt(textH / pxPerWorld));

            // Compute the actual fontSize to render at. When autoSize is on (default),
            // we MEASURE the text's preferred bounds at a reference fontSize and
            // scale to fit the rect - bypassing TMP's built-in enableAutoSizing
            // which behaves unreliably here. font_size is the upper cap; the fitter
            // shrinks down to tmpFitFloor so the text always fits the box.
            string renderText = text;
            bool renderWrap = wrap;
            int renderFontSize;
            if (wrap)
            {
                if (autoSize)
                {
                    renderFontSize = ComputeFitFontSizeWithManualWrap(text, font, tmpMaxFont, tmpFitFloor, worldRectW, worldRectH, styles, tmpAlign, out renderText);
                }
                else
                {
                    renderFontSize = tmpMaxFont;
                    renderText = WrapTextToWidth(text, font, renderFontSize, worldRectW, styles, tmpAlign);
                }

                // The render-to-texture path has proven inconsistent when relying
                // on TMP's runtime word wrapping. Forced line breaks make final
                // book/page text deterministic and prevent one-line shrinkage.
                renderWrap = false;
            }
            else
            {
                renderFontSize = autoSize
                    ? ComputeFitFontSize(text, font, tmpMaxFont, tmpFitFloor, worldRectW, worldRectH, styles, tmpAlign, false)
                    : tmpMaxFont;
            }

            // Measure the ACTUAL preferred height at the chosen fontSize. When TMP's
            // preferred height exceeds the rect (because min_font_size forced a
            // larger size than the rect can hold, or word-wrap re-flowed to an
            // extra line at the target size that the linear estimate missed), we
            // expand the render texture so the overflow stays VISIBLE - spilling
            // slightly into the surrounding canvas - instead of being silently
            // clipped mid-glyph inside the per-rect render texture. That silent
            // clip is the bug that crops the "y" descender in poster body text
            // like "Activated. Do not disturb until January.".
            // MeasurePreferredHeight returns world units; the render texture is
            // sized in pixels, so scale by pxPerWorld before comparing to textH.
            int measuredWorldH = MeasurePreferredHeight(renderText, font, renderFontSize, worldRectW, styles, tmpAlign, renderWrap);
            int measuredH = Mathf.CeilToInt(measuredWorldH * pxPerWorld);
            int extraH = Mathf.Max(0, measuredH - textH);
            int slackTop, slackBot;
            DistributeOverflowSlack(valign, extraH, out slackTop, out slackBot);
            int renderTexH = textH + slackTop + slackBot;
            int blitX = x;
            int blitY = y - slackTop;

            // Outline: render the text 8x in the outline color offset by outlineWidth in
            // each direction, then the main text in the fill color on top. Cheap halo
            // that survives JPEG compression and works on busy backgrounds.
            if (outlineColor.HasValue && outlineWidth > 0)
            {
                // Use bAutoSize=false here regardless - we already computed the fit size.
                Texture2D outlineTex = RTUtil.RenderTextToTexture2D(renderText, textW, renderTexH, font, renderFontSize, outlineColor.Value, false, new Vector2(1, 1), styles, tmpAlign, renderWrap, 0f, 0f);
                int[] dxA = { -outlineWidth, 0, outlineWidth, -outlineWidth, outlineWidth, -outlineWidth, 0, outlineWidth };
                int[] dyA = { -outlineWidth, -outlineWidth, -outlineWidth, 0, 0, outlineWidth, outlineWidth, outlineWidth };
                for (int i = 0; i < 8; i++)
                {
                    BlitTextureClipped(dst, outlineTex, blitX + dxA[i], blitY + dyA[i]);
                    if ((i & 1) == 0) yield return null;
                }
                UnityEngine.Object.Destroy(outlineTex);
            }

            Texture2D textTex = RTUtil.RenderTextToTexture2D(renderText, textW, renderTexH, font, renderFontSize, color, false, new Vector2(1, 1), styles, tmpAlign, renderWrap, 0f, 0f);
            BlitTextureClipped(dst, textTex, blitX, blitY);
            UnityEngine.Object.Destroy(textTex);

#if UNITY_STANDALONE && !RT_RELEASE
            // The font_size_arg is the LLM's PIXEL request; renderFontSize is the
            // TMP fontSize we resolved it to via pxPerFont. They differ by design
            // (pxPerFont is ~0.25 for the bundled fonts); the rendered pixel
            // height should track font_size_arg / fill the rect, not renderFontSize.
            Debug.Log($"draw_text: canvas={srcW}x{srcH} rect=({x},{y}) {w}x{h} " +
                      $"fontSize_arg={action.GetArg("font_size") ?? "(unset)"}px " +
                      $"min_arg={action.GetArg("min_font_size") ?? "(unset)"}px " +
                      $"auto={autoSize} wrap={wrap} -> renderFontSize={renderFontSize}(TMP) " +
                      $"measuredH={measuredH}px renderTexH={renderTexH}px chars={(text ?? "").Length} " +
                      $"| pxPerWorld={pxPerWorld:0.###} worldPerFont={worldPerFont:0.###} " +
                      $"pxPerFont={pxPerFont:0.###} tmpFontCap=[{tmpFitFloor},{tmpMaxFont}] reqMin={tmpMinFont}");
#endif

            // Editor-only: mirror the fit math into the AI Chat log. overflowPx > 0
            // means the floor (min_font_size) forced text taller than the rect can
            // hold, so it spills past the box and clips at the canvas edge - the
            // classic "poster title too big / lines overlap" symptom.
            AIChatLog.Note("draw_text",
                $"text=\"{((text != null && text.Length > 120) ? text.Substring(0, 120) + "…" : text)}\" " +
                $"canvas={srcW}x{srcH} rect=({x},{y}) {w}x{h} " +
                $"font_size_arg={action.GetArg("font_size") ?? "(unset)"} min_arg={action.GetArg("min_font_size") ?? "(unset)"} " +
                $"auto={autoSize} wrap={wrap} renderFontSize(TMP)={renderFontSize} " +
                $"rectH={textH}px measuredH={measuredH}px overflowPx={extraH} renderTexH={renderTexH}px " +
                $"lines={(renderText ?? "").Split('\n').Length} pxPerFont={pxPerFont:0.###} tmpFontCap=[{tmpFitFloor},{tmpMaxFont}] reqMin={tmpMinFont}");

            dst.Apply();
            yield return null;
        }

        /// <summary>
        /// Measure TMP's preferredHeight for <paramref name="text"/> at the given
        /// <paramref name="fontSize"/>, with the same width-wrap constraint the
        /// renderer will use. NOTE: the returned value is in TMP WORLD UNITS, not
        /// pixels (TMP fontSize is ~0.25 world units/unit for the bundled fonts).
        /// Callers must scale by pxPerWorld to compare against a pixel rect - the
        /// historical assumption that this returned pixels is what made poster
        /// text size unpredictable. <paramref name="rectW"/> is likewise in world
        /// units when used as a wrap width.
        /// </summary>
        private static int MeasurePreferredHeight(string text, TMP_FontAsset font, int fontSize, int rectW, FontStyles styles, TextAlignmentOptions alignment, bool wrap)
        {
            if (string.IsNullOrEmpty(text) || rectW <= 0 || fontSize <= 0) return 0;
            GameObject go = null;
            try
            {
                go = new GameObject("TMP_HeightProbe");
                go.layer = 31;
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.text = text;
                tmp.font = font;
                tmp.fontStyle = styles;
                tmp.alignment = alignment;
                tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#pragma warning disable CS0618
                tmp.enableWordWrapping = wrap;
#pragma warning restore CS0618
                tmp.enableAutoSizing = false;
                tmp.fontSize = fontSize;
                tmp.rectTransform.sizeDelta = new Vector2(rectW, 99999f);
                Vector2 preferred = tmp.GetPreferredValues(text, wrap ? rectW : Mathf.Infinity, Mathf.Infinity);
                return Mathf.CeilToInt(preferred.y);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("MeasurePreferredHeight failed: " + ex.Message);
                return 0;
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>
        /// Distribute extra render-texture height above and below the user's rect
        /// so a TMP <c>valign</c> still anchors the text to the same edge of the
        /// rect after expansion. <c>middle</c> splits evenly (text stays centered
        /// on the rect), <c>top</c> puts all slack at the bottom (top-of-text
        /// stays pinned to top-of-rect), <c>bottom</c> puts all slack at the top.
        /// </summary>
        private static void DistributeOverflowSlack(string valign, int extra, out int slackTop, out int slackBot)
        {
            if (extra <= 0) { slackTop = 0; slackBot = 0; return; }
            if (valign == "top") { slackTop = 0; slackBot = extra; return; }
            if (valign == "bottom") { slackTop = extra; slackBot = 0; return; }
            // middle (default)
            slackTop = extra / 2;
            slackBot = extra - slackTop;
        }

        private static int ComputeFitFontSizeWithManualWrap(string text, TMP_FontAsset font, int maxFont, int minFont, int rectW, int rectH, FontStyles styles, TextAlignmentOptions alignment, out string wrappedText)
        {
            if (string.IsNullOrEmpty(text) || rectW <= 0 || rectH <= 0)
            {
                wrappedText = text ?? "";
                return Mathf.Max(1, minFont > 0 ? minFont : (maxFont > 0 ? maxFont : 64));
            }

            int hardMax = maxFont > 0 ? maxFont : 9999;
            int hardMin = Mathf.Max(1, minFont);
            if (hardMax < hardMin) hardMax = hardMin;

            int bestSize = hardMin;
            string bestText = WrapTextToWidth(text, font, hardMin, rectW, styles, alignment);

            int low = hardMin;
            int high = hardMax;
            for (int i = 0; i < 16 && low <= high; i++)
            {
                int mid = low + ((high - low) / 2);
                string candidate = WrapTextToWidth(text, font, mid, rectW, styles, alignment);
                int candidateH = MeasurePreferredHeight(candidate, font, mid, rectW, styles, alignment, false);
                if (candidateH <= rectH)
                {
                    bestSize = mid;
                    bestText = candidate;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            wrappedText = bestText;
            return Mathf.Max(1, bestSize);
        }

        private static string WrapTextToWidth(string text, TMP_FontAsset font, int fontSize, int rectW, FontStyles styles, TextAlignmentOptions alignment)
        {
            if (string.IsNullOrEmpty(text) || font == null || fontSize <= 0 || rectW <= 0)
                return text ?? "";

            GameObject go = null;
            try
            {
                go = new GameObject("TMP_ManualWrapProbe");
                go.layer = 31;
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.font = font;
                tmp.fontStyle = styles;
                tmp.alignment = alignment;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
#pragma warning disable CS0618
                tmp.enableWordWrapping = false;
#pragma warning restore CS0618
                tmp.enableAutoSizing = false;
                tmp.fontSize = fontSize;
                tmp.rectTransform.sizeDelta = new Vector2(99999f, 99999f);

                float WidthOf(string value)
                {
                    if (string.IsNullOrEmpty(value)) return 0f;
                    return tmp.GetPreferredValues(value, Mathf.Infinity, Mathf.Infinity).x;
                }

                var lines = new List<string>();
                string current = "";

                void CommitCurrent()
                {
                    if (current.Length == 0) return;
                    lines.Add(current);
                    current = "";
                }

                void AddToken(string token)
                {
                    if (string.IsNullOrEmpty(token)) return;

                    if (current.Length > 0)
                    {
                        string candidate = current + " " + token;
                        if (WidthOf(candidate) <= rectW)
                        {
                            current = candidate;
                            return;
                        }

                        CommitCurrent();
                    }

                    if (WidthOf(token) <= rectW)
                    {
                        current = token;
                        return;
                    }

                    var chunk = new StringBuilder();
                    for (int i = 0; i < token.Length; i++)
                    {
                        string candidate = chunk.ToString() + token[i];
                        if (chunk.Length > 0 && WidthOf(candidate) > rectW)
                        {
                            lines.Add(chunk.ToString());
                            chunk.Length = 0;
                        }
                        chunk.Append(token[i]);
                    }
                    current = chunk.ToString();
                }

                string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                string[] paragraphs = normalized.Split('\n');
                for (int p = 0; p < paragraphs.Length; p++)
                {
                    if (p > 0)
                    {
                        CommitCurrent();
                        lines.Add("");
                    }

                    string paragraph = paragraphs[p];
                    if (string.IsNullOrWhiteSpace(paragraph))
                    {
                        CommitCurrent();
                        continue;
                    }

                    string[] words = paragraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < words.Length; i++)
                    {
                        AddToken(words[i]);
                    }
                }

                CommitCurrent();
                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("WrapTextToWidth failed: " + ex.Message);
                return text ?? "";
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>
        /// Compute the largest fontSize at which `text` fits inside a rect of
        /// `rectW` x `rectH` pixels for the given font/style/wrap settings, clamped
        /// to [minFont, maxFont]. Done by creating a temp TMP, measuring its
        /// preferredWidth/preferredHeight at a reference fontSize of 100, scaling
        /// proportionally, and then iteratively verifying that the chosen size
        /// actually fits.
        ///
        /// We do this manually rather than relying on TMP's enableAutoSizing
        /// because TMP's auto-sizer works unreliably in our synchronous
        /// render-to-texture path - it tends to settle near fontSizeMin even when
        /// the rect has plenty of room. Manual measurement uses TMP's actual
        /// preferred-bounds calculation (which IS reliable) and lets us scale
        /// linearly to fit.
        ///
        /// The follow-up iterative shrink matters because word-wrap line count is
        /// a step function of fontSize: at the 100-unit reference the text might
        /// wrap to N lines, but at the linear-scaled target it can wrap to N+1,
        /// pushing preferredHeight past the rect. Without re-verification that
        /// overflow would get silently clipped at the render texture edge -
        /// which is exactly the descender-cropping bug. The shrink stops at
        /// minFont (the readability floor); when even minFont overflows, the
        /// render path adds slack so the overflow stays visible instead of being
        /// chopped mid-glyph.
        /// </summary>
        private static int ComputeFitFontSize(string text, TMP_FontAsset font, int maxFont, int minFont, int rectW, int rectH, FontStyles styles, TextAlignmentOptions alignment, bool wrap)
        {
            if (string.IsNullOrEmpty(text) || rectW <= 0 || rectH <= 0)
                return Mathf.Max(1, minFont > 0 ? minFont : (maxFont > 0 ? maxFont : 64));

            int hardMax = maxFont > 0 ? maxFont : 9999;
            int hardMin = Mathf.Max(1, minFont);

            const float REFERENCE_FONT_SIZE = 100f;
            GameObject go = null;
            try
            {
                go = new GameObject("TMP_FitProbe");
                go.layer = 31; // unused layer (matches RTUtil.RenderTextToTexture2D's pattern)
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.text = text;
                tmp.font = font;
                tmp.fontStyle = styles;
                tmp.alignment = alignment;
                tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#pragma warning disable CS0618
                tmp.enableWordWrapping = wrap;
#pragma warning restore CS0618
                tmp.enableAutoSizing = false;
                tmp.fontSize = REFERENCE_FONT_SIZE;
                // Constrain width for word-wrap calc (height unconstrained so
                // preferredHeight reflects natural multi-line wrapping).
                tmp.rectTransform.sizeDelta = new Vector2(rectW, 99999f);
                Vector2 preferred = tmp.GetPreferredValues(text, wrap ? rectW : Mathf.Infinity, Mathf.Infinity);

                float pw = wrap ? Mathf.Min(preferred.x, rectW) : preferred.x;
                float ph = preferred.y;

                int fitSize;
                if (pw > 0f && ph > 0f)
                {
                    // Scale fontSize so that preferred bounds fit both axes of the rect.
                    float scaleW = rectW / pw;
                    float scaleH = rectH / ph;
                    float scale = Mathf.Min(scaleW, scaleH);
                    fitSize = Mathf.RoundToInt(REFERENCE_FONT_SIZE * scale);
                }
                else
                {
                    fitSize = hardMax;
                }

                if (fitSize > hardMax) fitSize = hardMax;
                if (fitSize < 1) fitSize = 1;

                // Iteratively verify the candidate ACTUALLY fits at that fontSize.
                // The linear estimate above can over-shoot when word-wrap re-flows
                // to an extra line at the target size that the reference size did
                // not have. Shrink toward hardMin until the measured bounds fit.
                // We never go below hardMin (readability floor); when text genuinely
                // can't fit at minFont the caller's render path adds slack to keep
                // the overflow visible instead of clipping it mid-glyph.
                for (int i = 0; i < 8 && fitSize > hardMin; i++)
                {
                    tmp.fontSize = fitSize;
                    Vector2 actual = tmp.GetPreferredValues(text, wrap ? rectW : Mathf.Infinity, Mathf.Infinity);
                    float aw = wrap ? Mathf.Min(actual.x, rectW) : actual.x;
                    float ah = actual.y;
                    if (ah <= rectH && aw <= rectW) break;
                    float shrink = Mathf.Min(rectW / Mathf.Max(aw, 1f), rectH / Mathf.Max(ah, 1f));
                    int next = Mathf.FloorToInt(fitSize * shrink);
                    if (next >= fitSize) next = fitSize - 1;
                    if (next < hardMin) next = hardMin;
                    fitSize = next;
                }

                if (minFont > 0 && fitSize < minFont) fitSize = minFont;
                if (fitSize < 1) fitSize = 1;
                return fitSize;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ComputeFitFontSize failed: " + ex.Message);
                return maxFont > 0 ? maxFont : 64;
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
        }

        private static TextAlignmentOptions ResolveTmpAlignment(string align, string valign)
        {
            // TMP combines horizontal + vertical into a single enum (Top/Mid/Bottom +
            // Left/Right/Center/Justified). Translate the LLM's two-axis args into the
            // closest combo. Default = MidlineGEO Center.
            bool top = valign == "top";
            bool bottom = valign == "bottom";
            if (align == "left")
            {
                if (top) return TextAlignmentOptions.TopLeft;
                if (bottom) return TextAlignmentOptions.BottomLeft;
                return TextAlignmentOptions.Left;
            }
            if (align == "right")
            {
                if (top) return TextAlignmentOptions.TopRight;
                if (bottom) return TextAlignmentOptions.BottomRight;
                return TextAlignmentOptions.Right;
            }
            // center / middle / unknown -> centered horizontally
            if (top) return TextAlignmentOptions.Top;
            if (bottom) return TextAlignmentOptions.Bottom;
            return TextAlignmentOptions.Center;
        }

        private static void BlitTextureClipped(Texture2D dst, Texture2D src, int dstX, int dstY)
        {
            if (src == null || dst == null) return;
            int dx = dstX, dy = dstY, dw = src.width, dh = src.height, sx = 0, sy = 0;
            if (dx < 0) { sx = -dx; dw += dx; dx = 0; }
            if (dy < 0) { sy = -dy; dh += dy; dy = 0; }
            if (dx + dw > dst.width) dw = dst.width - dx;
            if (dy + dh > dst.height) dh = dst.height - dy;
            if (dw <= 0 || dh <= 0) return;
            dst.BlitWithAlpha(dx, dy, src, sx, sy, dw, dh);
        }

        private void ExecuteAddBorder(SkillAction action)
        {
            byte[] canvasBytes = ResolveCanvasBytes(action, "add_border", out bool errored, out bool deferred, allowMissing: false);
            if (errored || deferred) return;

            Func<PicMain, IEnumerator> op = (pic) => AddBorderCoroutine(pic, action);
            RunOrChainLocalOp(action, "add_border", canvasBytes, op);
        }

        private IEnumerator AddBorderCoroutine(PicMain pic, SkillAction action)
        {
            var sprite = pic != null ? pic.m_pic?.sprite : null;
            var dst = sprite != null ? sprite.texture as Texture2D : null;
            if (dst == null)
            {
                Debug.LogWarning("add_border: Pic has no texture to border.");
                yield break;
            }
            int srcW = dst.width;
            int srcH = dst.height;
            // Percentages: left/right use source WIDTH (so "10%" = 10% of pic width);
            // top/bottom use source HEIGHT (so "25%" bottom band = 25% of pic height).
            // Different reference dim per axis is what keeps the band a STABLE
            // fraction of the final canvas across portrait/square/landscape sources.
            // Without this rule, a "bottom=35%" call on a 1280x720 landscape source
            // produces a band of 35%-of-1280 = 448 pixels added to a 720-tall image,
            // which makes the band 38% of the final canvas; the same "35%" call on
            // a 1024x1024 source produces a band that's only 25% of the final canvas
            // - so any text-position percentage the LLM picks lands in the wrong
            // place depending on which source it gets. With this rule, "bottom=25%"
            // always means "the bottom band is ~20% of the final canvas height".
            int left = ParsePixelOrPercent(action.GetArg("left"), srcW) ?? 0;
            int right = ParsePixelOrPercent(action.GetArg("right"), srcW) ?? 0;
            int top = ParsePixelOrPercent(action.GetArg("top"), srcH) ?? 0;
            int bottom = ParsePixelOrPercent(action.GetArg("bottom"), srcH) ?? 0;
            Color color = ParseColor(action.GetArg("color"), Color.white);

            if (left <= 0 && right <= 0 && top <= 0 && bottom <= 0)
            {
                _host?.AddInfoBubble("add_border: all borders were 0 - nothing to do.");
                yield break;
            }

            // Reuse PicMain.AddBorder which handles texture resize, sprite swap, and
            // mask resize. The bSetMaskToBorder=false matches the AIGuide motivational
            // path - we want a colored border, not an outpaint mask.
            yield return pic.StartCoroutine(pic.AddBorder(left, right, top, bottom, color, false));
        }

        private void ExecutePasteImage(SkillAction action)
        {
            // The "canvas" is the standard chat_image / attachment / chain source.
            // The "source" (image being pasted) comes from source_chat_image /
            // source_attachment to keep the two slots clearly distinct from the
            // existing 2-input preset's chat_image2 / attachment2 conventions.
            byte[] canvasBytes = ResolveCanvasBytes(action, "paste_image", out bool errored, out bool deferred, allowMissing: false);
            if (errored || deferred) return;

            byte[] sourceBytes = ResolveSourceImageBytes(action, "paste_image", out bool srcErrored, out bool srcDeferred);
            if (srcErrored || srcDeferred) return;
            if (sourceBytes == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    "paste_image needs a source image to paste. Use source_chat_image=\"N\" (an existing bubble) or source_attachment=\"N\" (a fresh paste).");
                return;
            }

            Func<PicMain, IEnumerator> op = (pic) => PasteImageCoroutine(pic, action, sourceBytes);
            RunOrChainLocalOp(action, "paste_image", canvasBytes, op);
        }

        private IEnumerator PasteImageCoroutine(PicMain pic, SkillAction action, byte[] sourceBytes)
        {
            var sprite = pic != null ? pic.m_pic?.sprite : null;
            var dst = sprite != null ? sprite.texture as Texture2D : null;
            if (dst == null)
            {
                Debug.LogWarning("paste_image: Pic has no texture to paste onto.");
                yield break;
            }

            Texture2D src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(sourceBytes))
            {
                UnityEngine.Object.Destroy(src);
                Debug.LogWarning("paste_image: could not decode source image bytes.");
                yield break;
            }

            int srcW = dst.width;
            int srcH = dst.height;
            int x = ParsePixelOrPercent(action.GetArg("x"), srcW) ?? 0;
            int y = ParsePixelOrPercent(action.GetArg("y"), srcH) ?? 0;
            int w = ParsePixelOrPercent(action.GetArg("width"), srcW) ?? src.width;
            int h = ParsePixelOrPercent(action.GetArg("height"), srcH) ?? src.height;
            string mode = (action.GetArg("mode") ?? "fit").Trim().ToLowerInvariant();
            float opacity = ParseFloat(action.GetArg("opacity"), 1f);
            float hAlign = ParseAlign(action.GetArg("align"), 0.5f, isVertical: false);
            float vAlign = ParseAlign(action.GetArg("valign"), 0.5f, isVertical: true);

            dst.BlitImageFitted(src, x, y, w, h, mode, opacity, hAlign, vAlign);
            dst.Apply();
            UnityEngine.Object.Destroy(src);
            yield return null;
        }

        private void ExecuteNewCanvas(SkillAction action)
        {
            // Fresh unchained spawn: mark stale so a chained decorator after a FAILED
            // new_canvas errors instead of corrupting the previous page (cleared on the
            // successful SetLastSpawnedPicForTurn below).
            _host?.MarkChainTargetStale();

            int w = ParsePositiveInt(action.GetArg("width"), 1024);
            int h = ParsePositiveInt(action.GetArg("height"), 1024);
            Color color = ParseColor(action.GetArg("color"), Color.white);

            // Hard cap to keep the LLM from accidentally allocating a 30000x30000 buffer.
            const int MAX_DIM = 8192;
            if (w > MAX_DIM || h > MAX_DIM)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"new_canvas: requested size {w}x{h} exceeds the {MAX_DIM}-pixel cap. Pick a smaller width/height.");
                return;
            }

            var imageGen = ImageGenerator.Get();
            if (imageGen == null)
            {
                _host?.AddInfoBubble("new_canvas error: ImageGenerator not initialized yet.");
                return;
            }

            var blank = new Texture2D(w, h, TextureFormat.RGBA32, false);
            blank.Fill(color);
            blank.Apply();

            GameObject picGO = imageGen.CreateNewPic();
            if (picGO == null)
            {
                UnityEngine.Object.Destroy(blank);
                _host?.AddInfoBubble("new_canvas error: failed to spawn a Pic.");
                return;
            }
            var picMain = picGO.GetComponent<PicMain>();
            if (picMain == null)
            {
                UnityEngine.Object.Destroy(blank);
                _host?.AddInfoBubble("new_canvas error: spawned object has no PicMain.");
                return;
            }

            // SetImage takes ownership when bDoFullCopy=false; do a full copy so the
            // Pic owns its own buffer and we can dispose ours predictably.
            picMain.SetImage(blank, true);
            UnityEngine.Object.Destroy(blank);

            _host?.AppendImageBubbleForPic(action, picMain);
            _host?.SetLastSpawnedPicForTurn(picMain);
            _lastLocalOpOutputChatImageIndex = _host?.GetLatestChatImageIndex() ?? -1;
            _lastLocalOpOutputPic = picMain;
        }

        private void ExecuteCropResize(SkillAction action)
        {
            byte[] canvasBytes = ResolveCanvasBytes(action, "crop_resize", out bool errored, out bool deferred, allowMissing: false);
            if (errored || deferred) return;

            Func<PicMain, IEnumerator> op = (pic) => CropResizeCoroutine(pic, action);
            RunOrChainLocalOp(action, "crop_resize", canvasBytes, op);
        }

        private IEnumerator CropResizeCoroutine(PicMain pic, SkillAction action)
        {
            var sprite = pic != null ? pic.m_pic?.sprite : null;
            var dst = sprite != null ? sprite.texture as Texture2D : null;
            if (dst == null)
            {
                Debug.LogWarning("crop_resize: Pic has no texture.");
                yield break;
            }

            int srcW = dst.width;
            int srcH = dst.height;
            int targetW = ParsePixelOrPercent(action.GetArg("width"), srcW) ?? srcW;
            int targetH = ParsePixelOrPercent(action.GetArg("height"), srcH) ?? srcH;
            string mode = (action.GetArg("mode") ?? "resize").Trim().ToLowerInvariant();
            Color bgColor = ParseColor(action.GetArg("bg_color"), new Color(0, 0, 0, 0));

            if (targetW <= 0 || targetH <= 0)
            {
                Debug.LogWarning("crop_resize: width/height must be > 0.");
                yield break;
            }

            switch (mode)
            {
                case "resize": // legacy "stretch" alias
                case "stretch":
                    pic.Resize(targetW, targetH, false, FilterMode.Bilinear);
                    break;
                case "fill":
                    pic.Resize(targetW, targetH, true, FilterMode.Bilinear);
                    break;
                case "fit":
                {
                    var blank = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
                    blank.Fill(bgColor);
                    blank.Apply();
                    blank.BlitImageFitted(dst, 0, 0, targetW, targetH, "fit", 1f, 0.5f, 0.5f);
                    blank.Apply();
                    pic.SetImage(blank, true);
                    UnityEngine.Object.Destroy(blank);
                    break;
                }
                case "crop":
                {
                    int cropX = ParsePixelOrPercent(action.GetArg("x"), srcW) ?? 0;
                    int cropY = ParsePixelOrPercent(action.GetArg("y"), srcH) ?? 0;
                    cropX = Mathf.Clamp(cropX, 0, Mathf.Max(0, srcW - 1));
                    cropY = Mathf.Clamp(cropY, 0, Mathf.Max(0, srcH - 1));
                    int cropW = Mathf.Min(targetW, srcW - cropX);
                    int cropH = Mathf.Min(targetH, srcH - cropY);
                    // ResizeTool.CropTexture takes (x, y) as top-left in y-down terms.
                    var cropped = ResizeTool.CropTexture(dst, new Rect(cropX, cropY, cropW, cropH));
                    pic.SetImage(cropped, false);
                    break;
                }
                default:
                    _host?.AddSystemInjectionAndBubble(
                        "crop_resize: mode=\"" + mode + "\" is not recognized. Use resize / fit / fill / crop.");
                    yield break;
            }
            yield return null;
        }

        private void ExecuteDrawShape(SkillAction action)
        {
            byte[] canvasBytes = ResolveCanvasBytes(action, "draw_shape", out bool errored, out bool deferred, allowMissing: false);
            if (errored || deferred) return;

            Func<PicMain, IEnumerator> op = (pic) => DrawShapeCoroutine(pic, action);
            RunOrChainLocalOp(action, "draw_shape", canvasBytes, op);
        }

        private IEnumerator DrawShapeCoroutine(PicMain pic, SkillAction action)
        {
            var sprite = pic != null ? pic.m_pic?.sprite : null;
            var dst = sprite != null ? sprite.texture as Texture2D : null;
            if (dst == null)
            {
                Debug.LogWarning("draw_shape: Pic has no texture.");
                yield break;
            }

            int srcW = dst.width;
            int srcH = dst.height;
            string shape = (action.GetArg("shape") ?? "rect").Trim().ToLowerInvariant();
            int x = ParsePixelOrPercent(action.GetArg("x"), srcW) ?? 0;
            int y = ParsePixelOrPercent(action.GetArg("y"), srcH) ?? 0;
            int w = ParsePixelOrPercent(action.GetArg("width"), srcW) ?? 0;
            int h = ParsePixelOrPercent(action.GetArg("height"), srcH) ?? 0;
            Color? fill = ParseColorOpt(action.GetArg("fill_color"));
            Color? outline = ParseColorOpt(action.GetArg("outline_color"));
            int outlineWidth = ParsePixelOrPercent(action.GetArg("outline_width"), srcW) ?? 1;
            int cornerRadius = ParsePixelOrPercent(action.GetArg("corner_radius"), srcW) ?? 0;

            if (!fill.HasValue && !outline.HasValue)
            {
                _host?.AddSystemInjectionAndBubble(
                    "draw_shape needs at least fill_color or outline_color (or both). Got neither.");
                yield break;
            }

            if (shape == "circle")
            {
                int cx = x + w / 2;
                int cy = y + h / 2;
                int radius = Mathf.Max(1, Mathf.Min(w, h) / 2);
                if (fill.HasValue) dst.DrawFilledCircle(cx, cy, radius, fill.Value);
                if (outline.HasValue) dst.DrawOutlineCircle(cx, cy, radius, outline.Value, outlineWidth);
            }
            else // rect (or anything else -> rect)
            {
                if (w <= 0 || h <= 0)
                {
                    _host?.AddSystemInjectionAndBubble("draw_shape rect needs width and height > 0.");
                    yield break;
                }
                if (fill.HasValue) dst.DrawFilledRect(x, y, w, h, fill.Value, cornerRadius);
                if (outline.HasValue) dst.DrawOutlineRect(x, y, w, h, outline.Value, outlineWidth, cornerRadius);
            }

            dst.Apply();
            yield return null;
        }

        // ---------- Composition helpers ----------

        private bool PromoteCanvasReferenceToChainIfNeeded(SkillAction action, string skillId, int chatN)
        {
            if (action == null || action.Chain || chatN <= 0)
                return false;

            PicMain currentTurnPic = _host?.GetLastSpawnedPicForTurn();
            if (currentTurnPic == null)
                return false;

            bool referencesLatestLocalOutput = _lastLocalOpOutputChatImageIndex > 0
                && chatN == _lastLocalOpOutputChatImageIndex
                && _lastLocalOpOutputPic != null
                && currentTurnPic == _lastLocalOpOutputPic;

            if (!referencesLatestLocalOutput)
                return false;

            action.Args.Remove("chat_image");
            action.Args["chain"] = "true";
            _host?.AddInfoBubble(
                $"(treated {skillId} chat_image=\"{chatN}\" as chain=\"true\" - it references the Pic just spawned earlier in this reply)");
            return true;
        }

        private bool TryResolveChatImageBytesOrDefer(SkillAction action, string skillId, string argName, int chatN, out byte[] bytes, out bool deferred)
        {
            bytes = _host?.GetChatImagePngBytes(chatN);
            deferred = false;

            // Non-null bytes are NOT proof the source is ready: a freshly-spawned Pic
            // carries a 512x512 BLACK default texture (PicMain.Awake) until its GPU render
            // lands, so an anchor referenced in the SAME reply that generated it would
            // otherwise feed that black placeholder into img2img. If the source Pic is
            // still generating, defer until the real image exists.
            bool stillGenerating = _host?.IsChatImagePicGenerating(chatN) ?? false;
            if (bytes != null && !stillGenerating)
                return true;

            if (TryDeferActionUntilChatImageReady(action, skillId, argName, chatN))
            {
                deferred = true;
                return false;
            }

            // Couldn't defer (no coroutine runner, or this action already deferred once and
            // the wait elapsed): use whatever bytes we have rather than failing outright.
            if (bytes != null)
                return true;

            return false;
        }

        private bool TryDeferActionUntilChatImageReady(SkillAction action, string skillId, string argName, int chatN)
        {
            if (action == null || chatN <= 0)
                return false;
            if (_reloadAttemptedActions.Contains(action))
                return false;

            var runner = _host?.CoroutineRunner;
            if (runner == null)
                return false;

            bool preparing = _host?.TryPrepareChatImageForRead(chatN) ?? false;
            if (!preparing)
                return false;

            _reloadAttemptedActions.Add(action);
            _host?.AddInfoBubble(
                $"(reloading {argName}=\"{chatN}\" before running {skillId})");
            // Signal the pump that this action parked itself - it must hold all
            // following actions in the turn until the coroutine resumes us.
            _lastActionDeferred = true;
            runner.StartCoroutine(ExecuteAfterChatImageReady(action, skillId, argName, chatN));
            return true;
        }

        /// <summary>
        /// True when every chat image this action references (primary
        /// chat_image plus chat_image2..5 for N-input presets) has readable
        /// bytes. <paramref name="anyBusy"/> reports whether any referenced Pic
        /// is still generating, so the wait can persist for slow GPUs instead
        /// of timing out on a fixed wall clock.
        /// </summary>
        private bool AllReferencedChatImagesReady(SkillAction action, out bool anyBusy)
        {
            anyBusy = false;
            if (action == null) return false;

            bool allReady = true;
            for (int slot = 1; slot <= 5; slot++)
            {
                int idx = slot == 1
                    ? (action.ChatImageIndex ?? -1)
                    : (action.GetExtraChatImageIndex(slot) ?? -1);
                if (idx <= 0) continue;

                byte[] bytes = _host?.GetChatImagePngBytes(idx);
                bool generating = _host?.IsChatImagePicGenerating(idx) ?? false;
                // "Ready" requires BOTH readable bytes AND a finished render: a still-
                // generating Pic only has its black placeholder texture, so treat it as
                // not-ready (and keep waiting via anyBusy) even though bytes != null.
                if (bytes == null || bytes.Length == 0 || generating)
                {
                    allReady = false;
                    if (generating)
                        anyBusy = true;
                }
            }
            return allReady;
        }

        private IEnumerator ExecuteAfterChatImageReady(SkillAction action, string skillId, string argName, int chatN)
        {
            int epoch = _turnEpoch;
            float start = Time.realtimeSinceStartup;

            while (true)
            {
                if (AllReferencedChatImagesReady(action, out bool anyBusy))
                    break;

                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= ChatImageReloadAbsoluteCapSeconds)
                    break;
                // Job queued but no GPU server has picked it up yet: give it a
                // short grace before concluding the image is never coming.
                if (!anyBusy && elapsed >= ChatImageNotYetBusyGraceSeconds)
                    break;

                yield return new WaitForSeconds(ChatImageReloadPollSeconds);
            }

            // A new turn started while we were waiting - do NOT spawn this old
            // book's page into the new conversation turn.
            if (_turnEpoch != epoch)
                yield break;

            try
            {
                if (AllReferencedChatImagesReady(action, out _))
                {
                    // Re-run end to end. The _reloadAttemptedActions guard
                    // prevents a second defer, so this spawns the page Pic and
                    // pushes it as the chain target before we resume followers.
                    Execute(action);
                }
                else
                {
                    int chatImageCount = _host?.GetChatImageCount() ?? 0;
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}': {argName}=\"{chatN}\" exists but could not be reloaded for reading. " +
                        $"There are {chatImageCount} numbered chat image slot(s) this session. " +
                        "Try focusing that Pic on the main canvas, or ask the user to paste the image again.");
                }
            }
            finally
            {
                // Always unblock the pump so the rest of the book never hangs,
                // even if this page failed.
                ResumePumpAfterDeferredComplete(action);
            }
        }

        private byte[] TryFallbackChatImageBytes(SkillAction action, string skillId, int requestedIndex, int chatImageCount)
        {
            if (requestedIndex <= 0 || chatImageCount <= 0)
                return null;

            if (_host?.GetLastSpawnedPicForTurn() != null)
                return null;

            int fallbackIndex = -1;
            if (requestedIndex == chatImageCount + 1)
                fallbackIndex = _host?.GetLatestChatImageIndex() ?? 0;

            if (fallbackIndex <= 0)
                return null;

            byte[] bytes = _host?.GetChatImagePngBytes(fallbackIndex);
            if (bytes == null)
                return null;

            if (action != null)
                action.Args["chat_image"] = fallbackIndex.ToString();

            _host?.AddInfoBubble(
                $"(chat_image=\"{requestedIndex}\" is not available; using latest chat_image=\"{fallbackIndex}\" for {skillId})");
            return bytes;
        }

        /// <summary>
        /// Resolve the canvas image (the one the local op operates on / draws into)
        /// using the same chat_image / attachment / chain semantics image_to_image
        /// already has. Returns null bytes for the chain="true" case (caller knows
        /// to inherit from the prior Pic). Sets <paramref name="errored"/> when the
        /// LLM asked for a slot that isn't available; caller should bail.
        /// When <paramref name="allowMissing"/> is true and no image slot is available,
        /// returns null bytes without erroring (used by skills that can paint on a
        /// freshly-implicit blank canvas - currently none, kept for forward-compat).
        /// </summary>
        private byte[] ResolveCanvasBytes(SkillAction action, string skillId, out bool errored, out bool deferred, bool allowMissing)
        {
            errored = false;
            deferred = false;
            if (action.Chain) return null; // caller routes to chain path

            int chatN = action.ChatImageIndex ?? -1;
            int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
            int chatImageCount = _host?.GetChatImageCount() ?? 0;

            if (chatN > 0)
            {
                if (PromoteCanvasReferenceToChainIfNeeded(action, skillId, chatN))
                    return null;

                if (!TryResolveChatImageBytesOrDefer(action, skillId, "chat_image", chatN, out byte[] bytes, out deferred))
                {
                    if (deferred) return null;
                    bytes = TryFallbackChatImageBytes(action, skillId, chatN, chatImageCount);
                    if (bytes != null)
                        return bytes;

                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}': chat_image=\"{chatN}\" is not available. " +
                        $"There are {chatImageCount} numbered chat image slot(s) this session.");
                    errored = true;
                    return null;
                }
                return bytes;
            }
            if (turnAttachCount > 0)
            {
                int idx = action.AttachmentIndex ?? 1;
                byte[] bytes = _host?.GetTurnAttachmentBytes(idx);
                if (bytes == null)
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}' wanted attachment={idx} but the user only attached {turnAttachCount} image(s) this turn.");
                    errored = true;
                    return null;
                }
                return bytes;
            }
            if (chatImageCount > 0)
            {
                int implicitIdx = _host?.GetLatestChatImageIndex() ?? 0;
                if (implicitIdx > 0
                    && TryResolveChatImageBytesOrDefer(action, skillId, "implicit chat_image", implicitIdx, out byte[] bytes, out deferred))
                {
                    _host?.AddInfoBubble($"(auto-picked chat_image=\"{implicitIdx}\" - the latest image - as the canvas for {skillId})");
                    return bytes;
                }
                if (deferred) return null;
            }

            if (allowMissing) return null;

            _host?.AddSystemInjectionAndBubble(
                $"Skill '{skillId}' needs a canvas image: pass chat_image=\"N\" / attachment=\"N\", " +
                "set chain=\"true\" to stack onto a prior step in this same reply, " +
                "or call new_canvas first to create a blank canvas.");
            errored = true;
            return null;
        }

        /// <summary>
        /// Resolve the "source image" for paste_image (the image being pasted ONTO
        /// the canvas). Looks at source_chat_image / source_attachment. Returns null
        /// when the LLM didn't specify a source image; caller decides whether that's
        /// an error.
        /// </summary>
        private byte[] ResolveSourceImageBytes(SkillAction action, string skillId, out bool errored, out bool deferred)
        {
            errored = false;
            deferred = false;
            string srcChat = action.GetArg("source_chat_image");
            string srcAttach = action.GetArg("source_attachment");

            if (!string.IsNullOrEmpty(srcChat) && int.TryParse(srcChat, out int chatN) && chatN > 0)
            {
                if (!TryResolveChatImageBytesOrDefer(action, skillId, "source_chat_image", chatN, out byte[] bytes, out deferred))
                {
                    if (deferred) return null;
                    int chatImageCount = _host?.GetChatImageCount() ?? 0;
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}': source_chat_image=\"{chatN}\" is not available. " +
                        $"There are {chatImageCount} numbered chat image slot(s) this session.");
                    errored = true;
                    return null;
                }
                return bytes;
            }
            if (!string.IsNullOrEmpty(srcAttach) && int.TryParse(srcAttach, out int attachN) && attachN > 0)
            {
                int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
                byte[] bytes = _host?.GetTurnAttachmentBytes(attachN);
                if (bytes == null)
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}' wanted source_attachment=\"{attachN}\" but the user only attached {turnAttachCount} image(s) this turn.");
                    errored = true;
                    return null;
                }
                return bytes;
            }
            return null;
        }

        /// <summary>
        /// Spawn a fresh Pic, seed its texture from canvasBytes, register the chat
        /// bubble, then run the supplied local op coroutine. For chain="true" the
        /// op is appended to the prior Pic's job queue instead. Single entry point so
        /// every composition skill behaves identically with respect to bubbles and
        /// chain semantics.
        /// </summary>
        private void RunOrChainLocalOp(SkillAction action, string skillId, byte[] canvasBytes, Func<PicMain, IEnumerator> op)
        {
            if (op == null) return;

            if (action.Chain)
            {
                // Tolerate the common slip of pairing chain="true" with a stray PRIMARY
                // chat_image / attachment - the canvas the chain ALREADY supplies. Drop the
                // redundant ref and proceed instead of erroring, so chained LOCAL ops behave
                // the same as chained GENERATES (see ExecuteChainedGenerate). NOTE: paste_image's
                // source_chat_image / source_attachment (the image being PASTED, not the canvas)
                // are SEPARATE args and are deliberately left intact.
                if (action.AttachmentIndex.HasValue || action.ChatImageIndex.HasValue)
                {
                    action.Args.Remove("chat_image");
                    action.Args.Remove("attachment");
                    Debug.Log($"SkillActionExecutor: dropped stray primary chat_image/attachment on chained '{skillId}' - chain=\"true\" already supplies the canvas.");
                }
                // Chained LOCAL ops decorate the current working image: border + body text
                // + page number all target the SAME most-recent Pic, so PEEK the head
                // instead of popping the LIFO. Popping here was the storybook bug - page 1's
                // add_border pops Page1, then the body draw_text pops the underlying anchor
                // and bakes text into it, corrupting the source every later page reuses.
                // Chained GENERATES still ConsumeChainTarget() (pop); see ExecuteChainedGenerate.
                var prevPic = _host?.PeekChainTarget();
                if (prevPic == null)
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}' was called with chain=\"true\" but no Pic was spawned earlier in this turn. " +
                        "Either drop chain=\"true\" or emit a base generate_image / new_canvas / image_to_image action first.");
                    return;
                }
                try
                {
                    prevPic.AppendLocalOp(op);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"SkillActionExecutor: AppendLocalOp threw for '{skillId}': " + ex);
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}' (chain=\"true\"): failed to append local op. See Unity console.");
                }
                return;
            }

            // Fresh unchained composition spawn (non-chain path): mark stale so a chained
            // decorator after a FAILED spawn (decode/spawn error) errors instead of
            // corrupting the previous page. Cleared by the successful SetLastSpawnedPicForTurn.
            _host?.MarkChainTargetStale();

            if (canvasBytes == null)
            {
                _host?.AddInfoBubble($"Skill '{skillId}': internal error - no canvas resolved.");
                return;
            }

            var imageGen = ImageGenerator.Get();
            if (imageGen == null)
            {
                _host?.AddInfoBubble($"Skill '{skillId}' error: ImageGenerator not initialized yet.");
                return;
            }
            GameObject picGO = imageGen.CreateNewPic();
            if (picGO == null)
            {
                _host?.AddInfoBubble($"Skill '{skillId}' error: failed to spawn a Pic.");
                return;
            }
            var picMain = picGO.GetComponent<PicMain>();
            if (picMain == null)
            {
                _host?.AddInfoBubble($"Skill '{skillId}' error: spawned object has no PicMain.");
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(canvasBytes))
            {
                UnityEngine.Object.Destroy(tex);
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}': could not decode the canvas image bytes.");
                return;
            }
            picMain.SetImage(tex, false);

            _host?.AppendImageBubbleForPic(action, picMain);
            _host?.SetLastSpawnedPicForTurn(picMain);
            _lastLocalOpOutputChatImageIndex = _host?.GetLatestChatImageIndex() ?? -1;
            _lastLocalOpOutputPic = picMain;

            // Wrap the coroutine launch so any synchronous exception thrown before
            // the coroutine's first yield (TMP setup, font lookup, etc.) is logged
            // with context instead of dropping the whole turn.
            try
            {
                picMain.RunLocalOpImmediate(op);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SkillActionExecutor: '{skillId}' coroutine launch threw {ex.GetType().Name}: {ex}");
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}': failed to start the local op ({ex.GetType().Name}: {ex.Message}). " +
                    "See Unity console for the full stack trace.");
            }
        }

        // ---------- Small parsers shared by the composition skills ----------

        /// <summary>
        /// Parse "120" as 120 pixels, "15%" as 15% of <paramref name="referenceDim"/>
        /// (rounded to int). Returns null on missing / unparseable input. Used by
        /// every composition skill so the LLM can express positions/sizes either way
        /// without learning two attribute conventions.
        /// </summary>
        private static int? ParsePixelOrPercent(string s, int referenceDim)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.EndsWith("%"))
            {
                string num = s.Substring(0, s.Length - 1).Trim();
                if (float.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct))
                    return Mathf.RoundToInt(pct * 0.01f * referenceDim);
                return null;
            }
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pixels))
                return Mathf.RoundToInt(pixels);
            return null;
        }

        private static int ParsePositiveInt(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (int.TryParse(s.Trim(), out int v) && v > 0) return v;
            return fallback;
        }

        private static float ParseFloat(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return fallback;
        }

        private static float ParseAlign(string s, float fallback, bool isVertical)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (!isVertical)
            {
                if (s == "left" || s == "start") return 0f;
                if (s == "right" || s == "end") return 1f;
                if (s == "center" || s == "middle") return 0.5f;
            }
            else
            {
                if (s == "top") return 0f;
                if (s == "bottom") return 1f;
                if (s == "middle" || s == "center") return 0.5f;
            }
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                return Mathf.Clamp01(v);
            return fallback;
        }

        /// <summary>
        /// Parse "#RGB" / "#RRGGBB" / "#RRGGBBAA" / named colors via Unity's HTML
        /// parser, falling back to <paramref name="fallback"/> on failure. Use
        /// <see cref="ParseColorOpt"/> when "missing" is meaningful (e.g. optional
        /// fill / outline).
        /// </summary>
        private static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (ColorUtility.TryParseHtmlString(s, out Color c)) return c;
            return fallback;
        }

        private static Color? ParseColorOpt(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string trimmed = s.Trim();
            if (!trimmed.StartsWith("#")) trimmed = "#" + trimmed;
            if (ColorUtility.TryParseHtmlString(trimmed, out Color c)) return c;
            return null;
        }

        /// <summary>
        /// Look up a TMP font by name via AIGuideManager's font array. Falls back to
        /// AIGuideManager font[0], then to TMP_Settings.defaultFontAsset, then to a
        /// global Resources lookup. Always returns a non-null TMP_FontAsset if at all
        /// possible - passing a null font to RTUtil.RenderTextToTexture2D crashes
        /// TextMeshPro internally with an IndexOutOfRangeException, which is the most
        /// common reason a draw_text call dies before its first yield.
        /// </summary>
        private static TMP_FontAsset ResolveFontByName(string name)
        {
            // 1. AIGuideManager font array (the curated set the existing poster
            //    pipeline already uses). Best font for our purposes since it's what
            //    the rest of the app's text rendering targets.
            var guide = AIGuideManager.Get();
            if (guide != null)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    var found = guide.GetFontByName(name);
                    if (found != null) return found;
                }
                var byId = guide.GetFontByID(0);
                if (byId != null) return byId;
            }

            // 2. TMP project default. This is what TMP uses when you create a
            //    TextMeshPro component without setting font explicitly. Should
            //    always be present in any project that has TMP installed.
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                    return TMP_Settings.defaultFontAsset;
            }
            catch { /* TMP_Settings missing - keep falling through */ }

            // 3. Last-ditch Resources lookup. Built-in TMP ships LiberationSans SDF
            //    in Resources/Fonts & Materials/.
            try
            {
                var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (fallback != null) return fallback;
            }
            catch { /* nothing more we can do */ }

            return null;
        }

    }
}
