using System.Collections.Generic;
using System.Text;

namespace AITools.AIChat.Context
{
    /// <summary>
    /// Frozen snapshot of one configured GPU/server at a point in time. Built by
    /// <see cref="ChatContextBuilder"/> from <see cref="Config"/>'s GPU list and
    /// formatted into the LLM's system prompt every turn so the model can target
    /// specific GPUs when invoking generation skills.
    /// </summary>
    public class GpuSnapshotEntry
    {
        public int Id;
        public string Name;
        public string RemoteUrl;
        public bool IsLocal;
        public bool IsActive;
        public bool IsBusy;
        public string RendererType;
        public float VramGB;          // 0 = unknown
        public int PendingLLMCount;
    }

    public static class GpuSnapshot
    {
        /// <summary>
        /// Build a one-line-per-GPU human-readable block to embed in the system prompt.
        /// Returns a header + lines like:
        ///   GPUS (use the gpu="N" attribute to target a specific one):
        ///   - 0: RTX 4090, 24 GB VRAM, ComfyUI, IDLE
        ///   - 1: RTX 3090, ?? GB VRAM, ComfyUI, BUSY
        /// </summary>
        public static string BuildBlock()
        {
            var entries = Capture();
            if (entries.Count == 0)
                return "GPUS: (none configured)\n";

            var sb = new StringBuilder();
            sb.AppendLine("GPUS (use the gpu=\"N\" attribute on action tags to target a specific one):");
            foreach (var e in entries)
            {
                sb.Append("- ").Append(e.Id).Append(": ");
                sb.Append(string.IsNullOrEmpty(e.Name) ? (e.RemoteUrl ?? "?") : e.Name);
                sb.Append(", ");
                sb.Append(e.VramGB > 0 ? e.VramGB.ToString("0.#") + " GB VRAM" : "?? GB VRAM");
                sb.Append(", ");
                sb.Append(e.RendererType);
                sb.Append(", ");
                if (!e.IsActive)            sb.Append("INACTIVE");
                else if (e.IsBusy)          sb.Append("BUSY");
                else                        sb.Append("IDLE");
                if (e.PendingLLMCount > 0)
                    sb.Append(" (").Append(e.PendingLLMCount).Append(" llm pending)");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static List<GpuSnapshotEntry> Capture()
        {
            var list = new List<GpuSnapshotEntry>();
            var cfg = Config.Get();
            if (cfg == null) return list;

            int n = cfg.GetGPUCount();
            for (int i = 0; i < n; i++)
            {
                var info = cfg.GetGPUInfo(i);
                if (info == null) continue;
                list.Add(new GpuSnapshotEntry
                {
                    Id = i,
                    Name = info._name,
                    RemoteUrl = info.remoteURL,
                    IsLocal = info.isLocal,
                    IsActive = info._bIsActive,
                    IsBusy = cfg.IsGPUBusy(i),
                    RendererType = info._requestedRendererType.ToString(),
                    VramGB = info._vramGB,
                    PendingLLMCount = info.pendingLLMCount,
                });
            }
            return list;
        }
    }
}
