// Canvas-maintained file: Core/Sim (v4 - data driven events)
// Source: Assets/Scripts/Core/Sim.cs
// Goal: Load all game data from DataRegistry and drive events/effects via config.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using UnityEngine;
//using Random = System.Random;

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










        private static void EnsureActiveAnomaly(GameState state, CityState node, string anomalyDefId, DataRegistry registry)
        {
            if (state == null || node == null || string.IsNullOrEmpty(anomalyDefId) || registry == null) return;



            // 真实实例真相：只在 state.Anomalies 里
            var anomalyState = GetOrCreateAnomalyState(state, node, anomalyDefId);

            // 目前版本仍用 NodeId 作为“生成锚点城市”（不是包含关系真相）
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
        private static string PickRandomAnomalyId(DataRegistry registry, System.Random rng)
        {
            var all = registry.AnomaliesById.Keys.ToList();
            if (all.Count == 0) return null;
            int idx = rng.Next(all.Count);
            return all[idx];
        }

        public static int GenerateScheduledAnomalies(GameState s, System.Random rng, DataRegistry registry, int day)
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

                var anomalyDefId = PickRandomAnomalyId(registry, rng);
                if (string.IsNullOrEmpty(anomalyDefId)) break;

                // ✅ 新重复规则：以 state.Anomalies 为真相（实例已存在就不再生成）
                bool alreadySpawned =
                    (s.Anomalies != null && s.Anomalies.Any(a =>
                        a != null && string.Equals(a.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                    || (s.Cities != null && s.Cities.Any(n =>
                        n != null &&
                        ((n.ManagedAnomalies != null && n.ManagedAnomalies.Any(m => m != null &&
                            string.Equals(m.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                         || (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Contains(anomalyDefId)))));

                if (alreadySpawned)
                    continue;

                var node = nodes[rng.Next(nodes.Count)];
                if (node == null) continue;

                // ✅ 不再读/写 node.ActiveAnomalyIds
                EnsureActiveAnomaly(s, node, anomalyDefId, registry);
                GetOrCreateAnomalyState(s, node, anomalyDefId);

                spawned++;

                // Emit fact for anomaly spawn (保留你原来的占位行为)
                var anomalyDef = registry.AnomaliesById.GetValueOrDefault(anomalyDefId);
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


     
   

      

    }
}
