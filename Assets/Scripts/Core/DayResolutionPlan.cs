using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    public enum DayEventType
    {
        FocusAnomaly,
        AgentCheck,
        AnomalyProgressDelta,
        AgentKilled,
        AnomalyRangeAttack,

        CityPopLoss,
        ResourceDelta,
        PhaseAdvanced,
        RosterRecalled,
        EndOfAnomaly,
        EndOfDay
    }

    /// <summary>
    /// v0: enum + struct payload (flat fields).
    /// UI must NOT infer rule results; all needed fields are carried here.
    /// </summary>
    [Serializable]
    public struct DayEvent
    {
        public DayEventType Type;

        public string AnomalyId;
        public Vector2 MapPos;
        public float Zoom;
        public float Duration;

        public string AgentId;
        public AssignmentSlot Slot;
        public int Roll;
        public int Dc;
        public bool Success;
        public string ReasonKey;

        public AnomalyPhase Phase;
        public float Before01;
        public float Delta01;
        public float After01;

        public Vector2 OriginPos;
        public float Range;

        public string CityId;
        public int BeforePop;
        public int Loss;
        public int AfterPop;
        public float Dist;

        public int MoneyDelta;
        public float PanicDelta;
        public int NegEntropyDelta;

        public AnomalyPhase FromPhase;
        public AnomalyPhase ToPhase;

        public string[] AgentIds;

        // --- Convenience factories ---
        public static DayEvent Focus(string anomalyId, Vector2 mapPos, float zoom, float duration)
            => new DayEvent { Type = DayEventType.FocusAnomaly, AnomalyId = anomalyId, MapPos = mapPos, Zoom = zoom, Duration = duration };

        public static DayEvent ProgressDelta(string anomalyId, AnomalyPhase phase, float before01, float delta01, float after01, string agentId = null)
            => new DayEvent
            {
                Type = DayEventType.AnomalyProgressDelta,
                AnomalyId = anomalyId,
                Phase = phase,
                Before01 = before01,
                Delta01 = delta01,
                After01 = after01,
                AgentId = agentId
            };

        public static DayEvent RangeAttack(string anomalyId, Vector2 originPos, float range)
            => new DayEvent { Type = DayEventType.AnomalyRangeAttack, AnomalyId = anomalyId, OriginPos = originPos, Range = range };

        public static DayEvent Resource(int moneyDelta, float panicDelta, int negEntropyDelta)
            => new DayEvent { Type = DayEventType.ResourceDelta, MoneyDelta = moneyDelta, PanicDelta = panicDelta, NegEntropyDelta = negEntropyDelta };

        public static DayEvent PhaseAdv(string anomalyId, AnomalyPhase from, AnomalyPhase to)
            => new DayEvent { Type = DayEventType.PhaseAdvanced, AnomalyId = anomalyId, FromPhase = from, ToPhase = to };

        public static DayEvent Recall(string anomalyId, AssignmentSlot slot, string[] agentIds)
            => new DayEvent { Type = DayEventType.RosterRecalled, AnomalyId = anomalyId, Slot = slot, AgentIds = agentIds };

        public static DayEvent EndAnomaly(string anomalyId)
            => new DayEvent { Type = DayEventType.EndOfAnomaly, AnomalyId = anomalyId };

        public static DayEvent EndDay()
            => new DayEvent { Type = DayEventType.EndOfDay };
    }

    [Serializable]
    public sealed class DayResolutionPlan
    {
        public int Day;
        public List<DayEvent> Events = new List<DayEvent>();
        public DayCommitPatch Patch;
    }
}
