using System;
using UnityEngine;
using Core;
using System.Linq;

namespace Settlement
{
    public static class SettlementCleanupSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            // TODO
            Debug.Log("[Settlement] SettlementCleanupSystem.Apply called");

            if (state == null || state.Anomalies == null) return;

            foreach (var a in state.Anomalies.OrderBy(x => x.SpawnSeq))
            {
                if (a.Phase == AnomalyPhase.Investigate && a.InvestigateProgress >= 1f) a.Phase = AnomalyPhase.Contain;
                else if (a.Phase == AnomalyPhase.Contain && a.ContainProgress >= 1f) a.Phase = AnomalyPhase.Operate;
            }
        }
    }
}
