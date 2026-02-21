using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// M6: HUD resource feedback (fly icons + rolling numbers).
/// - Strong references only: missing bindings should error at Awake.
/// - During playback, values are visual-only (do NOT write GameState).
/// </summary>
public sealed class HUDResourceAnimator : MonoBehaviour
{
    public static HUDResourceAnimator I { get; private set; }

    [Header("Bindings (required)")]
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text negEntropyText;

    [SerializeField] private RectTransform moneyIconAnchor;
    [SerializeField] private RectTransform negEntropyIconAnchor;

    [SerializeField] private RectTransform flyIconLayer;
    [SerializeField] private GameObject flyIconPrefab; // must contain Image + RectTransform

    [SerializeField] private UIIconLibrary iconLibrary;

    

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
[Header("Playback Speed")]
[Tooltip("Scale all HUD resource animations during end-day playback. 1 = normal, 2 = 2x slower.")]
[Min(0.1f)]
[SerializeField] private float playbackTimeScale = 1f;

[Header("Config")]
    [SerializeField] private float numberRollSeconds = 0.45f;

[Header("Burst Visual")]
[SerializeField] private float burstIconScale = 1.6f;
[SerializeField] private float burstIconMinSize = 48f;

[SerializeField] private bool showBurstText = true;
[SerializeField] private float burstTextSeconds = 0.9f;
[SerializeField] private float burstTextRise = 40f;
[SerializeField] private float burstTextFontSize = 36f;

    [Header("Money")]
    [SerializeField] private float coinFlySeconds = 0.65f;
    [SerializeField] private int coinBurstCount = 6;

    [Header("NegEntropy")]
    [SerializeField] private float neFlySeconds = 0.65f;
    [SerializeField] private int neBurstCount = 6;

    public float CoinFlySeconds => coinFlySeconds;
    public float CoinFlySecondsScaled => coinFlySeconds * Mathf.Max(0.1f, playbackTimeScale);

    public void SetPlaybackTimeScale(float scale)
    {
        playbackTimeScale = Mathf.Max(0.1f, scale);
    }

    private bool _inPlayback;

    private int _moneyDisplay;
    private int _moneyTarget;
    private Coroutine _moneyRollCo;

    private int _neDisplay;
    private int _neTarget;
    private Coroutine _neRollCo;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        Require(moneyText, nameof(moneyText));
        Require(negEntropyText, nameof(negEntropyText));
        Require(moneyIconAnchor, nameof(moneyIconAnchor));
        Require(negEntropyIconAnchor, nameof(negEntropyIconAnchor));
        Require(flyIconLayer, nameof(flyIconLayer));
        Require(flyIconPrefab, nameof(flyIconPrefab));
        Require(iconLibrary, nameof(iconLibrary));

        if (debugLogs)
        {
            var canvas = flyIconLayer != null ? flyIconLayer.GetComponentInParent<Canvas>() : null;
            Debug.Log($"[M6][HUD] Awake ok. canvas={(canvas!=null?canvas.name:"null")} renderMode={(canvas!=null?canvas.renderMode.ToString():"null")} worldCam={(canvas!=null && canvas.worldCamera!=null?canvas.worldCamera.name:"null")} flyLayer={(flyIconLayer!=null?flyIconLayer.name:"null")}", this);
        }

        if (flyIconPrefab != null && flyIconPrefab.GetComponentInChildren<Image>(true) == null)
        {
            Debug.LogError("[HUDResourceAnimator] flyIconPrefab must contain an Image.", this);
        }
    }

    private void OnDisable()
    {
        // Prevent coroutines from trying to move destroyed RectTransforms.
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private static void Require(UnityEngine.Object obj, string field)
    {
        if (obj != null) return;
        Debug.LogError($"[HUDResourceAnimator] Missing binding: {field}");
    }

    // ---------- Playback snapshot ----------

    public void BeginPlaybackSnapshot(int money, int negEntropy)
    {
        _inPlayback = true;
        if (debugLogs) Debug.Log($"[M6][HUD] BeginPlaybackSnapshot money={money} ne={negEntropy}", this);

        _moneyDisplay = _moneyTarget = Mathf.Max(0, money);
        _neDisplay = _neTarget = Mathf.Max(0, negEntropy);

        ApplyMoneyText(_moneyDisplay);
        ApplyNegEntropyText(_neDisplay);
    }

    public void EndPlaybackSnapshot()
    {
        _inPlayback = false;
        if (debugLogs) Debug.Log($"[M6][HUD] EndPlaybackSnapshot moneyDisp={_moneyDisplay} moneyTarget={_moneyTarget} neDisp={_neDisplay} neTarget={_neTarget}", this);
        // After commit, HUD.Refresh() will overwrite texts via OnStateChanged.
    }

    // ---------- Public API called by playback ----------

    public void PlayMoneyBurst(Vector3 sourceWorldPos, int deltaMoney, Camera worldCamera)
    {
        if (debugLogs)
            Debug.Log($"[M6][HUD][MoneyBurst] delta={deltaMoney} inPlayback={_inPlayback} iconLib={(iconLibrary!=null)} worldCam={(worldCamera!=null?worldCamera.name:"null")} src=({sourceWorldPos.x:0.##},{sourceWorldPos.y:0.##})", this);

        if (!_inPlayback || deltaMoney == 0) return;
        if (iconLibrary == null) return;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            moneyIconAnchor,
            iconLibrary.coinSprite,
            Mathf.Max(1, coinBurstCount),
            Mathf.Max(0.01f, coinFlySeconds) * Mathf.Max(0.1f, playbackTimeScale),
            $"+{deltaMoney}",
            new Color(1f, 0.92f, 0.25f, 1f)
        ));

        AddMoney(deltaMoney);
    }

    public void PlayNegEntropyBurst(Vector3 sourceWorldPos, int deltaNE, Camera worldCamera)
    {
        if (debugLogs)
            Debug.Log($"[M6][HUD][NEBurst] delta={deltaNE} inPlayback={_inPlayback} iconLib={(iconLibrary!=null)} worldCam={(worldCamera!=null?worldCamera.name:"null")} src=({sourceWorldPos.x:0.##},{sourceWorldPos.y:0.##})", this);
        if (!_inPlayback || deltaNE == 0) return;
        if (iconLibrary == null) return;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            negEntropyIconAnchor,
            iconLibrary.negEntropySprite,
            Mathf.Max(1, neBurstCount),
            Mathf.Max(0.01f, neFlySeconds) * Mathf.Max(0.1f, playbackTimeScale),
            $"+{deltaNE}",
            new Color(0.45f, 0.95f, 1f, 1f)
        ));

        AddNegEntropy(deltaNE);
    }

    // ---------- Internals ----------

    private void AddMoney(int delta)
    {
        _moneyTarget = Mathf.Max(0, _moneyTarget + delta);
        if (_moneyRollCo == null)
            _moneyRollCo = StartCoroutine(RollIntCoroutine(
                getter: () => _moneyDisplay,
                setter: v => { _moneyDisplay = v; ApplyMoneyText(v); },
                targetGetter: () => _moneyTarget,
                seconds: numberRollSeconds * Mathf.Max(0.1f, playbackTimeScale),
                onDone: () => _moneyRollCo = null
            ));
    }

    private void AddNegEntropy(int delta)
    {
        _neTarget = Mathf.Max(0, _neTarget + delta);
        if (_neRollCo == null)
            _neRollCo = StartCoroutine(RollIntCoroutine(
                getter: () => _neDisplay,
                setter: v => { _neDisplay = v; ApplyNegEntropyText(v); },
                targetGetter: () => _neTarget,
                seconds: numberRollSeconds * Mathf.Max(0.1f, playbackTimeScale),
                onDone: () => _neRollCo = null
            ));
    }

    private void ApplyMoneyText(int v)
    {
        if (moneyText != null) moneyText.text = $"$ {v}";
    }

    private void ApplyNegEntropyText(int v)
    {
        if (negEntropyText != null) negEntropyText.text = $"NE {v}";
    }

    private static IEnumerator RollIntCoroutine(Func<int> getter, Action<int> setter, Func<int> targetGetter, float seconds, Action onDone)
    {
        // If target changes mid-roll, keep rolling until stable.
        float minSeconds = Mathf.Max(0.01f, seconds);

        while (true)
        {
            int from = getter();
            int to = targetGetter();
            if (from == to) break;

            float t = 0f;
            while (t < minSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / minSeconds);
                float s = Mathf.SmoothStep(0f, 1f, k);
                int cur = Mathf.RoundToInt(Mathf.Lerp(from, to, s));
                setter(cur);
                yield return null;

                // target changed: restart segment
                if (to != targetGetter()) break;
            }

            // snap to current target and re-evaluate (in case target changed)
            setter(targetGetter());
            yield return null;
        }

        onDone?.Invoke();
    }

    private IEnumerator PlayBurstCoroutine(Vector3 sourceWorldPos, Camera worldCamera, RectTransform targetAnchor, Sprite sprite, int count, float flySeconds, string popText, Color popColor)
    {
        if (flyIconLayer == null || targetAnchor == null || flyIconPrefab == null)
        {
            if (debugLogs) Debug.Log($"[M6][HUD][Burst] abort missing bindings flyLayer={(flyIconLayer!=null)} target={(targetAnchor!=null)} prefab={(flyIconPrefab!=null)}", this);
            yield break;
        }


        // Convert source world pos to screen, then to flyLayer local.
        if (!TryWorldToFlyLayerLocal(sourceWorldPos, worldCamera, out var fromLocal))
        {
            if (debugLogs) Debug.Log($"[M6][HUD][Burst] abort WorldToFlyLayerLocal failed", this);
            yield break;
        }
        if (!TryRectToFlyLayerLocal(targetAnchor, out var toLocal))
        {
            if (debugLogs) Debug.Log($"[M6][HUD][Burst] abort RectToFlyLayerLocal failed", this);
            yield break;
        }

        if (debugLogs)
            Debug.Log($"[M6][HUD][Burst] count={count} flySeconds={flySeconds:0.##} from=({fromLocal.x:0.##},{fromLocal.y:0.##}) to=({toLocal.x:0.##},{toLocal.y:0.##})", this);


        SpawnBurstText(fromLocal, popText, popColor);

        // Deterministic offsets.
        int n = Mathf.Max(1, count);
        for (int i = 0; i < n; i++)
        {
            var go = Instantiate(flyIconPrefab, flyIconLayer);
            var rt = go.transform as RectTransform;
            if (rt == null) rt = go.GetComponent<RectTransform>();

var img = go.GetComponentInChildren<Image>(true);
if (img != null)
{
    img.enabled = true;
    img.raycastTarget = false;
    img.color = Color.white;
    img.sprite = sprite;
}

// Make sure it's visible even if prefab defaults are odd.
go.SetActive(true);
go.transform.SetAsLastSibling();

if (rt != null)
{
    if (rt.rect.width < 1f || rt.rect.height < 1f)
        rt.sizeDelta = new Vector2(burstIconMinSize, burstIconMinSize);
    rt.localScale = Vector3.one * burstIconScale;
}


            float angle = (n == 1) ? 0f : (i * (Mathf.PI * 2f / n));
            float r = Mathf.Min(28f, 10f + n * 1.2f);
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

            rt.anchoredPosition = fromLocal + offset;
            StartCoroutine(FlyOne(rt, fromLocal + offset, toLocal, flySeconds));
        }

        yield return null;
    }

    private IEnumerator FlyOne(RectTransform rt, Vector2 from, Vector2 to, float seconds)
    {
        if (rt == null) yield break;
        float t = 0f;
        float dur = Mathf.Max(0.01f, seconds);

        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);

            // rt might be destroyed if HUD gets rebuilt / scene reload happens mid-flight.
            if (rt == null) yield break;
            rt.anchoredPosition = Vector2.Lerp(from, to, s);
            yield return null;
        }

        if (rt == null) yield break;
        rt.anchoredPosition = to;
        Destroy(rt.gameObject);
    }

private void SpawnBurstText(Vector2 fromLocal, string text, Color color)
{
    if (!showBurstText) return;
    if (string.IsNullOrEmpty(text)) return;
    if (flyIconLayer == null) return;

    var go = new GameObject("BurstText", typeof(RectTransform), typeof(TextMeshProUGUI));
    var rt = (RectTransform)go.transform;
    rt.SetParent(flyIconLayer, false);
    rt.anchoredPosition = fromLocal + new Vector2(0f, 18f);
    go.transform.SetAsLastSibling();

    var tmp = go.GetComponent<TextMeshProUGUI>();
    tmp.text = text;
    tmp.fontSize = burstTextFontSize;
    tmp.color = color;
    tmp.raycastTarget = false;
    tmp.alignment = TextAlignmentOptions.Center;

    StartCoroutine(RiseFadeText(tmp, rt, burstTextSeconds * Mathf.Max(0.1f, playbackTimeScale), burstTextRise));
}

private static IEnumerator RiseFadeText(TextMeshProUGUI tmp, RectTransform rt, float seconds, float rise)
{
    if (tmp == null || rt == null) yield break;

    Color c0 = tmp.color;
    Vector2 p0 = rt.anchoredPosition;

    float t = 0f;
    float dur = Mathf.Max(0.01f, seconds);

    while (t < dur)
    {
        if (tmp == null || rt == null) yield break;

        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / dur);
        float s = Mathf.SmoothStep(0f, 1f, k);

        rt.anchoredPosition = p0 + Vector2.up * (rise * s);
        tmp.color = new Color(c0.r, c0.g, c0.b, Mathf.Lerp(c0.a, 0f, s));

        yield return null;
    }

    if (rt != null) Destroy(rt.gameObject);
}

    private bool TryWorldToFlyLayerLocal(Vector3 worldPos, Camera worldCamera, out Vector2 local)
    {
        local = default;

        // Determine screen point.
        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogError("[HUDResourceAnimator] No camera available for WorldToScreenPoint.", this);
            return false;
        }

        Vector2 screen = cam.WorldToScreenPoint(worldPos);

        // Determine camera for flyLayer's canvas.
        var canvas = flyIconLayer != null ? flyIconLayer.GetComponentInParent<Canvas>() : null;
        var layerCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, layerCam, out local);
        if (debugLogs)
            Debug.Log($"[M6][HUD][WorldToFly] ok={ok} worldCam={(cam!=null?cam.name:"null")} screen=({screen.x:0.##},{screen.y:0.##}) layerCam={(layerCam!=null?layerCam.name:"null")} local=({local.x:0.##},{local.y:0.##})", this);
        return ok;
    }

    private bool TryRectToFlyLayerLocal(RectTransform target, out Vector2 local)
    {
        local = default;
        if (flyIconLayer == null || target == null) return false;

        var canvas = flyIconLayer.GetComponentInParent<Canvas>();
        var cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, target.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, cam, out local);
    }
}
