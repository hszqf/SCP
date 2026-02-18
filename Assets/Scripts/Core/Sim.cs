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
            if (registry == null || rng == null) return null;

            // Stable ordering => deterministic with same seed
            var all = registry.AnomaliesById.Keys
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (all.Count == 0) return null;
            return all[rng.Next(all.Count)];
        }

        // ===== BEGIN M2: GenerateScheduledAnomalies (Type==1 only) FULL =====
        public static int GenerateScheduledAnomalies(GameState s, System.Random rng, DataRegistry registry, int day)
        {
            if (s == null || rng == null || registry == null) return 0;

            int genNum = registry.GetAnomaliesGenNumForDay(day);
            if (genNum <= 0) return 0;

            // --- 城市候选：只从 Type==1 且 Unlocked 中选（严格：没有就不生成） ---
            var nodes = s.Cities?
                .Where(n => n != null && n.Unlocked && n.Type == 1)
                .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase) // 稳定排序，保证同 seed 可复现
                .ToList();

            if (nodes == null || nodes.Count == 0)
            {
                Debug.LogWarning($"[AnomalyGen] day={day} no unlocked cities with Type==1; spawn skipped.");
                return 0;
            }

            int spawned = 0;
            int maxAttempts = Math.Max(10, genNum * 6); // 增加一点尝试次数，避免去重后刷不满
            int attempts = 0;

            while (spawned < genNum && attempts < maxAttempts)
            {
                attempts++;

                var anomalyDefId = PickRandomAnomalyId(registry, rng);
                if (string.IsNullOrEmpty(anomalyDefId)) break;

                // 去重：已在场/已管理/已知晓 的异常不重复生成
                if (IsAnomalyAlreadyPresent(s, anomalyDefId))
                    continue;

                var node = nodes[rng.Next(nodes.Count)];
                if (node == null) continue;

                // ✅ 唯一真相：state.Anomalies（EnsureActiveAnomaly 内部会写 NodeId/SpawnSeq 等）
                EnsureActiveAnomaly(s, node, anomalyDefId, registry);

                spawned++;
            }

            if (spawned < genNum)
                Debug.LogWarning($"[AnomalyGen] day={day} requested={genNum} spawned={spawned} attempts={attempts}");
            else
                Debug.Log($"[AnomalyGen] day={day} spawned={spawned}");

            return spawned;
        }

        private static bool IsAnomalyAlreadyPresent(GameState s, string anomalyDefId)
        {
            if (s == null || string.IsNullOrEmpty(anomalyDefId)) return false;

            // Active anomalies
            if (s.Anomalies != null && s.Anomalies.Any(a =>
                    a != null && string.Equals(a.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Cities: managed / known
            if (s.Cities != null && s.Cities.Any(n => n != null &&
                    ((n.ManagedAnomalies != null && n.ManagedAnomalies.Any(m =>
                          m != null && string.Equals(m.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                     || (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Any(id =>
                          string.Equals(id, anomalyDefId, StringComparison.OrdinalIgnoreCase))))))
                return true;

            return false;
        }
        // ===== END M2: GenerateScheduledAnomalies (Type==1 only) FULL =====

    }
}
