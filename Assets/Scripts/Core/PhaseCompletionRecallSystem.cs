using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// T6.6: When phase completes, recall agents of that slot back to base.
    /// Rule: EndDay doesn't auto-recall. Only recall on phase completion.
    /// Completion criteria (migration, fixed):
    /// - Investigate complete if AnomalyState.InvestigateProgress >= 1f
    /// - Contain complete if AnomalyState.ContainProgress >= 1f
    /// </summary>
    public static class PhaseCompletionRecallSystem
    {
        // shared empty roster to avoid allocations
        private static readonly List<string> Empty = new List<string>(0);

        public static void Apply(GameController gc)
        {
            var s = gc?.State;
            if (s == null || s.Anomalies == null) return;

            // 1) Recall by anomaly progress completion.
            for (int i = 0; i < s.Anomalies.Count; i++)
            {
                var a = s.Anomalies[i];
                if (a == null) continue;
                if (string.IsNullOrEmpty(a.Id)) continue;

                // Investigate complete -> recall investigate roster
                if (a.Phase == AnomalyPhase.Investigate && a.InvestigateProgress >= 1f && a.InvestigatorIds != null && a.InvestigatorIds.Count > 0)
                {
                    string err;
                    DispatchSystem.TrySetRoster(s, a.Id, AssignmentSlot.Investigate, Empty, out err);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"[PhaseCompletionRecall] Investigate recall failed anomaly={a.Id} err={err}");

                    // Task-compat cleanup (temporary): remove legacy Investigate task so UI/busy doesn't stick at 100%
                    TaskCompat_ClearNodeTask(s, a.NodeId, TaskType.Investigate);

                    // Advance phase: Investigate -> Contain
                    a.Phase = AnomalyPhase.Contain;
                    Debug.Log($"[Phase] advance anom={a.Id} to {a.Phase}");
                }

                // Contain complete -> recall contain roster
                if (a.Phase == AnomalyPhase.Contain && a.ContainProgress >= 1f && a.ContainmentIds != null && a.ContainmentIds.Count > 0)
                {
                    string err;
                    DispatchSystem.TrySetRoster(s, a.Id, AssignmentSlot.Contain, Empty, out err);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"[PhaseCompletionRecall] Contain recall failed anomaly={a.Id} err={err}");

                    // Task-compat cleanup (temporary): remove legacy Contain task so UI/busy doesn't stick at 100%
                    TaskCompat_ClearNodeTask(s, a.NodeId, TaskType.Contain);

                    // Advance phase: Contain -> Operate
                    a.Phase = AnomalyPhase.Operate;
                    Debug.Log($"[Phase] advance anom={a.Id} to {a.Phase}");
                }
            }

            // 2) Cleanup legacy tasks to avoid Busy residue (migration compatibility).
            // Minimal rule: if task is not Active, cancel/retreat it.
            if (s.Cities == null) return;

            for (int ci = 0; ci < s.Cities.Count; ci++)
            {
                var city = s.Cities[ci];
                if (city == null || city.Tasks == null) continue;

                for (int ti = 0; ti < city.Tasks.Count; ti++)
                {
                    var t = city.Tasks[ti];
                    if (t == null) continue;

                    // Only investigate/contain tasks matter here.
                    if (t.Type != TaskType.Investigate && t.Type != TaskType.Contain) continue;

                    // If already completed/cancelled (or any non-active), ensure it's cleaned up.
                    if (t.State != TaskState.Active)
                    {
                        // This API already exists in your project; use it as the migration cleanup hook.
                        gc.CancelOrRetreatTask(t.Id);
                    }
                }
            }
        }

        // Task compatibility helper: remove legacy tasks of a specific type on the given node.
        private static void TaskCompat_ClearNodeTask(GameState state, string nodeId, TaskType type)
        {
            if (state == null || string.IsNullOrEmpty(nodeId)) return;
            var node = state.Cities.Find(c => c != null && c.Id == nodeId);
            if (node == null || node.Tasks == null) return;

            for (int i = node.Tasks.Count - 1; i >= 0; i--)
            {
                var t = node.Tasks[i];
                if (t == null) continue;
                if (t.Type != type) continue;

                // safest: remove task entirely
                node.Tasks.RemoveAt(i);
            }
        }
    }
}
