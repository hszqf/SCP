using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using UnityEngine;

namespace Settlement
{
    public static class AnomalyWorkSystem
    {
        public static void Apply(GameController gc, Core.GameState state, DayPipelineResult r)
        {
            if (state == null || state.Anomalies == null) return;

            foreach (var anom in state.Anomalies.Where(a => a != null).OrderBy(a => a.SpawnSeq))
            {
                ApplyForAnomaly(state, anom, r, null, DataRegistry.Instance);
            }
        }

        /// <summary>
        /// v1-ish: per-agent matching rule (replaces old roster-based progress).
        /// - Each arrived agent contributes progress based on how many required attributes they meet.
        /// - Damage applied per agent using anomaly stage damage fields (ceil).
        /// - Emits AgentCheck / ProgressDelta / AgentKilled / AgentInsane.
        /// </summary>
        public static void ApplyForAnomaly(Core.GameState state, Core.AnomalyState anom, DayPipelineResult r, Core.IDayEventSink sink, DataRegistry registry)
        {
            if (state == null || anom == null) return;
            if (registry == null) registry = DataRegistry.Instance;

            registry.AnomaliesById.TryGetValue(anom.AnomalyDefId ?? string.Empty, out var def);
            if (def == null)
            {
                // still allow operate NE from roster
                def = null;
            }

            var key = anom.Id;

            var invIds = anom.GetRoster(AssignmentSlot.Investigate);
            var conIds = anom.GetRoster(AssignmentSlot.Contain);
            var opIds  = anom.GetRoster(AssignmentSlot.Operate);

            var invArrived = SettlementUtil.CollectArrived(state, key, invIds);
            var conArrived = SettlementUtil.CollectArrived(state, key, conIds);
            var opArrived  = SettlementUtil.CollectArrived(state, key, opIds);

            // stable order: by agentId
            invArrived = invArrived.Where(a => a != null && !string.IsNullOrEmpty(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
            conArrived = conArrived.Where(a => a != null && !string.IsNullOrEmpty(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
            opArrived  = opArrived .Where(a => a != null && !string.IsNullOrEmpty(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();

            switch (anom.Phase)
            {
                case AnomalyPhase.Investigate:
                    ApplyPhaseWork(state, anom, def, AssignmentSlot.Investigate, invArrived, sink);
                    break;

                case AnomalyPhase.Contain:
                    ApplyPhaseWork(state, anom, def, AssignmentSlot.Contain, conArrived, sink);
                    break;

                case AnomalyPhase.Operate:
                    // Operate: matching causes damage; NE still uses existing roster rule for now.
                    ApplyPhaseWork(state, anom, def, AssignmentSlot.Operate, opArrived, sink);

                    int dNE = SettlementUtil.CalcNegEntropyDelta_FromRoster(state, anom, opArrived, registry);
                    if (dNE > 0) state.NegEntropy += dNE;
                    break;
            }
        }

        private static void ApplyPhaseWork(Core.GameState state, Core.AnomalyState anom, AnomalyDef def, AssignmentSlot slot, List<Core.AgentState> arrived, Core.IDayEventSink sink)
        {
            if (state == null || anom == null || arrived == null || arrived.Count == 0) return;

            int baseHpDmg = 0;
            int baseSanDmg = 0;
            int[] req = null;

            if (def != null)
            {
                if (slot == AssignmentSlot.Investigate)
                {
                    baseHpDmg = def.invhpDmg;
                    baseSanDmg = def.invsanDmg;
                    req = def.invReq;
                }
                else if (slot == AssignmentSlot.Contain)
                {
                    baseHpDmg = def.conhpDmg;
                    baseSanDmg = def.consanDmg;
                    req = def.conReq;
                }
                else
                {
                    baseHpDmg = def.manhpDmg;
                    baseSanDmg = def.mansanDmg;
                    req = def.manReq;
                }
            }

            // ensure req length
            if (req == null || req.Length < 4) req = new int[4];

            for (int i = 0; i < arrived.Count; i++)
            {
                var ag = arrived[i];
                if (ag == null) continue;
                if (ag.IsDead) continue; // dead occupies slot but does not participate

                bool isInsane = ag.IsInsane; // insane occupies slot and keeps taking damage, but never advances progress

                int match = CalcMatchCount(ag, req);
                float add01 = isInsane ? 0f : CalcProgressAdd01(match);
                float dmgMul = CalcDamageMultiplier(match);

                // Emit check (roll=matchCount, dc=4) - deterministic explanation
                sink?.Add(Core.DayEvent.Check(
                    anomalyId: anom.Id,
                    agentId: ag.Id,
                    slot: slot,
                    roll: match,
                    dc: 4,
                    success: match >= 4,
                    reasonKey: $"match_{match}"
                ));

                // Progress (Investigate/Contain only)
                if (slot == AssignmentSlot.Investigate)
                {
                    float before = anom.InvestigateProgress;
                    float after = Mathf.Clamp01(before + add01);
                    anom.InvestigateProgress = after;
                    if (sink != null && Math.Abs(add01) > 0.0001f)
                        sink.Add(Core.DayEvent.ProgressDelta(anom.Id, anom.Phase, before, add01, after, ag.Id));
                }
                else if (slot == AssignmentSlot.Contain)
                {
                    float before = anom.ContainProgress;
                    float after = Mathf.Clamp01(before + add01);
                    anom.ContainProgress = after;
                    if (sink != null && Math.Abs(add01) > 0.0001f)
                        sink.Add(Core.DayEvent.ProgressDelta(anom.Id, anom.Phase, before, add01, after, ag.Id));
                }

                
// Damage
int hpDmgBase = CeilToInt(baseHpDmg * dmgMul);
int sanDmgBase = CeilToInt(baseSanDmg * dmgMul);

// SAN damage is applied first; overflow converts to additional HP damage (ceil already applied).
int sanApplied = 0;
if (sanDmgBase > 0)
{
    int sanAvail = Math.Max(0, ag.SAN);
    sanApplied = Math.Min(sanDmgBase, sanAvail);
    ag.SAN = Math.Max(0, sanAvail - sanApplied);
}

int sanOverflowToHp = Math.Max(0, sanDmgBase - sanApplied);
int hpDmgTotal = Math.Max(0, hpDmgBase + sanOverflowToHp);

if (hpDmgTotal > 0)
    ag.HP = Math.Max(0, ag.HP - hpDmgTotal);

// State transitions (do NOT remove from roster: dead/insane must occupy slot until rescued)
if (ag.HP <= 0 && !ag.IsDead)
{
    ag.IsDead = true;
    ag.HP = 0;
    sink?.Add(Core.DayEvent.Killed(anom.Id, ag.Id, $"dmg_{slot.ToString().ToLowerInvariant()}"));
}
else if (ag.SAN <= 0 && !ag.IsInsane && !ag.IsDead)
{
    ag.IsInsane = true;
    ag.SAN = 0;
    sink?.Add(Core.DayEvent.Insane(anom.Id, ag.Id, $"dmg_{slot.ToString().ToLowerInvariant()}"));
}
}
        }

        private static void RemoveAgentFromAllRosters(Core.AnomalyState anom, string agentId)
        {
            if (anom == null || string.IsNullOrEmpty(agentId)) return;
            anom.InvestigatorIds?.RemoveAll(x => x == agentId);
            anom.ContainmentIds?.RemoveAll(x => x == agentId);
            anom.OperateIds?.RemoveAll(x => x == agentId);
        }

        private static int CalcMatchCount(Core.AgentState ag, int[] req)
        {
            int m = 0;
            // order: Perception / Operation / Resistance / Power
            if (ag.Perception >= req[0]) m++;
            if (ag.Operation >= req[1]) m++;
            if (ag.Resistance >= req[2]) m++;
            if (ag.Power >= req[3]) m++;
            return Mathf.Clamp(m, 0, 4);
        }

        private static float CalcProgressAdd01(int match)
        {
            switch (match)
            {
                case 4: return 0.10f;
                case 3: return 0.08f;
                case 2: return 0.05f;
                case 1: return 0.02f;
                default: return 0f;
            }
        }

        private static float CalcDamageMultiplier(int match)
        {
            switch (match)
            {
                case 4: return 0f;
                case 3: return 0.25f;
                case 2: return 0.50f;
                case 1: return 0.75f;
                default: return 1f;
            }
        }

        private static int CeilToInt(float x) => (int)Math.Ceiling(x);
    }
}
