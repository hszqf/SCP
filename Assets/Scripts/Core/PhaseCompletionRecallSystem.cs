using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

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

        
        /// <summary>
        /// Plan-mode recall (no MovementToken): emits RosterRecalled/PhaseAdvanced and mutates the provided state directly.
        /// Used by DayEndPlanBuilder on a shadow state to keep deterministic ordering and avoid UI inference.
        /// </summary>
        public static void ApplyPlan(GameState s, IDayEventSink sink)
        {
            if (s == null || s.Anomalies == null) return;

            var anomalies = s.Anomalies.Where(a => a != null).OrderBy(a => a.SpawnSeq).ToList();

            for (int i = 0; i < anomalies.Count; i++)
            {
                var a = anomalies[i];
                if (a == null) continue;
                if (string.IsNullOrEmpty(a.Id)) continue;

                // Investigate complete -> recall investigate roster, advance to Contain
                if (a.Phase == AnomalyPhase.Investigate && a.InvestigateProgress >= 1f &&
                    a.InvestigatorIds != null && a.InvestigatorIds.Count > 0)
                {
                    var arrived = CollectArrivedIds(s, a.Id, AssignmentSlot.Investigate, a.InvestigatorIds);
                    ImmediateRecallToBase(s, a.Id, AssignmentSlot.Investigate, a.InvestigatorIds);

                    if (arrived.Count > 0)
                        sink?.Add(DayEvent.Recall(a.Id, AssignmentSlot.Investigate, arrived.ToArray()));

                    sink?.Add(DayEvent.PhaseAdv(a.Id, AnomalyPhase.Investigate, AnomalyPhase.Contain));
                    a.Phase = AnomalyPhase.Contain;

                    Debug.Log($"[Phase][Plan] advance anom={a.Id} to {a.Phase}");
                }

                // Contain complete -> recall contain roster, advance to Operate
                if (a.Phase == AnomalyPhase.Contain && a.ContainProgress >= 1f &&
                    a.ContainmentIds != null && a.ContainmentIds.Count > 0)
                {
                    var arrived = CollectArrivedIds(s, a.Id, AssignmentSlot.Contain, a.ContainmentIds);
                    ImmediateRecallToBase(s, a.Id, AssignmentSlot.Contain, a.ContainmentIds);

                    if (arrived.Count > 0)
                        sink?.Add(DayEvent.Recall(a.Id, AssignmentSlot.Contain, arrived.ToArray()));

                    sink?.Add(DayEvent.PhaseAdv(a.Id, AnomalyPhase.Contain, AnomalyPhase.Operate));
                    a.Phase = AnomalyPhase.Operate;

                    Debug.Log($"[Phase][Plan] advance anom={a.Id} to {a.Phase}");
                }
            }
        }

        private static List<string> CollectArrivedIds(GameState s, string anomId, AssignmentSlot slot, List<string> roster)
        {
            var outIds = new List<string>();
            if (s == null || s.Agents == null || roster == null) return outIds;

            for (int i = 0; i < roster.Count; i++)
            {
                var agentId = roster[i];
                if (string.IsNullOrEmpty(agentId)) continue;

                var ag = s.Agents.Find(x => x != null && x.Id == agentId);
                if (ag == null) continue;

                
                if (ag.IsDead || ag.IsInsane) continue;
if (ag.LocationKind == AgentLocationKind.AtAnomaly &&
                    string.Equals(ag.LocationAnomalyInstanceId, anomId, StringComparison.OrdinalIgnoreCase) &&
                    ag.LocationSlot == slot)
                {
                    outIds.Add(agentId);
                }
            }

            outIds.Sort(StringComparer.Ordinal);
            return outIds;
        }

        private static void ImmediateRecallToBase(GameState s, string anomId, AssignmentSlot slot, List<string> roster)
        {
            if (s == null) return;

            // Clear roster list
            roster?.Clear();

            if (s.Agents == null) return;

            for (int i = 0; i < s.Agents.Count; i++)
            {
                var ag = s.Agents[i];
                if (ag == null) continue;

                if (!string.Equals(ag.LocationAnomalyInstanceId, anomId, StringComparison.OrdinalIgnoreCase)) continue;
                if (ag.LocationSlot != slot) continue;

                
                if (ag.IsDead || ag.IsInsane) continue;
// For plan mode: snap back to base regardless of travelling/at anomaly.
                ag.LocationKind = AgentLocationKind.Base;
                ag.LocationAnomalyInstanceId = null;
            }
        }

public static void Apply(GameController gc)
        {
            var s = gc?.State;
            if (s == null || s.Anomalies == null) return;

            // Recall by anomaly progress completion (Ψһ���ࣺAnomalyState roster)
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