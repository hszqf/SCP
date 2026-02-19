using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Data;
using Settlement;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// M5 v0: Build DayResolutionPlan + DayCommitPatch using a shadow-state simulation.
    /// - Real state is NOT mutated during playback.
    /// - No MovementToken / Guid.NewGuid usage.
    /// </summary>
    public static class DayEndPlanBuilder
    {
        private const float DefaultFocusZoom = 1.15f;
        private const float DefaultFocusDuration = 0.6f;

        public static DayResolutionPlan Build(GameState live, System.Random rng, DataRegistry registry)
        {
            if (live == null) return null;
            if (registry == null) registry = DataRegistry.Instance;

            var shadow = DeepCopy(live);
            if (shadow == null) return null;

            // Plan mode: never use token/lock.
            shadow.MovementTokens = new List<MovementToken>();
            shadow.MovementLockCount = 0;

            var plan = new DayResolutionPlan
            {
                Day = live.Day,
                Events = new List<DayEvent>(),
                Patch = new DayCommitPatch()
            };

            var anomalies = shadow.Anomalies?.Where(a => a != null).OrderBy(a => a.SpawnSeq).ToList() ?? new List<AnomalyState>();

            int moneyBefore = shadow.Money;
            float panicBefore = shadow.WorldPanic;
            int neBefore = shadow.NegEntropy;

            // Focus -> Work -> RangeAttack/PopLoss -> EndOfAnomaly
            for (int i = 0; i < anomalies.Count; i++)
            {
                var a = anomalies[i];
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;

                plan.Events.Add(DayEvent.Focus(a.Id, a.MapPos, DefaultFocusZoom, DefaultFocusDuration));

                ApplyWork_ForOne(plan, shadow, a, registry);
                ApplyBehavior_ForOne(plan, shadow, a, registry);

                plan.Events.Add(DayEvent.EndAnomaly(a.Id));
            }

            // Keep original order semantics:
            // CityEconomy -> BaseRecovery -> Cleanup -> PhaseCompletionRecall
            Settlement.CityEconomySystem.Apply(null, shadow, null);
            Settlement.BaseRecoverySystem.Apply(null, shadow, null);
            Settlement.SettlementCleanupSystem.Apply(null, shadow, null);

            ApplyPhaseCompletionRecall_Plan(plan, shadow);

            int moneyAfter = shadow.Money;
            float panicAfter = shadow.WorldPanic;
            int neAfter = shadow.NegEntropy;

            int dMoney = moneyAfter - moneyBefore;
            float dPanic = panicAfter - panicBefore;
            int dNe = neAfter - neBefore;
            if (dMoney != 0 || Math.Abs(dPanic) > 0.0001f || dNe != 0)
                plan.Events.Add(DayEvent.Resource(dMoney, dPanic, dNe));

            plan.Events.Add(DayEvent.EndDay());

            FillPatchFromShadow(plan.Patch, shadow);

            Debug.Log($"[M5][Plan] day={plan.Day} events={plan.Events.Count} hash={ComputeEventsHash(plan.Events)}");
            return plan;
        }

        private static void ApplyWork_ForOne(DayResolutionPlan plan, GameState s, AnomalyState anom, DataRegistry registry)
{
    if (plan == null || s == null || anom == null) return;

    // Delegate to the single source-of-truth settlement system so damage/dead/insane/progress are all computed here.
    var sink = new ListDayEventSink(plan.Events);
    Settlement.AnomalyWorkSystem.ApplyForAnomaly(s, anom, null, sink, registry);
}


        private static void ApplyBehavior_ForOne(DayResolutionPlan plan, GameState s, AnomalyState a, DataRegistry registry)
        {
            if (s.Cities == null || s.Cities.Count == 0) return;
            if (a == null) return;

            float range = GetAnomalyRange(a, registry);
            var originPos = ResolveAnomalyPos_Settlement(s, a);
            if (!IsValidMapPos(originPos)) return;

            CityState range0City = null;
            if (range <= 0f)
            {
                range0City = FindNearestCity(s.Cities, originPos);
                if (range0City == null) return;
            }

            bool anyHit = false;
            for (int i = 0; i < s.Cities.Count; i++)
            {
                var c = s.Cities[i];
                if (c == null) continue;

                var cityPos = c.MapPos;
                if (!IsValidMapPos(cityPos)) continue;

                float dist = Vector2.Distance(originPos, cityPos);
                bool hit = (range <= 0f)
                    ? string.Equals(c.Id, range0City.Id, StringComparison.OrdinalIgnoreCase)
                    : (dist <= range);

                if (!hit) continue;

                int loss = SettlementUtil.CalcAnomalyCityPopDelta(s, a, c);
                if (loss <= 0) continue;

                anyHit = true;
                break;
            }

            if (!anyHit) return;

            plan.Events.Add(DayEvent.RangeAttack(a.Id, originPos, range));

            for (int i = 0; i < s.Cities.Count; i++)
            {
                var c = s.Cities[i];
                if (c == null) continue;

                var cityPos = c.MapPos;
                if (!IsValidMapPos(cityPos)) continue;

                float dist = Vector2.Distance(originPos, cityPos);
                bool hit = (range <= 0f)
                    ? string.Equals(c.Id, range0City.Id, StringComparison.OrdinalIgnoreCase)
                    : (dist <= range);

                if (!hit) continue;

                int loss = SettlementUtil.CalcAnomalyCityPopDelta(s, a, c);
                if (loss <= 0) continue;

                int before = c.Population;
                int after = Math.Max(0, before - loss);
                c.Population = after;

                plan.Events.Add(new DayEvent
                {
                    Type = DayEventType.CityPopLoss,
                    AnomalyId = a.Id,
                    CityId = c.Id,
                    BeforePop = before,
                    Loss = loss,
                    AfterPop = after,
                    Dist = dist,
                    Range = range
                });
            }
        }

        private static void ApplyPhaseCompletionRecall_Plan(DayResolutionPlan plan, GameState s)
        {
            if (s == null || s.Anomalies == null) return;

            var anomalies = s.Anomalies.Where(a => a != null).OrderBy(a => a.SpawnSeq);
            foreach (var a in anomalies)
            {
                if (string.IsNullOrEmpty(a.Id)) continue;

                if (a.Phase == AnomalyPhase.Investigate && a.InvestigateProgress >= 1f && a.InvestigatorIds != null && a.InvestigatorIds.Count > 0)
                {
                    var recalled = a.InvestigatorIds.Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                    RecallRosterImmediate(s, a, AssignmentSlot.Investigate);

                    plan.Events.Add(DayEvent.Recall(a.Id, AssignmentSlot.Investigate, recalled));
                    plan.Events.Add(DayEvent.PhaseAdv(a.Id, AnomalyPhase.Investigate, AnomalyPhase.Contain));

                    a.Phase = AnomalyPhase.Contain;
                }

                if (a.Phase == AnomalyPhase.Contain && a.ContainProgress >= 1f && a.ContainmentIds != null && a.ContainmentIds.Count > 0)
                {
                    var recalled = a.ContainmentIds.Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                    RecallRosterImmediate(s, a, AssignmentSlot.Contain);

                    plan.Events.Add(DayEvent.Recall(a.Id, AssignmentSlot.Contain, recalled));
                    plan.Events.Add(DayEvent.PhaseAdv(a.Id, AnomalyPhase.Contain, AnomalyPhase.Operate));

                    a.Phase = AnomalyPhase.Operate;
                }
            }
        }

        private static void RecallRosterImmediate(GameState s, AnomalyState a, AssignmentSlot slot)
        {
            var roster = a.GetRoster(slot);
            if (roster == null || roster.Count == 0) return;

            var old = new List<string>(roster);
            roster.Clear();

            if (s.Agents == null) return;

            for (int i = 0; i < s.Agents.Count; i++)
            {
                var ag = s.Agents[i];
                if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;
                if (!old.Contains(ag.Id)) continue;

                if ((ag.LocationKind == AgentLocationKind.AtAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToAnomaly ||
                     ag.LocationKind == AgentLocationKind.TravellingToBase) &&
                    ag.LocationAnomalyInstanceId == a.Id &&
                    ag.LocationSlot == slot)
                {
                    ag.LocationKind = AgentLocationKind.Base;
                    ag.LocationAnomalyInstanceId = null;
                }
            }
        }

        private static void FillPatchFromShadow(DayCommitPatch patch, GameState shadow)
        {
            if (patch == null || shadow == null) return;

            patch.MoneyAfter = shadow.Money;
            patch.WorldPanicAfter = shadow.WorldPanic;
            patch.NegEntropyAfter = shadow.NegEntropy;

            patch.CityPopulationAfter.Clear();
            if (shadow.Cities != null)
            {
                for (int i = 0; i < shadow.Cities.Count; i++)
                {
                    var c = shadow.Cities[i];
                    if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                    patch.CityPopulationAfter[c.Id] = c.Population;
                }
            }

            patch.AgentsAfter.Clear();
            if (shadow.Agents != null)
            {
                for (int i = 0; i < shadow.Agents.Count; i++)
                {
                    var a = shadow.Agents[i];
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;

                    patch.AgentsAfter[a.Id] = new AgentAfter
                    {
                        IsDead = a.IsDead,
                        IsInsane = a.IsInsane,
                        HP = a.HP,
                        SAN = a.SAN,
                        Exp = a.Exp,
                        Level = a.Level,
                        LocationKind = a.LocationKind,
                        LocationAnomalyInstanceId = a.LocationAnomalyInstanceId,
                        LocationSlot = a.LocationSlot
                    };
                }
            }

            patch.AnomaliesAfter.Clear();
            if (shadow.Anomalies != null)
            {
                for (int i = 0; i < shadow.Anomalies.Count; i++)
                {
                    var a = shadow.Anomalies[i];
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;

                    patch.AnomaliesAfter[a.Id] = new AnomalyAfter
                    {
                        Phase = a.Phase,
                        InvestigateProgress = a.InvestigateProgress,
                        ContainProgress = a.ContainProgress,
                        InvestigatorIds = a.InvestigatorIds != null ? new List<string>(a.InvestigatorIds) : new List<string>(),
                        ContainmentIds = a.ContainmentIds != null ? new List<string>(a.ContainmentIds) : new List<string>(),
                        OperateIds = a.OperateIds != null ? new List<string>(a.OperateIds) : new List<string>(),
                    };
                }
            }
        }

        private static GameState DeepCopy(GameState s)
        {
            var json = JsonUtility.ToJson(s);
            return JsonUtility.FromJson<GameState>(json);
        }

        private static string ComputeEventsHash(List<DayEvent> events)
        {
            if (events == null || events.Count == 0) return "<empty>";

            var sb = new StringBuilder(4096);
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                sb.Append((int)e.Type).Append('|');
                sb.Append(e.AnomalyId).Append('|');
                sb.Append(e.CityId).Append('|');
                sb.Append(e.AgentId).Append('|');
                sb.Append((int)e.Slot).Append('|');
                sb.Append((int)e.Phase).Append('|');
                sb.Append(e.Before01.ToString("0.###")).Append('|');
                sb.Append(e.Delta01.ToString("0.###")).Append('|');
                sb.Append(e.After01.ToString("0.###")).Append('|');
                sb.Append(e.BeforePop).Append('|');
                sb.Append(e.Loss).Append('|');
                sb.Append(e.AfterPop).Append('|');
                sb.Append(e.Dist.ToString("0.###")).Append('|');
                sb.Append(e.Range.ToString("0.###")).Append('|');
                sb.Append(e.MoneyDelta).Append('|');
                sb.Append(e.PanicDelta.ToString("0.###")).Append('|');
                sb.Append(e.NegEntropyDelta).Append('|');
                sb.Append((int)e.FromPhase).Append('|');
                sb.Append((int)e.ToPhase).Append('|');
                if (e.AgentIds != null && e.AgentIds.Length > 0)
                {
                    for (int k = 0; k < e.AgentIds.Length; k++) sb.Append(e.AgentIds[k]).Append(',');
                }
                sb.Append('\n');
            }

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsValidMapPos(Vector2 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y));

        private static Vector2 ResolveAnomalyPos_Settlement(GameState state, AnomalyState a)
        {
            if (a != null && IsValidMapPos(a.MapPos))
                return a.MapPos;

            if (state != null && a != null && !string.IsNullOrEmpty(a.NodeId))
            {
                state.EnsureIndex();
                var c = state.Index.GetCity(a.NodeId);
                if (c != null && IsValidMapPos(c.MapPos))
                {
                    a.MapPos = c.MapPos;
                    return a.MapPos;
                }
            }

            return new Vector2(float.NaN, float.NaN);
        }

        private static CityState FindNearestCity(List<CityState> cities, Vector2 pos)
        {
            CityState best = null;
            float bestSqr = float.PositiveInfinity;

            foreach (var c in cities)
            {
                if (c == null) continue;
                var p = c.MapPos;
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

        private static float GetAnomalyRange(AnomalyState a, DataRegistry registry)
        {
            if (a == null) return 0f;
            if (registry == null) registry = DataRegistry.Instance;
            if (registry == null || string.IsNullOrEmpty(a.AnomalyDefId)) return 0f;

            float val = registry.GetAnomalyFloatWithWarn(a.AnomalyDefId, "range", 0f);
            return Mathf.Max(0f, val);
        }
    }
}
