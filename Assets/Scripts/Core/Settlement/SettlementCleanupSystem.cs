using System;
using UnityEngine;
using Core;
using System.Linq;

namespace Settlement
{
    public static class SettlementCleanupSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayEndResult r)
        {
            // TODO
            Debug.Log("[Settlement] SettlementCleanupSystem.Apply called");

            if (state == null || state.Anomalies == null) return;


        }
    }
}
