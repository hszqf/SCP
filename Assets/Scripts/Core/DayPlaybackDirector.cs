using Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DayPlaybackDirector : MonoBehaviour
{
    public static DayPlaybackDirector I { get; private set; }

    [Header("v0 Debug Playback")]
    [SerializeField] private float eventStepSeconds = 0.05f;

    [Header("M6 Focus")]
    [SerializeField] private float defaultFocusSeconds = 0.6f;

    [Header("M6 Playback Speed")]
    [Tooltip("Playback slow motion multiplier. 1 = normal, 2 = 2x slower.")]
    [Min(0.1f)]
    [SerializeField] private float playbackSlowMo = 4f;

    [Header("M6 Agent Feedback")]
    [SerializeField] private float agentCheckBeatSeconds = 0.35f;
    [SerializeField] private float agentPulseSeconds = 0.28f;
    [SerializeField] private float agentPulseScaleMul = 1.25f;
    [SerializeField] private float iconPulseSeconds = 0.22f;
    [SerializeField] private float iconPulseScaleMul = 1.10f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("M6 Range Impact")]

    [Tooltip("If true, CityPopLoss will not move/pan the camera (keeps current focus).")]
    [SerializeField] private bool freezeCameraDuringCityPopLoss = true;

    [SerializeField] private float cityPopLossStartDelaySeconds = 0.10f;
    [SerializeField] private float cityPopLossAnimSeconds = 0.35f;

    [Header("M6 City Money")]
    [SerializeField] private float perCityStartDelaySeconds = 0.10f;

    public bool IsPlaying => _co != null;

    private Coroutine _co;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    public void Play(DayResolutionPlan plan, Action onFinished)
    {
        if (plan == null) { onFinished?.Invoke(); return; }
        if (_co != null) return;

        _co = StartCoroutine(PlayCoroutine(plan, onFinished));
    }

    
private float ScaleSeconds(float seconds)
{
    return Mathf.Max(0f, seconds) * Mathf.Max(0.1f, playbackSlowMo);
}

private IEnumerator PlayCoroutine(Core.DayResolutionPlan plan, Action onFinished)
{
    var camDir = DayPlaybackCameraDirector.I;
    var hudRes = HUDResourceAnimator.I;

    bool finished = false;

    try
    {
        // Global lock: no interactions during playback (HUD click gated, map click gated).
        DispatchAnimationSystem.I?.SetExternalInteractionLocked(true);
        HUD.I?.SetControlsInteractable(false);

        // Init camera + HUD resource snapshot (visual-only, no GameState mutation).
        camDir?.ResetFocusState();
        camDir?.CaptureBaseIfNeeded();
        camDir?.SetPlaybackDurationScale(Mathf.Max(0.1f, playbackSlowMo));

        var s0 = GameController.I?.State;
        if (hudRes != null && s0 != null)
        {
            hudRes.SetPlaybackTimeScale(Mathf.Max(0.1f, playbackSlowMo));
            hudRes.BeginPlaybackSnapshot(s0.Money, s0.NegEntropy);
        }

        // Playback overlay: drive anomaly UI without mutating GameState.
        BuildOverlayFromState();
        ApplyOverlayToViews();

        var events = plan.Events;

        if (debugLogs && events != null)
            Debug.Log($"[M6][Play] Begin events={events.Count} day={plan.Day} slowMo={playbackSlowMo:0.##}", this);

        if (events != null)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (debugLogs)
                    Debug.Log($"[M6][Play] {Describe(e)}");

                switch (e.Type)
                {
                    case Core.DayEventType.FocusAnomaly:
                        if (camDir != null)
                        {
                            var impacted = CollectImpactCityIds(events, i, e.AnomalyId);
                            float range = FindUpcomingRange(events, i, e.AnomalyId);

                            if (debugLogs)
                                Debug.Log($"[M6][Play][Focus] idx={i} anom={e.AnomalyId} range={range:0.##} impactedCount={(impacted != null ? impacted.Length : 0)} dur={(e.Duration > 0f ? e.Duration : defaultFocusSeconds):0.##}", this);

                            yield return camDir.EnterFocusAuto(e.AnomalyId, range, impacted, e.Duration > 0f ? e.Duration : defaultFocusSeconds);
                        }
                        else
                        {
                            if (eventStepSeconds > 0f) yield return new WaitForSeconds(ScaleSeconds(eventStepSeconds));
                            else yield return null;
                        }
                        break;

                    case Core.DayEventType.AgentCheck:
                        yield return PlayAgentCheck(e);
                        break;

                    case Core.DayEventType.AgentKilled:
                    case Core.DayEventType.AgentInsane:
                        yield return PlayAgentStatusChanged(e);
                        break;

                    case Core.DayEventType.AnomalyProgressDelta:
                        yield return PlayProgressDelta(e);
                        break;

                    case Core.DayEventType.RosterRecalled:
                        yield return PlayRosterRecall(e);
                        break;

                    case Core.DayEventType.PhaseAdvanced:
                        ApplyPhaseAdvancedToOverlayAndView(e);
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(ScaleSeconds(eventStepSeconds));
                        else yield return null;
                        break;

                    case Core.DayEventType.AnomalyNegEntropyBurst:
                        PlayNegEntropyBurst(e, camDir, hudRes);
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(ScaleSeconds(eventStepSeconds));
                        else yield return null;
                        break;

                    case Core.DayEventType.AnomalyRangeAttack:
                    {
                        int end = FindRangeImpactEnd(events, i);
                        yield return PlayRangeImpactSequence(events, i, end, camDir);
                        i = end - 1;
                        break;
                    }

                    case Core.DayEventType.CityMoneyBurst:
                    {
                        int end = FindCityMoneyEnd(events, i);
                        yield return PlayCityMoneySequence(events, i, end, camDir, hudRes);
                        i = end - 1;
                        break;
                    }

                    default:
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(ScaleSeconds(eventStepSeconds));
                        else yield return null;
                        break;
                }
            }
        }

        // End camera + HUD snapshot.
        if (camDir != null)
            yield return camDir.ReturnToBase();

        hudRes?.EndPlaybackSnapshot();

        finished = true;
    }
    finally
    {
        // Always unlock, even if playback coroutine is aborted by Disable/exception.
        if (!finished && debugLogs)
            Debug.LogWarning("[M6][Play] Playback aborted. Forcing unlock.", this);

        camDir?.ForceToBaseImmediate();
        hudRes?.EndPlaybackSnapshot();

        HUD.I?.SetControlsInteractable(true);
        DispatchAnimationSystem.I?.SetExternalInteractionLocked(false);

        _co = null;

        if (debugLogs)
            Debug.Log($"[M6][Play] End finished={finished} IsPlaying={IsPlaying}", this);

        onFinished?.Invoke();
    }
}

    private void PlayNegEntropyBurst(Core.DayEvent e, DayPlaybackCameraDirector camDir, HUDResourceAnimator hudRes)
    {
        if (hudRes == null) return;

        var worldCam = camDir != null ? camDir.MapCamera : Camera.main;

        if (debugLogs)
            Debug.Log($"[M6][NE][Burst] anom={e.AnomalyId} delta={e.NegEntropyDelta} mapPos=({e.MapPos.x:0.##},{e.MapPos.y:0.##}) worldCam={(worldCam!=null?worldCam.name:"null")} registry={(MapEntityRegistry.I!=null)}", this);

        Vector3 src = new Vector3(e.MapPos.x, e.MapPos.y, 0f);
        if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetAnomalyWorldPos(e.AnomalyId, out var anomPos))
            src = anomPos;

        hudRes.PlayNegEntropyBurst(src, e.NegEntropyDelta, worldCam);
    }

    private static int FindRangeImpactEnd(List<DayEvent> events, int rangeAttackIndex)
    {
        int j = rangeAttackIndex + 1;
        string anomId = events[rangeAttackIndex].AnomalyId;
        while (j < events.Count && events[j].Type == DayEventType.CityPopLoss &&
               string.Equals(events[j].AnomalyId, anomId, StringComparison.Ordinal))
            j++;
        return j;
    }

    private static int FindCityMoneyEnd(List<DayEvent> events, int startIndex)
    {
        int j = startIndex;
        while (j < events.Count && events[j].Type == DayEventType.CityMoneyBurst)
            j++;
        return j;
    }

    private IEnumerator PlayRangeImpactSequence(List<DayEvent> events, int rangeAttackIndex, int endIndexExclusive, DayPlaybackCameraDirector camDir)
    {
        var rangeEv = events[rangeAttackIndex];
        string anomId = rangeEv.AnomalyId;

        // Collect affected cities.
        var cityIds = new List<string>(8);
        for (int j = rangeAttackIndex + 1; j < endIndexExclusive; j++)
        {
            var ce = events[j];
            if (ce.Type != DayEventType.CityPopLoss) break;
            if (!string.IsNullOrEmpty(ce.CityId) && !cityIds.Contains(ce.CityId))
                cityIds.Add(ce.CityId);
        }
        // Camera: optional. By default, keep camera fixed during CityPopLoss (avoid moving/panning).
        if (!freezeCameraDuringCityPopLoss && camDir != null && cityIds.Count > 0)
            yield return camDir.FrameRange(anomId, cityIds.ToArray());
// City FX: stagger start.
        for (int j = rangeAttackIndex + 1; j < endIndexExclusive; j++)
        {
            var ce = events[j];
            if (ce.Type != DayEventType.CityPopLoss) break;

            if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityView(ce.CityId, out var cityView) && cityView != null)
                cityView.PlayPopLossFX(ce.Loss, ce.AfterPop, cityPopLossAnimSeconds);

            yield return new WaitForSeconds(ScaleSeconds(Mathf.Max(0f, cityPopLossStartDelaySeconds)));
        }

        if (endIndexExclusive > rangeAttackIndex + 1)
            yield return new WaitForSeconds(ScaleSeconds(Mathf.Max(0.01f, cityPopLossAnimSeconds)));
        if (!freezeCameraDuringCityPopLoss && camDir != null)
            yield return camDir.ReturnToFocus(anomId);
}

    private IEnumerator PlayCityMoneySequence(List<DayEvent> events, int startIndex, int endIndexExclusive, DayPlaybackCameraDirector camDir, HUDResourceAnimator hudRes)
    {
        if (camDir != null)
            yield return camDir.ReturnToBase();

        var worldCam = camDir != null ? camDir.MapCamera : Camera.main;

        for (int j = startIndex; j < endIndexExclusive; j++)
        {
            var ev = events[j];
            if (ev.Type != DayEventType.CityMoneyBurst) break;

            bool gotPos = false;
            Vector3 pos;
            if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityWorldPos(ev.CityId, out pos))
                gotPos = true;
            else
                pos = default;

            if (debugLogs)
                Debug.Log($"[M6][Money][Burst] city={ev.CityId} delta={ev.MoneyDelta} gotPos={gotPos} pos=({pos.x:0.##},{pos.y:0.##})", this);

            if (hudRes != null && gotPos)
                hudRes.PlayMoneyBurst(pos, ev.MoneyDelta, worldCam);

            yield return new WaitForSeconds(ScaleSeconds(Mathf.Max(0f, perCityStartDelaySeconds)));
        }

        if (hudRes != null)
        {
            if (debugLogs)
                Debug.Log($"[M6][Money][Seq] waitFly={hudRes.CoinFlySecondsScaled:0.##}", this);
            yield return new WaitForSeconds(Mathf.Max(0.01f, hudRes.CoinFlySecondsScaled));
        }
    }

    private void ApplyPhaseAdvancedToOverlayAndView(Core.DayEvent e)
    {
        if (string.IsNullOrEmpty(e.AnomalyId)) return;
        if (!_pb.TryGetValue(e.AnomalyId, out var pb)) return;

        pb.Phase = e.ToPhase;

        var reg = MapEntityRegistry.I;
        if (reg != null && reg.TryGetAnomalyView(e.AnomalyId, out var view) && view != null)
        {
            view.PlaybackSetProgress(pb.Phase, pb.Inv, pb.Con, pb.Arrived.Count);
            view.PlaybackSetArrivedAgents(pb.Arrived);
        }
    }

    private static string Describe(DayEvent e)
    {
        switch (e.Type)
        {
            case DayEventType.FocusAnomaly:
                return $"FocusAnomaly anom={e.AnomalyId} pos=({e.MapPos.x:0.##},{e.MapPos.y:0.##}) zoom={e.Zoom:0.##} dur={e.Duration:0.##}";

            case DayEventType.AgentCheck:
                return $"AgentCheck anom={e.AnomalyId} agent={e.AgentId} slot={e.Slot} roll={e.Roll} dc={e.Dc} {(e.Success ? "OK" : "FAIL")} reason={e.ReasonKey}";


            case DayEventType.AnomalyProgressDelta:
                return $"Progress anom={e.AnomalyId} phase={e.Phase} {e.Before01:0.###}->{e.After01:0.###} (d={e.Delta01:0.###})";

            case DayEventType.AnomalyRangeAttack:
                return $"RangeAttack anom={e.AnomalyId} origin=({e.OriginPos.x:0.##},{e.OriginPos.y:0.##}) range={e.Range:0.##}";

            case DayEventType.AnomalyNegEntropyBurst:
                return $"NEBurst anom={e.AnomalyId} +{e.NegEntropyDelta}";

            case DayEventType.CityPopLoss:
                return $"CityPopLoss anom={e.AnomalyId} city={e.CityId} {e.BeforePop}->{e.AfterPop} (loss={e.Loss}) dist={e.Dist:0.##} range={e.Range:0.##}";

            case DayEventType.CityMoneyBurst:
                return $"CityMoneyBurst city={e.CityId} +{e.MoneyDelta}";

            case DayEventType.RosterRecalled:
                return $"RosterRecalled anom={e.AnomalyId} slot={e.Slot} count={(e.AgentIds != null ? e.AgentIds.Length : 0)}";

            case DayEventType.PhaseAdvanced:
                return $"PhaseAdvanced anom={e.AnomalyId} {e.FromPhase}->{e.ToPhase}";

            case DayEventType.ResourceDelta:
                return $"ResourceDelta money={e.MoneyDelta:+0;-#;0} panic={e.PanicDelta:+0.##;-#.##;0} ne={e.NegEntropyDelta:+0;-#;0}";

            case DayEventType.EndOfAnomaly:
                return $"EndOfAnomaly anom={e.AnomalyId}";

            case DayEventType.EndOfDay:
                return "EndOfDay";

            default:
                return e.Type.ToString();
        }
    }

    private IEnumerator PlayAgentCheck(Core.DayEvent e)
{
    if (!string.IsNullOrEmpty(e.AnomalyId))
    {
        var reg = MapEntityRegistry.I;
        if (reg != null && reg.TryGetAnomalyView(e.AnomalyId, out var view) && view != null)
        {
            var tint = e.Success ? new Color(0.25f, 1f, 0.35f, 1f) : new Color(1f, 0.25f, 0.25f, 1f);
            view.PlaybackPulseIcon(tint, ScaleSeconds(iconPulseSeconds), iconPulseScaleMul);
            view.PlaybackPulseAgent(e.AgentId, tint, ScaleSeconds(agentPulseSeconds), agentPulseScaleMul);
        }
        else if (debugLogs)
        {
            Debug.Log($"[M6][AgentCheck] noView anom={e.AnomalyId} agent={e.AgentId}", this);
        }
    }

    yield return new WaitForSeconds(ScaleSeconds(Mathf.Max(0.01f, agentCheckBeatSeconds)));
}

private IEnumerator PlayAgentStatusChanged(Core.DayEvent e)
{
    if (!string.IsNullOrEmpty(e.AnomalyId))
    {
        var reg = MapEntityRegistry.I;
        if (reg != null && reg.TryGetAnomalyView(e.AnomalyId, out var view) && view != null)
        {
            Color tint;
            float scaleMul = agentPulseScaleMul * 1.15f;

            if (e.Type == Core.DayEventType.AgentKilled) tint = new Color(1f, 0.15f, 0.15f, 1f);
            else tint = new Color(1f, 1f, 0.25f, 1f); // insane

            view.PlaybackPulseIcon(tint, ScaleSeconds(iconPulseSeconds * 1.2f), iconPulseScaleMul * 1.1f);
            view.PlaybackPulseAgent(e.AgentId, tint, ScaleSeconds(agentPulseSeconds * 1.25f), scaleMul);
        }
        else if (debugLogs)
        {
            Debug.Log($"[M6][AgentStatus] noView anom={e.AnomalyId} agent={e.AgentId} type={e.Type}", this);
        }
    }

    yield return new WaitForSeconds(ScaleSeconds(Mathf.Max(0.01f, agentCheckBeatSeconds)));
}


// ---------------- Playback overlay----------------

    private sealed class PBAnom
    {
        public Core.AnomalyPhase Phase;
        public float Inv;
        public float Con;
        public readonly List<string> Arrived = new();
    }

    private readonly Dictionary<string, PBAnom> _pb = new(System.StringComparer.OrdinalIgnoreCase);

    private void BuildOverlayFromState()
    {
        _pb.Clear();

        var gc = GameController.I;
        var s = gc?.State;
        if (s?.Anomalies == null) return;

        // init per anomaly
        foreach (var a in s.Anomalies)
        {
            if (a == null || string.IsNullOrEmpty(a.Id)) continue;
            _pb[a.Id] = new PBAnom
            {
                Phase = a.Phase,
                Inv = a.InvestigateProgress,
                Con = a.ContainProgress,
            };
        }

        // arrived agents (AtAnomaly only)
        if (s.Agents != null)
        {
            foreach (var ag in s.Agents)
            {
                if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;
                if (ag.LocationKind != Core.AgentLocationKind.AtAnomaly) continue;
                if (string.IsNullOrEmpty(ag.LocationAnomalyInstanceId)) continue;

                if (_pb.TryGetValue(ag.LocationAnomalyInstanceId, out var pb))
                    pb.Arrived.Add(ag.Id);
            }
        }

        foreach (var kv in _pb)
            kv.Value.Arrived.Sort(System.StringComparer.Ordinal);
    }

    private void ApplyOverlayToViews()
    {
        var reg = MapEntityRegistry.I;
        if (reg == null) return;

        foreach (var kv in _pb)
        {
            if (!reg.TryGetAnomalyView(kv.Key, out var view)) continue;
            var pb = kv.Value;
            view.PlaybackSetProgress(pb.Phase, pb.Inv, pb.Con, pb.Arrived.Count);
            view.PlaybackSetArrivedAgents(pb.Arrived);
        }
    }

    private IEnumerator PlayProgressDelta(Core.DayEvent e, float seconds = 0.25f)
    {
        seconds = ScaleSeconds(seconds);
        if (string.IsNullOrEmpty(e.AnomalyId)) yield break;
        if (!_pb.TryGetValue(e.AnomalyId, out var pb)) yield break;

        pb.Phase = e.Phase;

        float from = e.Before01;
        float to = e.After01;
        if (to >= 0.999f) to = 1f; // show 100%

        var reg = MapEntityRegistry.I;
        reg.TryGetAnomalyView(e.AnomalyId, out var view);

        float t = 0f;
        while (t < seconds)
        {
            float k = seconds <= 0f ? 1f : Mathf.Clamp01(t / seconds);
            float cur = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k));

            if (pb.Phase == Core.AnomalyPhase.Investigate) pb.Inv = cur;
            else if (pb.Phase == Core.AnomalyPhase.Contain) pb.Con = cur;

            if (view != null)
                view.PlaybackSetProgress(pb.Phase, pb.Inv, pb.Con, pb.Arrived.Count);

            t += Time.deltaTime;
            yield return null;
        }

        if (pb.Phase == Core.AnomalyPhase.Investigate) pb.Inv = to;
        else if (pb.Phase == Core.AnomalyPhase.Contain) pb.Con = to;

        if (view != null)
            view.PlaybackSetProgress(pb.Phase, pb.Inv, pb.Con, pb.Arrived.Count);
    }

    private IEnumerator PlayRosterRecall(Core.DayEvent e)
    {
        if (string.IsNullOrEmpty(e.AnomalyId)) yield break;
        if (!_pb.TryGetValue(e.AnomalyId, out var pb)) yield break;

        var reg = MapEntityRegistry.I;
        reg.TryGetAnomalyView(e.AnomalyId, out var view);

        // 先把进度钉到 100%（满足“先满再回”）
        if (pb.Phase == Core.AnomalyPhase.Investigate) pb.Inv = 1f;
        if (pb.Phase == Core.AnomalyPhase.Contain) pb.Con = 1f;
        if (view != null) view.PlaybackSetProgress(pb.Phase, pb.Inv, pb.Con, pb.Arrived.Count);

        yield return new WaitForSeconds(ScaleSeconds(0.08f));

        if (e.AgentIds == null || e.AgentIds.Length == 0) yield break;

        var ids = e.AgentIds.Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

        for (int i = 0; i < ids.Length; i++)
        {
            var agentId = ids[i];

            // UI：先减头像，再飞人
            pb.Arrived.Remove(agentId);
            if (view != null) view.PlaybackSetArrivedAgents(pb.Arrived);

            if (DispatchAnimationSystem.I != null)
                yield return DispatchAnimationSystem.I.PlayVisualRecallOne(e.AnomalyId, agentId);
            else
                yield return new WaitForSeconds(ScaleSeconds(0.35f));
        }
    }

    /// <summary>
    /// Look ahead from a FocusAnomaly event and collect the impacted cities (CityPopLoss) for the same anomaly.
    /// Used to auto-compute focus orthographic size (or validate range-driven focus).
    /// </summary>
    private static string[] CollectImpactCityIds(IList<DayEvent> events, int focusIndex, string anomalyId)
    {
        if (events == null || focusIndex < 0 || focusIndex >= events.Count) return Array.Empty<string>();
        if (string.IsNullOrEmpty(anomalyId)) return Array.Empty<string>();

        // Deterministic: de-dup then sort by cityId (ordinal).
        var set = new HashSet<string>(StringComparer.Ordinal);

        for (int j = focusIndex + 1; j < events.Count; j++)
        {
            var e = events[j];

            // Stop when this anomaly ends.
            if (e.Type == DayEventType.EndOfAnomaly && StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
                break;

            // Safety: if the next anomaly focus appears before EndOfAnomaly, stop scanning.
            if (e.Type == DayEventType.FocusAnomaly && !StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
                break;

            if (e.Type == DayEventType.CityPopLoss && StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
            {
                if (!string.IsNullOrEmpty(e.CityId))
                    set.Add(e.CityId);
            }
        }

        if (set.Count == 0) return Array.Empty<string>();

        var list = set.ToList();
        list.Sort(StringComparer.Ordinal);
        return list.ToArray();
    }

    /// <summary>
    /// Look ahead from a FocusAnomaly event and find the upcoming AnomalyRangeAttack range for the same anomaly.
    /// Returns 0 if not found.
    /// </summary>
    private static float FindUpcomingRange(IList<DayEvent> events, int focusIndex, string anomalyId)
    {
        if (events == null || focusIndex < 0 || focusIndex >= events.Count) return 0f;
        if (string.IsNullOrEmpty(anomalyId)) return 0f;

        // Look ahead within the same anomaly block: from FocusAnomaly to EndOfAnomaly.
        for (int i = focusIndex + 1; i < events.Count; i++)
        {
            var e = events[i];

            if (e.Type == DayEventType.EndOfAnomaly && StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
                break;

            if (e.Type == DayEventType.FocusAnomaly && !StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
                break; // next anomaly started

            if (e.Type == DayEventType.AnomalyRangeAttack && StringComparer.Ordinal.Equals(e.AnomalyId, anomalyId))
                return Mathf.Max(0f, e.Range);
        }

        return 0f;
    }
}
