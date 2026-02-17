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

            // Recall by anomaly progress completion (Œ®“ª’Êœ‡£∫AnomalyState roster)
            for (int i = 0; i < s.Anomalies.Count; i++)
            {
                var a = s.Anomalies[i];
                if (a == null) continue;
                if (string.IsNullOrEmpty(a.Id)) continue;

                // Investigate complete -> recall investigate roster
                if (a.Phase == AnomalyPhase.Investigate && a.InvestigateProgress >= 1f &&
                    a.InvestigatorIds != null && a.InvestigatorIds.Count > 0)
                {
                    string err;
                    DispatchSystem.TrySetRoster(s, a.Id, AssignmentSlot.Investigate, Empty, out err);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"[PhaseCompletionRecall] Investigate recall failed anomaly={a.Id} err={err}");

                    // Advance phase: Investigate -> Contain
                    a.Phase = AnomalyPhase.Contain;
                    Debug.Log($"[Phase] advance anom={a.Id} to {a.Phase}");
                }

                // Contain complete -> recall contain roster
                if (a.Phase == AnomalyPhase.Contain && a.ContainProgress >= 1f &&
                    a.ContainmentIds != null && a.ContainmentIds.Count > 0)
                {
                    string err;
                    DispatchSystem.TrySetRoster(s, a.Id, AssignmentSlot.Contain, Empty, out err);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"[PhaseCompletionRecall] Contain recall failed anomaly={a.Id} err={err}");

                    // Advance phase: Contain -> Operate
                    a.Phase = AnomalyPhase.Operate;
                    Debug.Log($"[Phase] advance anom={a.Id} to {a.Phase}");
                }
            }
        }

    }
}
