using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SimpleJSON;
using TMPro;
using AITools.AIChat.Audio;
using AITools.AIChat.Video;
using AITools.AIChat.Web;
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
    /// <item>read_skill is fully in-process: looks up the skill by id, injects its
    /// full markdown body into the next prompt, and asks the host for one synthetic
    /// continue turn so the LLM can immediately use it.</item>
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
        private int _lastLocalOpInputChatImageIndex = -1;
        private PicMain _lastLocalOpOutputPic;
        private readonly HashSet<SkillAction> _reloadAttemptedActions = new HashSet<SkillAction>();
        private readonly Dictionary<PicMain, List<CompositionRectRecord>> _compositionRectsByPic = new Dictionary<PicMain, List<CompositionRectRecord>>();
        private readonly HashSet<string> _layoutAuditWarnings = new HashSet<string>();

        private sealed class CompositionRectRecord
        {
            public string Kind;
            public RectInt Rect;
            public string Label;
        }

        // Session-only "/applystyle" directive: when non-null, every generate-class
        // action (image/movie, fresh or chained) gets its prompt rewritten by a small
        // LLM job before the render is spawned. Set/cleared from the chat panel; NOT
        // persisted and NOT reset per turn (it's a sticky session preference). The
        // companion set marks actions whose prompt has already been restyled this turn
        // so the deferred re-run doesn't restyle a second time.
        private string _styleDirective;
        private readonly HashSet<SkillAction> _styleAppliedActions = new HashSet<SkillAction>();
        private const float ChatImageReloadPollSeconds = 0.2f;
        // Anchor GPU renders routinely exceed the old fixed 12s deadline. We now
        // wait as long as the referenced chat-image Pic is still generating, with
        // a generous absolute safety cap and a short grace for "not busy yet"
        // (job queued but no GPU server has picked it up).
        private const float ChatImageReloadAbsoluteCapSeconds = 600f;
        private const float ChatImageNotYetBusyGraceSeconds = 20f;
        private const double VideoToVideoWorkflowInputFps = 16.0;
        private const int VideoToVideoFrameStride = 4;
        private const string RifeVideoDefaultPreset = "Video To Video (RIFE Interpolation).txt";

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
        /// Install (or clear, when null/blank) the session "/applystyle" restyle
        /// directive. While set, every image/movie render the chat AI produces has its
        /// prompt rewritten by a small LLM job per this directive just before it is sent
        /// to the GPU. Not persisted; survives across turns until explicitly cleared.
        /// </summary>
        public void SetStyleDirective(string directive)
        {
            _styleDirective = string.IsNullOrWhiteSpace(directive) ? null : directive.Trim();
        }

        /// <summary>
        /// Enqueue a parsed action for in-order execution. The pump drains the
        /// queue synchronously; if an action defers, the pump parks and the rest
        /// of the turn's actions wait behind it.
        /// </summary>
        public void EnqueueAction(SkillAction action)
        {
            // Tally at ENQUEUE time (not dispatch): a render queued behind a deferred web
            // fetch has not dispatched yet when the reply finalizes, but it must still count
            // as the follow-up that makes the safety-net continue unnecessary.
            if (action != null)
            {
                string tallyId = NormalizeSkillId((action.SkillId ?? "").Trim().ToLowerInvariant());
                if (tallyId != BuiltInSkillIds.Continue && tallyId != BuiltInSkillIds.ReadSkill && tallyId != BuiltInSkillIds.InspectImage)
                {
                    if (IsPreparatorySkill(tallyId)) _turnPreparatoryActions++;
                    else _turnOtherActions++;
                }
            }
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
        /// True when the action pump has fully drained: nothing running, nothing queued,
        /// and nothing parked on a deferred coroutine. The automation harness uses this to
        /// know the post-text-turn action phase (image gen, local composition, etc.) is done.
        /// </summary>
        public bool IsIdle => _pumpState == PumpState.Idle && _actionQueue.Count == 0;

        // Per-turn tally used by the host's "unfinished plan" safety net: a reply that only
        // PREPARED media (fetched a web photo/clip, extracted a frame, cut a clip) but never
        // emitted the render it announced gets one automatic continue turn.
        private int _turnPreparatoryActions;
        private int _turnOtherActions;
        public bool TurnHadOnlyPreparatoryActions => _turnPreparatoryActions > 0 && _turnOtherActions == 0;

        private static bool IsPreparatorySkill(string skillId)
        {
            switch (skillId)
            {
                case BuiltInSkillIds.WebImage:
                case BuiltInSkillIds.WebVideo:
                case BuiltInSkillIds.WebSearch:
                case BuiltInSkillIds.WebPage:
                case BuiltInSkillIds.ExtractStill:
                case BuiltInSkillIds.ClipVideo:
                    return true;
                default:
                    return false;
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
            // The directive itself is sticky across turns; only the per-turn
            // "already restyled this action" markers are cleared.
            _styleAppliedActions.Clear();
            _pumpState = PumpState.Idle;
            _draining = false;
            _lastActionDeferred = false;
            _blockingAction = null;
            _turnPreparatoryActions = 0;
            _turnOtherActions = 0;
            _lastLocalOpOutputChatImageIndex = -1;
            _lastLocalOpInputChatImageIndex = -1;
            _lastLocalOpOutputPic = null;
            _compositionRectsByPic.Clear();
            _layoutAuditWarnings.Clear();
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
                _lastLocalOpInputChatImageIndex = -1;
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

            // stitch_video takes a LIST of sources. A model that writes the list into
            // chat_image="3,5,7" would otherwise die in the anchor rewrite below ("3,5,7"
            // is not an anchor), so move list-shaped values to chat_images first.
            if (action.SkillId == BuiltInSkillIds.StitchVideo)
                NormalizeStitchListArgs(action);

            // Rewrite any chat_image="<name>" into chat_image="<number>" using the host's
            // character-anchor registry, BEFORE the slot logic below (which parses those
            // attributes as integers). Idempotent, so the deferred re-execution path is
            // safe. Done here in the dispatcher so every skill that reads chat_image*
            // benefits, not just image_to_image.
            if (!NormalizeAnchorRefs(action))
                return;

            // Editor-only: record the raw tool call (skill id + every attribute) so
            // the AI Chat log shows exactly what the model emitted - including the
            // full generate_image prompt (where poster/book text sometimes gets
            // baked in instead of being laid out with draw_text).
            AIChatLog.Action(action.SkillId, action.GetArgsForToolLog());

            switch (action.SkillId.ToLowerInvariant())
            {
                case BuiltInSkillIds.GenerateImage:
                case BuiltInSkillIds.GenerateMovie:
                    ExecuteGenerate(action, useAttachment: false);
                    break;

                case BuiltInSkillIds.ImageToImage:
                case BuiltInSkillIds.ImageToMovie:
                case BuiltInSkillIds.VideoToVideo:
                    ExecuteGenerate(action, useAttachment: true);
                    break;

                case BuiltInSkillIds.RifeVideo:
                    ExecuteRifeVideo(action);
                    break;

                case BuiltInSkillIds.ClipVideo:
                    ExecuteClipVideo(action);
                    break;

                case BuiltInSkillIds.ExtractStill:
                    ExecuteExtractStill(action);
                    break;

                case BuiltInSkillIds.StitchVideo:
                    ExecuteStitchVideo(action);
                    break;

                case BuiltInSkillIds.WebSearch:
                    ExecuteWebSearch(action);
                    break;

                case BuiltInSkillIds.WebImage:
                    ExecuteWebImage(action);
                    break;

                case BuiltInSkillIds.WebVideo:
                    ExecuteWebVideo(action);
                    break;

                case BuiltInSkillIds.WebPage:
                    ExecuteWebPage(action);
                    break;

                case BuiltInSkillIds.GenerateMusic:
                    ExecuteGenerateAudio(action, AudioGenKind.Music);
                    break;

                case BuiltInSkillIds.GenerateSfx:
                    ExecuteGenerateAudio(action, AudioGenKind.Sfx);
                    break;

                case BuiltInSkillIds.GenerateSpeech:
                    ExecuteGenerateAudio(action, AudioGenKind.Speech);
                    break;

                case BuiltInSkillIds.SetVideoAudio:
                    ExecuteSetVideoAudio(action);
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
                case BuiltInSkillIds.InspectImage:
                    ExecuteInspectImage(action);
                    break;

                case BuiltInSkillIds.Continue:
                    // Control action: the model is telling us it isn't done and wants
                    // another turn. No Pic, no GPU - just register a synthetic continue
                    // through the host's auto-resume path (capped against runaways).
                    _host?.RequestContinueTurn();
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

        // ---------- RIFE video interpolation ----------

        private void ExecuteRifeVideo(SkillAction action)
        {
            if (string.IsNullOrWhiteSpace(action.GetArg("preset")))
                action.Args["preset"] = RifeVideoDefaultPreset;

            // Utility workflow: the prompt is only provenance text. The workflow does
            // not consume <AITOOLS_PROMPT>, but ExecuteGenerate's shared path expects a
            // prompt-like label for chat logs and Pic history.
            if (string.IsNullOrWhiteSpace(action.Prompt))
                action.Args["prompt"] = "RIFE frame interpolation only; preserve the source video exactly.";

            ExecuteGenerate(action, useAttachment: true);
        }

        // ---------- Local video clip import ----------

        private void ExecuteClipVideo(SkillAction action)
        {
            int chatN = action.ChatImageIndex ?? (_host?.GetLatestChatImageIndex() ?? -1);
            if (chatN <= 0)
            {
                _host?.AddSystemInjectionAndBubble(
                    "clip_video needs chat_image=\"N\" pointing at an existing Movie bubble. " +
                    "If the user just dropped a video, wait for it to import as Movie #N first.");
                return;
            }

            string moviePath = _host?.GetChatImageMovieFilePath(chatN);
            if (string.IsNullOrEmpty(moviePath))
            {
                _host?.AddSystemInjectionAndBubble(
                    $"clip_video needs a SOURCE VIDEO, but chat_image=\"{chatN}\" is not a Movie bubble. " +
                    "Use a Movie #N entry from CHAT IMAGES.");
                return;
            }

            float start = ParseFloat(
                action.GetArg("start")
                ?? action.GetArg("start_seconds")
                ?? action.GetArg("time")
                ?? action.GetArg("at"),
                0f);
            float duration = ParseFloat(
                action.GetArg("duration")
                ?? action.GetArg("duration_seconds")
                ?? action.GetArg("seconds")
                ?? action.GetArg("length"),
                FfmpegTool.DefaultClipDurationSeconds);
            float fps = ParseFloat(
                action.GetArg("fps")
                ?? action.GetArg("frame_rate")
                ?? action.GetArg("framerate"),
                0f);
            bool includeAudio = ParseBool(
                action.GetArg("include_audio")
                ?? action.GetArg("audio"),
                true);
            if (ParseBool(action.GetArg("no_audio"), false))
                includeAudio = false;

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartClipVideoAction(action, chatN, start, duration, fps, includeAudio, ok =>
            {
                ResumePumpAfterDeferredComplete(action);
            });

            if (!started)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"clip_video could not start for chat_image=\"{chatN}\". The movie may have been deleted or unloaded.");
                return;
            }

            _lastActionDeferred = true;
        }

        // ---------- Local still-frame extraction ----------

        private void ExecuteExtractStill(SkillAction action)
        {
            int chatN = action.ChatImageIndex ?? (_host?.GetLatestChatImageIndex() ?? -1);
            if (chatN <= 0)
            {
                _host?.AddSystemInjectionAndBubble(
                    "extract_still needs chat_image=\"N\" pointing at an existing Movie bubble. " +
                    "If the user just dropped a video, wait for it to import as Movie #N first.");
                return;
            }

            string moviePath = _host?.GetChatImageMovieFilePath(chatN);
            if (string.IsNullOrEmpty(moviePath))
            {
                _host?.AddSystemInjectionAndBubble(
                    $"extract_still needs a SOURCE VIDEO, but chat_image=\"{chatN}\" is not a Movie bubble. " +
                    "Use a Movie #N entry from CHAT IMAGES.");
                return;
            }

            float atSeconds = ParseFloat(
                action.GetArg("time")
                ?? action.GetArg("at")
                ?? action.GetArg("seconds")
                ?? action.GetArg("position")
                ?? action.GetArg("start"),
                0f);

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartExtractStillAction(action, chatN, atSeconds, ok =>
            {
                ResumePumpAfterDeferredComplete(action);
            });

            if (!started)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"extract_still could not start for chat_image=\"{chatN}\". The movie may have been deleted or unloaded.");
                return;
            }

            _lastActionDeferred = true;
        }

        // ---------- Local multi-clip stitch (stitch_video) ----------

        // Attribute names the model may use for the ordered source list.
        private static readonly string[] StitchListArgNames = { "chat_images", "clips", "videos", "movies", "sources", "inputs" };

        /// <summary>
        /// Move a list-shaped <c>chat_image</c> value ("3,5,7", "3-12", "3 5 7") into
        /// <c>chat_images</c> so the generic anchor rewrite doesn't reject it. A single
        /// number or a single anchor name stays where it is (slot form).
        /// </summary>
        private static void NormalizeStitchListArgs(SkillAction action)
        {
            if (action == null) return;
            if (!action.Args.TryGetValue("chat_image", out string raw) || string.IsNullOrWhiteSpace(raw))
                return;
            string val = raw.Trim();
            bool listShaped = val.IndexOf(',') >= 0 || val.IndexOf(';') >= 0 || val.IndexOf(' ') >= 0
                || Regex.IsMatch(val, @"^\d+\s*-\s*\d+$") || string.Equals(val, "all", StringComparison.OrdinalIgnoreCase);
            if (!listShaped) return;

            bool hasList = false;
            foreach (string name in StitchListArgNames)
            {
                if (action.Args.TryGetValue(name, out string existing) && !string.IsNullOrWhiteSpace(existing))
                {
                    hasList = true;
                    break;
                }
            }
            if (!hasList)
                action.Args["chat_images"] = val;
            action.Args.Remove("chat_image");
        }

        /// <summary>
        /// Parse the stitch source list: comma/semicolon separated tokens, each a Movie
        /// number ("7"), an inclusive range ("3-12"), an anchor name ("scene1"), or "all"
        /// (every live Movie bubble in chat order). Appends slot numbers to
        /// <paramref name="sources"/> in the order written; repeats are allowed.
        /// </summary>
        private bool ParseStitchSourceList(string list, List<int> sources, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(list)) return true;

            string[] tokens = list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (token.Length == 0) continue;
                if (!TryAddStitchSourceToken(token, sources, out error))
                {
                    // "3 5 7" (space separated) - retry the token as several tokens.
                    string[] parts = token.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) return false;
                    foreach (string part in parts)
                    {
                        if (!TryAddStitchSourceToken(part, sources, out error))
                            return false;
                    }
                    error = null;
                }
            }
            return true;
        }

        private bool TryAddStitchSourceToken(string token, List<int> sources, out string error)
        {
            error = null;
            token = token.Trim().TrimStart('#');
            // Tolerate "movie 3" / "Movie #3" style tokens.
            var m = Regex.Match(token, @"^(?:movie|clip|video)\s*#?\s*(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success) token = m.Groups[1].Value;

            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
            {
                int count = _host?.GetChatImageCount() ?? 0;
                int added = 0;
                for (int i = 1; i <= count; i++)
                {
                    if (_host.IsChatImageMovie(i))
                    {
                        sources.Add(i);
                        added++;
                    }
                }
                if (added == 0)
                {
                    error = "\"all\" matched no Movie bubbles - there are no movies in this chat yet.";
                    return false;
                }
                return true;
            }

            var range = Regex.Match(token, @"^(\d+)\s*-\s*(\d+)$");
            if (range.Success)
            {
                int a = int.Parse(range.Groups[1].Value);
                int b = int.Parse(range.Groups[2].Value);
                if (a > b) { int t = a; a = b; b = t; }
                if (a <= 0)
                {
                    error = $"range \"{token}\" starts below 1.";
                    return false;
                }
                if (b - a + 1 > FfmpegTool.MaxStitchClips)
                {
                    error = $"range \"{token}\" spans more than {FfmpegTool.MaxStitchClips} clips.";
                    return false;
                }
                for (int i = a; i <= b; i++) sources.Add(i);
                return true;
            }

            if (int.TryParse(token, out int n))
            {
                if (n <= 0)
                {
                    error = $"\"{token}\" is not a valid chat_image number.";
                    return false;
                }
                sources.Add(n);
                return true;
            }

            int resolved = _host?.ResolveAnchorToIndex(token) ?? 0;
            if (resolved <= 0)
            {
                error = $"\"{token}\" is neither a Movie number nor a known anchor name (see the ANCHORS line of CURRENT STATE). " +
                        "Same-reply clips need anchor=\"name\" on their image_to_movie action; earlier clips use their Movie #N number.";
                return false;
            }
            sources.Add(resolved);
            return true;
        }

        private void ExecuteStitchVideo(SkillAction action)
        {
            var sources = new List<int>();

            // Slot form: chat_image + chat_image2..N (anchor names already rewritten to numbers).
            if (action.ChatImageIndex.HasValue) sources.Add(action.ChatImageIndex.Value);
            for (int slot = 2; slot <= SkillAction.MaxExtraInputSlot; slot++)
            {
                int? idx = action.GetExtraChatImageIndex(slot);
                if (idx.HasValue) sources.Add(idx.Value);
            }

            // List form: chat_images="scene1,scene2,7-9" / "all".
            string list = FirstArg(action, StitchListArgNames);
            if (!string.IsNullOrEmpty(list) && !ParseStitchSourceList(list, sources, out string listError))
            {
                _host?.AddSystemInjectionAndBubble("stitch_video: " + listError);
                return;
            }

            if (sources.Count < 2)
            {
                _host?.AddSystemInjectionAndBubble(
                    "stitch_video needs at least TWO Movie bubbles, in playback order: " +
                    "chat_images=\"3,5,7\" (numbers, 3-7 ranges, anchor names, or \"all\"). " +
                    "For clips generated in this same reply, put anchor=\"sceneN\" on each image_to_movie " +
                    "action and list those names.");
                return;
            }
            if (sources.Count > FfmpegTool.MaxStitchClips)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"stitch_video joins at most {FfmpegTool.MaxStitchClips} clips per action ({sources.Count} were listed). " +
                    "Stitch them in batches, then stitch the batch results.");
                return;
            }

            // Every source must be a Movie slot. A movie whose render is still in flight
            // is fine (the host waits for it); a still image or unknown slot is not.
            var notMovies = new List<int>();
            foreach (int idx in sources)
            {
                bool isMovie = _host?.IsChatImageMovie(idx) ?? false;
                if (!isMovie && !notMovies.Contains(idx)) notMovies.Add(idx);
            }
            if (notMovies.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (int idx in notMovies)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append('#').Append(idx);
                }
                _host?.AddSystemInjectionAndBubble(
                    $"stitch_video only joins Movie bubbles, but {sb} {(notMovies.Count == 1 ? "is not a movie" : "are not movies")} " +
                    "(a still image, or a slot that does not exist). Use Movie #N entries from CHAT IMAGES, " +
                    "or animate the still first (image_to_movie) and stitch its anchor.");
                return;
            }

            var req = new FfmpegTool.StitchRequest();
            int width = ParseIntArg(action.GetArg("width"), 0);
            int height = ParseIntArg(action.GetArg("height"), 0);
            if (width > 0 && height > 0)
            {
                req.Width = width;
                req.Height = height;
            }
            req.Fps = ParseFloat(FirstArg(action, "fps", "frame_rate", "framerate"), 0f);
            req.IncludeAudio = ParseBool(action.GetArg("include_audio") ?? action.GetArg("audio"), true);
            if (ParseBool(action.GetArg("no_audio"), false))
                req.IncludeAudio = false;

            float crossfade = ParseFloat(FirstArg(action, "crossfade", "crossfade_seconds", "dissolve", "fade"), 0f);
            string transition = (FirstArg(action, "transition") ?? "").Trim().ToLowerInvariant();
            if (crossfade <= 0f && (transition == "crossfade" || transition == "fade" || transition == "dissolve" || transition == "xfade"))
                crossfade = 0.5f;
            if (transition == "cut" || transition == "none" || transition == "hard")
                crossfade = 0f;
            req.CrossfadeSeconds = Mathf.Clamp(crossfade, 0f, 5f);

            _host?.MarkChainTargetStale();
            int epoch = _turnEpoch;
            bool started = _host != null && _host.StartStitchVideoAction(action, sources, req, ok =>
            {
                // The host keeps waiting/stitching across later user turns (only Stop/Clear
                // cancel it). Resume the pump only if this is still the turn that parked it;
                // a newer turn has its own queue and may be parked on a different action.
                if (_turnEpoch == epoch)
                    ResumePumpAfterDeferredComplete(action);
            });

            if (!started)
            {
                _host?.AddSystemInjectionAndBubble("stitch_video could not start. The listed movies may have been deleted or unloaded.");
                return;
            }

            _lastActionDeferred = true;
        }

        // ---------- Web media fetch (web_search / web_image / web_video) ----------

        private static string FirstArg(SkillAction action, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string v = action.GetArg(names[i]);
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        private static int ParseIntArg(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v))
                return v;
            float f;
            if (float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f))
                return Mathf.RoundToInt(f);
            return fallback;
        }

        /// <summary>
        /// Shared pre-flight for the web skills: the Brave key must exist (the red Error
        /// bubble is user-facing; the silent injection keeps the model from retrying), and
        /// a model-supplied url= must be a public http/https address.
        /// </summary>
        private bool WebPreflight(SkillAction action, string skillId, string url, bool needsSearch)
        {
            // The AI Chat header "Web" toggle gates EVERY web action, direct url= included,
            // before any request is made. The model already sees "WEB ACCESS: OFF" in CURRENT
            // STATE; this is the hard stop for the case where it tries anyway.
            if (_host != null && !_host.IsWebAccessEnabled())
            {
                _host.AddWebTraceNotice(skillId + "  " + DescribeWebSource(action) + "\nNot started: Web access is OFF (the \"Web\" checkbox in the AI Chat header).");
                _host.AddSystemInjectionSilent(
                    "(" + skillId + " is disabled: the user turned Web access off in the AI Chat header, so web_search / web_image / web_video / web_page " +
                    "all fail and nothing was fetched. Do not emit them again until CURRENT STATE says WEB ACCESS: ON. Continue without online data " +
                    "and, if the request needed it, tell the user web access is off and that the Web checkbox in the AI Chat header turns it on.)");
                _host.RequestContinueTurn();
                return false;
            }
            if (needsSearch && !BraveSearchClient.HasApiKey())
            {
                _host?.AddWebTraceNotice(skillId + "  " + DescribeWebSource(action) + "\nNot started: no Brave Search API key is configured.");
                _host?.AddErrorBubble(
                    skillId + " needs a Brave Search API key. Set it in Settings > Web (stored as set_brave_search_api_key in config.txt). " +
                    "Keys come from https://brave.com/search/api/ (the Search plan includes free monthly credit).");
                _host?.AddSystemInjectionSilent(
                    "(" + skillId + " is unavailable: no Brave Search API key is configured. Tell the user to set one in Settings > Web. " +
                    "Do not retry web_search/web_image/web_video/web_page query= until they say it is set; a direct url= still works for web_image/web_video/web_page.)");
                // The streamed reply usually already promised the result; one bounded
                // continue turn lets the assistant tell the user what actually happened.
                _host?.RequestContinueTurn();
                return false;
            }
            if (!string.IsNullOrEmpty(url))
            {
                string reason;
                if (!WebMediaDownloader.IsAllowedPublicHttpUrl(url, out reason))
                {
                    _host?.AddWebTraceNotice(skillId + "  url=\"" + url + "\"\nRejected before any request: " + reason + ".");
                    _host?.AddSystemInjectionSilent(
                        "(" + skillId + " rejected url=\"" + url + "\": " + reason + ". Only public http/https URLs are allowed; never invent URLs - use query= to search instead.)");
                    return false;
                }
            }
            return true;
        }

        private static string DescribeWebSource(SkillAction action)
        {
            string u = FirstArg(action, "url", "link", "href", "src", "page");
            if (!string.IsNullOrEmpty(u)) return "url=\"" + u + "\"";
            string q = FirstArg(action, "query", "q", "search");
            if (!string.IsNullOrEmpty(q)) return "query=\"" + q + "\"";
            string r = FirstArg(action, "result", "pick");
            if (!string.IsNullOrEmpty(r)) return "result=\"" + r + "\"";
            return "";
        }

        private void ExecuteWebSearch(SkillAction action)
        {
            var req = new WebSearchRequest();
            req.Query = FirstArg(action, "query", "q", "search", "text", "prompt");
            if (string.IsNullOrEmpty(req.Query))
            {
                _host?.AddSystemInjectionAndBubble("web_search needs query=\"...\".");
                return;
            }
            string kind = (FirstArg(action, "kind", "type", "mode") ?? "images").ToLowerInvariant();
            if (kind.StartsWith("vid")) req.Kind = WebSearchKind.Videos;
            else if (kind.StartsWith("web") || kind.StartsWith("page") || kind.StartsWith("text")) req.Kind = WebSearchKind.Web;
            else req.Kind = WebSearchKind.Images;
            req.Count = Mathf.Clamp(ParseIntArg(FirstArg(action, "count", "n", "max", "results"), 10), 1, WebRequestLimits.MaxSearchCount);
            req.SafeSearch = WebRequestLimits.ParseSafeSearch(FirstArg(action, "safesearch", "safe_search", "safe"));

            if (!WebPreflight(action, "web_search", null, needsSearch: true))
                return;

            // A list-only action is useless without a turn to act on it: auto-continue
            // unless the model explicitly opted out with resume="false".
            bool resume = ParseBool(action.GetArg("resume"), true);
            if (resume)
                _host?.RequestAutoResumeAfterWebFetch();

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartWebSearchAction(action, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble("web_search could not start.");
                return;
            }
            _lastActionDeferred = true;
        }

        private void ExecuteWebImage(SkillAction action)
        {
            var req = new WebImageRequest();
            req.Query = FirstArg(action, "query", "q", "search");
            req.Url = FirstArg(action, "url", "link", "src", "href");
            req.ResultToken = FirstArg(action, "result", "pick", "from_result", "search_result");
            int sources = (string.IsNullOrEmpty(req.Query) ? 0 : 1) + (string.IsNullOrEmpty(req.Url) ? 0 : 1) + (string.IsNullOrEmpty(req.ResultToken) ? 0 : 1);
            if (sources == 0)
            {
                _host?.AddSystemInjectionAndBubble("web_image needs one of query=\"...\", url=\"https://...\", or result=\"S1:3\".");
                return;
            }
            if (sources > 1)
            {
                // Prefer the most specific source; tell the model so it stops mixing them.
                if (!string.IsNullOrEmpty(req.Url)) { req.Query = null; req.ResultToken = null; }
                else req.Query = null;
                _host?.AddSystemInjectionSilent("(web_image: use only ONE of query/url/result per action; the most specific one was used.)");
            }
            req.Count = Mathf.Clamp(ParseIntArg(FirstArg(action, "count", "n", "max", "images"), 1), 1, WebRequestLimits.MaxImageSuccesses);
            req.MinWidth = Mathf.Clamp(ParseIntArg(FirstArg(action, "min_width", "minwidth", "min_size", "minsize"), 256), 16, 4096);
            req.SafeSearch = WebRequestLimits.ParseSafeSearch(FirstArg(action, "safesearch", "safe_search", "safe"));
            req.Anchor = string.IsNullOrWhiteSpace(action.AnchorName) ? null : action.AnchorName.Trim();
            req.Verify = ParseBool(FirstArg(action, "verify", "check", "inspect"), true);
            req.Criteria = FirstArg(action, "criteria", "want", "require", "requirements", "must");

            // result= tokens never need a new Brave call: S-lists only exist if a key already
            // worked, and P-lists (web_page image lists) are plain page URLs.
            if (!WebPreflight(action, "web_image", req.Url, needsSearch: string.IsNullOrEmpty(req.Url) && string.IsNullOrEmpty(req.ResultToken)))
                return;

            if (action.Resume)
                _host?.RequestAutoResumeAfterWebFetch();

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartWebImageAction(action, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble("web_image could not start.");
                return;
            }
            _lastActionDeferred = true;
        }

        /// <summary>
        /// web_page: fetch ONE page (url=, a kind="web" search result, or the best hit of query=),
        /// extract its readable text into the prompt and list candidate images as P&lt;n&gt;:&lt;i&gt;.
        /// Deferred like the other web fetches; auto-continues by default so the model can use
        /// the text without the user pressing Send.
        /// </summary>
        private void ExecuteWebPage(SkillAction action)
        {
            var req = new WebPageRequest();
            req.Url = FirstArg(action, "url", "link", "href", "src", "page", "address");
            req.ResultToken = FirstArg(action, "result", "pick", "from_result", "search_result");
            req.Query = FirstArg(action, "query", "q", "search", "topic", "about");
            int sources = (string.IsNullOrEmpty(req.Url) ? 0 : 1) + (string.IsNullOrEmpty(req.ResultToken) ? 0 : 1) + (string.IsNullOrEmpty(req.Query) ? 0 : 1);
            if (sources == 0)
            {
                _host?.AddSystemInjectionAndBubble("web_page needs one of url=\"https://...\", result=\"S1:3\" (a web_search kind=\"web\" hit), or query=\"...\".");
                return;
            }
            if (sources > 1)
            {
                if (!string.IsNullOrEmpty(req.Url)) { req.ResultToken = null; req.Query = null; }
                else req.Query = null;
                _host?.AddSystemInjectionSilent("(web_page: use only ONE of url/result/query per action; the most specific one was used.)");
            }
            if (!string.IsNullOrEmpty(req.Url) && req.Url.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                // "atari 2600 history" typed into url= is really a query; "en.wikipedia.org/wiki/X" just lacks the scheme.
                if (req.Url.IndexOf('.') < 0 || req.Url.IndexOf(' ') >= 0) { req.Query = req.Url; req.Url = null; }
                else req.Url = "https://" + req.Url;
            }
            req.MaxChars = Mathf.Clamp(ParseIntArg(FirstArg(action, "max_chars", "chars", "length", "max_length", "limit"), WebRequestLimits.DefaultPageChars),
                WebRequestLimits.MinPageChars, WebRequestLimits.MaxPageChars);
            req.Images = ParseBool(FirstArg(action, "images", "list_images", "with_images", "image_list"), true);
            req.MaxImages = Mathf.Clamp(ParseIntArg(FirstArg(action, "max_images", "image_count", "images_max"), WebRequestLimits.DefaultPageImages), 1, WebRequestLimits.MaxPageImages);
            req.SafeSearch = WebRequestLimits.ParseSafeSearch(FirstArg(action, "safesearch", "safe_search", "safe"));
            req.Resume = ParseBool(action.GetArg("resume"), true);

            if (!WebPreflight(action, "web_page", req.Url, needsSearch: string.IsNullOrEmpty(req.Url) && string.IsNullOrEmpty(req.ResultToken)))
                return;

            if (req.Resume)
                _host?.RequestAutoResumeAfterWebFetch();

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartWebPageAction(action, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble("web_page could not start.");
                return;
            }
            _lastActionDeferred = true;
        }

        private void ExecuteWebVideo(SkillAction action)
        {
            var req = new WebVideoRequest();
            req.Query = FirstArg(action, "query", "q", "search");
            req.Url = FirstArg(action, "url", "link", "src", "href");
            req.ResultToken = FirstArg(action, "result", "pick", "from_result", "search_result");
            int sources = (string.IsNullOrEmpty(req.Query) ? 0 : 1) + (string.IsNullOrEmpty(req.Url) ? 0 : 1) + (string.IsNullOrEmpty(req.ResultToken) ? 0 : 1);
            if (sources == 0)
            {
                _host?.AddSystemInjectionAndBubble("web_video needs one of query=\"...\", url=\"https://...\", or result=\"S2:1\".");
                return;
            }
            if (sources > 1)
            {
                if (!string.IsNullOrEmpty(req.Url)) { req.Query = null; req.ResultToken = null; }
                else req.Query = null;
                _host?.AddSystemInjectionSilent("(web_video: use only ONE of query/url/result per action; the most specific one was used.)");
            }
            req.StartSeconds = Mathf.Max(0f, ParseFloat(FirstArg(action, "start", "start_seconds", "time", "at", "offset"), 0f));
            req.DurationSeconds = Mathf.Clamp(ParseFloat(FirstArg(action, "duration", "duration_seconds", "seconds", "length"), FfmpegTool.DefaultClipDurationSeconds),
                WebRequestLimits.MinClipSeconds, WebRequestLimits.MaxClipSeconds);
            req.MaxSourceMinutes = Mathf.Max(0f, ParseFloat(FirstArg(action, "max_source_minutes", "max_minutes", "max_source_duration"), 20f));
            req.IncludeAudio = ParseBool(FirstArg(action, "include_audio", "audio"), true);
            if (ParseBool(action.GetArg("no_audio"), false)) req.IncludeAudio = false;
            req.SafeSearch = WebRequestLimits.ParseSafeSearch(FirstArg(action, "safesearch", "safe_search", "safe"));
            req.Anchor = string.IsNullOrWhiteSpace(action.AnchorName) ? null : action.AnchorName.Trim();
            req.Verify = ParseBool(FirstArg(action, "verify", "check", "inspect"), true);
            req.Criteria = FirstArg(action, "criteria", "want", "require", "requirements", "must");
            req.RequireSpeech = ParseBool(FirstArg(action, "speech", "dialog", "dialogue", "needs_speech", "voice", "talking"), false);
            if (!req.RequireSpeech && !string.IsNullOrEmpty(req.Criteria))
            {
                string c = req.Criteria.ToLowerInvariant();
                if (c.Contains("speak") || c.Contains("talk") || c.Contains("dialog") || c.Contains("voice") || c.Contains("says") || c.Contains("saying"))
                    req.RequireSpeech = true;
            }

            if (!WebPreflight(action, "web_video", req.Url, needsSearch: string.IsNullOrEmpty(req.Url)))
                return;

            // A fetched clip is almost always a stepping stone (a reference for a render) and
            // the model cannot know what a searched clip shows until its caption exists, so
            // auto-continue by default; resume="false" opts out.
            bool resume = ParseBool(action.GetArg("resume"), true);
            if (resume)
                _host?.RequestAutoResumeAfterWebFetch();

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartWebVideoAction(action, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble("web_video could not start.");
                return;
            }
            _lastActionDeferred = true;
        }

        // ---------- Audio generation (music / sfx / speech via the audio gateway) ----------

        // The music model refuses anything under 10 s (HTTP 422); the mix step cuts the
        // track to the video anyway, so a "7 s song for a 5 s clip" is silently raised.
        private const float MusicMinSeconds = 10f;
        private const float MusicMaxSeconds = 360f;
        private const float SfxMaxSeconds = 11f;

        /// <summary>
        /// generate_music / generate_sfx / generate_speech: validate the attributes the model
        /// wrote, translate them into gateway form fields, and hand the request to the host,
        /// which does the HTTP call, the waveform preview, and the "Audio #N" bubble. The
        /// action is deferred (pump parked) so a same-reply set_video_audio audio="anchor"
        /// finds the new bubble.
        /// </summary>
        private void ExecuteGenerateAudio(SkillAction action, AudioGenKind kind)
        {
            var req = new AudioGenRequest { Kind = kind };
            string skill = req.SkillName;

            if (!AudioGenClient.TryGetBaseUrl(out _, out _, out string why))
            {
                _host?.AddSystemInjectionAndBubble(
                    $"{skill} is unavailable: {why}. Tell the user to enter the gateway URL under Settings > Audio > Audio generation, then stop.");
                return;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            switch (kind)
            {
                case AudioGenKind.Music:
                {
                    string prompt = FirstArg(action, "prompt", "caption", "description", "text");
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        _host?.AddSystemInjectionAndBubble("generate_music needs prompt=\"...\" (a structured caption describing the track).");
                        return;
                    }
                    float duration = ParseFloat(FirstArg(action, "duration", "seconds", "length", "duration_seconds"), 30f);
                    if (duration > MusicMaxSeconds)
                    {
                        _host?.AddSystemInjectionSilent($"(generate_music: duration {duration:0} s exceeds the {MusicMaxSeconds:0} s maximum; clamped.)");
                        duration = MusicMaxSeconds;
                    }
                    if (duration < MusicMinSeconds)
                    {
                        _host?.AddSystemInjectionSilent($"(generate_music: the music model needs duration >= {MusicMinSeconds:0} s; {duration:0.#} s was raised to {MusicMinSeconds:0}. set_video_audio cuts it to the clip.)");
                        duration = MusicMinSeconds;
                    }
                    duration = Mathf.Clamp(duration, MusicMinSeconds, MusicMaxSeconds);
                    req.Fields["prompt"] = prompt;
                    req.Fields["duration"] = duration.ToString("0.##", ci);
                    // Short jingles would otherwise auto-route to the sound-effect model.
                    req.Fields["mode"] = "music";
                    req.Fields["format"] = "wav";

                    string lyrics = FirstArg(action, "lyrics", "lyric", "words", "verse");
                    bool lyricsHaveWords = false;
                    if (!string.IsNullOrWhiteSpace(lyrics))
                    {
                        lyrics = lyrics.Replace("\\n", "\n").Trim();
                        req.Fields["lyrics"] = lyrics;
                        foreach (string line in lyrics.Split('\n'))
                        {
                            string t = line.Trim();
                            if (t.Length > 0 && !t.StartsWith("[")) { lyricsHaveWords = true; break; }
                        }
                    }
                    string vocalsArg = FirstArg(action, "vocals", "vocal", "singing", "sing", "sung");
                    bool vocals = ParseBool(vocalsArg, lyricsHaveWords);
                    if (vocals) req.Fields["vocals"] = "true";
                    CopyArgIfPresent(action, req, "seed");
                    CopyArgIfPresent(action, req, "bpm");
                    CopyArgIfPresent(action, req, "steps");
                    req.Label = CompactLabel(FirstArg(action, "title", "name") ?? prompt, 90);
                    break;
                }
                case AudioGenKind.Sfx:
                {
                    string prompt = FirstArg(action, "prompt", "description", "sound", "effect", "text");
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        _host?.AddSystemInjectionAndBubble("generate_sfx needs prompt=\"...\" (a foley-style description of the sound).");
                        return;
                    }
                    float duration = ParseFloat(FirstArg(action, "duration", "seconds", "length", "duration_seconds"), 2f);
                    if (duration > SfxMaxSeconds)
                    {
                        _host?.AddSystemInjectionSilent($"(generate_sfx: sound effects are at most {SfxMaxSeconds:0} s; duration clamped. Longer material is generate_music territory.)");
                        duration = SfxMaxSeconds;
                    }
                    duration = Mathf.Clamp(duration, 0.1f, SfxMaxSeconds);
                    req.Fields["prompt"] = prompt;
                    req.Fields["duration"] = duration.ToString("0.##", ci);
                    req.Fields["format"] = "wav";
                    CopyArgIfPresent(action, req, "seed");
                    CopyArgIfPresent(action, req, "steps");
                    req.Label = CompactLabel(prompt, 90);
                    break;
                }
                default:
                {
                    string text = FirstArg(action, "text", "prompt", "line", "say", "says", "dialog", "dialogue", "speech");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _host?.AddSystemInjectionAndBubble("generate_speech needs text=\"...\" (the exact words to speak).");
                        return;
                    }
                    req.Fields["text"] = text;
                    string scene = FirstArg(action, "scene", "direction", "delivery", "emotion", "style", "mood", "tone");
                    if (!string.IsNullOrWhiteSpace(scene)) req.Fields["scene"] = scene;
                    CopyArgIfPresent(action, req, "language");
                    CopyArgIfPresent(action, req, "engine");
                    CopyArgIfPresent(action, req, "temperature");
                    CopyArgIfPresent(action, req, "seed");

                    // Voice cloning: ref_voice points at a chat bubble (Audio or Movie) whose
                    // sound is uploaded as the sample; a library voice name is used otherwise.
                    string voice = FirstArg(action, "voice", "voice_name", "speaker", "voice_id");
                    string refRaw = FirstArg(action, "ref_voice", "voice_ref", "clone", "clone_voice", "voice_from", "ref_audio", "reference_voice");
                    if (!string.IsNullOrWhiteSpace(refRaw))
                    {
                        int refIdx = ResolveChatMediaRef(refRaw);
                        if (refIdx > 0)
                        {
                            req.RefVoiceChatImageIndex = refIdx;
                            req.RefVoiceStartSeconds = Mathf.Max(0f, ParseFloat(FirstArg(action, "ref_start", "ref_voice_start"), 0f));
                            req.RefVoiceDurationSeconds = Mathf.Clamp(ParseFloat(FirstArg(action, "ref_duration", "ref_voice_duration"), 25f), 3f, 30f);
                            voice = null;
                        }
                        else if (string.IsNullOrWhiteSpace(voice))
                        {
                            // Probably a library voice name written into the wrong attribute.
                            voice = refRaw;
                            _host?.AddSystemInjectionSilent($"(generate_speech: ref_voice=\"{refRaw}\" is not a chat bubble; used it as voice=\"{refRaw}\".)");
                        }
                        else
                        {
                            _host?.AddSystemInjectionSilent($"(generate_speech: ref_voice=\"{refRaw}\" matched no Audio/Movie bubble or anchor; using voice=\"{voice}\".)");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(voice)) req.Fields["voice"] = voice;
                    req.Label = CompactLabel(text, 90);
                    break;
                }
            }

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartGenerateAudioAction(action, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble($"{skill} could not start (the chat host refused the request).");
                return;
            }
            _lastActionDeferred = true;
        }

        private static void CopyArgIfPresent(SkillAction action, AudioGenRequest req, string name)
        {
            string v = action.GetArg(name);
            if (!string.IsNullOrWhiteSpace(v)) req.Fields[name] = v.Trim();
        }

        private static string CompactLabel(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max).TrimEnd() + "..." : s;
        }

        /// <summary>
        /// "7", "#7", "Audio #7", "Movie 3", or an anchor name -> chat image index (0 = none).
        /// </summary>
        private int ResolveChatMediaRef(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || _host == null) return 0;
            string token = raw.Trim().TrimStart('#');
            var m = Regex.Match(token, @"^(?:audio|movie|clip|video|sound|song|image)\s*#?\s*(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success) token = m.Groups[1].Value;
            if (int.TryParse(token, out int n))
                return n > 0 && n <= _host.GetChatImageCount() ? n : 0;
            return Mathf.Max(0, _host.ResolveAnchorToIndex(token));
        }

        private int FindLatestChatMedia(bool wantAudio)
        {
            if (_host == null) return 0;
            for (int i = _host.GetChatImageCount(); i >= 1; i--)
            {
                bool isAudio = _host.IsChatImageAudio(i);
                if (wantAudio ? isAudio : (_host.IsChatImageMovie(i) && !isAudio))
                    return i;
            }
            return 0;
        }

        // ---------- set_video_audio (local FFmpeg: mix / replace a Movie's soundtrack) ----------

        private void ExecuteSetVideoAudio(SkillAction action)
        {
            // Video source: chat_image (anchor already rewritten to a number), or video=/movie=,
            // else the newest real Movie bubble.
            int videoIdx = action.ChatImageIndex ?? 0;
            if (videoIdx <= 0)
            {
                string v = FirstArg(action, "video", "movie", "clip", "video_chat_image", "target");
                if (!string.IsNullOrWhiteSpace(v)) videoIdx = ResolveChatMediaRef(v);
            }
            if (videoIdx <= 0) videoIdx = FindLatestChatMedia(wantAudio: false);
            if (videoIdx <= 0)
            {
                _host?.AddSystemInjectionAndBubble("set_video_audio needs chat_image=\"N\" pointing at an existing Movie bubble (there is no Movie in this chat yet).");
                return;
            }
            if (!_host.IsChatImageMovie(videoIdx) || _host.IsChatImageAudio(videoIdx))
            {
                _host?.AddSystemInjectionAndBubble(
                    $"set_video_audio: chat_image=\"{videoIdx}\" is not a Movie bubble. chat_image must be the VIDEO (a Movie #N entry); put the sound in audio=\"...\".");
                return;
            }

            // Audio source: audio=/sound=/music=/song= (number or anchor), or chat_image2, else
            // the newest Audio bubble. A Movie is accepted too (its soundtrack is used).
            int audioIdx = 0;
            string a = FirstArg(action, "audio", "sound", "music", "song", "track", "audio_chat_image", "voice", "speech", "source_audio");
            if (!string.IsNullOrWhiteSpace(a))
            {
                audioIdx = ResolveChatMediaRef(a);
                if (audioIdx <= 0)
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"set_video_audio: audio=\"{a}\" is neither an Audio/Movie number nor a known anchor. Use the Audio #N number from CHAT IMAGES, " +
                        "or the anchor=\"name\" you gave the same-reply generate_music / generate_sfx / generate_speech action.");
                    return;
                }
            }
            if (audioIdx <= 0) audioIdx = action.ChatImageIndex2 ?? 0;
            if (audioIdx <= 0) audioIdx = FindLatestChatMedia(wantAudio: true);
            if (audioIdx <= 0)
            {
                _host?.AddSystemInjectionAndBubble(
                    "set_video_audio needs audio=\"N\" pointing at an Audio bubble (generate_music / generate_sfx / generate_speech first, or drop an audio file into the chat).");
                return;
            }
            if (!_host.IsChatImageMovie(audioIdx))
            {
                _host?.AddSystemInjectionAndBubble($"set_video_audio: audio=\"{audioIdx}\" is a still image, not a sound. Use an Audio #N (or a Movie whose soundtrack you want).");
                return;
            }
            if (audioIdx == videoIdx)
            {
                _host?.AddSystemInjectionAndBubble("set_video_audio: audio and chat_image point at the same bubble.");
                return;
            }

            var req = new FfmpegTool.MuxAudioRequest();
            string modeRaw = (FirstArg(action, "mode", "how", "blend") ?? "mix").Trim().ToLowerInvariant();
            switch (modeRaw)
            {
                case "replace":
                case "swap":
                case "only":
                case "new":
                case "instead":
                case "mute":
                    req.Mode = FfmpegTool.AudioMuxMode.Replace;
                    break;
                default:
                    req.Mode = FfmpegTool.AudioMuxMode.Mix;
                    break;
            }
            if (ParseBool(FirstArg(action, "replace", "replace_audio"), false)) req.Mode = FfmpegTool.AudioMuxMode.Replace;
            if (ParseBool(FirstArg(action, "keep_original", "keep_audio", "mix"), false)) req.Mode = FfmpegTool.AudioMuxMode.Mix;

            req.AudioVolume = Mathf.Clamp(ParseFloat(FirstArg(action, "volume", "audio_volume", "gain", "level", "music_volume"), 1f), 0f, 4f);
            req.OriginalVolume = Mathf.Clamp(ParseFloat(FirstArg(action, "original_volume", "video_volume", "existing_volume", "duck", "background_volume"), 1f), 0f, 4f);
            req.StartSeconds = Mathf.Max(0f, ParseFloat(FirstArg(action, "start", "offset", "at", "start_seconds"), 0f));
            req.Loop = ParseBool(FirstArg(action, "loop", "repeat"), false);
            req.FadeInSeconds = Mathf.Max(0f, ParseFloat(FirstArg(action, "fade_in", "fadein"), 0f));
            string fadeOut = FirstArg(action, "fade_out", "fadeout", "fade");
            req.FadeOutSeconds = string.IsNullOrWhiteSpace(fadeOut) ? -1f : Mathf.Max(0f, ParseFloat(fadeOut, 1f));

            _host?.MarkChainTargetStale();
            bool started = _host != null && _host.StartSetVideoAudioAction(action, videoIdx, audioIdx, req, ok => ResumePumpAfterDeferredComplete(action));
            if (!started)
            {
                _host?.AddSystemInjectionAndBubble($"set_video_audio could not start for Movie #{videoIdx} / audio #{audioIdx}.");
                return;
            }
            _lastActionDeferred = true;
        }

        // ---------- Generate (image or movie) ----------

        // H3 reference-to-video presets (photo refs, no pinned start frame). Distinct
        // from isH3RefVideoPreset ("Reference Video To Video", clip refs): the photo
        // presets ride the image_to_movie skill, so the start-frame aspect logic below
        // must exempt them by preset name.
        private static bool IsReferencePhotoPreset(string presetName)
            => !string.IsNullOrEmpty(presetName)
               && presetName.IndexOf("Reference To Video", StringComparison.OrdinalIgnoreCase) >= 0;

        // H3 reference prompts bind prose to pixels ONLY through per-type tags in
        // connection order (<Picture 1>.., <Video 1>.. - docs/minimax_h3.md "Model
        // facts"). Whitespace is required between word and number because the tag
        // reaches the encoder literally; "<Picture1>" is not a form we've verified.
        private static readonly Regex s_promptPictureTagRegex =
            new Regex(@"<\s*picture\s+(\d+)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex s_promptVideoTagRegex =
            new Regex(@"<\s*video\s+(\d+)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Describe where a staged reference came from, for correction notes. Slot 1
        /// is the primary chat_image/attachment; slots 2+ are the extra-input slots.
        /// Attachment refs are translated to their paste's durable chat_image bubble
        /// number when possible: correction notes ride a synthetic continue turn,
        /// which clears the per-turn attachment list, so naming attachment indexes
        /// there is dead advice.
        /// </summary>
        private string DescribeStagedRefSource(SkillAction action, int slot)
        {
            string suffix = slot == 1 ? "" : slot.ToString();
            string ci = action.GetArg("chat_image" + suffix);
            if (!string.IsNullOrEmpty(ci)) return $"chat_image{suffix}=\"{ci}\"";
            string at = action.GetArg("attachment" + suffix);
            if (!string.IsNullOrEmpty(at))
            {
                if (int.TryParse(at, out int aIdx))
                {
                    int bubble = _host?.ResolvePasteAttachmentToChatIndex(aIdx) ?? 0;
                    if (bubble > 0) return $"the pasted image chat_image=\"{bubble}\"";
                }
                return $"attachment{suffix}=\"{at}\"";
            }
            return slot == 1 ? "the primary source" : $"the photo staged in slot {slot}";
        }

        /// <summary>
        /// Deterministic pre-queue gate for H3 reference presets: the encoder gets no
        /// binding between prose and a staged reference except its tag, so a prompt
        /// that says "the blond woman" over three untagged photos routinely renders a
        /// different person. Require every staged clip/photo to appear as its exact
        /// tag (and no out-of-range tags) BEFORE spending GPU minutes; on mismatch,
        /// inject a slot->tag map plus a correction turn and report blocked=true (the
        /// caller must return without queuing). Descriptor lists are in tag order:
        /// clipDescs[0] = &lt;Video 1&gt;, photoDescs[0] = &lt;Picture 1&gt;, etc.
        /// </summary>
        private bool BlockH3ReferencePromptTagMismatch(SkillAction action, string resolvedPreset,
            List<string> clipDescs, List<string> photoDescs, string reEmitHint)
        {
            if ((clipDescs?.Count ?? 0) == 0 && (photoDescs?.Count ?? 0) == 0) return false;

            string prompt = action.Prompt ?? "";
            var picturesInPrompt = new HashSet<int>();
            foreach (Match m in s_promptPictureTagRegex.Matches(prompt))
                if (int.TryParse(m.Groups[1].Value, out int n)) picturesInPrompt.Add(n);
            var videosInPrompt = new HashSet<int>();
            foreach (Match m in s_promptVideoTagRegex.Matches(prompt))
                if (int.TryParse(m.Groups[1].Value, out int n)) videosInPrompt.Add(n);

            var problems = new List<string>();
            for (int k = 1; k <= photoDescs.Count; k++)
                if (!picturesInPrompt.Contains(k))
                    problems.Add($"<Picture {k}> ({photoDescs[k - 1]}) never appears in the prompt");
            foreach (int k in picturesInPrompt)
                if (k > photoDescs.Count)
                    problems.Add($"<Picture {k}> has no staged photo behind it (only {photoDescs.Count} staged)");
            for (int k = 1; k <= clipDescs.Count; k++)
                if (!videosInPrompt.Contains(k))
                    problems.Add($"<Video {k}> ({clipDescs[k - 1]}) never appears in the prompt");
            foreach (int k in videosInPrompt)
                if (k > clipDescs.Count)
                    problems.Add($"<Video {k}> has no staged clip behind it (only {clipDescs.Count} staged)");
            if (problems.Count == 0) return false;

            var map = new List<string>();
            for (int k = 1; k <= clipDescs.Count; k++) map.Add($"<Video {k}> = {clipDescs[k - 1]}");
            for (int k = 1; k <= photoDescs.Count; k++) map.Add($"<Picture {k}> = {photoDescs[k - 1]}");

            _host?.AddSystemInjectionAndBubble(
                $"Skill '{action.SkillId}' (preset '{resolvedPreset}') was NOT run: H3 reference prompts must " +
                "address EVERY staged reference by its exact tag - prose alone (\"the blond woman\") does not bind " +
                "to a photo and the render drifts off-identity. Tag map for what you staged (per-type, slot order): " +
                string.Join(", ", map) + ". Problems: " + string.Join("; ", problems) + ". " +
                reEmitHint + ", rewriting only the prompt so each reference is used by its tag at least once " +
                "(e.g. 'the woman from <Picture 1>' - angle brackets, capitalized, a space before the number).");
            _host?.RequestContinueTurn();
            AIChatLog.Note("ref_tag_gate",
                $"{action.SkillId}/{resolvedPreset}: blocked - {string.Join("; ", problems)}");
            return true;
        }

        // Explicit width/height on a START-FRAME video preset (H3/LTX/WAN i2v) whose
        // aspect clashes with the source image would visibly SQUISH the pinned first
        // frame: H3's MiniMaxH3ImageToVideo resizes the frame to the canvas with crop
        // disabled (plain lanczos stretch). Reinterpret such requests as a PIXEL BUDGET
        // at the source's aspect instead - "720p" on a portrait photo renders a
        // ~0.92MP portrait canvas - so the user's quality tier is honored without
        // distortion. Requests within ~5% of the source aspect pass through exactly
        // (covers 864x480-vs-16:9 style rounding). Reference presets and video sources
        // pin no frame, so their call sites keep SetWorkflowDimensionOverride directly.
        private void ApplyBudgetDimensionOverride(PicMain pic, int reqW, int reqH, int srcW, int srcH)
        {
            float reqAspect = (float)reqW / reqH;
            float srcAspect = (float)srcW / srcH;
            float ratio = reqAspect / srcAspect;
            if (ratio < 0.95f || ratio > 1.05f)
            {
                double budget = (double)reqW * reqH;
                int fitH = Mathf.Max(32, Mathf.RoundToInt((float)Math.Sqrt(budget / srcAspect) / 32f) * 32);
                int fitW = Mathf.Max(32, Mathf.RoundToInt((float)(budget / fitH) / 32f) * 32);
                _host?.AddInfoBubble(
                    $"(requested {reqW}x{reqH} doesn't match the source image's aspect - rendering " +
                    $"{fitW}x{fitH} instead, same pixel budget, so the start frame isn't distorted)");
                reqW = fitW;
                reqH = fitH;
            }
            pic.SetWorkflowDimensionOverride(reqW, reqH);
        }

        private void ExecuteGenerate(SkillAction action, bool useAttachment)
        {
            _lastLocalOpOutputChatImageIndex = -1;
            _lastLocalOpInputChatImageIndex = -1;
            _lastLocalOpOutputPic = null;

            // Generate-class skills with an empty prompt produce a workflow that runs
            // against whatever GameLogic.GetModifiedGlobalPrompt() happens to return -
            // typically the main GUI's prompt field, which has nothing to do with the
            // chat. Surface this loudly so it doesn't ship silent "no prompt" videos.
            // Checked for both chained AND non-chained paths, so the fast-fail catches
            // the common LLM mistake of emitting an action tag with no prompt attribute.
            if (SkillRequiresPrompt(action.SkillId) && string.IsNullOrWhiteSpace(action.Prompt))
            {
                var skill = _skills?.GetById(action.SkillId);
                string template = skill != null && !string.IsNullOrEmpty(skill.Template)
                    ? skill.Template
                    : $"<aitools_action skill=\"{action.SkillId}\" preset=\"...\" prompt=\"...\"/>";
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' was emitted without a prompt attribute (or it was empty). " +
                    "Generate-class skills must carry a non-empty prompt - the chat does NOT inherit prompt text " +
                    "from the main GUI. Re-emit with the prompt filled in. Template:\n  " + template);
                _host?.RequestContinueTurn();
                return;
            }

            // Rewrite STALE attachment="N" refs (this turn has no live attachments, so
            // the model means an EARLIER paste) to the bubble it actually meant, BEFORE
            // the movie gate and chain inference below read the source attributes.
            // Never touches the same-turn attachment flow.
            NormalizeStaleAttachmentRefs(action, useAttachment);

            // Movie bubbles expose their current frame as PNG bytes for the legitimate
            // "use this exact frame as a still/start frame" workflow. That permissive
            // byte path also made a bad image_to_image decision silently collapse a
            // requested video edit into one Klein still. Require an explicit opt-in for
            // Movie-as-frame actions; scene/motion/dialog/audio edits stay video-native.
            bool wouldAutoChain = !action.Chain
                && useAttachment
                && !action.AttachmentIndex.HasValue
                && !action.ChatImageIndex.HasValue
                && (_host?.GetTurnAttachmentCount() ?? 0) == 0
                && _host?.GetLastSpawnedPicForTurn() != null;
            if (RejectImplicitMovieFrameAction(action, action.Chain || wouldAutoChain))
                return;

            // Bernini's v2v graph produces pixels only; it has no output-audio wire.
            // If the model asks Bernini to create speech, dialog, music, or sound, stop
            // before spending GPU time and give it an automatic correction turn that
            // selects H3 Ref2VA and rewrites the prompt for a reference video.
            if (RejectBerniniForGeneratedAudio(action))
                return;

            // ---------- /applystyle restyle pass ----------
            // If the user installed a session restyle directive, rewrite THIS render's
            // prompt through a small LLM job before anything spawns. Done here at the top
            // of the single generate funnel so it covers image AND movie, fresh AND
            // chained, with one interception. The rewrite is async: TryApplyStyleDirective
            // parks the pump (sets _lastActionDeferred) and re-runs this same action with
            // the restyled prompt on completion; _styleAppliedActions stops the re-run from
            // restyling again. Returns false (and we fall through to render unstyled) when
            // there's no small LLM available or no coroutine runner to dispatch on.
            if (!string.IsNullOrEmpty(_styleDirective)
                && !_styleAppliedActions.Contains(action)
                && !IsRifeVideoSkill(action.SkillId)
                && TryApplyStyleDirective(action))
            {
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
                                DescribeUserAttachmentBubbles() +
                                $"Re-emit with a valid chat_image=\"N\", ask the user to paste an image, or use generate_image instead.");
                            _host?.RequestContinueTurn();
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
                        // Out-of-range usually means the model copied the paste's BUBBLE
                        // number from its [Attached Image chat_image="K"] header into
                        // attachment=. If idx matches the bubble slot of one of THIS
                        // turn's pastes, use that paste instead of failing.
                        for (int a = 1; a <= turnAttachCount && attachmentBytes == null; a++)
                        {
                            if ((_host?.ResolvePasteAttachmentToChatIndex(a) ?? 0) != idx)
                                continue;
                            attachmentBytes = _host?.GetTurnAttachmentBytes(a);
                            if (attachmentBytes != null)
                            {
                                AIChatLog.Note("source_fix",
                                    $"{action.SkillId}: attachment=\"{idx}\" was the bubble number - used this turn's attachment {a}");
                                _host?.AddInfoBubble(
                                    $"(attachment=\"{idx}\" is the paste's bubble number - used this turn's attachment {a}, which is chat_image=\"{idx}\", for {action.SkillId})");
                            }
                        }
                    }
                    if (attachmentBytes == null)
                    {
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}' wanted attachment={idx} but the user only attached {turnAttachCount} image(s) this turn " +
                            $"(attachment indexes are per-message, 1..{turnAttachCount}). " +
                            DescribeTurnPasteBubbles(turnAttachCount) +
                            "Re-emit the action with the correct source.");
                        _host?.RequestContinueTurn();
                        return;
                    }
                }
                else if (chatImageCount > 0)
                {
                    // A stale attachment="N" that normalization could NOT resolve means we
                    // have no idea which image the model meant - the old "substitute the
                    // latest bubble" guess animated the wrong media in practice (often the
                    // just-spawned Movie). Name the usable numbers and let the model
                    // correct itself in this same reply.
                    if (action.AttachmentIndex.HasValue)
                    {
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}': attachment=\"{action.AttachmentIndex.Value}\" can't be resolved - attachment indexes are per-message and the user attached nothing THIS turn. " +
                            DescribeUserAttachmentBubbles() +
                            $"Re-emit with chat_image=\"N\" (1..{chatImageCount}) instead; attachment= will not work on your continue turn either.");
                        _host?.RequestContinueTurn();
                        return;
                    }

                    // For still-input skills, "the latest image" must skip Movie bubbles:
                    // the newest slot is often the clip the model JUST queued, and its
                    // poster/placeholder frame is never the intended img2img/img2vid source.
                    string skillLower = action.SkillId?.ToLowerInvariant() ?? "";
                    bool wantsStillSource = (skillLower == BuiltInSkillIds.ImageToImage || skillLower == BuiltInSkillIds.ImageToMovie)
                        && !ParseBool(action.GetArg("movie_frame"), false);
                    int implicitIdx = wantsStillSource
                        ? (_host?.GetLatestStillChatImageIndex() ?? 0)
                        : (_host?.GetLatestChatImageIndex() ?? 0);
                    if (implicitIdx <= 0)
                    {
                        if (wantsStillSource && (_host?.GetLatestChatImageIndex() ?? 0) > 0)
                        {
                            // Live media exists, but it's all Movies.
                            _host?.AddSystemInjectionAndBubble(
                                $"Skill '{action.SkillId}' has no still-image source: the only live chat media are Movies. " +
                                "For scene, motion, dialogue, or audio changes re-emit video_to_video with the Movie's chat_image=\"N\". " +
                                $"Only for an explicit single-frame request, re-emit {action.SkillId} with movie_frame=\"true\" and the Movie's chat_image.");
                        }
                        else
                        {
                            _host?.AddSystemInjectionAndBubble(
                                $"Skill '{action.SkillId}' needs the user to paste an image into the chat first " +
                                "(or you can reference an earlier chat image once one exists, via chat_image=\"N\"). " +
                                "There are no live chat images right now.");
                        }
                        _host?.RequestContinueTurn();
                        return;
                    }

                    bool hasChainTarget = _host?.GetLastSpawnedPicForTurn() != null;
                    if (hasChainTarget)
                    {
                        // Same-reply pair: the LLM emitted (e.g.) generate_image then a
                        // bare image_to_movie without chain="true". We can't safely
                        // auto-pick a chat_image because the just-spawned Pic isn't a
                        // numbered bubble yet - point the LLM at the real slot number
                        // (chain state is reset before its continue turn, so "add
                        // chain=\"true\"" would be dead advice there).
                        int spawnedIdx = _host?.GetChatImageIndexForPic(_host?.GetLastSpawnedPicForTurn()) ?? 0;
                        string spawnedRef = spawnedIdx > 0
                            ? $"The image you just generated is chat_image=\"{spawnedIdx}\" - re-emit with that. "
                            : "Re-emit with chain=\"true\" to stack onto the image you just generated (do not also pass chat_image / attachment). ";
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}' has no input image. " +
                            spawnedRef +
                            $"Otherwise reference an existing chat bubble via chat_image=\"N\" (1..{chatImageCount}).");
                        _host?.RequestContinueTurn();
                        return;
                    }

                    // Standalone reply (e.g. follow-up "turn it into a video") - the LLM
                    // forgot chat_image="N" but there's only one reasonable target: the
                    // most recent (still, for still-input skills) chat image. Fall back to
                    // it instead of erroring; this is the single most common LLM omission
                    // with smaller models, and failing strictly here breaks the user's
                    // flow for no real benefit.
                    action.Args["chat_image"] = implicitIdx.ToString();
                    if (!TryResolveChatImageBytesOrDefer(action, action.SkillId, "implicit chat_image", implicitIdx, out attachmentBytes, out bool deferred))
                    {
                        if (deferred) return;
                        // Race: pic was destroyed between count and fetch. Fall through
                        // to the same explicit-error message the chat_image="N" path uses.
                        _host?.AddSystemInjectionAndBubble(
                            $"Skill '{action.SkillId}': implicit chat_image=\"{implicitIdx}\" is no longer available (the world Pic may have been deleted). " +
                            $"Use a smaller chat_image=\"N\" index, or ask the user to paste a new image.");
                        _host?.RequestContinueTurn();
                        return;
                    }
                    _host?.AddInfoBubble($"(auto-picked chat_image=\"{implicitIdx}\" - the latest {(wantsStillSource ? "still image" : "image")} - as the source for {action.SkillId})");
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
                    _host?.RequestContinueTurn();
                    return;
                }
            }

            // A Movie bubble in chat_image2 on an H3 reference-video preset is the SECOND
            // REFERENCE CLIP (-> @upload|video2|input2|), not a still: resolving it as bytes
            // would only grab the bubble's poster/placeholder texture. Detect it up front and
            // skip slot 2's byte resolution; a still in chat_image2 (and attachment2 always)
            // stays a photo reference.
            string secondClipPath = null;
            bool isH3RefVideoPreset = action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo
                && !string.IsNullOrEmpty(preset)
                && preset.IndexOf("Reference Video To Video", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isH3RefVideoPreset)
            {
                int chat2N = action.GetExtraChatImageIndex(2) ?? -1;
                if (chat2N > 0)
                    secondClipPath = _host?.GetChatImageMovieFilePath(chat2N);
            }

            // Resolve optional extra input images (slots 2..PicMain.MaxExtraInputImageSlot).
            // Used by N-input presets (Image To Image Klein Edit 2/3/4/5 Input) and the H3
            // reference presets (up to 9 photo refs). Each entry is null when the LLM
            // didn't ask for that slot; an unavailable request emits a bubble and we bail.
            byte[][] extraBytes = new byte[PicMain.MaxExtraInputImageSlot + 1][];
            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
            {
                if (slot == 2 && secondClipPath != null)
                    continue; // slot 2 is the second reference CLIP here, not a still
                extraBytes[slot] = ResolveExtraInputBytes(action, slot, out bool slotErrored, out bool slotDeferred);
                if (slotErrored || slotDeferred) return;
            }

            if (action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo)
            {
                // Reference-VIDEO generation (MiniMax H3 Ref2VA): the model explicitly picked
                // the preset that carries the source clip's subject/motion into a brand-NEW
                // clip. That's a different operation from Bernini restyle/edit, so it must
                // survive the auto-select below (which exists only to spare the model from
                // matching the two Bernini restyle presets by hand).
                bool wantsRefVideoGenerate = !string.IsNullOrEmpty(preset)
                    && preset.IndexOf("Reference Video To Video", StringComparison.OrdinalIgnoreCase) >= 0;
                if (wantsRefVideoGenerate)
                {
                    // Photo-reference rescue, H3 flavor (mirrors the Bernini rescue below):
                    // fresh stills pasted this turn alongside a reference-video preset are
                    // almost certainly meant as photo references (identity/setting), even if
                    // the model forgot the attachment2/attachment3 attributes. Adopt them
                    // into the free photo slots; the universal workflow prunes unused ones.
                    if (extraBytes[2] == null && secondClipPath == null
                        && (_host?.GetTurnAttachmentCount() ?? 0) > 0)
                    {
                        byte[] refBytes = _host?.GetTurnAttachmentBytes(1);
                        if (refBytes != null)
                        {
                            extraBytes[2] = refBytes;
                            _host?.AddInfoBubble("(using your attached image as a photo reference for the new video)");
                            if (extraBytes[3] == null && (_host?.GetTurnAttachmentCount() ?? 0) > 1)
                            {
                                byte[] refBytes2 = _host?.GetTurnAttachmentBytes(2);
                                if (refBytes2 != null)
                                    extraBytes[3] = refBytes2;
                            }
                        }
                    }
                }
                else
                {
                    // Reference-still rescue. For v2v the primary source is ALWAYS the movie
                    // (chat_image / chain), so any fresh STILL the user pasted this turn is almost
                    // certainly the intended reference (face/look to inject). Models routinely
                    // mis-slot it - e.g. attachment="2" or "image 2" instead of attachment2/
                    // chat_image2 - which would otherwise be silently dropped and fall back to a
                    // plain restyle. If slot 2 isn't already wired, adopt the first turn attachment
                    // as the reference so "swap in this face" works without precise slot syntax.
                    if (extraBytes[2] == null && (_host?.GetTurnAttachmentCount() ?? 0) > 0)
                    {
                        byte[] refBytes = _host?.GetTurnAttachmentBytes(1);
                        if (refBytes != null)
                        {
                            extraBytes[2] = refBytes;
                            _host?.AddInfoBubble("(using your attached image as the face/style reference for the video edit)");
                        }
                    }

                    // Pick the workflow by whether a reference still ended up wired into slot 2:
                    // with one -> the reference-guided "_ref" preset (inject a face/character/style
                    // onto the clip); without -> plain restyle. Auto-selecting here means the model
                    // never has to match presets by hand, and a stray ref-preset pick without a
                    // reference can't dead-end on a "need image2" abort. ResolvePresetName still
                    preset = (extraBytes[2] != null)
                        ? "Video To Video Ref (Bernini).txt"
                        : "Video To Video (Bernini).txt";
                }
            }

            // Auto-downgrade preset name when fewer inputs were wired than the preset
            // expects. The LLM frequently picks a "5 Input" preset but only supplies 4
            // anchors (or picks "3 Input" with only one anchor) - without this rescue,
            // PicMain's @upload|imageN|inputN| step aborts at runtime with "Need imageN
            // image first!" and the failure NEVER reaches the LLM, so it can't learn.
            // Rewriting the preset to match the actual input count avoids the dead-end.
            int wiredInputCount = (useAttachment && attachmentBytes != null ? 1 : 0);
            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                if (extraBytes[slot] != null) wiredInputCount++;
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
                _host?.RequestContinueTurn();
                return;
            }

            // Resolve the preset filename robustly (case-insensitive, with/without .txt).
            string resolved = ResolvePresetName(preset, _recentlyResolvedPresets, out bool presetFuzzy);
            if (resolved == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}': preset '{preset}' was not found in Presets/. " +
                    "Re-pick from the list shown in your skill description.");
                _host?.RequestContinueTurn();
                return;
            }
            if (presetFuzzy)
                _host?.AddInfoBubble(
                    $"(preset '{preset}' wasn't found - used the closest match '{resolved}' instead. Use that exact name next time.)");
            RecordResolvedPreset(resolved);

            // H3 reference presets: block before queuing when the prompt doesn't
            // address every staged reference by its tag (see the helper's doc).
            // Checked against the RESOLVED name so fuzzy preset matches gate too.
            bool gateRefVideo = action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo
                && resolved.IndexOf("Reference Video To Video", StringComparison.OrdinalIgnoreCase) >= 0;
            if (gateRefVideo || IsReferencePhotoPreset(resolved))
            {
                var clipDescs = new List<string>();
                var photoDescs = new List<string>();
                if (gateRefVideo)
                {
                    clipDescs.Add($"the source clip ({DescribeStagedRefSource(action, 1)})");
                    if (secondClipPath != null)
                        clipDescs.Add($"the second clip ({DescribeStagedRefSource(action, 2)})");
                }
                else if (useAttachment && attachmentBytes != null)
                {
                    photoDescs.Add(DescribeStagedRefSource(action, 1));
                }
                for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                    if (extraBytes[slot] != null) photoDescs.Add(DescribeStagedRefSource(action, slot));
                if (BlockH3ReferencePromptTagMismatch(action, resolved, clipDescs, photoDescs,
                        "Re-emit the SAME action with the SAME attributes"))
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

            // Optional extra inputs (slots 2..10) - feed the workflow's "image2".."image10"
            // upload slots. The Pic takes ownership of each texture and uploads it (no
            // display, no mask).
            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                if (!TryWireExtraInput(picMain, extraBytes[slot], slot, action.SkillId)) return;

            // Tell the model up front when it staged more reference slots than this
            // preset can consume - otherwise the extras vanish silently and the model
            // only finds out from the rendered result (see the chat_image4 incident).
            WarnUnconsumedExtraInputSlots(action, resolved, extraBytes, secondClipPath != null);

            // video_to_video needs the actual SOURCE VIDEO file - the Bernini v2v preset
            // uploads it via @upload|video|input1|. We hand the source clip's path to the Pic
            // as a pending upload path (NOT by playing it as a movie): the Pic stays an image
            // (the source frame set above) so that when the rendered result lands it transitions
            // image -> video exactly like image_to_movie, and the chat bubble updates correctly.
            // The chat source resolved above is only a still FRAME; the path below is the real clip.
            // (Chained v2v runs on the prior movie Pic via ExecuteChainedGenerate and never reaches
            // here - it already has its movie and uploads via m_picMovie.)
            // Real pixel dimensions of the source CLIP (not the UI snapshot). Filled in
            // below for video-source skills so the aspect match uses the video itself.
            int videoSrcW = 0, videoSrcH = 0;
            if (IsVideoSourceWorkflowSkill(action.SkillId))
            {
                int srcChatN = action.ChatImageIndex ?? (_host?.GetLatestChatImageIndex() ?? -1);
                string moviePath = _host?.GetChatImageMovieFilePath(srcChatN);
                if (string.IsNullOrEmpty(moviePath))
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{action.SkillId}' needs a SOURCE VIDEO, but chat_image=\"{srcChatN}\" is not a \"Movie #N\" bubble. " +
                        "Point chat_image at an existing movie clip, or to operate on a movie you make in THIS same reply, emit the " +
                        "generate/animate/clip action first and add chain=\"true\" to this action.");
                    return;
                }
                picMain.m_pendingVideoUploadPath = moviePath;
                TryGetVideoAspectSource(moviePath, out videoSrcW, out videoSrcH);
                if (action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo)
                {
                    // Explicit duration wins over the source-duration match (which is
                    // already neutralized by H3 preset design; skipping avoids the
                    // stale-replace warnings its appended overrides would log).
                    if (!(IsH3Preset(resolved) && ParseH3DurationFrames(action) > 0))
                    {
                        int frameCount = EstimateVideoToVideoFrameCount(moviePath);
                        if (frameCount > 0)
                            picMain.SetWorkflowFrameCountOverride(frameCount);
                    }
                    if (isH3RefVideoPreset)
                    {
                        if (secondClipPath != null)
                        {
                            picMain.m_pendingVideoUploadPath2 = secondClipPath;
                            _host?.AddInfoBubble("(chat_image2 is a movie - wiring it as reference clip 2 / <Video 2>)");
                        }
                        AppendSilentClipPruneDirectives(picMain, moviePath, secondClipPath);
                    }
                }
            }

            if (IsRifeVideoSkill(action.SkillId))
                ConfigureRifeVideoVariables(picMain, action);

            // Explicit duration on any H3 generation action (t2v/i2v/r2v/rv2v).
            ApplyH3DurationOverride(picMain, action, resolved);

            // Aspect-aware dimension override for img2X presets. Explicit width/height
            // attributes from the LLM win; otherwise fall back to "match the source's
            // aspect at the preset's pixel budget" so a 1024x1024 source no longer
            // gets center-cropped into LTX's default 960x544. Standalone generate_*
            // (no source image) and presets that don't follow the standard %width%/
            // %height% @replace pattern are unaffected - PicMain's helper no-ops.
            if (action.Width.HasValue && action.Height.HasValue
                && action.Width.Value > 0 && action.Height.Value > 0)
            {
                // Image source feeding a pinned start frame: refit mismatched-aspect
                // requests as a pixel budget (see ApplyBudgetDimensionOverride).
                // Reference presets (photo or clip refs) and video sources pin no
                // frame, so the explicit dims pass through exactly.
                bool pinsStartFrame = useAttachment && srcW > 0 && srcH > 0
                    && videoSrcW <= 0
                    && !isH3RefVideoPreset
                    && !IsReferencePhotoPreset(resolved);
                if (pinsStartFrame)
                    ApplyBudgetDimensionOverride(picMain, action.Width.Value, action.Height.Value, srcW, srcH);
                else
                    picMain.SetWorkflowDimensionOverride(action.Width.Value, action.Height.Value);
            }
            else if (videoSrcW > 0 && videoSrcH > 0)
            {
                // Video source: the clip's REAL dimensions win over the still snapshot.
                // srcW/srcH above come from whatever the Movie bubble's Pic was displaying,
                // which is a UI artifact - a movie Pic that has never been played (or was
                // unloaded to save memory) still carries PicMain.Awake's 512x512 black
                // placeholder sprite. That square placeholder used to drive the aspect
                // match, so a 16:9 clip queued a SQUARE render (864x480's budget rotated
                // to 1:1 = 640x640). ffprobe (cached per path+size+mtime) is the truth.
                picMain.SetWorkflowAspectSource(videoSrcW, videoSrcH);
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

            // Second-clip detection for H3 reference presets, mirroring ExecuteGenerate:
            // a Movie bubble in chat_image2 is reference clip 2, not a still to decode.
            string chainSecondClipPath = null;
            bool chainIsH3RefVideo = action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo
                && !string.IsNullOrEmpty(preset)
                && preset.IndexOf("Reference Video To Video", StringComparison.OrdinalIgnoreCase) >= 0;
            if (chainIsH3RefVideo)
            {
                int chainChat2N = action.GetExtraChatImageIndex(2) ?? -1;
                if (chainChat2N > 0)
                    chainSecondClipPath = _host?.GetChatImageMovieFilePath(chainChat2N);
            }

            // Optional extra inputs (slots 2..10) - chain inherits image1 from the prior
            // step, but the LLM can still bring separate image2..image10 references in via
            // attachment{N} / chat_image{N} for N-input presets.
            byte[][] chainExtraBytes = new byte[PicMain.MaxExtraInputImageSlot + 1][];
            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
            {
                if (slot == 2 && chainSecondClipPath != null)
                    continue; // slot 2 is the second reference CLIP here, not a still
                chainExtraBytes[slot] = ResolveExtraInputBytes(action, slot, out bool slotErrored, out bool slotDeferred);
                if (slotErrored || slotDeferred) return;
            }

            // Auto-downgrade preset to match wired input count (see ExecuteGenerate for
            // the rationale). Chain always provides image1 from the prior step's output,
            // so wired = 1 + (non-null extras). Done BEFORE ResolvePresetName so the
            // resolver sees the corrected filename.
            int chainWiredInputCount = 1;
            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                if (chainExtraBytes[slot] != null) chainWiredInputCount++;
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

            // Same H3 reference-tag gate as the non-chained path. Runs BEFORE prevPic
            // is touched, so a block leaves the chained-from still rendering as its own
            // bubble. The chain target was already consumed and continue turns reset
            // chain state, so the correction must re-point at the Pic's bubble number.
            bool chainGateRefVideo = action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo
                && resolved.IndexOf("Reference Video To Video", StringComparison.OrdinalIgnoreCase) >= 0;
            if (chainGateRefVideo || IsReferencePhotoPreset(resolved))
            {
                var clipDescs = new List<string>();
                var photoDescs = new List<string>();
                if (chainGateRefVideo)
                {
                    clipDescs.Add("the source clip (the movie chained from this reply)");
                    if (chainSecondClipPath != null)
                        clipDescs.Add($"the second clip ({DescribeStagedRefSource(action, 2)})");
                }
                else
                {
                    photoDescs.Add("the image chained from this reply");
                }
                for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                    if (chainExtraBytes[slot] != null) photoDescs.Add(DescribeStagedRefSource(action, slot));
                int chainBubbleIdx = _host?.GetChatImageIndexForPic(prevPic) ?? 0;
                string reEmit = chainBubbleIdx > 0
                    ? $"Re-emit the SAME action with chain=\"true\" replaced by chat_image=\"{chainBubbleIdx}\" (chain does not survive a continue turn), other attributes unchanged"
                    : "Re-emit the SAME action with chain=\"true\" replaced by the chained-from bubble's chat_image=\"N\" (chain does not survive a continue turn), other attributes unchanged";
                if (BlockH3ReferencePromptTagMismatch(action, resolved, clipDescs, photoDescs, reEmit))
                    return;
            }

            for (int slot = 2; slot <= PicMain.MaxExtraInputImageSlot; slot++)
                if (!TryWireExtraInput(prevPic, chainExtraBytes[slot], slot, action.SkillId)) return;

            // Same unconsumed-slot check as the non-chained path.
            WarnUnconsumedExtraInputSlots(action, resolved, chainExtraBytes, chainSecondClipPath != null);

            // Same per-Pic negative-prompt extraction as the non-chained path so the
            // chained workflow inherits the preset author's negative prompt instead of
            // whatever GameLogic's global state happens to hold.
            string negFromPreset = ReadPresetDefaultNegativePrompt(resolved);

            // Aspect-aware dimension override: explicit width/height from the LLM win
            // (refit to the chain source's aspect when it feeds a pinned start frame);
            // otherwise inherit the prior step's actual texture dimensions if any are
            // already on the Pic, else fall back to the prior step's last queued
            // workflow dimensions (best-effort) - this keeps a Z-Image -> LTX chain
            // running at the Z-Image source's aspect even though the texture isn't
            // rendered yet at queue time.
            int chainSrcW = 0, chainSrcH = 0;
            bool chainSourceIsMovie = prevPic.m_picMovie != null && prevPic.IsMovie();
            // Movie source: probe the clip itself first. prevPic's live texture is the
            // VideoPlayer RenderTexture only while the movie is loaded; an unloaded (or
            // never-played) movie Pic falls back to PicMain's square 512x512 placeholder
            // sprite, which would rotate the preset's pixel budget into a square render.
            if (chainSourceIsMovie
                && TryGetVideoAspectSource(prevPic.m_picMovie.GetProcessingFileName(), out int movieW, out int movieH))
            {
                chainSrcW = movieW;
                chainSrcH = movieH;
            }
            // Queued dims BEFORE the live texture: the still-source placeholder trap.
            // A chained generate's target queued its workflow earlier this same turn,
            // and until that render lands the Pic is still displaying Awake's square
            // 512x512 placeholder sprite - TryGetCurrentTexture returns that as a real
            // texture, so texture-first turned an 864x480 request into a 640x640 render
            // via the start-frame budget refit. The queued dimensions are the truth for
            // any Pic that ran a workflow; the live texture is only consulted for chain
            // targets that never queued one (local composition ops like new_canvas /
            // paste_image, whose real pixels exist immediately).
            else if (prevPic.LastQueuedWorkflowWidth > 0 && prevPic.LastQueuedWorkflowHeight > 0)
            {
                chainSrcW = prevPic.LastQueuedWorkflowWidth;
                chainSrcH = prevPic.LastQueuedWorkflowHeight;
            }
            else if (prevPic.TryGetCurrentTexture(out var prevTex) && prevTex != null)
            {
                chainSrcW = prevTex.width;
                chainSrcH = prevTex.height;
            }

            if (action.Width.HasValue && action.Height.HasValue
                && action.Width.Value > 0 && action.Height.Value > 0)
            {
                // Same start-frame aspect guard as the non-chained path: a chained
                // still feeding an i2v start frame must not be squished by a
                // mismatched explicit canvas. (When the skill-documented pattern of
                // identical width/height on both actions is followed, the aspects
                // match and this passes the request through exactly.)
                bool chainPinsStartFrame = !chainSourceIsMovie
                    && !chainIsH3RefVideo
                    && !IsReferencePhotoPreset(resolved)
                    && chainSrcW > 0 && chainSrcH > 0;
                if (chainPinsStartFrame)
                    ApplyBudgetDimensionOverride(prevPic, action.Width.Value, action.Height.Value, chainSrcW, chainSrcH);
                else
                    prevPic.SetWorkflowDimensionOverride(action.Width.Value, action.Height.Value);
            }
            else if (chainSrcW > 0 && chainSrcH > 0)
            {
                prevPic.SetWorkflowAspectSource(chainSrcW, chainSrcH);
            }

            if (action.SkillId.ToLowerInvariant() == BuiltInSkillIds.VideoToVideo
                && prevPic.m_picMovie != null
                && prevPic.IsMovie())
            {
                string moviePath = prevPic.m_picMovie.GetProcessingFileName();
                // Same explicit-duration gate as the non-chained path.
                if (!(IsH3Preset(resolved) && ParseH3DurationFrames(action) > 0))
                {
                    int frameCount = EstimateVideoToVideoFrameCount(moviePath);
                    if (frameCount > 0)
                        prevPic.SetWorkflowFrameCountOverride(frameCount);
                }
                if (chainIsH3RefVideo)
                {
                    // Unconditional assignment: prevPic is a reused Pic, so a null here
                    // also CLEARS any second clip left over from an earlier action.
                    prevPic.m_pendingVideoUploadPath2 = chainSecondClipPath;
                    if (chainSecondClipPath != null)
                        _host?.AddInfoBubble("(chat_image2 is a movie - wiring it as reference clip 2 / <Video 2>)");
                    AppendSilentClipPruneDirectives(prevPic, moviePath, chainSecondClipPath);
                }
            }

            if (IsRifeVideoSkill(action.SkillId))
                ConfigureRifeVideoVariables(prevPic, action);

            // Explicit duration on any H3 generation action (t2v/i2v/r2v/rv2v).
            ApplyH3DurationOverride(prevPic, action, resolved);

            // Same workflow-error reporter as the non-chained path: surface PicMain
            // runtime aborts back to the LLM as a system injection.
            WireWorkflowErrorReporter(prevPic, action.SkillId, resolved);

            try
            {
                prevPic.AppendPresetJobs(resolved, action.Prompt, negFromPreset);
                _host?.RecordChatImageProvenance(prevPic, action);
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
        /// Rewrite a STALE <c>attachment="N"</c> (emitted on a turn with no live
        /// attachments - the paste happened on an EARLIER turn, or this is a synthetic
        /// continue turn, which always clears the per-turn attachment list) to the
        /// chat_image slot the model actually meant. Two deterministic rules, in order:
        ///   1. attachment "N" of the MOST RECENT paste group -> that paste's current bubble.
        ///   2. N is itself the bubble number of a user-pasted image -> that bubble
        ///      (the model copied the number from the [Attached Image chat_image="N"] header).
        /// Anything else is left in place for the failure paths, which auto-continue
        /// with the usable numbers. Never runs while this turn HAS attachments, so the
        /// normal same-turn attachment flow is untouched.
        /// </summary>
        private void NormalizeStaleAttachmentRefs(SkillAction action, bool useAttachment)
        {
            if (action == null || _host == null) return;
            if (_host.GetTurnAttachmentCount() > 0) return;

            if (useAttachment && action.AttachmentIndex.HasValue && !action.ChatImageIndex.HasValue)
            {
                int n = action.AttachmentIndex.Value;
                int resolved = _host.ResolvePasteAttachmentToChatIndex(n);
                if (resolved <= 0 && _host.IsChatImageUserAttachment(n))
                    resolved = n;
                if (resolved > 0)
                {
                    action.Args["chat_image"] = resolved.ToString();
                    action.Args.Remove("attachment");
                    AIChatLog.Note("source_fix",
                        $"{action.SkillId}: stale attachment=\"{n}\" resolved to chat_image=\"{resolved}\"");
                    _host.AddInfoBubble(
                        $"(attachment=\"{n}\" was pasted on an earlier turn - resolved it to that paste's bubble, chat_image=\"{resolved}\", for {action.SkillId})");
                }
            }

            // Extra slots (attachment2..N on Klein multi-input / H3 reference presets).
            for (int slot = 2; slot <= SkillAction.MaxExtraInputSlot; slot++)
            {
                int? attachN = action.GetExtraAttachmentIndex(slot);
                if (!attachN.HasValue || action.GetExtraChatImageIndex(slot).HasValue) continue;
                int resolved = _host.ResolvePasteAttachmentToChatIndex(attachN.Value);
                if (resolved <= 0 && _host.IsChatImageUserAttachment(attachN.Value))
                    resolved = attachN.Value;
                if (resolved > 0)
                {
                    action.Args["chat_image" + slot] = resolved.ToString();
                    action.Args.Remove("attachment" + slot);
                    _host.AddInfoBubble(
                        $"(attachment{slot}=\"{attachN.Value}\" was pasted on an earlier turn - resolved to chat_image{slot}=\"{resolved}\")");
                }
            }
        }

        /// <summary>
        /// "Those pastes are chat_image=..." fragment for correction notes about THIS
        /// turn's paste group. Continue turns clear the per-turn attachment list, so a
        /// note must name chat_image numbers - "use attachment=1" would fail again.
        /// Empty string when nothing resolves.
        /// </summary>
        private string DescribeTurnPasteBubbles(int turnAttachCount)
        {
            var nums = new List<string>();
            for (int a = 1; a <= turnAttachCount; a++)
            {
                int k = _host?.ResolvePasteAttachmentToChatIndex(a) ?? 0;
                if (k > 0) nums.Add($"\"{k}\"");
            }
            return nums.Count > 0
                ? $"Those pastes are chat_image={string.Join(", ", nums)} - reference them that way. "
                : "";
        }

        /// <summary>
        /// "The user's pasted images are chat_image=..." fragment listing the newest
        /// live user-attachment bubbles, for correction notes when a source ref could
        /// not be resolved at all. Empty string when none exist.
        /// </summary>
        private string DescribeUserAttachmentBubbles(int maxToList = 4)
        {
            int count = _host?.GetChatImageCount() ?? 0;
            var nums = new List<string>();
            for (int i = count; i >= 1 && nums.Count < maxToList; i--)
                if (_host?.IsChatImageUserAttachment(i) == true)
                    nums.Insert(0, $"\"{i}\"");
            return nums.Count > 0
                ? $"The user's pasted images are chat_image={string.Join(", ", nums)} (newest last). "
                : "";
        }

        private bool RejectImplicitMovieFrameAction(SkillAction action, bool usesChainTarget)
        {
            if (action == null)
                return false;

            string skillId = action.SkillId?.ToLowerInvariant() ?? "";
            if (skillId != BuiltInSkillIds.ImageToImage && skillId != BuiltInSkillIds.ImageToMovie)
                return false;
            if (ParseBool(action.GetArg("movie_frame"), false))
                return false;

            int chatN = action.ChatImageIndex ?? -1;
            PicMain chainTarget = usesChainTarget ? _host?.GetLastSpawnedPicForTurn() : null;
            bool chainTargetIsSource = chainTarget != null;
            // A real same-reply chain target wins over a redundant chat_image. The
            // chained executor deliberately drops that stray primary attribute later.
            // Movie-ness comes from the spawn-time record flag OR live state
            // (IsChatImageMovie/IsChatPicMovie), NOT GetChatImageMovieFilePath: a Movie
            // whose clip is STILL RENDERING has no file yet, and probing the file let
            // such actions slip past this gate into a multi-minute defer.
            bool sourceIsMovie = chainTargetIsSource
                ? (_host?.IsChatPicMovie(chainTarget) ?? chainTarget.IsMovie())
                : chatN > 0 && (_host?.IsChatImageMovie(chatN) ?? false);
            if (!sourceIsMovie)
                return false;

            string sourceLabel = chainTargetIsSource ? "the chained Movie" : $"Movie #{chatN}";
            string h3Preset = SkillManager.ApplyPresetPrefix("{{Reference Video To Video (MiniMax H3) 5s.txt}}");
            _host?.AddSystemInjectionAndBubble(
                $"Blocked '{action.SkillId}' because its source is {sourceLabel}, not a still image. " +
                "For any scene, motion, dialogue, voice, audio, or sound change, re-emit video_to_video against the Movie itself. " +
                $"When the request creates new dialogue or sound, use preset '{h3Preset}', keep the Movie in chat_image, and refer to the source as Video 1 in the prompt. " +
                $"Only if the user explicitly requested a single still/current frame may you re-emit '{action.SkillId}' with movie_frame=\"true\".");
            _host?.RequestContinueTurn();
            return true;
        }

        private bool RejectBerniniForGeneratedAudio(SkillAction action)
        {
            if (action == null || !string.Equals(action.SkillId, BuiltInSkillIds.VideoToVideo, StringComparison.OrdinalIgnoreCase))
                return false;

            string preset = action.Preset ?? "";
            bool berniniOrDefault = string.IsNullOrWhiteSpace(preset)
                || preset.IndexOf("Bernini", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!berniniOrDefault || !PromptRequestsGeneratedAudio(action.Prompt))
                return false;

            string h3Preset = SkillManager.ApplyPresetPrefix("{{Reference Video To Video (MiniMax H3) 5s.txt}}");
            _host?.AddSystemInjectionAndBubble(
                $"Blocked the Bernini video action because its prompt asks for new dialogue/audio/sound, but Bernini's video workflow is silent. " +
                $"Re-emit video_to_video with preset '{h3Preset}', the same Movie chat_image, and a self-contained H3 prompt that refers to the source as Video 1 and states the exact spoken line and sound effects.");
            _host?.RequestContinueTurn();
            return true;
        }

        private static bool PromptRequestsGeneratedAudio(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            // Speech, vocal actions, and concrete sound effects unambiguously need an
            // audio-generating model even if the same prompt also says "no music".
            if (Regex.IsMatch(
                prompt,
                @"\b(?:says?|speaks?|shouts?|whispers?|yells?|sings?|dialogue|dialog|spoken\s+line|voice[- ]?over|new\s+voice|sound\s+effects?|farts?|burps?)\b",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (!Regex.IsMatch(prompt, @"\b(?:audio|soundtrack|music)\b", RegexOptions.IgnoreCase))
                return false;

            // A request to remove/mute sound is compatible with Bernini's silent output;
            // preserving, replacing, or creating audio is not.
            return !Regex.IsMatch(
                prompt,
                @"\b(?:silent|muted?|no\s+(?:audio|sound|music)|without\s+(?:audio|sound|music)|(?:remove|strip|drop|mute)\s+(?:the\s+)?(?:audio|soundtrack|music))\b",
                RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Real display dimensions of a video file, for aspect matching. Uses the shared
        /// ffprobe helper (results cached per path+size+mtime, so repeated actions on the
        /// same clip never re-spawn ffprobe) and swaps width/height for 90/270-degree
        /// rotated sources so a portrait phone clip matches as portrait. Returns false when
        /// the path is missing or ffprobe can't read a video stream; callers then fall back
        /// to whatever the Pic is displaying.
        /// </summary>
        private static bool TryGetVideoAspectSource(string moviePath, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(moviePath) || !System.IO.File.Exists(moviePath))
                return false;

            if (!FfmpegTool.TryProbeVideoSync(moviePath, out var info, out string error) || info == null)
            {
                Debug.LogWarning("SkillActionExecutor: could not probe video source dimensions: " + error);
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
                return false;

            int rot = ((info.RotationDegrees % 360) + 360) % 360;
            if (rot == 90 || rot == 270)
            {
                width = info.Height;
                height = info.Width;
            }
            else
            {
                width = info.Width;
                height = info.Height;
            }
            return true;
        }

        private static int EstimateVideoToVideoFrameCount(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !System.IO.File.Exists(moviePath))
                return 0;

            if (!FfmpegTool.TryProbeVideoSync(moviePath, out var info, out string error) || info == null)
            {
                Debug.LogWarning("SkillActionExecutor: could not probe v2v source frame count: " + error);
                return 0;
            }

            if (info.DurationSeconds <= 0 || double.IsNaN(info.DurationSeconds) || double.IsInfinity(info.DurationSeconds))
                return 0;

            // Bernini v2v currently forces the source loader to 16 fps and Wan-style
            // temporal models expect lengths on a 4n+1 cadence. 5s therefore becomes
            // 81 frames, and an 8s source becomes 129 instead of being capped to 81.
            int frames = Mathf.Max(1, Mathf.CeilToInt((float)(info.DurationSeconds * VideoToVideoWorkflowInputFps - 0.001)));
            int remainder = (frames - 1) % VideoToVideoFrameStride;
            if (remainder != 0)
                frames += VideoToVideoFrameStride - remainder;
            return frames;
        }

        // ---- Explicit H3 duration (duration="10" seconds on a generation action) ----
        // H3's length is a 24fps frame count on a 17k+5 grid, trained range ~5-15s
        // (124..362). The workflow's shipped default is 124; the 5s presets keep that
        // literal intact (their own length replace is a 124->124 no-op or absent), so an
        // appended @replace can retarget it. A 15s preset's 124->362 replace would stale
        // the directive into a silent no-op, so duration is refused there with a hint.
        private const int H3DefaultLengthFrames = 124;

        private static bool IsH3Preset(string preset)
        {
            return !string.IsNullOrEmpty(preset)
                && preset.IndexOf("(MiniMax H3)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ParseH3DurationFrames(SkillAction action)
        {
            float seconds = ParseFloat(
                action.GetArg("duration")
                ?? action.GetArg("duration_seconds")
                ?? action.GetArg("seconds"),
                0f);
            if (seconds <= 0f) return 0;
            int frames = Mathf.CeilToInt(seconds * 24f);
            int k = Mathf.CeilToInt(Mathf.Max(0, frames - 5) / 17f);
            frames = 17 * k + 5;
            return Mathf.Clamp(frames, 124, 362);
        }

        private void ApplyH3DurationOverride(PicMain picMain, SkillAction action, string preset)
        {
            int frames = IsH3Preset(preset) ? ParseH3DurationFrames(action) : 0;
            if (frames <= 0 || picMain == null) return;
            if (preset.IndexOf("15s", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _host?.AddInfoBubble(
                    "(duration=\"N\" is ignored on the 15s preset - put it on the 5s preset instead for non-default lengths)");
                return;
            }
            picMain.AddWorkflowDirective($"@replace|\"length\": {H3DefaultLengthFrames}|\"length\": {frames}|");
            _host?.AddInfoBubble($"(H3 duration: {frames} frames = ~{frames / 24f:0.#}s on the model's 17k+5 grid)");
        }

        // H3 reference workflows hard-fail in the VHS loader when a source clip has no
        // audio stream. Probe each wired clip (cached ffprobe); for silent ones append a
        // @prune_input directive so the submit-time pruner drops just that clip's audio
        // wire and H3 synthesizes the soundtrack from the prompt instead.
        private void AppendSilentClipPruneDirectives(PicMain picMain, string clip1Path, string clip2Path)
        {
            if (ClipLacksAudio(clip1Path))
            {
                picMain.AddWorkflowDirective("@prune_input|ref_video_audios.ref_video_audio_0|");
                _host?.AddInfoBubble("(source clip has no audio track - the soundtrack will be synthesized from the prompt)");
            }
            if (!string.IsNullOrEmpty(clip2Path) && ClipLacksAudio(clip2Path))
            {
                picMain.AddWorkflowDirective("@prune_input|ref_video_audios.ref_video_audio_1|");
                _host?.AddInfoBubble("(second clip has no audio track - its audio reference is skipped)");
            }
        }

        private static bool ClipLacksAudio(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !System.IO.File.Exists(moviePath))
                return false;
            if (!FfmpegTool.TryProbeVideoSync(moviePath, out var info, out _) || info == null)
                return false;
            return !info.HasAudio;
        }

        private static bool IsRifeVideoSkill(string skillId)
        {
            return string.Equals(skillId, BuiltInSkillIds.RifeVideo, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoSourceWorkflowSkill(string skillId)
        {
            return string.Equals(skillId, BuiltInSkillIds.VideoToVideo, StringComparison.OrdinalIgnoreCase)
                || string.Equals(skillId, BuiltInSkillIds.RifeVideo, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SkillRequiresPrompt(string skillId)
        {
            return !IsRifeVideoSkill(skillId);
        }

        private static void ConfigureRifeVideoVariables(PicMain picMain, SkillAction action)
        {
            if (picMain == null || action == null)
                return;

            var vm = picMain.GetVariableManager();
            if (vm == null)
                return;

            float explicitFps = ParseFloat(
                action.GetArg("fps")
                ?? action.GetArg("frame_rate")
                ?? action.GetArg("framerate"),
                0f);
            if (explicitFps > 0f)
            {
                float clamped = Mathf.Clamp(explicitFps, 1f, 240f);
                vm.SetText("rife_output_fps", clamped.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                vm.Clear("rife_output_fps");
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
        /// Resolve an optional extra input image (slot 2..PicMain.MaxExtraInputImageSlot)
        /// to PNG bytes. Reads attachment{slot} / chat_image{slot} from the action args.
        /// Returns null when the LLM didn't ask for an image at that slot; returns null
        /// and sets <paramref name="errored"/>=true (after emitting a system-injection
        /// bubble) when it asked for one that isn't available; callers should bail in
        /// that case. chat_image{slot} wins over attachment{slot} if both are set,
        /// matching the precedence rule on the primary slot.
        /// </summary>
        private byte[] ResolveExtraInputBytes(SkillAction action, int slot, out bool errored, out bool deferred)
        {
            errored = false;
            deferred = false;
            if (slot < 2 || slot > PicMain.MaxExtraInputImageSlot) return null;

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
                    _host?.RequestContinueTurn();
                    errored = true;
                    return null;
                }
                // Clip slots never reach this resolver (chat_image is the primary clip and
                // chat_image2 is skipped as the second clip on Reference Video To Video), so a
                // Movie landing here is being coerced into a STILL: its current display
                // frame becomes the photo reference. The Seinfeld test staged a third voice
                // clip in chat_image3 this way. Say so, so the model can fix the slots.
                if (_host?.IsChatImageMovie(chatN) ?? false)
                {
                    _host?.AddSystemInjectionAndBubble(
                        $"NOTE: {chatKey}=\"{chatN}\" is a Movie, but this action takes a clip only in chat_image " +
                        "(plus chat_image2 on Reference Video To Video - at most 2 clips per render). " +
                        $"Its current display frame was used as the PHOTO reference for slot {slot} instead, so count it as a <Picture N>. " +
                        "For a third speaker use their photo anchor only, or drop that slot.");
                }
                return bytes;
            }

            // attachN > 0
            int turnAttachCount = _host?.GetTurnAttachmentCount() ?? 0;
            byte[] aBytes = _host?.GetTurnAttachmentBytes(attachN);
            if (aBytes == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{action.SkillId}' wanted {attachKey}=\"{attachN}\" but the user only attached {turnAttachCount} image(s) this turn " +
                    "(attachment indexes are per-message). " +
                    DescribeTurnPasteBubbles(turnAttachCount) +
                    $"Use a valid index, or drop {attachKey} if an input at slot {slot} isn't needed.");
                _host?.RequestContinueTurn();
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
            if (slot < 2 || slot > PicMain.MaxExtraInputImageSlot)
            {
                UnityEngine.Object.Destroy(tex);
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}': internal error - unsupported input slot {slot}.");
                return false;
            }
            pic.SetExtraInputImage(slot, tex);
            return true;
        }

        /// <summary>
        /// Emits a system-injection bubble when the action wired more extra image slots
        /// than the resolved preset's @upload lines actually consume, so the model learns
        /// IMMEDIATELY instead of discovering missing references in the rendered result.
        /// A staged slot K is consumed when the preset uploads "imageK" (or, for K=2 with
        /// a second reference clip wired, "video2"). The render still proceeds with the
        /// supported subset - the extras are simply never uploaded.
        /// </summary>
        private void WarnUnconsumedExtraInputSlots(SkillAction action, string resolvedPresetName,
            byte[][] extraBytes, bool secondClipWired)
        {
            if (extraBytes == null || string.IsNullOrEmpty(resolvedPresetName)) return;

            string presetText;
            try
            {
                string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                string presetPath = System.IO.Path.Combine(projectRoot, "Presets", resolvedPresetName);
                if (!System.IO.File.Exists(presetPath)) return;
                presetText = System.IO.File.ReadAllText(presetPath);
            }
            catch (Exception)
            {
                return; // best-effort check only - never block a render over it
            }

            var unconsumed = new List<int>();
            var consumedSlots = new HashSet<int>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                presetText, @"@upload\|image(\d+)\|", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[1].Value, out int n)) consumedSlots.Add(n);
            }
            bool presetTakesVideo2 = presetText.IndexOf("@upload|video2|", StringComparison.OrdinalIgnoreCase) >= 0;

            for (int slot = 2; slot < extraBytes.Length; slot++)
            {
                if (extraBytes[slot] == null) continue;
                if (consumedSlots.Contains(slot)) continue;
                unconsumed.Add(slot);
            }
            if (secondClipWired && !presetTakesVideo2)
                unconsumed.Insert(0, 2);
            if (unconsumed.Count == 0) return;

            int maxImageSlot = 0;
            foreach (int s in consumedSlots) maxImageSlot = Math.Max(maxImageSlot, s);
            string slotList = string.Join(", ", unconsumed.ConvertAll(s => $"chat_image{s}/attachment{s}"));
            _host?.AddSystemInjectionAndBubble(
                $"Skill '{action.SkillId}': preset '{resolvedPresetName}' only consumes image slots up to " +
                $"image{Math.Max(1, maxImageSlot)}, so {slotList} was IGNORED for this render. " +
                "Re-run with fewer references, or use a preset that supports more input slots.");
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

            string prefix = PlayerPrefs.GetString(SkillManager.PresetPrefixPrefsKey, "");
            string found = null;
            if (!string.IsNullOrEmpty(prefix))
            {
                if (!requestedFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // The prompt builder rewrites {{Preset Name.txt}} markers to
                    // <prefix>Preset Name.txt before the LLM sees them, but some host-side
                    // actions intentionally choose a base preset in code (notably
                    // video_to_video's plain/ref auto-switch). When a prefix is active,
                    // prefer that parallel preset family before falling back to the bare
                    // production filename.
                    string withoutLeadingUnderscore = requestedFile.TrimStart('_');
                    found = FindPresetFile(presetsDir, prefix + withoutLeadingUnderscore);
                    if (found != null)
                        return found;
                }
                else
                {
                    found = FindPresetFile(presetsDir, requestedFile);
                    if (found != null)
                        return found;

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

            found = FindPresetFile(presetsDir, requestedFile);
            if (found != null)
                return found;

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

            string looseRequested = LoosePresetKey(requestedFile);
            if (looseRequested.Length == 0)
                return null;

            string looseMatch = null;
            foreach (var path in System.IO.Directory.GetFiles(presetsDir, "*.txt"))
            {
                string name = System.IO.Path.GetFileName(path);
                if (!string.Equals(LoosePresetKey(name), looseRequested, StringComparison.Ordinal))
                    continue;

                // Only silently canonicalize punctuation/case variants when the
                // normalized filename is unique. Ambiguous names still fall through
                // to the conservative fuzzy resolver, which may ask the model/user
                // to be more precise instead of guessing.
                if (looseMatch != null)
                    return null;

                looseMatch = name;
            }

            return looseMatch;
        }

        private static string LoosePresetKey(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "";

            string s = fileName.Trim();
            if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);

            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Last-resort fuzzy resolver: return the on-disk preset whose normalized name is
        /// the CLOSEST match to <paramref name="requestedFile"/>, but ONLY when that match
        /// is both close and clearly unambiguous. Catches the common LLM slip of dropping
        /// or garbling a word in a long preset name ("Image To Image Edit" ->
        /// "Image To Image Klein Edit 1 Input"). Returns the on-disk filename, or null when no
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

            // Inject the body INTO THE LLM's next prompt but DON'T splash the entire
            // markdown body into the chat as a bubble. That pattern was loud, and
            // mid-stream skill output cannot reach the model that requested it. Ask
            // the host for one synthetic continue turn so the very next assistant
            // turn can use the reference material without the user clicking Send.
            _host?.AddSystemInjectionSilent(
                "Reference material for skill '" + skill.Id + "' (the full body of " +
                "aichat/skills/" + skill.Id + ".md). Use this knowledge directly on " +
                "the next assistant turn - do NOT call read_skill again for this id, " +
                "and do NOT ask the user 'should I proceed' - just act on what they " +
                "already requested using the patterns documented below.\n\n" + body);
            _host?.AddInfoBubble(
                "(loaded skill '" + skill.Id + "' - continuing automatically so the LLM can use it.)");
            _host?.RequestAutoResumeAfterSkillLoad(skill.Id);
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
        /// Ask a vision-capable LLM to inspect/caption an existing image. This is the
        /// explicit counterpart to the optional auto-caption sidecar: generated images
        /// normally use provenance only, but the assistant can call this when the user
        /// asks it to check the actual pixels.
        /// </summary>
        private void ExecuteInspectImage(SkillAction action)
        {
            byte[] png = null;
            string sourceLabel = "";

            if (action.Chain)
            {
                if (!TryReadChainedInspectImage(out png, out sourceLabel))
                    return;
            }
            else
            {
                byte[] canvasBytes = ResolveCanvasBytes(action, "inspect_image", out bool errored, out bool deferred, allowMissing: false);
                if (errored || deferred) return;
                if (action.Chain)
                {
                    // ResolveCanvasBytes can promote a repeated same-turn canvas
                    // reference to chain="true" as a composition rescue. Honor that
                    // promotion here instead of treating the null canvas bytes as a
                    // failed read.
                    if (!TryReadChainedInspectImage(out png, out sourceLabel))
                        return;
                }
                else
                {
                    png = canvasBytes;
                    sourceLabel = DescribeInspectSource(action);
                }
            }

            if (png == null || png.Length == 0)
            {
                _host?.AddSystemInjectionAndBubble("inspect_image could not read image bytes.");
                return;
            }

            string prompt = action.Prompt;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt =
                    "QA inspect this image. Start with PASS or FAIL. Answer only from visible pixels. " +
                    "Mark FAIL for blank panels, black rectangles, missing text, unreadable text, duplicated text, " +
                    "title/text touching or overlapping unrelated artwork, bad gutters, clipped content, " +
                    "or content that does not match the requested subject. Name affected regions.";
            }

            DispatchInspectImageRequest(action, png, prompt.Trim(), sourceLabel);
        }

        private bool TryReadChainedInspectImage(out byte[] png, out string sourceLabel)
        {
            png = null;
            sourceLabel = "the chained image";

            PicMain pic = _host?.PeekChainTarget();
            if (pic == null)
            {
                _host?.AddSystemInjectionAndBubble(
                    "inspect_image was called with chain=\"true\" but no Pic was spawned earlier in this turn. " +
                    "Use chat_image=\"N\" for an existing image, or inspect after creating a same-reply image.");
                return false;
            }
            if (pic.IsBusy())
            {
                _host?.AddSystemInjectionAndBubble(
                    "inspect_image cannot read the chained image yet because it is still rendering. " +
                    "Use chat_image=\"N\" on the next turn after the image is done.");
                return false;
            }
            if (!pic.TryGetImageAsPng(out png))
            {
                _host?.AddSystemInjectionAndBubble("inspect_image could not read PNG bytes from the chained image.");
                return false;
            }
            return true;
        }

        private string DescribeInspectSource(SkillAction action)
        {
            if (action == null) return "the image";
            if (action.ChatImageIndex.HasValue) return $"chat_image=\"{action.ChatImageIndex.Value}\"";
            if (action.AttachmentIndex.HasValue) return $"attachment=\"{action.AttachmentIndex.Value}\"";
            int latest = _host?.GetLatestChatImageIndex() ?? 0;
            return latest > 0 ? $"latest chat_image=\"{latest}\"" : "the image";
        }

        private void DispatchInspectImageRequest(SkillAction action, byte[] png, string prompt, string sourceLabel)
        {
            _host?.EnqueueInspectImage(png, prompt, sourceLabel, action?.LlmInstanceId, action != null && action.Resume);
        }

        // ---------- /applystyle restyle ----------

        /// <summary>
        /// Kick off a small LLM job that rewrites <paramref name="action"/>'s prompt per
        /// the active <c>/applystyle</c> directive, then re-runs the action with the
        /// restyled prompt. Returns true when the job was dispatched and the pump has been
        /// parked (the caller must <c>return</c>); false when no restyle could be started
        /// (no small LLM / no runner / no instances) and the caller should render unstyled.
        /// Marks the action in <see cref="_styleAppliedActions"/> in every path so the
        /// deferred re-run never restyles a second time.
        /// </summary>
        private bool TryApplyStyleDirective(SkillAction action)
        {
            var runner = _host?.CoroutineRunner;
            if (runner == null) return false; // can't dispatch async - render the original prompt

            var instanceMgr = LLMInstanceManager.Get();
            if (instanceMgr == null || instanceMgr.GetInstanceCount() == 0)
            {
                _styleAppliedActions.Add(action);
                _host?.AddLocalInfoBubble("(/applystyle is set but no LLM instances are configured - rendering the original prompt.)");
                return false;
            }

            int replicaIndex = 0;
            int targetId = instanceMgr.GetFreeLLM(isSmallJob: true, isVisionJob: false, out replicaIndex);
            if (targetId < 0)
                targetId = instanceMgr.GetLeastBusyLLM(isSmallJob: true, isVisionJob: false, out replicaIndex);
            if (targetId < 0)
            {
                _styleAppliedActions.Add(action);
                _host?.AddLocalInfoBubble("(/applystyle is set but no small-job LLM is available - rendering the original prompt.)");
                return false;
            }

            var inst = instanceMgr.GetInstance(targetId);
            if (inst == null || inst.settings == null)
            {
                _styleAppliedActions.Add(action);
                _host?.AddLocalInfoBubble("(/applystyle: picked LLM instance has no settings - rendering the original prompt.)");
                return false;
            }

            // Only dispatch to providers DispatchOneShot actually handles asynchronously.
            // Its default branch invokes onDone SYNCHRONOUSLY with an error string, which
            // would re-enter the pump mid-drain (double-dequeue) and bake the error text
            // into the render prompt. All six current providers are supported, so this
            // guard only trips if a new provider is added without DispatchOneShot support.
            if (!IsDispatchOneShotSupported(inst.providerType))
            {
                _styleAppliedActions.Add(action);
                _host?.AddLocalInfoBubble($"(/applystyle: provider {inst.providerType} can't run the restyle job - rendering the original prompt.)");
                return false;
            }

            // Mark BEFORE dispatch so the re-run after the rewrite skips this path even if
            // the callback runs synchronously on some provider.
            _styleAppliedActions.Add(action);
            instanceMgr.SetLLMBusy(targetId, replicaIndex, true);

            string originalPrompt = action.Prompt ?? "";
            string directive = _styleDirective;

            var lines = new Queue<GTPChatLine>();
            lines.Enqueue(new GTPChatLine("system",
                "You rewrite image/video generation prompts. Apply the user's STYLE DIRECTIVE to the PROMPT and output ONLY the rewritten prompt - no preamble, no quotes, no commentary, no markdown. Keep the original subject and important details intact; change only what the directive asks for."));
            lines.Enqueue(new GTPChatLine("user",
                "STYLE DIRECTIVE: " + directive + "\n\nPROMPT:\n" + originalPrompt));

            int capturedTargetId = targetId;
            int capturedReplicaIndex = replicaIndex;
            int capturedEpoch = _turnEpoch;
            const string callerLabel = "ApplyStyle";

            // Signal the pump that this action parked itself on the dispatch below.
            _lastActionDeferred = true;

            Action<RTDB, JSONObject, string> onDone = (db, json, text) =>
            {
                instanceMgr.SetLLMBusy(capturedTargetId, capturedReplicaIndex, false);

                // A new turn began while the rewrite was in flight: ResetForNewTurn
                // already cleared the pump/queue, so re-running would spawn a stale
                // render into the new turn. Drop it.
                if (_turnEpoch != capturedEpoch)
                    return;

                string restyled = (text ?? "").Trim();
                if (string.IsNullOrEmpty(restyled) && json != null)
                {
                    try { restyled = (OpenAITextCompletionManager.ExtractTextFromResponseJSON(json) ?? "").Trim(); }
                    catch { /* leave empty -> fall back to the original prompt below */ }
                }

                if (!string.IsNullOrEmpty(restyled))
                {
                    if (string.IsNullOrEmpty(action.PromptForLogsOverride))
                        action.PromptForLogsOverride = originalPrompt;
                    action.Args["prompt"] = restyled;
                    string preview = restyled.Length > 160 ? restyled.Substring(0, 157) + "..." : restyled;
                    _host?.AddLocalInfoBubble("(/applystyle restyled the render prompt -> \"" + preview + "\")");
                }
                else
                {
                    _host?.AddLocalInfoBubble("(/applystyle: the small LLM returned nothing - rendering the original prompt.)");
                }

                // Re-run the action end to end now that its prompt is finalized. If THIS
                // run parks itself again (e.g. a chat_image reload defer), that coroutine
                // owns resuming the pump - so only resume here when it did not.
                _lastActionDeferred = false;
                try
                {
                    Execute(action);
                }
                finally
                {
                    if (!_lastActionDeferred)
                        ResumePumpAfterDeferredComplete(action);
                }
            };

            _host?.AddLocalInfoBubble($"(/applystyle: restyling the render prompt via small LLM #{capturedTargetId}...)");
            DispatchOneShot(runner, inst, lines, onDone, callerLabel);
            return true;
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
        /// <summary>
        /// True for the providers <see cref="DispatchOneShot"/> serves via a real async
        /// web request (so its onDone fires later, off the call stack). The unsupported
        /// providers only reach DispatchOneShot's default branch, which calls onDone
        /// synchronously - unsafe to use from inside the pump's drain loop.
        /// </summary>
        public static bool IsDispatchOneShotSupported(LLMProvider provider)
        {
            switch (provider)
            {
                case LLMProvider.Ollama:
                case LLMProvider.LlamaCpp:
                case LLMProvider.OpenAICompatible:
                case LLMProvider.OpenAI:
                case LLMProvider.Anthropic:
                case LLMProvider.Gemini:
                    return true;
                default:
                    return false;
            }
        }

        // maxNewTokens: optional output-token budget for specialized callers. The
        // default is uncapped: optional limit fields are omitted so the model/server
        // owns the ceiling. Anthropic requires a value and gets its model maximum.
        // onStreamChunk: optional delta-text callback; when provided the request is
        // sent streaming and chunks arrive as they generate (used by the compact
        // summary's live preview). The completion callback still fires at the end.
        public static void DispatchOneShot(
            MonoBehaviour runner,
            LLMInstanceInfo inst,
            Queue<GTPChatLine> lines,
            Action<RTDB, JSONObject, string> onDone,
            string callerLabel,
            string sentJsonFilename = "text_completion_sent.json",
            int maxNewTokens = LLMRequestProfile.NoExplicitOutputTokenCap,
            Action<string> onStreamChunk = null)
        {
            var settings = inst.settings;
            var db = new RTDB();
            string apiKey = settings.apiKey ?? "";
            bool stream = onStreamChunk != null;

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
                    string json = mgr.BuildForInstructJSON(lines, out suggestedEndpoint, maxNewTokens, 0.4f, "chat-instruct", stream, null, true, false);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, serverAddress, suggestedEndpoint, onStreamChunk, stream, apiKey, sentJsonFilename, debugJobSize: LLMDebugLog.JobSize.Small);
                    break;
                }
                case LLMProvider.LlamaCpp:
                {
                    var mgr = runner.gameObject.AddComponent<TexGenWebUITextCompletionManager>();
                    string serverAddress = settings.endpoint ?? "";
                    string suggestedEndpoint;
                    var llmParms = BuildLLMParmsForInstance(inst);
                    string json = mgr.BuildForInstructJSON(lines, out suggestedEndpoint, maxNewTokens, 0.4f, "chat-instruct", stream, llmParms, false, true);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, serverAddress, suggestedEndpoint, onStreamChunk, stream, apiKey, sentJsonFilename, debugJobSize: LLMDebugLog.JobSize.Small);
                    break;
                }
                case LLMProvider.OpenAICompatible:
                {
                    var mgr = runner.gameObject.AddComponent<OpenAITextCompletionManager>();
                    string serverAddress = settings.endpoint ?? "";
                    string endpoint = serverAddress.TrimEnd('/') + "/v1/chat/completions";
                    string model = settings.selectedModel ?? "";
                    bool isDeepSeek = LLMRequestProfile.IsDeepSeekModel(model);
                    var compatReasoning = LLMRequestProfile.ResolveCompatReasoning(model, settings);
                    float temp = isDeepSeek ? LLMRequestProfile.GetRecommendedTemperature(model, compatReasoning.effort, 0.4f) : 0.4f;
                    float? topP = isDeepSeek ? (float?)LLMRequestProfile.GetRecommendedTopP(model, compatReasoning.effort, 1.0f) : null;
                    string json = mgr.BuildChatCompleteJSON(lines, maxNewTokens, temp, model, stream,
                        enableThinking: compatReasoning.enableThinking,
                        topP: topP,
                        customReasoningEffort: compatReasoning.customReasoningEffortParam);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, endpoint, onStreamChunk, stream, sentJsonFilename, debugJobSize: LLMDebugLog.JobSize.Small);
                    break;
                }
                case LLMProvider.OpenAI:
                {
                    var mgr = runner.gameObject.AddComponent<OpenAITextCompletionManager>();
                    string model = string.IsNullOrEmpty(settings.selectedModel) ? "gpt-4o-mini" : settings.selectedModel;
                    var profile = OpenAIRequestProfileResolver.Resolve(model, settings, 0);
                    string json = mgr.BuildChatCompleteJSON(lines, maxNewTokens, 0.4f, model, stream,
                        profile.useResponsesAPI, profile.isReasoningModel, profile.includeTemperature,
                        profile.reasoningEffort, profile.enableThinking);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, profile.endpoint, onStreamChunk, stream, sentJsonFilename, debugJobSize: LLMDebugLog.JobSize.Small);
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
                    // Anthropic requires max_tokens; "no cap" resolves to the model max.
                    int anthropicMaxTokens = maxNewTokens > 0 ? maxNewTokens : LLMRequestProfile.GetAnthropicMaxOutputTokens(model);
                    string json = mgr.BuildChatCompleteJSON(lines, anthropicMaxTokens, 0.4f, model, stream);
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
                    }, db, anthropicKey, endpoint, onStreamChunk, stream, sentJsonFilename, debugJobSize: LLMDebugLog.JobSize.Small);
                    break;
                }
                case LLMProvider.Gemini:
                {
                    var mgr = runner.gameObject.AddComponent<GeminiTextCompletionManager>();
                    // No silent fallback model: an unset model surfaces an error, not a default.
                    string model = settings.selectedModel ?? "";
                    string baseEndpoint = string.IsNullOrEmpty(settings.endpoint)
                        ? "https://generativelanguage.googleapis.com/v1beta/models"
                        : settings.endpoint;
                    // Non-streaming one-shot: GeminiTextCompletionManager hands the
                    // already-extracted response text back as `str`. Images carried
                    // by `lines` (via GTPChatLine.AddImage) are serialized as
                    // inlineData parts, so this path covers the vision-caption
                    // sidecar as well as plain text summarization.
                    string endpoint = GeminiTextCompletionManager.BuildEndpointUrl(baseEndpoint, model, stream);
                    string json = mgr.BuildChatCompleteJSON(lines, maxNewTokens, 0.4f, model, stream, settings.enableThinking);
                    mgr.SpawnChatCompleteRequest(json, (rtdb, jn, str) =>
                    {
                        try { onDone(rtdb, jn, str ?? ""); }
                        finally { UnityEngine.Object.Destroy(mgr); }
                    }, db, apiKey, endpoint, onStreamChunk, stream, debugJobSize: LLMDebugLog.JobSize.Small);
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

        // chat_image/source_chat_image slot attributes that may carry an anchor NAME
        // instead of a number. Resolved to live slot numbers in NormalizeAnchorRefs so
        // the rest of the executor (which int-parses these) needs no changes.
        private static readonly string[] AnchorRefArgKeys = BuildAnchorRefArgKeys();

        private static string[] BuildAnchorRefArgKeys()
        {
            var keys = new List<string> { "chat_image" };
            for (int slot = 2; slot <= SkillAction.MaxExtraInputSlot; slot++)
                keys.Add("chat_image" + slot);
            keys.Add("source_chat_image");
            return keys.ToArray();
        }

        /// <summary>
        /// Rewrite any chat_image* / source_chat_image attribute whose value is an anchor NAME
        /// (e.g. <c>chat_image="Bob"</c>) into its current numeric slot using the host's
        /// anchor registry. Numeric values are left untouched. A name that resolves to no
        /// live slot aborts the action with a help bubble so the downstream integer
        /// parse doesn't silently treat it as "missing" and then fail with a less
        /// useful "missing source/input" error. Safe to call more than once on the
        /// same action (numbers pass straight through), which the deferred
        /// re-execution path relies on.
        /// </summary>
        private bool NormalizeAnchorRefs(SkillAction action)
        {
            if (action == null || _host == null) return true;

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
                    _host.AddSystemInjectionAndBubble(
                        $"{key}=\"{val}\" did not match any known character anchor. Known anchors are " +
                        "listed in the ANCHORS line of CURRENT STATE - reference one of those by name, " +
                        "or use a numeric chat_image=\"N\".");
                    return false;
                }
            }

            return true;
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

                // inspect / caption / examine -> inspect_image
                case "inspect":
                case "inspectimage":
                case "check_image":
                case "checkimage":
                case "examine_image":
                case "examineimage":
                case "caption_image":
                case "captionimage":
                    return BuiltInSkillIds.InspectImage;

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

                // music / song / compose -> generate_music
                case "music":
                case "song":
                case "generate_song":
                case "make_song":
                case "make_music":
                case "compose":
                case "compose_music":
                case "generatemusic":
                case "soundtrack":
                case "jingle":
                    return BuiltInSkillIds.GenerateMusic;

                // sfx / sound_effect / foley -> generate_sfx
                case "sfx":
                case "sound":
                case "sound_effect":
                case "soundeffect":
                case "sound_fx":
                case "generate_sound":
                case "generate_sound_effect":
                case "generatesfx":
                case "make_sound":
                case "foley":
                    return BuiltInSkillIds.GenerateSfx;

                // speech / tts / voice / narrate -> generate_speech
                case "speech":
                case "tts":
                case "text_to_speech":
                case "texttospeech":
                case "speak":
                case "say":
                case "voice":
                case "voiceover":
                case "voice_over":
                case "narrate":
                case "narration":
                case "generate_voice":
                case "generate_tts":
                case "generatespeech":
                    return BuiltInSkillIds.GenerateSpeech;

                // add_audio / replace_audio / mux -> set_video_audio
                case "add_audio":
                case "add_music":
                case "add_song":
                case "add_sound":
                case "add_soundtrack":
                case "replace_audio":
                case "replace_music":
                case "replace_sound":
                case "replace_soundtrack":
                case "swap_audio":
                case "mux_audio":
                case "mux":
                case "video_audio":
                case "set_audio":
                case "attach_audio":
                case "mix_audio":
                case "audio_to_video":
                case "add_audio_to_video":
                case "setvideoaudio":
                case "dub":
                case "score_video":
                    return BuiltInSkillIds.SetVideoAudio;

                // video_to_video / v2v / restyle a clip -> video_to_video
                case "v2v":
                case "vid2vid":
                case "videotovideo":
                case "video2video":
                    return BuiltInSkillIds.VideoToVideo;

                // rife / interpolate video -> rife_video
                case "rife":
                case "rifevideo":
                case "rife_video":
                case "interpolate":
                case "interpolate_video":
                case "interpolatevideo":
                case "frame_interpolate":
                case "frameinterpolate":
                case "smooth_video":
                case "smoothvideo":
                    return BuiltInSkillIds.RifeVideo;

                // clip / trim / cut video -> clip_video
                case "clip":
                case "clipvideo":
                case "trim_video":
                case "trimvideo":
                case "cut_video":
                case "cutvideo":
                case "cut_clip":
                case "make_clip":
                    return BuiltInSkillIds.ClipVideo;

                // extract / grab a frame from a movie -> extract_still
                case "extract_frame":
                case "extractframe":
                case "extractstill":
                case "grab_frame":
                case "grabframe":
                case "still_frame":
                case "stillframe":
                case "frame_extract":
                    return BuiltInSkillIds.ExtractStill;

                // stitch / concat / join / merge clips -> stitch_video
                case "stitch":
                case "stitchvideo":
                case "stitch_videos":
                case "stitch_clips":
                case "stitch_movies":
                case "concat":
                case "concat_video":
                case "concat_videos":
                case "concatenate":
                case "concatenate_videos":
                case "join_videos":
                case "join_clips":
                case "join_movies":
                case "combine_videos":
                case "combine_clips":
                case "combine_movies":
                case "merge_videos":
                case "merge_clips":
                case "merge_movies":
                case "sequence_videos":
                case "sequence_clips":
                    return BuiltInSkillIds.StitchVideo;

                // web search / image / video fetch aliases
                case "search_web":
                case "websearch":
                case "image_search":
                case "video_search":
                case "brave_search":
                case "brave":
                case "search_images":
                case "search_videos":
                    return BuiltInSkillIds.WebSearch;

                case "webimage":
                case "find_image":
                case "fetch_image":
                case "download_image":
                case "get_image":
                case "fetch_photo":
                case "download_photo":
                case "find_photo":
                case "get_photo":
                case "web_photo":
                case "image_from_web":
                    return BuiltInSkillIds.WebImage;

                case "webvideo":
                case "download_video":
                case "fetch_video":
                case "get_video":
                case "find_video":
                case "youtube":
                case "web_clip":
                case "download_clip":
                case "fetch_clip":
                case "video_from_web":
                    return BuiltInSkillIds.WebVideo;

                // web_page: read one page's text + image list. "web_fetch" moved here from
                // web_image: fetching a URL is a page read now that the page action exists.
                case "webpage":
                case "read_page":
                case "fetch_page":
                case "open_page":
                case "open_url":
                case "read_url":
                case "fetch_url":
                case "get_page":
                case "page_text":
                case "read_website":
                case "read_webpage":
                case "web_read":
                case "browse":
                case "visit":
                case "web_fetch":
                    return BuiltInSkillIds.WebPage;

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
            RectInt textRect = new RectInt(x, y, w, h);
            AuditLikelyTitleAgainstPastedPanels(pic, text, textRect, srcW, srcH);
            RecordCompositionRect(pic, "draw_text", textRect, CompactLayoutAuditText(text, 80));
            // Optional auto-size lower bound. When omitted, TMP uses its built-in
            // default (18). Set higher to guarantee body text stays readable even
            // when long; TMP will OVERFLOW the rect rather than shrink below this.
            int minFontSize = ParsePixelOrPercent(action.GetArg("min_font_size"), srcH) ?? 0;

            Color color = ParseColor(action.GetArg("color"), Color.white);
            Color? bgColor = ParseColorOpt(action.GetArg("bg_color"));
            int bgCornerRadius = ParsePixelOrPercent(action.GetArg("bg_corner_radius") ?? action.GetArg("corner_radius"), srcW) ?? 0;
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
                dst.DrawFilledRect(x, y, w, h, bgColor.Value, bgCornerRadius);
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

            RectInt pasteRect = new RectInt(x, y, w, h);
            string sourceLabel = DescribePasteSource(action);
            RecordCompositionRect(pic, "paste_image", pasteRect, sourceLabel);
            AuditPastedPanelAgainstExistingTitles(pic, pasteRect, sourceLabel, srcW, srcH);
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
            _lastLocalOpInputChatImageIndex = -1;
            _lastLocalOpOutputPic = picMain;
            _compositionRectsByPic[picMain] = new List<CompositionRectRecord>();
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
            Color? outline = ParseColorOpt(action.GetArg("outline_color") ?? action.GetArg("stroke_color"));
            int outlineWidth = ParsePixelOrPercent(action.GetArg("outline_width") ?? action.GetArg("stroke_width"), srcW) ?? 1;
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

        private void RecordCompositionRect(PicMain pic, string kind, RectInt rect, string label)
        {
            if (pic == null || rect.width <= 0 || rect.height <= 0)
                return;

            if (!_compositionRectsByPic.TryGetValue(pic, out var records) || records == null)
            {
                records = new List<CompositionRectRecord>();
                _compositionRectsByPic[pic] = records;
            }

            records.Add(new CompositionRectRecord
            {
                Kind = kind ?? "",
                Rect = rect,
                Label = label ?? ""
            });
        }

        private void AuditLikelyTitleAgainstPastedPanels(PicMain pic, string text, RectInt textRect, int canvasW, int canvasH)
        {
            if (pic == null || canvasW <= 0 || canvasH <= 0 || textRect.width <= 0 || textRect.height <= 0)
                return;
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!IsLikelyTopTitleRect(textRect, canvasW, canvasH))
                return;

            if (!_compositionRectsByPic.TryGetValue(pic, out var records) || records == null || records.Count == 0)
                return;

            int minGutter = Mathf.Max(8, Mathf.RoundToInt(canvasH * 0.02f));
            foreach (var record in records)
            {
                if (record == null || record.Kind != "paste_image")
                    continue;
                RectInt panel = record.Rect;
                bool horizontalOverlap = textRect.x < panel.xMax && textRect.xMax > panel.x;
                if (!horizontalOverlap)
                    continue;

                bool overlapsPanel = textRect.y < panel.yMax && textRect.yMax > panel.y;
                bool tooCloseAbovePanel = textRect.yMax <= panel.y && textRect.yMax + minGutter > panel.y;
                if (!overlapsPanel && !tooCloseAbovePanel)
                    continue;

                string key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pic)}:{textRect.x},{textRect.y},{textRect.width},{textRect.height}";
                if (!_layoutAuditWarnings.Add(key))
                    return;

                string issue = overlapsPanel ? "overlaps" : "is too close to";
                string panelLabel = string.IsNullOrEmpty(record.Label) ? "a pasted panel" : $"a pasted panel ({record.Label})";
                _host?.AddSystemInjectionAndBubble(
                    $"Layout warning: probable title text \"{CompactLayoutAuditText(text.Trim(), 80)}\" at rect ({textRect.x},{textRect.y}) {textRect.width}x{textRect.height} {issue} {panelLabel} rect ({panel.x},{panel.y}) {panel.width}x{panel.height}. " +
                    "For a comic/page title fix, rebuild with a reserved title band and at least 2% vertical gutter, or use clean_base=\"true\" before redrawing the title.");
                return;
            }
        }

        private void AuditPastedPanelAgainstExistingTitles(PicMain pic, RectInt panelRect, string sourceLabel, int canvasW, int canvasH)
        {
            if (pic == null || canvasW <= 0 || canvasH <= 0 || panelRect.width <= 0 || panelRect.height <= 0)
                return;
            if (!_compositionRectsByPic.TryGetValue(pic, out var records) || records == null || records.Count == 0)
                return;

            int minGutter = Mathf.Max(8, Mathf.RoundToInt(canvasH * 0.02f));
            foreach (var record in records)
            {
                if (record == null || record.Kind != "draw_text")
                    continue;
                RectInt title = record.Rect;
                if (!IsLikelyTopTitleRect(title, canvasW, canvasH))
                    continue;

                bool horizontalOverlap = title.x < panelRect.xMax && title.xMax > panelRect.x;
                if (!horizontalOverlap)
                    continue;

                bool overlapsTitle = title.y < panelRect.yMax && title.yMax > panelRect.y;
                bool tooCloseBelowTitle = title.yMax <= panelRect.y && title.yMax + minGutter > panelRect.y;
                if (!overlapsTitle && !tooCloseBelowTitle)
                    continue;

                string key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pic)}:{title.x},{title.y},{title.width},{title.height}:paste:{panelRect.x},{panelRect.y},{panelRect.width},{panelRect.height}";
                if (!_layoutAuditWarnings.Add(key))
                    return;

                string issue = overlapsTitle ? "overlaps" : "is too close below";
                string panelLabel = string.IsNullOrEmpty(sourceLabel) ? "a pasted panel" : $"a pasted panel ({sourceLabel})";
                _host?.AddSystemInjectionAndBubble(
                    $"Layout warning: {panelLabel} rect ({panelRect.x},{panelRect.y}) {panelRect.width}x{panelRect.height} {issue} probable title text \"{record.Label}\" at rect ({title.x},{title.y}) {title.width}x{title.height}. " +
                    "For a comic/page title fix, rebuild with a reserved title band and at least 2% vertical gutter, or use clean_base=\"true\" before redrawing the title.");
                return;
            }
        }

        private static bool IsLikelyTopTitleRect(RectInt rect, int canvasW, int canvasH)
        {
            // A probable page title is near the top and spans most of the canvas.
            // This avoids warning for normal speech bubbles inside upper comic panels.
            return canvasW > 0
                && canvasH > 0
                && rect.width > 0
                && rect.height > 0
                && rect.y <= Mathf.RoundToInt(canvasH * 0.15f)
                && rect.width >= Mathf.RoundToInt(canvasW * 0.75f)
                && rect.height <= Mathf.RoundToInt(canvasH * 0.20f);
        }

        private static string CompactLayoutAuditText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            string compact = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (compact.Length <= maxChars)
                return compact;
            return compact.Substring(0, Mathf.Max(0, maxChars - 3)) + "...";
        }

        private static string DescribePasteSource(SkillAction action)
        {
            if (action == null)
                return "";
            string srcChat = action.GetArg("source_chat_image");
            if (!string.IsNullOrWhiteSpace(srcChat))
                return "source_chat_image=" + srcChat.Trim();
            string srcAttach = action.GetArg("source_attachment");
            if (!string.IsNullOrWhiteSpace(srcAttach))
                return "source_attachment=" + srcAttach.Trim();
            return "";
        }

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

            // The layout/comic recipes historically told the model to keep using
            // chat_image="<canvas>" for every paste/text pass. Local ops actually
            // produce a new working Pic unless chained, so repeated references to the
            // original canvas rebuild from the blank source and leave black/empty
            // panels. Treat a repeated same-turn canvas reference as chain="true" so
            // the operation stacks onto the latest local output.
            bool repeatsPreviousLocalInput = _lastLocalOpInputChatImageIndex > 0
                && chatN == _lastLocalOpInputChatImageIndex
                && _lastLocalOpOutputPic != null
                && currentTurnPic == _lastLocalOpOutputPic;

            if (!referencesLatestLocalOutput && !repeatsPreviousLocalInput)
                return false;

            action.Args.Remove("chat_image");
            action.Args["chain"] = "true";
            string reason = referencesLatestLocalOutput
                ? "it references the Pic just spawned earlier in this reply"
                : "it repeats the canvas from the previous local composition step";
            _host?.AddInfoBubble($"(treated {skillId} chat_image=\"{chatN}\" as chain=\"true\" - {reason})");
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
        /// True when every chat image this action references has readable bytes.
        /// This includes primary chat_image, chat_image2..5 for N-input presets,
        /// and paste_image's source_chat_image. <paramref name="anyBusy"/> reports
        /// whether any referenced Pic is still generating, so the wait can persist
        /// for slow GPUs instead of timing out on a fixed wall clock.
        /// </summary>
        private bool AllReferencedChatImagesReady(SkillAction action, out bool anyBusy)
        {
            anyBusy = false;
            if (action == null) return false;

            bool allReady = true;
            for (int slot = 1; slot <= PicMain.MaxExtraInputImageSlot; slot++)
            {
                int idx = slot == 1
                    ? (action.ChatImageIndex ?? -1)
                    : (action.GetExtraChatImageIndex(slot) ?? -1);
                if (idx > 0)
                    allReady &= IsReferencedChatImageReady(idx, ref anyBusy);
            }

            string srcChat = action.GetArg("source_chat_image");
            if (!string.IsNullOrEmpty(srcChat) && int.TryParse(srcChat, out int sourceIdx) && sourceIdx > 0)
            {
                allReady &= IsReferencedChatImageReady(sourceIdx, ref anyBusy);
            }
            return allReady;
        }

        private bool IsReferencedChatImageReady(int idx, ref bool anyBusy)
        {
            byte[] bytes = _host?.GetChatImagePngBytes(idx);
            bool generating = _host?.IsChatImagePicGenerating(idx) ?? false;
            // "Ready" requires BOTH readable bytes AND a finished render: a still-
            // generating Pic only has its black placeholder texture, so treat it as
            // not-ready (and keep waiting via anyBusy) even though bytes != null.
            if (bytes != null && bytes.Length > 0 && !generating)
                return true;

            if (generating)
                anyBusy = true;
            return false;
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
                    _host?.RequestContinueTurn();
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

            // Still-input skills must not be rescued onto a Movie bubble: this path reads
            // raw bytes with no movie gate, so "latest" being a Movie would silently
            // animate/edit its poster frame. Fall back to the latest STILL instead.
            string skillLower = skillId?.ToLowerInvariant() ?? "";
            bool wantsStillSource = (skillLower == BuiltInSkillIds.ImageToImage || skillLower == BuiltInSkillIds.ImageToMovie)
                && !ParseBool(action?.GetArg("movie_frame"), false);

            int fallbackIndex = -1;
            if (requestedIndex == chatImageCount + 1)
                fallbackIndex = wantsStillSource
                    ? (_host?.GetLatestStillChatImageIndex() ?? 0)
                    : (_host?.GetLatestChatImageIndex() ?? 0);

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
                bool useCleanBase = WantsCleanBase(action);
                if (!useCleanBase && PromoteCanvasReferenceToChainIfNeeded(action, skillId, chatN))
                    return null;

                if (useCleanBase)
                {
                    byte[] cleanBase = _host?.GetChatImageCleanBasePngBytes(chatN);
                    if (cleanBase != null && cleanBase.Length > 0)
                        return cleanBase;

                    _host?.AddSystemInjectionAndBubble(
                        $"Skill '{skillId}' asked for clean_base=\"true\" on chat_image=\"{chatN}\", but no clean pre-overlay base is available for that image. " +
                        "Use the current image without clean_base, pick an earlier source image that has clean_base=available, or regenerate the base art.");
                    errored = true;
                    return null;
                }

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
                if (implicitIdx > 0)
                    action.Args["chat_image"] = implicitIdx.ToString();
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
            Func<PicMain, IEnumerator> opToRun = WrapLocalOpWithCleanBaseCapture(skillId, op);

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
                    prevPic.AppendLocalOp(opToRun);
                    _host?.RecordChatImageProvenance(prevPic, action);
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
            _lastLocalOpInputChatImageIndex = action.ChatImageIndex ?? -1;
            _lastLocalOpOutputPic = picMain;

            // Wrap the coroutine launch so any synchronous exception thrown before
            // the coroutine's first yield (TMP setup, font lookup, etc.) is logged
            // with context instead of dropping the whole turn.
            try
            {
                picMain.RunLocalOpImmediate(opToRun);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SkillActionExecutor: '{skillId}' coroutine launch threw {ex.GetType().Name}: {ex}");
                _host?.AddSystemInjectionAndBubble(
                    $"Skill '{skillId}': failed to start the local op ({ex.GetType().Name}: {ex.Message}). " +
                    "See Unity console for the full stack trace.");
            }
        }

        private Func<PicMain, IEnumerator> WrapLocalOpWithCleanBaseCapture(string skillId, Func<PicMain, IEnumerator> op)
        {
            return pic =>
            {
                if (ShouldCaptureCleanBaseBeforeLocalOp(skillId))
                    _host?.CaptureCleanBaseIfMissing(pic);
                return op(pic);
            };
        }

        private static bool ShouldCaptureCleanBaseBeforeLocalOp(string skillId)
        {
            switch (skillId ?? "")
            {
                case BuiltInSkillIds.AddBorder:
                case BuiltInSkillIds.DrawText:
                case BuiltInSkillIds.DrawShape:
                    return true;
                default:
                    return false;
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

        private static bool WantsCleanBase(SkillAction action)
        {
            if (action == null) return false;
            return ParseBool(action.GetArg("clean_base"), false);
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
