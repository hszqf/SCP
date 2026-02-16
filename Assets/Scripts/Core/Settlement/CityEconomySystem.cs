using Core;
using Data;
using System;
using UnityEngine;

namespace Settlement
{
    public static class CityEconomySystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            Debug.Log("[Settlement] CityEconomySystem.Apply called");

            if (state?.Cities == null) return;

            int total = 0;
            var registry = DataRegistry.Instance;
            float popToMoneyRate = registry.GetBalanceFloatWithWarn("PopToMoneyRate", 0f);


            for (int i = 0; i < state.Cities.Count; i++)
            {
                var c = state.Cities[i];
                if (c == null) continue;

                int pop = Math.Max(0, c.Population);
                int delta = (int)(pop * popToMoneyRate); // TODO: later replace with data-driven formula
                if (delta <= 0) continue;

                state.Money += delta;
                total += delta;

                // 可选：只打少量，避免刷屏
                // r?.Log($"[Settle][CityEco] city={c.Id} pop={pop} +Money={delta} money={state.Money}");
            }

            r?.Log($"[Settle][CityEco] +MoneyTotal={total} money={state.Money}");


        }
    }
}
