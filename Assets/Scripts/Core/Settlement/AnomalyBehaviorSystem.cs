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

            if (MapEntityRegistry.I == null)
            {
                Debug.LogWarning("[Settle][AnomBehavior] MapEntityRegistry missing, skipping anomaly behavior");
                r?.Log("[Settle][AnomBehavior] MapEntityRegistry missing, skipped all anomalies");
                return;
            }

            int missingAnomPosWarns = 0;
            const int MissingAnomPosWarnLimit = 3;

            // Iterate anomalies by SpawnSeq ascending
            foreach (var a in anomalies.OrderBy(x => x.SpawnSeq))
            {
                if (a == null) continue;

                float radius = GetAnomalyRadius(state, a);
                if (radius <= 0f)
                {
                    Debug.LogWarning($"[Settle][AnomBehavior] anomaly radius missing for anom={a.Id} def={a.AnomalyDefId}");
                    r?.Log($"[Settle][AnomBehavior] anom={a.Id} missing radius");
                    continue;
                }

                // Resolve anomaly world position using canonical key only (a.Id)
                Vector3 anomPos3 = default;
                if (!MapEntityRegistry.I.TryGetAnomalyWorldPos(a.Id, out anomPos3))
                {
                    if (missingAnomPosWarns < MissingAnomPosWarnLimit)
                    {
                        Debug.LogWarning($"[Settle][AnomBehavior] can't resolve world pos for anom={a.Id} def={a.AnomalyDefId} (tried key '{a.Id}')");
                        r?.Log($"[Settle][AnomBehavior] anom={a.Id} missing worldPos");
                        missingAnomPosWarns++;
                    }
                    // skip this anomaly if we can't resolve a world position
                    continue;
                }

                var anomPos = (Vector2)anomPos3;

                int hitCount = 0;
                int skippedNotRegistered = 0;

                // Check all cities for being within radius
                foreach (var city in cities)
                {
                    if (city == null) continue;

                    var cityKey = city.Id.ToString();

                    Vector3 cityPos3;
                    if (!MapEntityRegistry.I.TryGetCityWorldPos(cityKey, out cityPos3))
                    {
                        // city not registered/visible, skip without spamming warnings
                        skippedNotRegistered++;
                        continue;
                    }

                    var cityPos = (Vector2)cityPos3;
                    float dist = Vector2.Distance(anomPos, cityPos);
                    if (dist <= radius)
                    {
                        hitCount++;
                        // still only logging; population change is deferred
                        // log city name when available
                        string cityName;
                        if (MapEntityRegistry.I.TryGetCityView(cityKey, out var cityView) && cityView != null)
                            cityName = cityView.CityName;
                        else
                            cityName = string.IsNullOrEmpty(city.Name) ? cityKey : city.Name;

                        r?.Log($"[Settle][AnomBehavior] anom={a.Id} hitCityName={cityName} dist={dist:0.#} radius={radius}");
                    }
                }

                // Per-anomaly summary log
                r?.Log($"[Settle][AnomBehavior] anom={a.Id} radius={radius} hitCount={hitCount} skippedNotRegistered={skippedNotRegistered}");
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
