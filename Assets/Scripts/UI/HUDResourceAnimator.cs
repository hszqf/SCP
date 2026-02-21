using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// M6: HUD resource feedback (fly icons + rolling numbers).
/// Strong references only.
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
    [SerializeField] private bool debugLogs = false;

    [Header("Playback Speed")]
    [Tooltip("Scale all HUD resource animations during playback. 1 = normal, 4 = 4x slower.")]
    [Min(0.1f)]
    [SerializeField] private float playbackTimeScale = 1f;

    // Fallback config (overridden by M6PlaybackTuning if present)
    [Header("Fallback Config (overridden by M6PlaybackTuning)")]
    [SerializeField] private float numberRollSeconds = 0.85f;

    [SerializeField] private float burstIconScale = 1.6f;
    [SerializeField] private float burstIconMinSize = 48f;

    [SerializeField] private bool showBurstText = true;
    [SerializeField] private float burstTextSeconds = 0.9f;
    [SerializeField] private float burstTextRise = 40f;
    [SerializeField] private float burstTextFontSize = 36f;

    [SerializeField] private float coinFlySeconds = 0.90f;
    [SerializeField] private int coinBurstCount = 14;
    [SerializeField] private float neFlySeconds = 0.85f;
    [SerializeField] private int neBurstCount = 10;
    [SerializeField, Min(0)] private int prewarmFlyIconCount = 24;

    private M6PlaybackTuning T => M6PlaybackTuning.I;

    private float MoneyRollSecondsBase => T != null ? T.moneyRollSeconds : numberRollSeconds;
    private float NeRollSecondsBase => T != null ? T.neRollSeconds : numberRollSeconds;

    private int CoinBurstCount => T != null ? Mathf.Max(1, T.coinBurstCount) : Mathf.Max(1, coinBurstCount);
    private int NeBurstCount => T != null ? Mathf.Max(1, T.neBurstCount) : Mathf.Max(1, neBurstCount);

    private float CoinScatterRadius => T != null ? Mathf.Max(0f, T.coinScatterRadius) : 44f;
    private float CoinScatterSecondsBase => T != null ? Mathf.Max(0.01f, T.coinScatterSeconds) : 0.18f;
    private float CoinFlySecondsBase => T != null ? Mathf.Max(0.01f, T.coinFlySeconds) : coinFlySeconds;

    /**
     * For sequencing in DayPlaybackDirector: an estimate of the per-coin fly duration after applying playbackTimeScale.
     * This is intentionally a simple scalar (not the whole burst duration).
     */
    public float CoinFlySecondsScaled => CoinFlySecondsBase * playbackTimeScale;

    private float CoinPerIconDelayBase => T != null ? Mathf.Max(0f, T.coinPerIconStartDelaySeconds) : 0.03f;

    private float NeScatterRadius => T != null ? Mathf.Max(0f, T.neScatterRadius) : 38f;
    private float NeScatterSecondsBase => T != null ? Mathf.Max(0.01f, T.neScatterSeconds) : 0.18f;
    private float NeFlySecondsBase => T != null ? Mathf.Max(0.01f, T.neFlySeconds) : neFlySeconds;
    private float NePerIconDelayBase => T != null ? Mathf.Max(0f, T.nePerIconStartDelaySeconds) : 0.03f;

    private float MoneyPunchScale => T != null ? Mathf.Max(1f, T.moneyPunchScale) : 1.12f;
    private float MoneyPunchSecondsBase => T != null ? Mathf.Max(0.01f, T.moneyPunchSeconds) : 0.18f;
    private float NePunchScale => T != null ? Mathf.Max(1f, T.nePunchScale) : 1.10f;
    private float NePunchSecondsBase => T != null ? Mathf.Max(0.01f, T.nePunchSeconds) : 0.18f;

    private bool _inPlayback;

    private int _moneyDisplay;
    private int _moneyTarget;
    private Coroutine _moneyRollCo;
    private Coroutine _moneyPunchCo;

    private int _neDisplay;
    private int _neTarget;
    private Coroutine _neRollCo;
    private Coroutine _nePunchCo;

    private readonly Queue<RectTransform> _flyIconPool = new();

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

        if (flyIconPrefab != null && flyIconPrefab.GetComponentInChildren<Image>(true) == null)
            Debug.LogError("[HUDResourceAnimator] flyIconPrefab must contain an Image.", this);

        PrewarmFlyIcons();
    }

    private void OnDisable()
    {
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

    private bool IsFlyIconPrefabValid()
    {
        if (flyIconPrefab == null) return false;
        if (ReferenceEquals(flyIconPrefab, gameObject)) return false;
        return flyIconPrefab.GetComponentInChildren<Image>(true) != null;
    }

    public void SetPlaybackTimeScale(float scale)
    {
        playbackTimeScale = Mathf.Max(0.1f, scale);
    }

    public void BeginPlaybackSnapshot(int money, int negEntropy)
    {
        _inPlayback = true;
        _moneyDisplay = _moneyTarget = Mathf.Max(0, money);
        _neDisplay = _neTarget = Mathf.Max(0, negEntropy);
        ApplyMoneyText(_moneyDisplay);
        ApplyNegEntropyText(_neDisplay);

        if (debugLogs) Debug.Log($"[M6][HUD] BeginPlaybackSnapshot money={money} ne={negEntropy}", this);
    }

    public void EndPlaybackSnapshot()
    {
        _inPlayback = false;
        if (debugLogs) Debug.Log($"[M6][HUD] EndPlaybackSnapshot moneyDisp={_moneyDisplay} moneyTarget={_moneyTarget} neDisp={_neDisplay} neTarget={_neTarget}", this);
    }

    // ---------- Public API called by playback ----------

    public void PlayMoneyBurst(Vector3 sourceWorldPos, int deltaMoney, Camera worldCamera)
    {
        if (debugLogs)
            Debug.Log($"[M6][HUD][MoneyBurst] delta={deltaMoney} inPlayback={_inPlayback} coinSprite={(iconLibrary!=null?iconLibrary.coinSprite!=null:false)} src=({sourceWorldPos.x:0.##},{sourceWorldPos.y:0.##})", this);

        if (!_inPlayback || deltaMoney == 0) return;
        if (iconLibrary == null || iconLibrary.coinSprite == null)
        {
            Debug.LogError("[M6][HUD] Missing iconLibrary.coinSprite - cannot render coins.", this);
            return;
        }

        float scatterSec = CoinScatterSecondsBase * playbackTimeScale;
        float flySec = CoinFlySecondsBase * playbackTimeScale;
        float perIconDelay = CoinPerIconDelayBase * playbackTimeScale;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            moneyIconAnchor,
            iconLibrary.coinSprite,
            CoinBurstCount,
            CoinScatterRadius,
            scatterSec,
            flySec,
            perIconDelay,
            $"+{deltaMoney}",
            new Color(1f, 0.92f, 0.25f, 1f)
        ));

        AddMoney(deltaMoney);
    }

    public void PlayNegEntropyBurst(Vector3 sourceWorldPos, int deltaNE, Camera worldCamera)
    {
        if (debugLogs)
            Debug.Log($"[M6][HUD][NEBurst] delta={deltaNE} inPlayback={_inPlayback} neSprite={(iconLibrary!=null?iconLibrary.negEntropySprite!=null:false)} src=({sourceWorldPos.x:0.##},{sourceWorldPos.y:0.##})", this);

        if (!_inPlayback || deltaNE == 0) return;
        if (iconLibrary == null || iconLibrary.negEntropySprite == null)
        {
            Debug.LogError("[M6][HUD] Missing iconLibrary.negEntropySprite - cannot render NE.", this);
            return;
        }

        float scatterSec = NeScatterSecondsBase * playbackTimeScale;
        float flySec = NeFlySecondsBase * playbackTimeScale;
        float perIconDelay = NePerIconDelayBase * playbackTimeScale;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            negEntropyIconAnchor,
            iconLibrary.negEntropySprite,
            NeBurstCount,
            NeScatterRadius,
            scatterSec,
            flySec,
            perIconDelay,
            $"+{deltaNE}",
            new Color(0.40f, 0.95f, 0.95f, 1f)
        ));

        AddNegEntropy(deltaNE);
    }

    // ---------- Internal visuals ----------

    private IEnumerator PlayBurstCoroutine(
        Vector3 sourceWorldPos,
        Camera worldCamera,
        RectTransform targetAnchor,
        Sprite sprite,
        int count,
        float scatterRadius,
        float scatterSeconds,
        float flySeconds,
        float perIconStartDelay,
        string popText,
        Color popColor)
    {
        if (flyIconLayer == null || targetAnchor == null || flyIconPrefab == null)
        {
            if (debugLogs) Debug.Log($"[M6][HUD][Burst] abort missing bindings flyLayer={(flyIconLayer!=null)} target={(targetAnchor!=null)} prefab={(flyIconPrefab!=null)}", this);
            yield break;
        }

        if (!TryWorldToFlyLayerLocal(sourceWorldPos, worldCamera, out var fromLocal))
        {
            if (debugLogs) Debug.Log("[M6][HUD][Burst] abort WorldToFlyLayerLocal failed", this);
            yield break;
        }
        if (!TryRectToFlyLayerLocal(targetAnchor, out var toLocal))
        {
            if (debugLogs) Debug.Log("[M6][HUD][Burst] abort RectToFlyLayerLocal failed", this);
            yield break;
        }

        if (debugLogs)
            Debug.Log($"[M6][HUD][Burst] count={count} scatter={scatterSeconds:0.##} fly={flySeconds:0.##} from=({fromLocal.x:0.##},{fromLocal.y:0.##}) to=({toLocal.x:0.##},{toLocal.y:0.##})", this);

        SpawnBurstText(fromLocal, popText, popColor);

        int n = Mathf.Max(1, count);
        for (int i = 0; i < n; i++)
        {
            var rt = AcquireFlyIcon();
            if (rt == null) continue;

            var img = rt.GetComponentInChildren<Image>(true);
            if (img != null)
            {
                img.enabled = true;
                img.raycastTarget = false;
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, 1f);
                img.sprite = sprite;
            }

            // CanvasGroup force visible
            var cg = rt.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
            rt.gameObject.SetActive(true);
            rt.transform.SetAsLastSibling();

            if (rt != null)
            {
                if (rt.rect.width < 1f || rt.rect.height < 1f)
                    rt.sizeDelta = new Vector2(burstIconMinSize, burstIconMinSize);
                rt.localScale = Vector3.one * burstIconScale;
                rt.anchoredPosition = fromLocal;
            }

            // Deterministic scatter offsets (ring distribution).
            float angle = (n == 1) ? 0f : (i * (Mathf.PI * 2f / n));
            float r = Mathf.Max(0f, scatterRadius);
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

            float delay = perIconStartDelay * i;
            StartCoroutine(ScatterThenFly(rt, fromLocal, offset, scatterSeconds, toLocal, flySeconds, delay));
        }

        yield return null;
    }

    private IEnumerator ScatterThenFly(RectTransform rt, Vector2 from, Vector2 scatterOffset, float scatterSeconds, Vector2 to, float flySeconds, float delay)
    {
        if (rt == null) yield break;
        if (delay > 0f)
        {
            float dt = 0f;
            while (dt < delay)
            {
                if (rt == null) yield break;
                dt += Time.deltaTime;
                yield return null;
            }
        }

        // Scatter (jump out)
        float t = 0f;
        float dur = Mathf.Max(0.01f, scatterSeconds);
        Vector2 p0 = from;
        Vector2 p1 = from + scatterOffset;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);
            rt.anchoredPosition = Vector2.Lerp(p0, p1, s);
            yield return null;
        }
        if (rt == null) yield break;
        rt.anchoredPosition = p1;

        // Fly to HUD
        yield return FlyOne(rt, p1, to, flySeconds);
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
            rt.anchoredPosition = Vector2.Lerp(from, to, s);
            yield return null;
        }
        if (rt == null) yield break;
        rt.anchoredPosition = to;
        ReleaseFlyIcon(rt);
    }

    private RectTransform AcquireFlyIcon()
    {
        while (_flyIconPool.Count > 0)
        {
            var pooled = _flyIconPool.Dequeue();
            if (pooled != null)
                return pooled;
        }

        if (flyIconPrefab == null || flyIconLayer == null) return null;

        var go = Instantiate(flyIconPrefab, flyIconLayer);
        go.name = "FlyIcon";
        var rt = go.transform as RectTransform;
        if (rt == null) rt = go.GetComponent<RectTransform>();
        return rt;
    }

    private void ReleaseFlyIcon(RectTransform rt)
    {
        if (rt == null) return;
        rt.gameObject.SetActive(false);
        rt.SetParent(flyIconLayer, false);
        _flyIconPool.Enqueue(rt);
    }

    private void PrewarmFlyIcons()
    {
        if (flyIconPrefab == null || flyIconLayer == null) return;
        int n = Mathf.Max(0, prewarmFlyIconCount);
        for (int i = 0; i < n; i++)
        {
            var rt = AcquireFlyIcon();
            if (rt == null) break;
            ReleaseFlyIcon(rt);
        }
    }

    private void AddMoney(int delta)
    {
        _moneyTarget = Mathf.Max(0, _moneyTarget + delta);
        if (_moneyRollCo != null) StopCoroutine(_moneyRollCo);
        _moneyRollCo = StartCoroutine(RollMoneyCoroutine(MoneyRollSecondsBase * playbackTimeScale));

        if (_moneyPunchCo != null) StopCoroutine(_moneyPunchCo);
        _moneyPunchCo = StartCoroutine(PunchText(moneyText != null ? moneyText.rectTransform : null, MoneyPunchScale, MoneyPunchSecondsBase * playbackTimeScale));
    }

    private void AddNegEntropy(int delta)
    {
        _neTarget = Mathf.Max(0, _neTarget + delta);
        if (_neRollCo != null) StopCoroutine(_neRollCo);
        _neRollCo = StartCoroutine(RollNegEntropyCoroutine(NeRollSecondsBase * playbackTimeScale));

        if (_nePunchCo != null) StopCoroutine(_nePunchCo);
        _nePunchCo = StartCoroutine(PunchText(negEntropyText != null ? negEntropyText.rectTransform : null, NePunchScale, NePunchSecondsBase * playbackTimeScale));
    }

    private IEnumerator RollMoneyCoroutine(float seconds)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, seconds);
        int from = _moneyDisplay;
        int to = _moneyTarget;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);
            _moneyDisplay = Mathf.RoundToInt(Mathf.Lerp(from, to, s));
            ApplyMoneyText(_moneyDisplay);
            yield return null;
        }
        _moneyDisplay = to;
        ApplyMoneyText(_moneyDisplay);
    }

    private IEnumerator RollNegEntropyCoroutine(float seconds)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, seconds);
        int from = _neDisplay;
        int to = _neTarget;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);
            _neDisplay = Mathf.RoundToInt(Mathf.Lerp(from, to, s));
            ApplyNegEntropyText(_neDisplay);
            yield return null;
        }
        _neDisplay = to;
        ApplyNegEntropyText(_neDisplay);
    }

    private static IEnumerator PunchText(RectTransform rt, float scaleMul, float seconds)
    {
        if (rt == null) yield break;
        Vector3 s0 = rt.localScale;
        Vector3 s1 = s0 * Mathf.Max(1f, scaleMul);
        float t = 0f;
        float dur = Mathf.Max(0.01f, seconds);
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // up then down
            float tri = k < 0.5f ? (k / 0.5f) : (1f - (k - 0.5f) / 0.5f);
            float s = Mathf.SmoothStep(0f, 1f, tri);
            rt.localScale = Vector3.Lerp(s0, s1, s);
            yield return null;
        }
        if (rt != null) rt.localScale = s0;
    }

    private void ApplyMoneyText(int value)
    {
        if (moneyText) moneyText.text = value.ToString();
    }

    private void ApplyNegEntropyText(int value)
    {
        if (negEntropyText) negEntropyText.text = value.ToString();
    }

    // ---------- Coordinate conversion helpers ----------

    private bool TryWorldToFlyLayerLocal(Vector3 worldPos, Camera worldCamera, out Vector2 local)
    {
        local = default;
        var canvas = flyIconLayer != null ? flyIconLayer.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return false;

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            var screen = worldCamera != null ? (Vector2)worldCamera.WorldToScreenPoint(worldPos) : (Vector2)Camera.main.WorldToScreenPoint(worldPos);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, null, out local);
        }
        else
        {
            var cam = canvas.worldCamera;
            if (cam == null) cam = worldCamera;
            if (cam == null) cam = Camera.main;
            var screen = cam.WorldToScreenPoint(worldPos);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, cam, out local);
        }
    }

    private bool TryRectToFlyLayerLocal(RectTransform target, out Vector2 local)
    {
        local = default;
        if (flyIconLayer == null || target == null) return false;
        var canvas = flyIconLayer.GetComponentInParent<Canvas>();
        if (canvas == null) return false;

        Vector3 world = target.TransformPoint(target.rect.center);

        Camera cam = null;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, cam, out local);
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

        StartCoroutine(RiseFadeText(tmp, rt, burstTextSeconds * playbackTimeScale, burstTextRise));
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
}
