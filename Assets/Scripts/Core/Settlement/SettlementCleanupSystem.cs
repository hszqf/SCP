using System;
using UnityEngine;
using Core;
using System.Linq;

namespace Settlement
{
    public static class SettlementCleanupSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayPipelineResult r)
        {
            if (state == null) return;

            // Increment tint counters for dead/insane agents that are still stuck at anomalies.
            if (state.Agents != null)
            {
                for (int i = 0; i < state.Agents.Count; i++)
                {
                    var ag = state.Agents[i];
                    if (ag == null) continue;

                    bool stuckAtAnomaly = ag.LocationKind == AgentLocationKind.AtAnomaly && !string.IsNullOrEmpty(ag.LocationAnomalyInstanceId);

                    if (ag.IsDead && stuckAtAnomaly) ag.DeadDays = Mathf.Min(999, ag.DeadDays + 1);
                    else if (!ag.IsDead) ag.DeadDays = 0;

                    if (ag.IsInsane && stuckAtAnomaly) ag.InsaneDays = Mathf.Min(999, ag.InsaneDays + 1);
                    else if (!ag.IsInsane) ag.InsaneDays = 0;
                }
            }

            // Keep existing TODO hook for anomaly cleanup later.
            r?.Log("[Settlement] Cleanup applied (dead/insane counters)");
        }
    }
}
