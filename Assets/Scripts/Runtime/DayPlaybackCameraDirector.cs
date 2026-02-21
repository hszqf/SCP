using System.Collections;
using UnityEngine;

/// <summary>
/// M6: Camera direction for DayPlayback.
/// Works with an orthographic camera driving a world/map view.
/// </summary>
public sealed class DayPlaybackCameraDirector : MonoBehaviour
{
    public static DayPlaybackCameraDirector I { get; private set; }

    [Header("Bindings (required)")]
    [SerializeField] private Camera mapCamera;

    [Header("Focus View (fallback if no M6PlaybackTuning)")]
    [Tooltip("Manual tuning: orthographic size when focused on anomalies (lower bound).")]
    [SerializeField] private float focusOrthoSize = 400f;
    [SerializeField] private float focusPanSeconds = 0.45f;
    [SerializeField] private float zoomSeconds = 0.55f;

    [Header("Focus Auto (fallback)")]
    [SerializeField] private bool autoFocusOrthoFromImpact = true;
    [SerializeField] private float autoFocusMaxOrthoSize = 600f;
    [SerializeField] private bool allowAutoFocusZoomOnSubsequentAnomalies = false;

    [Header("Auto Focus From Range (fallback)")]
    [SerializeField] private bool autoFocusPreferRange = true;
    [SerializeField] private float rangeToOrthoScale = 1.33f;

    [Header("Range Frame (fallback)")]
    [SerializeField] private float framePadding = 1.25f;

    [Header("Playback Speed")]
    [Min(0.1f)]
    [SerializeField] private float playbackDurationScale = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    public Camera MapCamera => mapCamera;

    private Vector3 _basePos;
    private float _baseOrthoSize;
    private bool _baseCaptured;
    private bool _hasEnteredFocus;

    private M6PlaybackTuning T => M6PlaybackTuning.I;

    private float FocusMin => T != null ? T.focusOrthoMin : focusOrthoSize;
    private float PanSeconds => T != null ? T.focusPanSeconds : focusPanSeconds;
    private float ZoomSeconds => T != null ? T.zoomSeconds : zoomSeconds;

    private bool PreferRange => T != null ? T.autoFocusPreferRange : autoFocusPreferRange;
    private float RangeScale => T != null ? T.rangeToOrthoScale : rangeToOrthoScale;
    private float MaxOrthoClamp => T != null ? T.autoFocusMaxOrthoSize : autoFocusMaxOrthoSize;
    private float FramePadding => T != null ? T.framePadding : framePadding;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (mapCamera == null)
            Debug.LogError("[DayPlaybackCameraDirector] Missing binding: mapCamera", this);
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    public void SetPlaybackDurationScale(float scale)
    {
        playbackDurationScale = Mathf.Max(0.1f, scale);
    }

    public void CaptureBaseIfNeeded()
    {
        if (_baseCaptured) return;
        if (!mapCamera) return;
        _baseCaptured = true;
        _basePos = mapCamera.transform.position;
        _baseOrthoSize = mapCamera.orthographicSize;
    }

    public void ResetFocusState()
    {
        _hasEnteredFocus = false;
    }

    public void ForceToBaseImmediate()
    {
        if (!mapCamera) return;
        if (!_baseCaptured) CaptureBaseIfNeeded();
        mapCamera.transform.position = _basePos;
        mapCamera.orthographicSize = _baseOrthoSize;
        _hasEnteredFocus = false;
    }

    public IEnumerator EnterFocusAuto(string anomalyId, float range, string[] impactedCityIds, float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;

        if (!TryGetAnomalyWorldPos(anomalyId, out var pos))
            pos = mapCamera.transform.position;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(PanSeconds, ZoomSeconds);

        float targetOrtho = FocusMin;
        bool usedRange = false;
        bool usedImpactBounds = false;

        if (PreferRange && range > 0.01f)
        {
            float need = range * Mathf.Max(0.01f, RangeScale);
            targetOrtho = Mathf.Max(FocusMin, need);
            usedRange = true;
        }
        else if (autoFocusOrthoFromImpact && impactedCityIds != null && impactedCityIds.Length > 0)
        {
            float aspect = Mathf.Max(0.01f, mapCamera.aspect);
            float maxDx = 0f;
            float maxDy = 0f;
            for (int i = 0; i < impactedCityIds.Length; i++)
            {
                var cid = impactedCityIds[i];
                if (string.IsNullOrEmpty(cid)) continue;
                if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityWorldPos(cid, out var cpos))
                {
                    maxDx = Mathf.Max(maxDx, Mathf.Abs(cpos.x - pos.x));
                    maxDy = Mathf.Max(maxDy, Mathf.Abs(cpos.y - pos.y));
                }
            }
            float need = Mathf.Max(maxDy, maxDx / aspect) * FramePadding;
            targetOrtho = Mathf.Max(FocusMin, need);
            usedImpactBounds = true;
        }

        // Clamp only if enabled and reasonable.
        float clamp = MaxOrthoClamp;
        if (clamp > FocusMin + 0.01f)
            targetOrtho = Mathf.Min(clamp, targetOrtho);

        if (debugLogs)
        {
            Debug.Log($"[M6][Cam][AutoFocus] anom={anomalyId} pos=({pos.x:0.##},{pos.y:0.##}) range={range:0.##} scale={RangeScale:0.##} usedRange={usedRange} usedImpactBounds={usedImpactBounds} focusMin={FocusMin:0.##} targetOrtho={targetOrtho:0.##} baseOrtho={_baseOrthoSize:0.##} aspect={mapCamera.aspect:0.###}", this);
        }

        if (!_hasEnteredFocus)
        {
            _hasEnteredFocus = true;
            yield return TweenTo(pos, targetOrtho, dur);
        }
        else
        {
            if (allowAutoFocusZoomOnSubsequentAnomalies)
                yield return TweenTo(pos, targetOrtho, dur);
            else
                yield return TweenTo(pos, mapCamera.orthographicSize, durationOverrideSeconds > 0f ? durationOverrideSeconds : PanSeconds);
        }
    }

    public IEnumerator PanToAnomaly(string anomalyId, float durationOverrideSeconds = -1f)
    {
        if (!mapCamera) yield break;
        if (!TryGetAnomalyWorldPos(anomalyId, out var pos)) yield break;
        yield return TweenTo(pos, mapCamera.orthographicSize, durationOverrideSeconds > 0f ? durationOverrideSeconds : PanSeconds);
    }

    public IEnumerator FrameRange(string anomalyId, string[] cityIds, float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;
        if (!TryGetAnomalyWorldPos(anomalyId, out var anomPos)) yield break;

        var min = (Vector2)anomPos;
        var max = (Vector2)anomPos;

        if (cityIds != null)
        {
            for (int i = 0; i < cityIds.Length; i++)
            {
                var cid = cityIds[i];
                if (string.IsNullOrEmpty(cid)) continue;
                if (MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetCityWorldPos(cid, out var cpos))
                {
                    min = Vector2.Min(min, cpos);
                    max = Vector2.Max(max, cpos);
                }
            }
        }

        var center = (min + max) * 0.5f;
        float width = Mathf.Max(0.1f, max.x - min.x);
        float height = Mathf.Max(0.1f, max.y - min.y);
        float aspect = Mathf.Max(0.01f, mapCamera.aspect);

        float needOrtho = Mathf.Max(height * 0.5f, (width * 0.5f) / aspect) * FramePadding;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(PanSeconds, ZoomSeconds);
        yield return TweenTo(center, Mathf.Max(FocusMin, needOrtho), dur);
    }

    public IEnumerator ReturnToFocus(string anomalyId, float durationOverrideSeconds = -1f)
    {
        if (!mapCamera) yield break;
        if (!TryGetAnomalyWorldPos(anomalyId, out var pos)) yield break;
        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(PanSeconds, ZoomSeconds);
        yield return TweenTo(pos, FocusMin, dur);
    }

    public IEnumerator ReturnToBase(float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;
        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(PanSeconds, ZoomSeconds);
        _hasEnteredFocus = false;
        yield return TweenTo(_basePos, _baseOrthoSize, dur);
    }

    private IEnumerator TweenTo(Vector3 worldPos, float orthoSize, float seconds)
    {
        if (!mapCamera) yield break;

        float dur = Mathf.Max(0.01f, seconds) * Mathf.Max(0.1f, playbackDurationScale);

        var fromPos = mapCamera.transform.position;
        var toPos = new Vector3(worldPos.x, worldPos.y, fromPos.z);

        float fromSize = mapCamera.orthographicSize;
        float toSize = Mathf.Max(0.01f, orthoSize);

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);

            mapCamera.transform.position = Vector3.Lerp(fromPos, toPos, s);
            mapCamera.orthographicSize = Mathf.Lerp(fromSize, toSize, s);

            yield return null;
        }

        mapCamera.transform.position = toPos;
        mapCamera.orthographicSize = toSize;
    }

    private static bool TryGetAnomalyWorldPos(string anomalyId, out Vector3 pos)
    {
        pos = default;
        if (string.IsNullOrEmpty(anomalyId)) return false;
        return MapEntityRegistry.I != null && MapEntityRegistry.I.TryGetAnomalyWorldPos(anomalyId, out pos);
    }
}
