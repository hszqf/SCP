using System.Collections;
using UnityEngine;

/// <summary>
/// M6: Camera direction for DayPlayback.
/// Works with an orthographic camera driving a ScreenSpace-Camera map canvas.
/// </summary>
public sealed class DayPlaybackCameraDirector : MonoBehaviour
{
    public static DayPlaybackCameraDirector I { get; private set; }

    [Header("Bindings (required)")]
    [SerializeField] private Camera mapCamera;

    [Header("Focus View")]
    [Tooltip("Manual tuning: orthographic size when focused on anomalies.")]
    [SerializeField] private float focusOrthoSize = 4.8f;
    [SerializeField] private float focusPanSeconds = 0.45f;
    [SerializeField] private float zoomSeconds = 0.55f;

    [Header("Focus Auto (optional)")]
    [Tooltip("If true, the first FocusAnomaly will auto-compute ortho size from that anomaly's upcoming CityPopLoss set (keeps camera centered on anomaly).")]
    [SerializeField] private bool autoFocusOrthoFromImpact = true;

    [Tooltip("Clamp to avoid zooming out too far when impacted cities are very spread.")]
    [SerializeField] private float autoFocusMaxOrthoSize = 600f;

    [Tooltip("If true, later anomalies may also adjust orthographic size (otherwise PanOnly keeps current size).")]
    [SerializeField] private bool allowAutoFocusZoomOnSubsequentAnomalies = false;

    

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

[Header("Playback Speed")]
[Tooltip("Scale all camera tweens during end-day playback. 1 = normal, 2 = 2x slower.")]
[Min(0.1f)]
[SerializeField] private float playbackDurationScale = 1f;

    [Header("Auto Focus From Range")]
    [Tooltip("Prefer using upcoming AnomalyRangeAttack.Range to compute focus ortho size. Falls back to impacted cities bounds if range is not available.")]
    [SerializeField] private bool autoFocusPreferRange = true;

    [Tooltip("Convert anomaly Range to orthographicSize: ortho ~= range * scale. Tune this to match your map units (e.g. range=300 => ortho~400).")]
    [SerializeField] private float rangeToOrthoScale = 1.33f;
[Header("Range Frame")]
    [SerializeField] private float framePadding = 1.25f;

    private Vector3 _basePos;
    private float _baseOrthoSize;

    private bool _capturedBase;
    private bool _hasEnteredFocus;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (!mapCamera)
        {
            Debug.LogError("[DayPlaybackCameraDirector] Missing binding: mapCamera", this);
        }

        CaptureBaseIfNeeded();
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    public Camera MapCamera => mapCamera;

public void SetPlaybackDurationScale(float scale)
{
    playbackDurationScale = Mathf.Max(0.1f, scale);
}

public void ForceToBaseImmediate()
{
    CaptureBaseIfNeeded();
    if (!mapCamera) return;

    mapCamera.transform.position = _basePos;
    mapCamera.orthographicSize = _baseOrthoSize;
    _hasEnteredFocus = false;
}

    public void ResetFocusState()
    {
        _hasEnteredFocus = false;
    }

    public void CaptureBaseIfNeeded()
    {
        if (_capturedBase) return;
        if (!mapCamera) return;

        _capturedBase = true;
        _basePos = mapCamera.transform.position;
        _baseOrthoSize = mapCamera.orthographicSize;

        // If designer forgot to tune focus size, infer from base.
        if (focusOrthoSize <= 0.01f) focusOrthoSize = Mathf.Max(0.01f, _baseOrthoSize / 1.15f);
    }

    public IEnumerator EnterFocus(string anomalyId, float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;

        if (!TryGetAnomalyWorldPos(anomalyId, out var pos))
            pos = mapCamera.transform.position;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(focusPanSeconds, zoomSeconds);

        if (!_hasEnteredFocus)
        {
            _hasEnteredFocus = true;
            yield return TweenTo(pos, focusOrthoSize, dur);
        }
        else
        {
            // Pan only (keep current ortho size).
            yield return TweenTo(pos, mapCamera.orthographicSize, durationOverrideSeconds > 0f ? durationOverrideSeconds : focusPanSeconds);
        }
    }

    /// <summary>
    /// Focus on anomaly, but auto-compute orthographic size from impacted cities while keeping camera centered on anomaly.
    /// This avoids camera movement during CityPopLoss.
    /// </summary>
    
    /// <summary>
    /// Auto focus centered on anomaly. Prefer using upcoming AnomalyRangeAttack.Range (range * scale),
    /// otherwise fall back to bounding impacted cities while staying centered on anomaly.
    /// </summary>
    public IEnumerator EnterFocusAuto(string anomalyId, float range, string[] impactedCityIds, float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;

        if (!TryGetAnomalyWorldPos(anomalyId, out var pos))
            pos = mapCamera.transform.position;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(focusPanSeconds, zoomSeconds);

        float targetOrtho = focusOrthoSize;

        bool usedRange = false;
        bool usedImpactBounds = false;

        if (autoFocusPreferRange && range > 0.01f)
        {
            float needOrtho = range * Mathf.Max(0.01f, rangeToOrthoScale);
            targetOrtho = Mathf.Max(focusOrthoSize, needOrtho);
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

            float needOrtho = Mathf.Max(maxDy, maxDx / aspect) * framePadding;
            targetOrtho = Mathf.Max(focusOrthoSize, needOrtho);
            usedImpactBounds = true;
        }

        if (autoFocusMaxOrthoSize > focusOrthoSize + 0.01f)
            targetOrtho = Mathf.Min(autoFocusMaxOrthoSize, targetOrtho);

        if (debugLogs)
        {
            Debug.Log($"[M6][Cam][AutoFocus] anom={anomalyId} pos=({pos.x:0.##},{pos.y:0.##}) range={range:0.##} scale={rangeToOrthoScale:0.##} usedRange={usedRange} usedImpactBounds={usedImpactBounds} focusMin={focusOrthoSize:0.##} targetOrtho={targetOrtho:0.##} baseOrtho={_baseOrthoSize:0.##} aspect={mapCamera.aspect:0.###}", this);
        }

        if (!_hasEnteredFocus)
        {
            _hasEnteredFocus = true;
            yield return TweenTo(pos, targetOrtho, dur);
        }
        else
        {
            // Subsequent anomalies: PanOnly by default, unless explicitly allowed.
            if (allowAutoFocusZoomOnSubsequentAnomalies)
                yield return TweenTo(pos, targetOrtho, dur);
            else
                yield return TweenTo(pos, mapCamera.orthographicSize, durationOverrideSeconds > 0f ? durationOverrideSeconds : focusPanSeconds);
        }
    }

public IEnumerator EnterFocusAutoFromImpact(string anomalyId, string[] impactedCityIds, float durationOverrideSeconds = -1f)
    {
        // Backward-compatible entry.
        return EnterFocusAuto(anomalyId, 0f, impactedCityIds, durationOverrideSeconds);
    }

    public IEnumerator PanToAnomaly(string anomalyId, float durationOverrideSeconds = -1f)
    {
        if (!mapCamera) yield break;
        if (!TryGetAnomalyWorldPos(anomalyId, out var pos)) yield break;

        yield return TweenTo(pos, mapCamera.orthographicSize, durationOverrideSeconds > 0f ? durationOverrideSeconds : focusPanSeconds);
    }

    public IEnumerator FrameRange(string anomalyId, string[] cityIds, float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;

        if (!TryGetAnomalyWorldPos(anomalyId, out var anomPos))
            yield break;

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
                    var p = (Vector2)cpos;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
        }

        Vector2 center2 = (min + max) * 0.5f;
        Vector2 size2 = (max - min);

        float aspect = mapCamera.aspect;
        float halfH = Mathf.Max(0.01f, size2.y * 0.5f) * framePadding;
        float halfW = Mathf.Max(0.01f, size2.x * 0.5f) * framePadding;

        float needOrtho = Mathf.Max(halfH, halfW / Mathf.Max(0.01f, aspect));
        float targetOrtho = Mathf.Max(focusOrthoSize, needOrtho);

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : zoomSeconds;

        yield return TweenTo(new Vector3(center2.x, center2.y, mapCamera.transform.position.z), targetOrtho, dur);
    }

    public IEnumerator ReturnToFocus(string anomalyId, float durationOverrideSeconds = -1f)
    {
        if (!mapCamera) yield break;
        if (!TryGetAnomalyWorldPos(anomalyId, out var pos)) yield break;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(focusPanSeconds, zoomSeconds);
        yield return TweenTo(pos, focusOrthoSize, dur);
    }

    public IEnumerator ReturnToBase(float durationOverrideSeconds = -1f)
    {
        CaptureBaseIfNeeded();
        if (!mapCamera) yield break;

        float dur = durationOverrideSeconds > 0f ? durationOverrideSeconds : Mathf.Max(focusPanSeconds, zoomSeconds);
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
