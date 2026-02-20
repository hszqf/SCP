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

    [Header("M6 Range Impact")]
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

    private IEnumerator PlayCoroutine(Core.DayResolutionPlan plan, Action onFinished)
    {
        // 全局锁：HUD + 地图异常点击（Anomaly.HandleClick 会检查 IsInteractionLocked）
        DispatchAnimationSystem.I?.SetExternalInteractionLocked(true);
        HUD.I?.SetControlsInteractable(false);

        // M6: init camera + HUD resource snapshot (visual-only, no GameState mutation)
        var camDir = DayPlaybackCameraDirector.I;
        camDir?.ResetFocusState();
        camDir?.CaptureBaseIfNeeded();

        var hudRes = HUDResourceAnimator.I;
        var s0 = GameController.I?.State;
        if (s0 != null) hudRes?.BeginPlaybackSnapshot(s0.Money, s0.NegEntropy);

        // 播放期：用 overlay 驱动 anomaly 进度与头像（不写 GameState）
        BuildOverlayFromState();
        ApplyOverlayToViews();

        var events = plan.Events;
        if (events != null)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                Debug.Log($"[M6][Play] {Describe(e)}");

                switch (e.Type)
                {
                    case Core.DayEventType.FocusAnomaly:
                        if (camDir != null)
                            yield return camDir.EnterFocus(e.AnomalyId, e.Duration > 0f ? e.Duration : defaultFocusSeconds);
                        else
                            yield return (eventStepSeconds > 0f ? new WaitForSeconds(eventStepSeconds) : null);
                        break;

                    case Core.DayEventType.AgentCheck:
                        yield return PlayAgentCheck(e);
                        break;

                    case Core.DayEventType.AgentKilled:
                    case Core.DayEventType.AgentInsane:
                        yield return PlayAgentStatusChanged(e);
                        break;

                    case Core.DayEventType.AnomalyProgressDelta:
                        // 进度 tween（含 near-1 显示 100%）
                        yield return PlayProgressDelta(e);
                        break;

                    case Core.DayEventType.RosterRecalled:
                        // 先保证进度 100%，再逐人：减头像 + 用对应头像飞回基地
                        yield return PlayRosterRecall(e);
                        break;

                    case Core.DayEventType.PhaseAdvanced:
                        ApplyPhaseAdvancedToOverlayAndView(e);
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(eventStepSeconds);
                        else yield return null;
                        break;

                    case Core.DayEventType.AnomalyNegEntropyBurst:
                        PlayNegEntropyBurst(e, camDir, hudRes);
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(eventStepSeconds);
                        else yield return null;
                        break;

                    case Core.DayEventType.AnomalyRangeAttack:
                        {
                            int end = FindRangeImpactEnd(events, i);
                            yield return PlayRangeImpactSequence(events, i, end, camDir);
                            i = end - 1; // skip consumed CityPopLoss*
                        }
                        break;

                    case Core.DayEventType.CityMoneyBurst:
                        {
                            int end = FindCityMoneyEnd(events, i);
                            yield return PlayCityMoneySequence(events, i, end, camDir, hudRes);
                            i = end - 1;
                        }
                        break;

                    default:
                        if (eventStepSeconds > 0f) yield return new WaitForSeconds(eventStepSeconds);
                        else yield return null;
                        break;
                }
            }
        }

        // M6: end camera + HUD snapshot
        if (camDir != null)
            yield return camDir.ReturnToBase();
        hudRes?.EndPlaybackSnapshot();

        HUD.I?.SetControlsInteractable(true);
        DispatchAnimationSystem.I?.SetExternalInteractionLocked(false);

        _co = null;
        onFinished?.Invoke();
    }

    private void PlayNegEntropyBurst(Core.DayEvent e, DayPlaybackCameraDirector camDir, HUDResourceAnimator hudRes)
    {
        if (hudRes == null) return;

        var worldCam = camDir != null ? camDir.MapCamera : Camera.main;
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

        // Camera: frame anomaly + all affected cities.
        if (camDir != null && cityIds.Count > 0)
            yield return camDir.FrameRange(anomId, cityIds.ToArray());

        // City FX: stagger start.
        for (int j = rangeAttackIndex + 1; j < endIndexExclusive; j++)
        {
            var ce = events[j];
            if (ce.Type != DayEventType.CityPopLoss) break;

            if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityView(ce.CityId, out var cityView) && cityView != null)
                cityView.PlayPopLossFX(ce.Loss, ce.AfterPop, cityPopLossAnimSeconds);

            yield return new WaitForSeconds(Mathf.Max(0f, cityPopLossStartDelaySeconds));
        }

        if (endIndexExclusive > rangeAttackIndex + 1)
            yield return new WaitForSeconds(Mathf.Max(0.01f, cityPopLossAnimSeconds));

        if (camDir != null)
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

            if (hudRes != null && MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityWorldPos(ev.CityId, out var pos))
                hudRes.PlayMoneyBurst(pos, ev.MoneyDelta, worldCam);

            yield return new WaitForSeconds(Mathf.Max(0f, perCityStartDelaySeconds));
        }

        if (hudRes != null)
            yield return new WaitForSeconds(Mathf.Max(0.01f, hudRes.CoinFlySeconds));
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

    private IEnumerator PlayAgentCheck(Core.DayEvent e, float seconds = 0.08f)
{
    // v0: just a small beat for readability (later: floating text / sfx)
    if (seconds > 0f) yield return new WaitForSeconds(seconds);
    else yield return null;
}

    private IEnumerator PlayAgentStatusChanged(Core.DayEvent e, float seconds = 0.08f)
    {
        // v0: placeholder beat (later: avatar tinting / status text / sfx)
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        else yield return null;
    }


// ---------------- Playback overlay ----------------

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

        yield return new WaitForSeconds(0.08f);

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
                yield return new WaitForSeconds(0.35f);
        }
    }
}
