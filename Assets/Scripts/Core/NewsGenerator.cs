using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using UnityEngine;

namespace Core
{
    public static class NewsGenerator
    {
        public static void EnsureBootstrapNews(GameState state, DataRegistry data)
        {
            if (state == null || data == null) return;

            int day = state.Day;
            if (day != 1) return;

            state.NewsLog ??= new List<NewsInstance>();
            var existing = new HashSet<string>(state.NewsLog.Where(n => n != null).Select(n => n.NewsDefId), StringComparer.OrdinalIgnoreCase);

            var defs = data.NewsDefs ?? new List<NewsDef>();
            var bootstrapDefs = defs
                .Where(def => def != null &&
                              string.Equals(def.source, "Bootstrap", StringComparison.OrdinalIgnoreCase) &&
                              (def.minDay <= 0 || day >= def.minDay) &&
                              (def.maxDay <= 0 || day <= def.maxDay))
                .ToList();

            if (bootstrapDefs.Count == 0) return;

            int created = 0;
            foreach (var def in bootstrapDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.newsDefId)) continue;
                if (existing.Contains(def.newsDefId)) continue;

                string nodeId = ResolveBootstrapNodeId(state, def.requiresNodeId);
                if (string.Equals(def.requiresNodeId, "START", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[NewsGen] bind START newsDefId={def.newsDefId} nodeId={nodeId}");
                }

                var instance = NewsInstanceFactory.Create(def.newsDefId, nodeId, null, "Bootstrap");
                state.NewsLog.Add(instance);
                existing.Add(def.newsDefId);
                created += 1;
            }

            Debug.Log($"[NewsGen] bootstrap day=1 created={created}");
        }

        private static string ResolveBootstrapNodeId(GameState state, string requiresNodeId)
        {
            if (state?.Nodes == null || state.Nodes.Count == 0) return null;

            if (string.IsNullOrEmpty(requiresNodeId) || string.Equals(requiresNodeId, "ANY", StringComparison.OrdinalIgnoreCase))
            {
                return state.Nodes[0].Id;
            }

            if (string.Equals(requiresNodeId, "START", StringComparison.OrdinalIgnoreCase))
            {
                return state.Nodes[0].Id;
            }

            return requiresNodeId;
        }
    }
}
