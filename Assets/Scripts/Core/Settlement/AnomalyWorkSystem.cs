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

                var invArrived = SettlementUtil.CollectArrived(state, canonicalKey, invIds);
                var conArrived = SettlementUtil.CollectArrived(state, canonicalKey, conIds);
                var opArrived  = SettlementUtil.CollectArrived(state, canonicalKey, opIds);

                
                switch (anom.Phase)
                {
                    case AnomalyPhase.Investigate:
                        {
                            float d01 = SettlementUtil.CalcInvestigateDelta01_FromRoster(state, anom, invArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork] anom={anom.Id} phase={anom.Phase} invArr={invArrived.Count} addInv01={d01:0.###} cur={anom.InvestigateProgress:0.###}");

                            anom.InvestigateProgress = Mathf.Clamp01(anom.InvestigateProgress + d01);

                        }
                        break;
                    case AnomalyPhase.Contain:
                        {
                            float d01 = SettlementUtil.CalcContainDelta01_FromRoster(state, anom, conArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork] anom={anom.Id} phase={anom.Phase} conArr={conArrived.Count} addCon01={d01:0.###} cur={anom.ContainProgress:0.###}");

                                anom.ContainProgress = Mathf.Clamp01(anom.ContainProgress + d01);
                        }
                        break;
                    case AnomalyPhase.Operate:
                        {
                            int dNE = SettlementUtil.CalcNegEntropyDelta_FromRoster(state, anom, opArrived, DataRegistry.Instance);
                            r?.Log($"[Settle][AnomWork] anom={anom.Id} phase={anom.Phase} opArr={opArrived.Count} addNegEntropy={dNE}");


                            if (dNE > 0) state.NegEntropy += dNE;

                        }
                        break;
                }
            }
        }


    }
}
