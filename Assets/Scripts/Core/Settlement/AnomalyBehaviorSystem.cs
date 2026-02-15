using System;
using System.Linq;
using UnityEngine;
using Core;
using Data;

namespace Settlement
{
    public static class AnomalyBehaviorSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            if (state == null)
                return;

            var anomalies = state.Anomalies;
            var cities = state.Cities;
            if (anomalies == null || anomalies.Count == 0 || cities == null || cities.Count == 0)
                return;

            // Iterate anomalies by SpawnSeq ascending
            foreach (var a in anomalies.OrderBy(x => x.SpawnSeq))
            {
                if (a == null) continue;

                var anomPos = a.Position;
                float radius = GetAnomalyRadius(state, a);
                if (radius <= 0f)
                {
                    Debug.LogWarning($"[Settle][AnomBehavior] anomaly radius missing for anom={a.Id} def={a.AnomalyDefId}");
                    r?.Log($"[Settle][AnomBehavior] anom={a.Id} missing radius");
                    continue;
                }

                // Check all cities for being within radius
                foreach (var city in cities)
                {
                    if (city == null) continue;

                    Vector2 cityPos = Vector2.zero;
                    if (city.Location != null && city.Location.Length >= 2)
                    {
                        cityPos = new Vector2(city.Location[0], city.Location[1]);
                    }
                    else
                    {
                        cityPos = new Vector2(city.X, city.Y);
                    }

                    if (Vector2.Distance(anomPos, cityPos) <= radius)
                    {
                        int deltaPop = 1; // placeholder for future formula
                        r?.Log($"[Settle][AnomBehavior] anom={a.Id} hitCity={city.Id} deltaPop={deltaPop} radius={radius}");
                    }
                }
            }
        }

        private static float GetAnomalyRadius(Core.GameState state, Core.AnomalyState a)
        {
            if (a == null) return 0f;

            var registry = DataRegistry.Instance;
            if (registry != null && !string.IsNullOrEmpty(a.AnomalyDefId) && registry.AnomaliesById != null)
            {
                if (registry.AnomaliesById.TryGetValue(a.AnomalyDefId, out var def) && def != null)
                    return Mathf.Max(0f, def.range);

                // fallback: try registry helper
                try
                {
                    float val = registry.GetAnomalyFloatWithWarn(a.AnomalyDefId, "range", 0f);
                    return Mathf.Max(0f, val);
                }
                catch
                {
                    // ignore and warn below
                }
            }

            Debug.LogWarning($"[Settle][AnomBehavior] can't resolve anomaly radius for anom={a.Id} defId={a.AnomalyDefId}");
            return 0f;
        }
    }
}
