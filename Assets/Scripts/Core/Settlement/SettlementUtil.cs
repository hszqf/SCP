// Assets/Scripts/Core/Settlement/SettlementUtil.cs
using Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    /// <summary>
    /// Shared helpers for Settlement systems.
    /// Keep Systems small; put reusable query logic here.
    /// </summary>
    public static class SettlementUtil
    {
        public static List<AgentState> CollectArrived(GameState state, string canonicalKey, List<string> rosterIds)
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



        /// <summary>
        /// Unified entry point for applying HP/SAN impacts to agents.
        /// Clamps values to [0, max] and logs the impact.
        /// </summary>
        public static void ApplyAgentImpact(GameState s, string agentId, int hpDelta, int sanDelta, string reason)
        {
            if (s == null || s.Agents == null) return;

            if (string.IsNullOrEmpty(agentId))
            {
                Debug.LogWarning($"[SettlementUtil][AgentImpact] day={(s != null ? s.Day : -1)} agent=NULL reason={reason}");
                return;
            }

            var agent = s.Agents.FirstOrDefault(a => a != null && a.Id == agentId);
            if (agent == null)
            {
                Debug.LogWarning($"[SettlementUtil][AgentImpact] day={s.Day} agent={agentId} NOTFOUND reason={reason}");
                return;
            }

            int hpBefore = agent.HP;
            int sanBefore = agent.SAN;

            int hpAfter = agent.HP + hpDelta;
            int sanAfter = agent.SAN + sanDelta;

            // Clamp to [0, max]
            agent.HP = Math.Max(0, Math.Min(agent.MaxHP, hpAfter));
            agent.SAN = Math.Max(0, Math.Min(agent.MaxSAN, sanAfter));

            Debug.Log(
                $"[SettlementUtil][AgentImpact] day={s.Day} agent={agent.Id} " +
                $"hp={hpDelta:+0;-#;0} ({hpBefore}->{agent.HP}) " +
                $"san={sanDelta:+0;-#;0} ({sanBefore}->{agent.SAN}) " +
                $"reason={reason}"
            );
        }

        /// <summary>
        /// Apply a Money delta to GameState with optional logging.
        /// (No clamp; can be negative if you decide so later.)
        /// </summary>
        public static void ApplyMoney(GameState s, int delta, string reason, DayEndResult r = null)
        {
            if (s == null) return;
            int before = s.Money;
            s.Money += delta;
            r?.Log($"[SettlementUtil][Money] day={s.Day} {before}->{s.Money} delta={delta:+0;-#;0} reason={reason}");
        }

        /// <summary>
        /// Apply a NegEntropy delta to GameState (clamped to >=0) with optional logging.
        /// </summary>
        public static void ApplyNegEntropy(GameState s, int delta, string reason, DayEndResult r = null)
        {
            if (s == null) return;
            int before = s.NegEntropy;
            int after = before + delta;
            if (after < 0) after = 0;
            s.NegEntropy = after;
            r?.Log($"[SettlementUtil][NegEntropy] day={s.Day} {before}->{s.NegEntropy} delta={delta:+0;-#;0} reason={reason}");
        }

        /// <summary>
        /// Apply a population delta to a city (clamped to >=0) with optional logging.
        /// </summary>
        public static void ApplyCityPopulation(CityState city, int delta, int day, string reason, DayEndResult r = null)
        {
            if (city == null) return;
            int before = city.Population;
            int after = before + delta;
            if (after < 0) after = 0;
            city.Population = after;
            r?.Log($"[SettlementUtil][CityPop] day={day} city={city.Id} {before}->{city.Population} delta={delta:+0;-#;0} reason={reason}");
        }
    }
}
