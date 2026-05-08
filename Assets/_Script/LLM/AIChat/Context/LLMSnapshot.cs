using System.Collections.Generic;
using System.Text;

namespace AITools.AIChat.Context
{
    /// <summary>
    /// Snapshot of one LLM instance at a point in time. Mirrors the runtime view of
    /// <see cref="LLMInstanceInfo"/> in a thin POCO so the prompt builder doesn't have
    /// to reach into runtime state directly.
    /// </summary>
    public class LLMSnapshotEntry
    {
        public int InstanceId;
        public string Name;
        public string Provider;
        public string Model;
        public bool IsActive;
        public string JobMode;          // Any / SmallJobsOnly / BigJobsOnly / VisionJobsOnly / NonVisionOnly
        public int ActiveTasks;
        public int Capacity;            // maxConcurrentTasks * effective replica count
        public bool IsCallingChat;      // true for the instance currently running this chat
    }

    public static class LLMSnapshot
    {
        /// <summary>
        /// Build a one-line-per-instance block embedded in the system prompt:
        ///   OTHER LLMS (use llm="N" to delegate small jobs):
        ///   - 0: "OpenAI" gpt-4o (OpenAI), role=Any, in-flight 0/3
        ///   - 1: "Local Qwen Small" qwen2.5:7b (Ollama), role=SmallJobsOnly, in-flight 1/2  &lt;-- you
        /// </summary>
        public static string BuildBlock(int callingInstanceId)
        {
            var entries = Capture(callingInstanceId);
            if (entries.Count == 0)
                return "OTHER LLMS: (no instances configured beyond the active provider)\n";

            var sb = new StringBuilder();
            sb.AppendLine("OTHER LLMS (use llm=\"N\" with delegation skills to target a specific one):");
            foreach (var e in entries)
            {
                sb.Append("- ").Append(e.InstanceId).Append(": \"")
                  .Append(string.IsNullOrEmpty(e.Name) ? "(unnamed)" : e.Name)
                  .Append("\" ")
                  .Append(string.IsNullOrEmpty(e.Model) ? "(no model)" : e.Model)
                  .Append(" (").Append(e.Provider).Append("), role=").Append(e.JobMode)
                  .Append(", in-flight ").Append(e.ActiveTasks).Append("/").Append(e.Capacity);
                if (!e.IsActive) sb.Append(" [DISABLED]");
                if (e.IsCallingChat) sb.Append("  <-- you");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static List<LLMSnapshotEntry> Capture(int callingInstanceId)
        {
            var list = new List<LLMSnapshotEntry>();
            var mgr = LLMInstanceManager.Get();
            if (mgr == null) return list;

            foreach (var inst in mgr.GetAllInstances())
            {
                if (inst == null) continue;
                inst.EnsureReplicaActiveTasks();
                int capacity = inst.maxConcurrentTasks * inst.GetEffectiveReplicaCount();
                list.Add(new LLMSnapshotEntry
                {
                    InstanceId = inst.instanceID,
                    Name = inst.name,
                    Provider = inst.providerType.ToString(),
                    Model = inst.settings != null ? inst.settings.selectedModel : "",
                    IsActive = inst.isActive,
                    JobMode = inst.jobMode.ToString(),
                    ActiveTasks = inst.GetTotalActiveTasks(),
                    Capacity = capacity,
                    IsCallingChat = (inst.instanceID == callingInstanceId)
                });
            }
            return list;
        }
    }
}
