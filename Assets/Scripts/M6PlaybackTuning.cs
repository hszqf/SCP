using UnityEngine;

/// <summary>
/// M6: Central tuning knobs for playback / camera / HUD / map FX.
/// Put ONE instance in the scene (recommended on a root object).
/// Other M6 scripts will read from this singleton when present.
/// </summary>
[DefaultExecutionOrder(-950)]
public sealed class M6PlaybackTuning : MonoBehaviour
{
    public static M6PlaybackTuning I { get; private set; }

    [Header("Global")]
    [Tooltip("Slow motion multiplier for all end-day/start-day presentations. 1=normal, 4=4x slower.")]
    [Min(0.1f)] public float playbackSlowMo = 4f;

    // ---------- Camera ----------
    [Header("Camera: Focus / Zoom / Pan")]
    [Min(0.01f)] public float focusPanSeconds = 0.45f;
    [Min(0.01f)] public float zoomSeconds = 0.55f;

    [Tooltip("Manual minimum orthographic size when focused (used as lower bound).")]
    public float focusOrthoMin = 400f;

    [Header("Camera: Auto Focus From Range")]
    public bool autoFocusPreferRange = true;

    [Tooltip("ortho ~= range * scale. e.g. range=300 => ortho~400 => scale~1.33")]
    [Min(0.01f)] public float rangeToOrthoScale = 1.33f;

    [Tooltip("Clamp maximum orthographic size during auto focus. Set 0 to disable clamp.")]
    [Min(0f)] public float autoFocusMaxOrthoSize = 600f;

    [Header("Camera: Range Frame (optional)")]
    [Min(0.5f)] public float framePadding = 1.25f;

    // ---------- Agent / Anomaly feedback ----------
    [Header("Agent Settlement Feedback")]
    [Min(0.01f)] public float agentCheckBeatSeconds = 0.55f;
    [Min(0.01f)] public float agentPulseSeconds = 0.45f;
    [Min(1.0f)] public float agentPulseScaleMul = 1.28f;

    [Header("Anomaly Icon Pulse")]
    [Min(0.01f)] public float iconPulseSeconds = 0.35f;
    [Min(1.0f)] public float iconPulseScaleMul = 1.12f;

    // ---------- City poploss ----------
    [Header("City PopLoss FX")]
    [Min(0f)] public float cityPopLossStartDelaySeconds = 0.20f;
    [Min(0.01f)] public float cityPopLossAnimSeconds = 0.55f;

    // ---------- Money ----------
    [Header("Money: Per-city Stagger")]
    [Min(0f)] public float perCityStartDelaySeconds = 0.18f;

    [Header("Money: Coin Burst + Fly")]
    [Min(1)] public int coinBurstCount = 14;
    [Min(0f)] public float coinScatterRadius = 44f;
    [Min(0.01f)] public float coinScatterSeconds = 0.18f;
    [Min(0.01f)] public float coinFlySeconds = 0.90f;
    [Min(0f)] public float coinPerIconStartDelaySeconds = 0.03f;

    [Header("Money: Number Roll + Punch")]
    [Min(0.01f)] public float moneyRollSeconds = 0.85f;
    [Min(1.0f)] public float moneyPunchScale = 1.15f;
    [Min(0.01f)] public float moneyPunchSeconds = 0.22f;

    // ---------- NegEntropy ----------
    [Header("NegEntropy: Burst + Fly")]
    [Min(1)] public int neBurstCount = 10;
    [Min(0f)] public float neScatterRadius = 38f;
    [Min(0.01f)] public float neScatterSeconds = 0.18f;
    [Min(0.01f)] public float neFlySeconds = 0.85f;
    [Min(0f)] public float nePerIconStartDelaySeconds = 0.03f;

    [Header("NegEntropy: Number Roll + Punch")]
    [Min(0.01f)] public float neRollSeconds = 0.85f;
    [Min(1.0f)] public float nePunchScale = 1.12f;
    [Min(0.01f)] public float nePunchSeconds = 0.22f;

    // ---------- StartDay spawn ----------
    [Header("StartDay: New Anomaly Presentation")]
    [Min(0.01f)] public float spawnFocusSeconds = 0.75f;
    [Min(0f)] public float spawnHoldSeconds = 0.85f;

    [Header("StartDay: Appear / Range Ring")]
    [Min(0.01f)] public float spawnFadeInSeconds = 0.35f;
    [Min(0.01f)] public float ringExpandSeconds = 0.65f;
    [Range(0f, 1f)] public float ringAlpha = 0.25f;
    [Min(0.01f)] public float ringStartScale = 0.10f;

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
}
