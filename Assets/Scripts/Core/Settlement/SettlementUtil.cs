// Assets/Scripts/Core/Settlement/SettlementUtil.cs
using Core;
using Data;
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
                if (a.LocationAnomalyInstanceId != canonicalKey) continue;
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



        // Calculate how many population to deduct for a given anomaly instance and target city
        // This is a pure function: it does NOT modify state.
        public static int CalcAnomalyCityPopDelta(GameState state, AnomalyState anom, CityState city)
        {
            if (state == null || anom == null || city == null) return 0;

            var registry = DataRegistry.Instance;
            if (registry == null) return 0;

            // 只返回该异常的“每日人口减少值”，范围判定由 AnomalyBehaviorSystem 统一处理（MapPos）。
            int kill = registry.GetAnomalyIntWithWarn(anom.AnomalyDefId, "actPeopleKill", 0);
            return Math.Max(0, kill);
        }





        public static float CalcInvestigateDelta01_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0f;
            if (registry == null) return 0f;


            if (anom == null) return 0f; // 或 0
            if (string.IsNullOrEmpty(anom.AnomalyDefId))
            {
                Debug.LogWarning($"[SettleCalc] Missing anom.AnomalyDefId for anomStateId={anom?.Id ?? "null"}");
                return 0f; // NegEntropy 的函数返回 0
            }

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0f;

            int[] req = NormalizeIntArray4(def.invReq);
            float effDelta = CalcEffDelta_FromRosterReq(arrived, req);

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(def));
            float delta01 = effDelta / Mathf.Max(1f, (float)requiredDays);
            return Mathf.Clamp01(delta01);
        }

        public static float CalcContainDelta01_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0f;
            if (registry == null) return 0f;

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0f;

            int[] req = NormalizeIntArray4(def.conReq);
            float effDelta = CalcEffDelta_FromRosterReq(arrived, req);

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(def));
            float delta01 = effDelta / Mathf.Max(1f, (float)requiredDays);
            return Mathf.Clamp01(delta01);
        }

        public static int CalcNegEntropyDelta_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0;
            if (registry == null) return 0;

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0;

            return CalcDailyNegEntropyYield(def);
        }

        private static int[] NormalizeIntArray4(int[] input)
        {
            var result = new int[4];
            if (input == null) return result;
            var count = Math.Min(input.Length, 4);
            Array.Copy(input, result, count);
            return result;
        }
        private static float CalcEffDelta_FromRosterReq(List<AgentState> team, int[] req)
        {
            req = NormalizeIntArray4(req);
            float sMatch = ComputeMatchS_NoWeight(ComputeTeamAvgProps(team), req);
            float progressScale = MapSToMult(sMatch);
            return progressScale;
        }
        private static float ComputeMatchS_NoWeight(float[] team, int[] req)
        {
            if (team == null || req == null) return 1f;

            float minR = float.PositiveInfinity;
            float sumR = 0f;
            int count = 0;

            for (int i = 0; i < 4; i++)
            {
                if (req[i] <= 0) continue;
                float ratio = req[i] > 0 ? team[i] / req[i] : 0f;
                ratio = Mathf.Clamp(ratio, 0f, 2f);
                if (ratio < minR) minR = ratio;
                sumR += ratio;
                count += 1;
            }

            if (count <= 0) return 1f;

            float avgR = sumR / count;
            return 0.5f * minR + 0.5f * avgR;
        }
        private static float[] ComputeTeamAvgProps(List<AgentState> members)
        {
            if (members == null || members.Count == 0)
            {
                Debug.LogWarning("[TeamAvg] Empty members list. Using [0,0,0,0] to avoid divide-by-zero.");
                return new[] { 0f, 0f, 0f, 0f };
            }

            float p = 0f, r = 0f, o = 0f, pow = 0f;
            int count = 0;

            foreach (var m in members)
            {
                if (m == null) continue;
                p += m.Perception;
                r += m.Resistance;
                o += m.Operation;
                pow += m.Power;
                count += 1;
            }

            if (count <= 0)
            {
                Debug.LogWarning("[TeamAvg] All members were null. Using [0,0,0,0] to avoid divide-by-zero.");
                return new[] { 0f, 0f, 0f, 0f };
            }

            return new[] { p, r, o, pow };
        }


        private static float MapSToMult(float s)
        {
            if (s < 0.7f) return 0.3f;
            if (s < 1.0f) return Mathf.Lerp(0.3f, 1.0f, (s - 0.7f) / 0.3f);
            if (s < 1.3f) return Mathf.Lerp(1.0f, 1.6f, (s - 1.0f) / 0.3f);
            return 1.6f;
        }

        private static int GetTaskBaseDaysFromAnomaly(AnomalyDef anomalyDef)
        {
            int baseDays = anomalyDef?.baseDays ?? 0;
            return baseDays > 0 ? baseDays : 1;
        }

        private static int CalcDailyNegEntropyYield(AnomalyDef def)
        {
            if (def == null) return 0;
            return Math.Max(0, def.manNegentropyPerDay);
        }


        public static bool AddExpAndTryLevelUp(Core.AgentState a, int addExp, System.Random rng)
        {
            if (addExp > 0)
            {
                Debug.Log($"[AgentLevel] agent={a.Id} lv={a.Level} exp={a.Exp}->{a.Exp + addExp}");
            }
            a.Exp += addExp;
            bool leveled = false;
            while (a.Exp >= ExpToNext(a.Level))
            {
                int oldLv = a.Level;
                a.Exp -= ExpToNext(a.Level);
                a.Level += 1;
                int grow = rng.Next(0, 4);
                string growStr = "";
                switch (grow)
                {
                    case 0:
                        a.Perception += 1;
                        growStr = "Perception+1";
                        break;
                    case 1:
                        a.Resistance += 1;
                        growStr = "Resistance+1";
                        break;
                    case 2:
                        a.Operation += 1;
                        growStr = "Operation+1";
                        break;
                    case 3:
                        a.Power += 1;
                        growStr = "Power+1";
                        break;
                }
                Debug.Log($"[AgentLevel] agent={a.Id} lv={oldLv}->{a.Level} exp={a.Exp} grow={growStr}");
                leveled = true;
            }
            return leveled;
        }
        // Agent Level/Exp helpers
        public static int ExpToNext(int level)
        {
            return 20 + (level - 1) * 10;
        }



        // =====================
        // Agent Busy Text
        // =====================

        /// <summary>
        /// Builds a descriptive text for what an agent is currently doing.
        /// Returns empty string if the agent is idle (not assigned to any active task).
        /// New behavior: only use AgentState.LocationKind/LocationSlot/IsTravelling to determine text.
        /// </summary>
        public static string BuildAgentBusyText(GameState state, string agentId)
        {
            if (state == null || string.IsNullOrEmpty(agentId))
                return string.Empty;

            var agent = state.Agents?.FirstOrDefault(a => a != null && a.Id == agentId);
            if (agent == null) return string.Empty;

            // If at base and not travelling, consider idle
            if (agent.LocationKind == AgentLocationKind.Base && !agent.IsTravelling)
                return string.Empty;

            // Map slot to Chinese label
            string SlotToChinese(AssignmentSlot slot)
            {
                return slot switch
                {
                    AssignmentSlot.Investigate => "调查",
                    AssignmentSlot.Contain => "收容",
                    AssignmentSlot.Operate => "管理",
                    _ => "管理",
                };
            }

            switch (agent.LocationKind)
            {
                case AgentLocationKind.TravellingToAnomaly:
                    return $"在途·前往{SlotToChinese(agent.LocationSlot)}";
                case AgentLocationKind.AtAnomaly:
                    return $"{SlotToChinese(agent.LocationSlot)}中";
                case AgentLocationKind.TravellingToBase:
                    return "返程·回基地";
                case AgentLocationKind.Base:
                default:
                    return string.Empty;
            }
        }
    }
}
