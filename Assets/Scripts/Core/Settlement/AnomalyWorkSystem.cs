using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using UnityEngine;

namespace Settlement
{
    public static class AnomalyWorkSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            Debug.Log("[Settlement] AnomalyWorkSystem.Apply called");
            if (state == null || state.Anomalies == null) return;

            foreach (var anom in state.Anomalies.OrderBy(a => a.SpawnSeq))
            {
                var canonicalKey = anom.Id;

                var invIds = anom.GetRoster(AssignmentSlot.Investigate);
                var conIds = anom.GetRoster(AssignmentSlot.Contain);
                var opIds  = anom.GetRoster(AssignmentSlot.Operate);

                var invArrived = CollectArrived(state, canonicalKey, invIds);
                var conArrived = CollectArrived(state, canonicalKey, conIds);
                var opArrived  = CollectArrived(state, canonicalKey, opIds);

             
                switch (anom.Phase)
                {
                    case AnomalyPhase.Investigate:
                        {
                            float d01 = Sim.CalcInvestigateDelta01_FromRoster(state, anom, invArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork][DRY] anom={anom.Id} phase={anom.Phase} invArr={invArrived.Count} addInv01={d01:0.###} cur={anom.InvestigateProgress:0.###}");
                        }
                        break;
                    case AnomalyPhase.Contain:
                        {
                            float d01 = Sim.CalcContainDelta01_FromRoster(state, anom, conArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork][DRY] anom={anom.Id} phase={anom.Phase} conArr={conArrived.Count} addCon01={d01:0.###} cur={anom.ContainProgress:0.###}");
                        }
                        break;
                    case AnomalyPhase.Operate:
                        {
                            int dNE = Sim.CalcNegEntropyDelta_FromRoster(state, anom, opArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork][DRY] anom={anom.Id} phase={anom.Phase} opArr={opArrived.Count} addNegEntropy={dNE}");
                        }
                        break;
                }
            }
        }

        private static List<AgentState> CollectArrived(GameState state, string canonicalKey, List<string> rosterIds)
        {
            var list = new List<AgentState>();
            if (rosterIds == null || rosterIds.Count == 0) return list;
            var set = new HashSet<string>(rosterIds);
            foreach (var a in state.Agents)
            {
                if (a.LocationKind != AgentLocationKind.AtAnomaly) continue;
                if (a.LocationAnomalyKey != canonicalKey) continue;
                if (!set.Contains(a.Id)) continue;
                list.Add(a);
            }
            return list;
        }
    }
}
