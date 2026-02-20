using Core;
using Data;
using System;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    public static class AnomalyBehaviorSystem
    {
        /// <summary>
        /// Legacy entry: applies behavior for all anomalies. Order: SpawnSeq ascending.
        /// </summary>
        public static void Apply(GameController gc, Core.GameState state, DayPipelineResult r)
        {
            if (state == null || state.Anomalies == null || state.Anomalies.Count == 0) return;

            var registry = DataRegistry.Instance;

            foreach (var a in state.Anomalies.Where(x => x != null).OrderBy(x => x.SpawnSeq))
            {
                ApplyForAnomaly(state, a, r, null, registry);
            }
        }

        /// <summary>
        /// Per-anomaly behavior pass.
        /// - Computes anomaly range impact on cities (CityPopLoss).
        /// - Emits AnomalyRangeAttack + CityPopLoss events when sink != null.
        /// - Mutates the provided state (shadow or live) directly.
        /// </summary>
        public static void ApplyForAnomaly(Core.GameState state, Core.AnomalyState a, DayPipelineResult r, Core.IDayEventSink sink, DataRegistry registry)
        {
            if (state == null || a == null) return;

            var cities = state.Cities;
            if (cities == null || cities.Count == 0) return;

            if (registry == null) registry = DataRegistry.Instance;

            float range = GetAnomalyRange(a, registry); // >=0; 0 => only nearest city

            var originPos = ResolveAnomalyPos(state, a, out var originHint);
            if (!IsValidMapPos(originPos))
            {
                r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} originPos=INVALID hint={originHint} skipped");
                return;
            }

            CityState range0City = null;
            if (range <= 0f)
            {
                range0City = FindNearestCity(cities, originPos);
                if (range0City == null)
                {
                    r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} range=0 nearestCity=NULL skipped");
                    return;
                }
            }

            // First pass: detect any effective hit (loss > 0). If none, don't emit RangeAttack.
            bool anyHit = false;
            for (int i = 0; i < cities.Count; i++)
            {
                var city = cities[i];
                if (city == null) continue;

                var cityPos = ResolveCityPos(city);
                if (!IsValidMapPos(cityPos)) continue;

                float dist = Vector2.Distance(originPos, cityPos);

                bool hit =
                    (range <= 0f)
                        ? string.Equals(city.Id, range0City.Id, StringComparison.OrdinalIgnoreCase)
                        : (dist <= range);

                if (!hit) continue;

                int loss = SettlementUtil.CalcAnomalyCityPopDelta(state, a, city);
                if (loss <= 0) continue;

                anyHit = true;
                break;
            }

            if (!anyHit)
            {
                string range0CityId0 = range0City != null ? range0City.Id : "<n/a>";
                r?.Log(
                    $"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} range={range:0.##} hitCount=0 " +
                    $"range0City={range0CityId0} nodeId={a.NodeId} origin=({originPos.x:0.##},{originPos.y:0.##}) hint={originHint}");
                return;
            }

            // Emit range attack once per anomaly (before CityPopLoss) for UI camera framing / VFX.
            sink?.Add(DayEvent.RangeAttack(a.Id, originPos, range));

            int hitCount = 0;

            for (int i = 0; i < cities.Count; i++)
            {
                var city = cities[i];
                if (city == null) continue;

                var cityPos = ResolveCityPos(city);
                if (!IsValidMapPos(cityPos)) continue;

                float dist = Vector2.Distance(originPos, cityPos);

                bool hit =
                    (range <= 0f)
                        ? string.Equals(city.Id, range0City.Id, StringComparison.OrdinalIgnoreCase)
                        : (dist <= range);

                if (!hit) continue;

                int deltaPop = SettlementUtil.CalcAnomalyCityPopDelta(state, a, city);
                if (deltaPop <= 0) continue;

                hitCount++;

                int beforePop = city.Population;
                int afterPop = Math.Max(0, beforePop - deltaPop);
                city.Population = afterPop;

                sink?.Add(new DayEvent
                {
                    Type = DayEventType.CityPopLoss,
                    AnomalyId = a.Id,
                    CityId = city.Id,
                    BeforePop = beforePop,
                    Loss = deltaPop,
                    AfterPop = afterPop,
                    Dist = dist,
                    Range = range
                });

                var cityName = string.IsNullOrEmpty(city.Name) ? city.Id : city.Name;
                r?.Log(
                    $"[Settle][AnomPopLoss] day={state.Day} city={cityName} dist={dist:0.##} range={range:0.##} " +
                    $"loss={deltaPop} before={beforePop} after={afterPop} anom={a.Id} def={a.AnomalyDefId} " +
                    $"origin=({originPos.x:0.##},{originPos.y:0.##})");
            }

            string range0CityId = range0City != null ? range0City.Id : "<n/a>";
            r?.Log(
                $"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} range={range:0.##} hitCount={hitCount} " +
                $"range0City={range0CityId} nodeId={a.NodeId} origin=({originPos.x:0.##},{originPos.y:0.##}) hint={originHint}");
        }

        // ===== BEGIN FIX M2: Anomaly-centric helpers =====
        private static bool IsValidMapPos(Vector2 p)
        {
            return !(float.IsNaN(p.x) || float.IsNaN(p.y));
        }

        private static Vector2 ResolveAnomalyPos(Core.GameState state, Core.AnomalyState a, out string hint)
        {
            hint = "anom.MapPos";
            if (a != null && IsValidMapPos(a.MapPos))
                return a.MapPos;

            // fallback: if anomaly has no MapPos, use its NodeId city's MapPos to place the anomaly.
            hint = "fallback:city.MapPos";
            if (state != null && a != null && !string.IsNullOrEmpty(a.NodeId))
            {
                state.EnsureIndex();
                var c = state.Index.GetCity(a.NodeId);
                if (c != null && IsValidMapPos(c.MapPos))
                {
                    a.MapPos = c.MapPos; // write back
                    return a.MapPos;
                }
            }

            hint = "INVALID";
            return new Vector2(float.NaN, float.NaN);
        }

        private static CityState FindNearestCity(System.Collections.Generic.List<CityState> cities, Vector2 pos)
        {
            CityState best = null;
            float bestSqr = float.PositiveInfinity;

            foreach (var c in cities)
            {
                if (c == null) continue;
                var p = ResolveCityPos(c);
                if (!IsValidMapPos(p)) continue;

                float sqr = (p - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = c;
                }
            }

            return best;
        }
        // ===== END FIX M2: Anomaly-centric helpers =====

        private static Vector2 ResolveCityPos(CityState c)
        {
            if (c == null) return Vector2.zero;
            return c.MapPos;
        }

        private static float GetAnomalyRange(Core.AnomalyState a, DataRegistry registry)
        {
            if (a == null) return 0f;
            if (registry == null || string.IsNullOrEmpty(a.AnomalyDefId)) return 0f;

            // Unified: only read table field "range"
            float val = registry.GetAnomalyFloatWithWarn(a.AnomalyDefId, "range", 0f);
            return Mathf.Max(0f, val);
        }
    }
}
