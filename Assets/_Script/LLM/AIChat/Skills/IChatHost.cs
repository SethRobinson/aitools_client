using System.Collections.Generic;
using UnityEngine;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// Slim contract that <see cref="SkillActionExecutor"/> uses to talk back to the
    /// AI Chat panel. Lets the executor stay independent of <c>AIChatPanel</c>'s internals
    /// (and lets us unit-test or stub it in future). The panel implements this and passes
    /// `this` to the executor.
    /// </summary>
    public interface IChatHost
    {
        /// <summary>
        /// MonoBehaviour to run coroutines on (panels themselves are MonoBehaviours).
        /// </summary>
        MonoBehaviour CoroutineRunner { get; }

        /// <summary>
        /// Read-only byte array (PNG) for the Nth attachment the user pasted/dropped on
        /// THIS turn (1-based). Returns null if out of range or the user attached nothing.
        /// </summary>
        byte[] GetTurnAttachmentBytes(int oneBasedIndex);

        /// <summary>
        /// Number of attachments the user added this turn (after panel.OnSendClicked()
        /// captured them but before they were consumed).
        /// </summary>
        int GetTurnAttachmentCount();

        /// <summary>
        /// Append a system / info bubble in the chat (like "Conversation cleared.").
        /// Not added to the LLM history.
        /// </summary>
        void AddInfoBubble(string text);

        /// <summary>
        /// Append a system / info bubble that is shown to the USER ONLY and is NEVER
        /// forwarded to the chat model (not as history, not via the info-recap tail).
        /// Use for local-only notices whose content must not influence the model - e.g.
        /// the "/applystyle" restyle feedback, which echoes the rewritten render prompt
        /// that the original chat AI must not see. (Plain <see cref="AddInfoBubble"/>
        /// rides the info-recap and DOES reach the model on the next turn.)
        /// </summary>
        void AddLocalInfoBubble(string text);

        /// <summary>
        /// Append a system / info bubble AND queue the same text for the LLM, delivered
        /// inside the user's NEXT outgoing message (info-recap section). Deliberately
        /// NOT a system-role interaction: those get folded into the front system
        /// message, and growing the prompt head mid-conversation breaks server-side
        /// prompt caching for the whole history.
        /// </summary>
        void AddSystemInjectionAndBubble(string text);

        /// <summary>
        /// Queue text for the LLM (delivered inside the user's NEXT outgoing message,
        /// same cache-friendly recap path as <see cref="AddSystemInjectionAndBubble"/>)
        /// without spamming the chat with the full content. Use when the injected text
        /// is large (e.g. a full skill markdown body) and the user doesn't need to see
        /// it in their chat - they only care that "something was loaded". Pair with a
        /// small <see cref="AddInfoBubble"/> if you want a visual confirmation.
        /// </summary>
        void AddSystemInjectionSilent(string text);

        /// <summary>
        /// Append a chat-side image bubble with a live mirror of the supplied PicMain
        /// (which has been spawned and queued via the standard ImageGenerator pipeline).
        /// The bubble is positioned in stream order with the rest of the chat content.
        /// </summary>
        void AppendImageBubbleForPic(SkillAction action, PicMain spawnedPic);

        /// <summary>
        /// Read the current PNG bytes of the Nth chat-image bubble (1-based, in spawn
        /// order over the lifetime of THIS chat session). Returns null if N is out of
        /// range OR the underlying world Pic has been destroyed OR the Pic has no
        /// displayable texture yet (queued render). Lets the LLM say
        /// <c>chat_image="3"</c> to reuse an earlier bubble's image as input for a
        /// new img2img / img2vid spawn.
        /// </summary>
        byte[] GetChatImagePngBytes(int oneBasedIndex);

        /// <summary>
        /// Number of numbered chat-image slots tracked by this chat session.
        /// The number maps to the visible "Image #N" / "Movie #N" labels; a slot may
        /// still need to be reloaded before its pixels can be read.
        /// </summary>
        int GetChatImageCount();

        /// <summary>
        /// Highest currently tracked chat_image index whose world Pic still exists,
        /// or 0 if none exist. Used for "latest image" fallbacks without compressing
        /// stable chat_image numbering when older movies are unloaded.
        /// </summary>
        int GetLatestChatImageIndex();

        /// <summary>
        /// Best-effort request to make the Nth chat image readable by
        /// <see cref="GetChatImagePngBytes"/>. Returns true if the slot exists and is
        /// either already readable or a reload/preparation was started. Returns false
        /// for out-of-range, deleted, or non-reloadable slots.
        /// </summary>
        bool TryPrepareChatImageForRead(int oneBasedIndex);

        /// <summary>
        /// Short visual caption (~15 words) for the Nth chat image, or "" if the
        /// caption hasn't been computed yet, the index is out of range, or no
        /// vision LLM is available to caption with. Used by ChatContextBuilder to
        /// list per-image descriptions in the system prompt's CHAT IMAGES block
        /// so the LLM can map "the one with grandma" to chat_image="N".
        /// </summary>
        string GetChatImageCaption(int oneBasedIndex);

        /// <summary>
        /// The most recent PicMain the executor spawned via a non-chained action this
        /// turn, or null if nothing has been spawned yet (or the underlying GameObject
        /// has been destroyed). Used to resolve <c>chain="true"</c> follow-up actions.
        /// Reset on each user submit.
        /// </summary>
        PicMain GetLastSpawnedPicForTurn();

        /// <summary>
        /// Record a freshly-spawned Pic as a chain target for any subsequent
        /// chain="true" actions in this same turn. Called by the executor right after
        /// <see cref="AppendImageBubbleForPic"/>. Chained steps do NOT update this -
        /// a 3-step chain (base -> chain -> chain) all references the same root Pic.
        /// Also pushes the Pic onto the per-turn LIFO stack consumed by
        /// <see cref="ConsumeChainTarget"/> / peeked by <see cref="PeekChainTarget"/>.
        /// </summary>
        void SetLastSpawnedPicForTurn(PicMain spawnedPic);

        /// <summary>
        /// Resolve and CONSUME the next chain target. Pops the MOST-RECENT unmatched Pic
        /// from this turn's LIFO stack, so when the LLM emits N generate-class actions
        /// followed by N chain="true" GENERATES (a "grouped" reply), each chain pairs
        /// with a distinct source (most-recent first) instead of every chain stacking on
        /// the same Pic. Falls back to <see cref="GetLastSpawnedPicForTurn"/> when the
        /// stack is empty (so 3+ step chains on the same root Pic still work). Returns
        /// null if no usable chain target exists this turn. NOTE: only chained GENERATES
        /// consume; chained LOCAL composition ops use <see cref="PeekChainTarget"/>
        /// (non-consuming) so several decorations stack onto one working image.
        /// </summary>
        PicMain ConsumeChainTarget();

        /// <summary>
        /// Resolve the current chain target WITHOUT consuming it: the most-recent
        /// unchained Pic this turn (the LIFO head), or null if none is usable. Chained
        /// LOCAL composition ops (draw_text, add_border, draw_shape, paste_image,
        /// crop_resize) use this so MANY decorations stack onto the SAME working image.
        /// Only chained GENERATES consume via <see cref="ConsumeChainTarget"/>.
        /// </summary>
        PicMain PeekChainTarget();

        /// <summary>
        /// Mark the chain target STALE: called by the executor at the start of every
        /// fresh (unchained) spawn attempt. A successful spawn clears it via
        /// <see cref="SetLastSpawnedPicForTurn"/>; a FAILED spawn (bad preset, missing
        /// source, decode error) leaves it set, so a following chain="true" decorator
        /// (border/text) sees a null target and errors cleanly instead of silently
        /// stacking onto - and corrupting - the PREVIOUS page's Pic. Both
        /// <see cref="PeekChainTarget"/> and <see cref="ConsumeChainTarget"/> honor it.
        /// </summary>
        void MarkChainTargetStale();

        /// <summary>
        /// True if the Nth chat image's underlying Pic still has an active or
        /// queued generation job (GPU render or queued local op). Used by the
        /// deferred-action wait so a slow anchor render doesn't time out
        /// prematurely. False for an out-of-range or destroyed slot.
        /// </summary>
        bool IsChatImagePicGenerating(int oneBasedIndex);

        /// <summary>
        /// Resolve a named character anchor (declared earlier via <c>anchor="Name"</c>)
        /// to its CURRENT 1-based chat-image slot, or 0 if the name is unknown or its
        /// underlying Pic has been trimmed/destroyed. Lets the executor accept
        /// <c>chat_image="Bob"</c> and rewrite it to the live number, which is robust
        /// against the renumbering that happens when the media list is trimmed. Names
        /// are matched case-insensitively.
        /// </summary>
        int ResolveAnchorToIndex(string anchorName);
    }
}
