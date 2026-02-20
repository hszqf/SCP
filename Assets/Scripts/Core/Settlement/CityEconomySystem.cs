using Core;
using Data;
using System;
using System.Linq;
using UnityEngine;

namespace Settlement
{
    public static class CityEconomySystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayPipelineResult r, Core.IDayEventSink sink = null)
        {
            Debug.Log("[Settlement] CityEconomySystem.Apply called");

            if (state?.Cities == null) return;

            int total = 0;
            var registry = DataRegistry.Instance;
            float popToMoneyRate = registry.GetBalanceFloatWithWarn("PopToMoneyRate", 0f);


            // M6: deterministic ordering for visuals (type=1, cityId asc)
            var list = state.Cities
                .Where(c => c != null && c.Type == 1 && !string.IsNullOrEmpty(c.Id))
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];

                int pop = Math.Max(0, c.Population);
                int delta = (int)(pop * popToMoneyRate);
                if (delta <= 0) continue;

                state.Money += delta;
                total += delta;

                // M6: per-city money burst event for playback visuals.
                sink?.Add(Core.DayEvent.MoneyBurst(c.Id, c.MapPos, delta));
            }

            r?.Log($"[Settle][CityEco] +MoneyTotal={total} money={state.Money}");


        }
    }
}
