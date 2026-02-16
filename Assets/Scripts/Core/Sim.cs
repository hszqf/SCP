// Canvas-maintained file: Core/Sim (v4 - data driven events)
// Source: Assets/Scripts/Core/Sim.cs
// Goal: Load all game data from DataRegistry and drive events/effects via config.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using UnityEngine;
using Random = System.Random;

namespace Core
{
    public static class Sim
    {
   
        /// <summary>
        /// Public wrapper to generate scheduled anomalies for the current day using the provided RNG.
        /// This exists so callers outside Sim.StepDay can control when anomaly generation happens
        /// (e.g. after settlement pipeline completes).
        /// </summary>
        public static void GenerateScheduledAnomalies_Public(GameState s, System.Random rng)
        {
            var registry = Data.DataRegistry.Instance;
            if (s == null || registry == null || rng == null) return;
            GenerateScheduledAnomalies(s, rng, registry, s.Day);
        }

        // Advance day only: increment day counter and perform lightweight per-day initializations
        // (used by pipeline path where full Sim.StepDay is executed via settlement systems)
        public static void AdvanceDay_Only(GameState s)
        {
            if (s == null) return;
            s.Day += 1;

            if (s.RecruitPool != null)
            {
                s.RecruitPool.day = -1;
                s.RecruitPool.refreshUsedToday = 0;
                s.RecruitPool.candidates?.Clear();
            }
        }


    




        // =====================
        // Management (NegEntropy) - formalized as NodeTask.Manage
        // =====================


        private static void EnsureActiveAnomaly(GameState state, CityState node, string anomalyId, DataRegistry registry)
        {
            if (node.ActiveAnomalyIds == null) node.ActiveAnomalyIds = new List<string>();
            if (!node.ActiveAnomalyIds.Contains(anomalyId)) node.ActiveAnomalyIds.Add(anomalyId);
            node.HasAnomaly = node.ActiveAnomalyIds.Count > 0;

            if (registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly))
            {
                node.AnomalyLevel = Math.Max(node.AnomalyLevel, 1);
            }

            var anomalyState = GetOrCreateAnomalyState(state, node, anomalyId);
            if (anomalyState != null && string.IsNullOrEmpty(anomalyState.NodeId))
                anomalyState.NodeId = node.Id;
        }

        private static AnomalyState GetOrCreateAnomalyState(GameState state, CityState node, string anomalyId)
        {
            if (state == null || string.IsNullOrEmpty(anomalyId)) return null;
            state.Anomalies ??= new List<AnomalyState>();

            var existing = state.Anomalies.FirstOrDefault(a => a != null && string.Equals(a.AnomalyDefId, anomalyId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (node != null && string.IsNullOrEmpty(existing.NodeId))
                    existing.NodeId = node.Id;
                return existing;
            }

            var created = new AnomalyState
            {
                Id = "AN_STATE_" + Guid.NewGuid().ToString("N")[..8],
                AnomalyDefId = anomalyId,
                NodeId = node?.Id,
                SpawnDay = state.Day,
            };
            // S1: record spawn sequence for deterministic ordering of anomaly actions
            try
            {
                created.SpawnSeq = state.NextAnomalySpawnSeq++;
            }
            catch
            {
                // In case state or NextAnomalySpawnSeq is null/uninitialized, fall back to 0
                created.SpawnSeq = 0;
                if (state != null) state.NextAnomalySpawnSeq = (state.NextAnomalySpawnSeq >= 0) ? state.NextAnomalySpawnSeq : 0;
            }
            state.Anomalies.Add(created);
            return created;
        }

        /// <summary>
        /// Picks a random anomaly ID from the registry that is not currently active or managed in the game state.
        /// </summary>
        private static string PickRandomAnomalyId(DataRegistry registry, Random rng)
        {
            var all = registry.AnomaliesById.Keys.ToList();
            if (all.Count == 0) return null;
            int idx = rng.Next(all.Count);
            return all[idx];
        }

        public static int GenerateScheduledAnomalies(GameState s, Random rng, DataRegistry registry, int day)
        {
            if (s == null || rng == null || registry == null) return 0;

            int genNum = registry.GetAnomaliesGenNumForDay(day);
            if (genNum <= 0) return 0;

            var nodes = s.Cities?.Where(n => n != null).ToList();
            if (nodes == null || nodes.Count == 0) return 0;
            nodes = nodes.Where(n => n != null && n.Type != 0 && n.Unlocked).ToList();
            if (nodes.Count == 0) return 0;

            int spawned = 0;
            int maxAttempts = Math.Max(10, genNum * 4);
            int attempts = 0;

            while (spawned < genNum && attempts < maxAttempts)
            {
                attempts++;
                var anomalyId = PickRandomAnomalyId(registry, rng);
                if (string.IsNullOrEmpty(anomalyId)) break;

                bool alreadySpawned = s.Cities.Any(n =>
                    n != null &&
                    ((n.ActiveAnomalyIds != null && n.ActiveAnomalyIds.Contains(anomalyId)) ||
                     (n.ManagedAnomalies != null && n.ManagedAnomalies.Any(m => m != null && m.AnomalyId == anomalyId)) ||
                     (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Contains(anomalyId))));
                if (alreadySpawned)
                    continue;

                var node = nodes[rng.Next(nodes.Count)];
                if (node == null) continue;

                if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Contains(anomalyId))
                    continue;

                EnsureActiveAnomaly(s, node, anomalyId, registry);
                GetOrCreateAnomalyState(s, node, anomalyId);
                spawned++;

                // Emit fact for anomaly spawn
                var anomalyDef = registry.AnomaliesById.GetValueOrDefault(anomalyId);
            }

            if (spawned < genNum)
            {
                Debug.LogWarning($"[AnomalyGen] day={day} requested={genNum} spawned={spawned} attempts={attempts}");
            }
            else
            {
                Debug.Log($"[AnomalyGen] day={day} spawned={spawned}");
            }

            return spawned;
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
