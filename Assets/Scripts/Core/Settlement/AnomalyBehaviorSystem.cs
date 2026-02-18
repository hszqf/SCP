using Core;
using Data;
using System;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    public static class AnomalyBehaviorSystem
    {
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

                float range = GetAnomalyRange(a); // >=0；0 表示仅影响锚点城市

                // 以 NodeId 作为“异常锚点城市”（当前版本的生成逻辑就是这么写的）
                var origin = ResolveAnchorCity(state, a);
                if (origin == null)
                {
                    r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} anchorCity=NULL nodeId={a.NodeId} skipped");
                    continue;
                }

                var originPos = ResolveCityPos(origin);

                int hitCount = 0;

                foreach (var city in cities)
                {
                    if (city == null) continue;

                    if (!IsCityInRange(origin, originPos, city, range))
                        continue;

                    int deltaPop = SettlementUtil.CalcAnomalyCityPopDelta(state, a, city);
                    if (deltaPop <= 0) continue;

                    hitCount++;

                    int beforePop = city.Population;
                    int afterPop = Math.Max(0, beforePop - deltaPop);
                    city.Population = afterPop;

                    var cityName = string.IsNullOrEmpty(city.Name) ? city.Id : city.Name;

                    // 额外打印 dist/range，方便你肉眼确认“为什么命中”
                    float dist = (city.Id == origin.Id) ? 0f : Vector2.Distance(originPos, ResolveCityPos(city));
                    r?.Log($"[Settle][AnomPopLoss] day={state.Day} city={cityName} dist={dist:0.##} range={range:0.##} loss={deltaPop} before={beforePop} after={afterPop} anom={a.Id} def={a.AnomalyDefId}");
                }

                r?.Log($"[Settle][AnomBehavior] anom={a.Id} def={a.AnomalyDefId} range={range:0.##} hitCount={hitCount} anchorCity={origin.Id}");
            }
        }

        private static CityState ResolveAnchorCity(Core.GameState state, Core.AnomalyState a)
        {
            if (state == null || a == null) return null;
            if (string.IsNullOrEmpty(a.NodeId)) return null;

            state.EnsureIndex();
            return state.Index.GetCity(a.NodeId);
        }

        private static bool IsCityInRange(CityState origin, Vector2 originPos, CityState target, float range)
        {
            if (origin == null || target == null) return false;

            // range<=0：只影响锚点城市自身
            if (range <= 0f)
                return string.Equals(origin.Id, target.Id, StringComparison.OrdinalIgnoreCase);

            // 锚点城市永远命中（避免坐标缺失时“锚点也不扣”）
            if (string.Equals(origin.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                return true;

            var targetPos = ResolveCityPos(target);

            return Vector2.Distance(originPos, targetPos) <= range;
        }

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
