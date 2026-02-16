using Core;
using System;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    public static class BaseRecoverySystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            Debug.Log("[Settlement] BaseRecoverySystem.Apply called");
            if (state?.Agents == null) return;

            int healedCount = 0;

            foreach (var agent in state.Agents)
            {
                if (agent == null) continue;
                if (agent.IsDead || agent.IsInsane) continue;

                // 新口径：只恢复基地里的
                if (agent.LocationKind != AgentLocationKind.Base) continue;
                if (agent.IsTravelling) continue; // 防御

                int hpHeal = Mathf.CeilToInt(agent.MaxHP * 0.1f);
                int sanHeal = Mathf.CeilToInt(agent.MaxSAN * 0.1f);
                if (hpHeal <= 0 && sanHeal <= 0) continue;

                SettlementUtil.ApplyAgentImpact(state, agent.Id, hpHeal, sanHeal, "BaseRecovery");
                healedCount++;
            }

            r?.Log($"[Settle][BaseRecovery] healedAgents={healedCount}");


        }

    }
}
