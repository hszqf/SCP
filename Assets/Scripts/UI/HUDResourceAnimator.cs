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

    [Header("Config")]
    [SerializeField] private float numberRollSeconds = 0.45f;

    [Header("Money")]
    [SerializeField] private float coinFlySeconds = 0.65f;
    [SerializeField] private int coinBurstCount = 6;

    [Header("NegEntropy")]
    [SerializeField] private float neFlySeconds = 0.65f;
    [SerializeField] private int neBurstCount = 6;

    public float CoinFlySeconds => coinFlySeconds;

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

        if (flyIconPrefab != null && flyIconPrefab.GetComponentInChildren<Image>(true) == null)
        {
            Debug.LogError("[HUDResourceAnimator] flyIconPrefab must contain an Image.", this);
        }
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

        _moneyDisplay = _moneyTarget = Mathf.Max(0, money);
        _neDisplay = _neTarget = Mathf.Max(0, negEntropy);

        ApplyMoneyText(_moneyDisplay);
        ApplyNegEntropyText(_neDisplay);
    }

    public void EndPlaybackSnapshot()
    {
        _inPlayback = false;
        // After commit, HUD.Refresh() will overwrite texts via OnStateChanged.
    }

    // ---------- Public API called by playback ----------

    public void PlayMoneyBurst(Vector3 sourceWorldPos, int deltaMoney, Camera worldCamera)
    {
        if (!_inPlayback || deltaMoney == 0) return;
        if (iconLibrary == null) return;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            moneyIconAnchor,
            iconLibrary.coinSprite,
            Mathf.Max(1, coinBurstCount),
            Mathf.Max(0.01f, coinFlySeconds)
        ));

        AddMoney(deltaMoney);
    }

    public void PlayNegEntropyBurst(Vector3 sourceWorldPos, int deltaNE, Camera worldCamera)
    {
        if (!_inPlayback || deltaNE == 0) return;
        if (iconLibrary == null) return;

        StartCoroutine(PlayBurstCoroutine(
            sourceWorldPos,
            worldCamera,
            negEntropyIconAnchor,
            iconLibrary.negEntropySprite,
            Mathf.Max(1, neBurstCount),
            Mathf.Max(0.01f, neFlySeconds)
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
                seconds: numberRollSeconds,
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
                seconds: numberRollSeconds,
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

    private IEnumerator PlayBurstCoroutine(Vector3 sourceWorldPos, Camera worldCamera, RectTransform targetAnchor, Sprite sprite, int count, float flySeconds)
    {
        if (flyIconLayer == null || targetAnchor == null || flyIconPrefab == null) yield break;

        // Convert source world pos to screen, then to flyLayer local.
        if (!TryWorldToFlyLayerLocal(sourceWorldPos, worldCamera, out var fromLocal)) yield break;
        if (!TryRectToFlyLayerLocal(targetAnchor, out var toLocal)) yield break;

        // Deterministic offsets.
        int n = Mathf.Max(1, count);
        for (int i = 0; i < n; i++)
        {
            var go = Instantiate(flyIconPrefab, flyIconLayer);
            var rt = go.transform as RectTransform;
            if (rt == null) rt = go.GetComponent<RectTransform>();

            var img = go.GetComponentInChildren<Image>(true);
            if (img != null) img.sprite = sprite;

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
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = Mathf.SmoothStep(0f, 1f, k);
            rt.anchoredPosition = Vector2.Lerp(from, to, s);
            yield return null;
        }

        rt.anchoredPosition = to;
        Destroy(rt.gameObject);
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

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(flyIconLayer, screen, layerCam, out local);
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
