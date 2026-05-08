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
        /// Append a system / info bubble AND inject the same text as a system-role
        /// interaction in the prompt manager so the LLM sees it on its next turn.
        /// </summary>
        void AddSystemInjectionAndBubble(string text);

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
        /// Number of chat-image bubbles spawned this session that still have a live
        /// world Pic. Lets the executor and skill descriptions tell the LLM how many
        /// chat_image slots are reachable right now.
        /// </summary>
        int GetChatImageCount();

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
        /// Also pushes the Pic onto the per-turn FIFO queue consumed by
        /// <see cref="ConsumeChainTarget"/>.
        /// </summary>
        void SetLastSpawnedPicForTurn(PicMain spawnedPic);

        /// <summary>
        /// Resolve and CONSUME the next chain target. Pops the oldest unmatched Pic
        /// from this turn's FIFO queue, so when the LLM emits N generate-class actions
        /// followed by N chain="true" actions (a "grouped" reply), each chain pairs
        /// first-with-first instead of every chain stacking on the most recent Pic.
        /// Falls back to <see cref="GetLastSpawnedPicForTurn"/> when the queue is empty
        /// (so 3+ step chains on the same root Pic still work). Returns null if no
        /// usable chain target exists this turn.
        /// </summary>
        PicMain ConsumeChainTarget();
    }
}
