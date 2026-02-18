using Core;
using Data;
using System;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    public static class AnomalyBehaviorSystem
    {
        // ===== BEGIN FIX M2: Anomaly-centric range (Apply FULL) =====
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            if (state == null) return;

            var anomalies = state.Anomalies;
            var cities = state.Cities;
            if (anomalies == null || anomalies.Count == 0 || cities == null || cities.Count == 0)
                return;

            foreach (var a in anomalies.OrderBy(x => x.SpawnSeq))
            {
                if (a == null) continue;

                float range = GetAnomalyRange(a); // >=0；0 表示仅影响“离异常最近的城市”

                // ✅ anomaly-centric：以异常 MapPos 为中心
                var originPos = ResolveAnomalyPos(state, a, out var originHint);
                if (!IsValidMapPos(originPos))
                {
                    r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} originPos=INVALID hint={originHint} skipped");
                    continue;
                }

                CityState range0City = null;
                if (range <= 0f)
                {
                    // range<=0：只影响离异常最近的城市（异常才是锚点）
                    range0City = FindNearestCity(cities, originPos);
                    if (range0City == null)
                    {
                        r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} range=0 nearestCity=NULL skipped");
                        continue;
                    }
                }

                int hitCount = 0;

                foreach (var city in cities)
                {
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
        }
        // ===== END FIX M2: Anomaly-centric range (Apply FULL) =====

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

            // fallback：如果异常还没写 MapPos，就用 NodeId 城市的 MapPos（不作为“锚点城市”，只是兜底给异常定位）
            hint = "fallback:city.MapPos";
            if (state != null && a != null && !string.IsNullOrEmpty(a.NodeId))
            {
                state.EnsureIndex();
                var c = state.Index.GetCity(a.NodeId);
                if (c != null && IsValidMapPos(c.MapPos))
                {
                    a.MapPos = c.MapPos; // 写回，避免下次仍然缺失
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


        // ===== BEGIN M2 MapPos (ResolveCityPos) =====
        private static Vector2 ResolveCityPos(CityState c)
        {
            if (c == null) return Vector2.zero;
            // M2: Settlement distance uses ONLY MapPos (MapRoot-local).
            return c.MapPos;
        }
        // ===== END M2 MapPos (ResolveCityPos) =====

        private static float GetAnomalyRange(Core.AnomalyState a)
        {
            if (a == null) return 0f;

            var registry = DataRegistry.Instance;
            if (registry == null || string.IsNullOrEmpty(a.AnomalyDefId)) return 0f;

            // 统一口径：只认表字段 range
            float val = registry.GetAnomalyFloatWithWarn(a.AnomalyDefId, "range", 0f);
            return Mathf.Max(0f, val);
        }
    }
}
