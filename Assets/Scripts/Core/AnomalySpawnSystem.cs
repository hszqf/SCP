using System;
using System.Collections.Generic;
using System.Linq;
using Data;

namespace Core
{
    public static class AnomalySpawnSystem
    {
        public readonly struct AnomalySpawnReport
        {
            public readonly int Day;
            public readonly int Requested;
            public readonly int Spawned;
            public readonly int Attempts;
            public readonly string Warning;

            public AnomalySpawnReport(int day, int requested, int spawned, int attempts, string warning)
            {
                Day = day;
                Requested = requested;
                Spawned = spawned;
                Attempts = attempts;
                Warning = warning;
            }
        }

        public static AnomalySpawnReport GenerateScheduled(GameState s, System.Random rng, DataRegistry registry, int day)
        {
            if (s == null || rng == null || registry == null)
                return new AnomalySpawnReport(day, 0, 0, 0, "[AnomalyGen] invalid args (state/rng/registry null)");

            int genNum = registry.GetAnomaliesGenNumForDay(day);
            if (genNum <= 0)
                return new AnomalySpawnReport(day, 0, 0, 0, null);

            var nodes = s.Cities?
                .Where(n => n != null && n.Unlocked && n.Type == 1)
                .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (nodes == null || nodes.Count == 0)
                return new AnomalySpawnReport(day, genNum, 0, 0, $"[AnomalyGen] day={day} no unlocked cities with Type==1; spawn skipped.");

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = Math.Max(10, genNum * 6);

            while (spawned < genNum && attempts < maxAttempts)
            {
                attempts++;

                var anomalyDefId = PickRandomAnomalyId(registry, rng);
                if (string.IsNullOrEmpty(anomalyDefId)) break;

                if (IsAnomalyAlreadyPresent(s, anomalyDefId))
                    continue;

                var node = nodes[rng.Next(nodes.Count)];
                if (node == null) continue;

                EnsureActiveAnomaly(s, node, anomalyDefId);
                spawned++;
            }

            string warn = null;
            if (spawned < genNum)
                warn = $"[AnomalyGen] day={day} requested={genNum} spawned={spawned} attempts={attempts}";

            return new AnomalySpawnReport(day, genNum, spawned, attempts, warn);
        }

        private static void EnsureActiveAnomaly(GameState state, CityState node, string anomalyDefId)
        {
            if (state == null || node == null || string.IsNullOrEmpty(anomalyDefId)) return;

            var a = GetOrCreateAnomalyState(state, node, anomalyDefId);

            // 生成锚点城市（不是“包含关系真相”）
            if (a != null)
                a.NodeId = node.Id;
        }

        // ===== BEGIN M4: Deterministic anomaly instance id (GetOrCreateAnomalyState FULL) =====
        private static AnomalyState GetOrCreateAnomalyState(GameState state, CityState node, string anomalyDefId)
        {
            if (state == null || string.IsNullOrEmpty(anomalyDefId)) return null;

            state.Anomalies ??= new List<AnomalyState>();

            // Uniqueness: one active instance per anomalyDefId (current design constraint)
            var existing = state.Anomalies.FirstOrDefault(a =>
                a != null && string.Equals(a.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (node != null && string.IsNullOrEmpty(existing.NodeId))
                    existing.NodeId = node.Id;
                return existing;
            }

            // Deterministic spawn sequence key
            int seq = state.NextAnomalySpawnSeq++;
            string instanceId = BuildDeterministicAnomalyInstanceId(state.Day, anomalyDefId, seq);

            var created = new AnomalyState
            {
                Id = instanceId,
                AnomalyDefId = anomalyDefId,
                NodeId = node?.Id,
                SpawnDay = state.Day,
                SpawnSeq = seq,
                Phase = AnomalyPhase.Investigate,
                RevealLevel = 0,
            };

            state.Anomalies.Add(created);
            return created;
        }

        private static string BuildDeterministicAnomalyInstanceId(int day, string anomalyDefId, int spawnSeq)
        {
            // Example: AN_001_0003_SCP_173
            return $"AN_{day:D3}_{spawnSeq:D4}_{SanitizeKey(anomalyDefId)}";
        }

        private static string SanitizeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "NULL";
            // Keep [A-Za-z0-9_] only (stable across platforms)
            var chars = s.Select(ch =>
                (ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ? ch : '_').ToArray();
            return new string(chars);
        }
        // ===== END M4: Deterministic anomaly instance id (GetOrCreateAnomalyState FULL) =====

        private static string PickRandomAnomalyId(DataRegistry registry, System.Random rng)
        {
            if (registry?.AnomaliesById == null || rng == null) return null;

            var all = registry.AnomaliesById.Keys
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (all.Count == 0) return null;
            return all[rng.Next(all.Count)];
        }

        private static bool IsAnomalyAlreadyPresent(GameState s, string anomalyDefId)
        {
            if (s == null || string.IsNullOrEmpty(anomalyDefId)) return false;

            if (s.Anomalies != null && s.Anomalies.Any(a =>
                    a != null && string.Equals(a.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (s.Cities != null && s.Cities.Any(n => n != null &&
                    ((n.ManagedAnomalies != null && n.ManagedAnomalies.Any(m =>
                          m != null && string.Equals(m.AnomalyDefId, anomalyDefId, StringComparison.OrdinalIgnoreCase)))
                     || (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Any(id =>
                          string.Equals(id, anomalyDefId, StringComparison.OrdinalIgnoreCase))))))
                return true;

            return false;
        }
    }
}
