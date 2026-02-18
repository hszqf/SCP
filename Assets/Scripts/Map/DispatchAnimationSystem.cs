using System.Collections;
using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.UI;


public class DispatchAnimationSystem : MonoBehaviour
{
    public static DispatchAnimationSystem I { get; private set; }

    [Header("Refs")]
    [SerializeField] private RectTransform tokenLayer;
    [SerializeField] private GameObject agentPrefab;

    [Header("Config")]
    [SerializeField] private float baseSpawnRadius = 5f;
    [SerializeField] private float fallbackTravelSeconds = 0.45f; // token travel duration

    // ��Node�� �������ͬ ��City anchor������ͼ�ϵĳ��е㣩
    private readonly Dictionary<string, RectTransform> _cities = new();
    // marker key ��Ϊ cityId:defId�����㵱ǰ AnomalySpawner.RegisterAnomaly(nodeId, defId, rt) һ�£�
    private readonly Dictionary<string, RectTransform> _anomalyMarkers = new();

    private readonly HashSet<string> _offBaseAgents = new();
    private readonly HashSet<string> _missingCityWarned = new();

    private string _cachedBaseCityId;
    private bool _citiesScanned;

    private bool _hudLocked;

    private bool _gcHooked;

    private void HookGameController()
    {
        if (_gcHooked) return;
        var gc = GameController.I;
        if (gc == null) return;
        gc.OnStateChanged += OnGameStateChanged;
        _gcHooked = true;
    }

    private void UnhookGameController()
    {
        if (!_gcHooked) return;
        var gc = GameController.I;
        if (gc == null) return;
        gc.OnStateChanged -= OnGameStateChanged;
        _gcHooked = false;
    }

    private void OnGameStateChanged()
    {
        SyncOffBaseAgents();
    }

    // Token-based interaction lock
    private Coroutine _tokenCo;
    private Core.MovementToken _playingToken;

    private bool _externalLocked;
    public bool IsInteractionLocked => _tokenCo != null || _externalLocked;

    public int GetVisualAvailableAgentCount()
    {
        var gc = GameController.I;
        if (gc?.State?.Agents == null) return 0;

        int count = 0;
        foreach (var agent in gc.State.Agents)
        {
            if (agent == null || agent.IsDead || agent.IsInsane) continue;
            if (_offBaseAgents.Contains(agent.Id)) continue;
            count += 1;
        }
        return count;
    }

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
        Debug.Log("[MapUI] DispatchAnimationSystem Awake");
    }

    private void OnEnable()
    {
        SyncOffBaseAgents();
        EnsureCityRegistry();
        HookGameController();
        Debug.Log("[MapUI] DispatchAnimationSystem OnEnable");
    }

    private void OnDisable()
    {
        UnhookGameController();
        SetHudLocked(false);
        Debug.Log("[MapUI] DispatchAnimationSystem OnDisable");
    }

    private void Update()
    {
        
        HookGameController();
if (_tokenCo != null) return;

        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        var token = FindFirstPendingToken(gc.State);
        if (token == null) return;

        _tokenCo = StartCoroutine(ConsumeTokenCoroutine(gc, token));
    }

    // HUD �� EndDay ���������������� task ���У���Ϊ���������̲���һ�� pending token��
    public void PlayPending()
    {
        if (_tokenCo != null) return;

        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        var token = FindFirstPendingToken(gc.State);
        if (token == null) return;

        _tokenCo = StartCoroutine(ConsumeTokenCoroutine(gc, token));
    }


    public void SetExternalInteractionLocked(bool locked)
    {
        _externalLocked = locked;
        // ע�⣺token ����ʱҲ���� HUD����������Ҫ OR һ��
        SetHudLocked(locked || _tokenCo != null);
    }

    /// <summary>
    /// v0�����Ӿ� Recall�������� token������ GameState��
    /// �� anomaly marker / city anchor �ɻ� base city anchor���� count ��С��
    /// </summary>
    public IEnumerator PlayVisualRecallCoroutine(string anomalyInstanceId, int count, float durationOverride = -1f)
    {
        var gc = GameController.I;
        if (gc == null || gc.State == null)
        {
            yield return new WaitForSeconds(fallbackTravelSeconds);
            yield break;
        }

        var anom = Core.DispatchSystem.FindAnomaly(gc.State, anomalyInstanceId);
        var cityId = anom?.NodeId;
        var anomalyDefIdForMarker = anom?.AnomalyDefId ?? anom?.ManagedState?.AnomalyDefId;

        var baseCityId = ResolveBaseCityId();

        Vector2 baseLocal = Vector2.zero;
        bool haveBase = !string.IsNullOrEmpty(baseCityId) && TryGetCityLocalPoint(baseCityId, out baseLocal);

        Vector2 anomLocal = Vector2.zero;
        bool haveAnom = false;

        if (!string.IsNullOrEmpty(cityId) && !string.IsNullOrEmpty(anomalyDefIdForMarker))
        {
            var key = BuildMarkerKey(cityId, anomalyDefIdForMarker);
            if (_anomalyMarkers.TryGetValue(key, out var markerRT) && markerRT != null)
                haveAnom = TryGetLocalPoint(markerRT, out anomLocal);
        }
        if (!haveAnom && !string.IsNullOrEmpty(cityId))
            haveAnom = TryGetCityLocalPoint(cityId, out anomLocal);

        if (!haveBase || !haveAnom)
        {
            yield return new WaitForSeconds(fallbackTravelSeconds);
            yield break;
        }

        float duration = durationOverride > 0f ? durationOverride : fallbackTravelSeconds;
        int n = Mathf.Max(1, count);

        // deterministic offsets������ Random Ӱ��ɸ�����־�����Ӿ�Ҳ�����ȣ�
        for (int i = 0; i < n; i++)
        {
            float angle = (n == 1) ? 0f : (i * (Mathf.PI * 2f / n));
            float r = Mathf.Min(8f, 2f + n * 0.6f);
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

            StartCoroutine(AnimateAgent(
                debugId: $"plan_recall:{anomalyInstanceId}:{i}",
                index: i + 1,
                total: n,
                startPos: anomLocal + offset,
                toLocal: baseLocal,
                duration: Mathf.Max(0.01f, duration),
                onComplete: null
            ));
        }

        yield return new WaitForSeconds(duration);
    }

    // ���ݾɵ��ã�City.cs ���ڵ��� RegisterNode(cityId, rt)
    public void RegisterNode(string nodeId, RectTransform nodeRT) => RegisterCity(nodeId, nodeRT);

    public void RegisterCity(string cityId, RectTransform cityRT)
    {
        if (string.IsNullOrEmpty(cityId) || cityRT == null) return;
        _cities[cityId] = cityRT;

        if (!string.IsNullOrEmpty(_cachedBaseCityId) &&
            string.Equals(_cachedBaseCityId, cityId, System.StringComparison.OrdinalIgnoreCase))
        {
            _missingCityWarned.Remove("__base__");
        }
    }

    public void RegisterAnomaly(string nodeId, string anomalyId, RectTransform anomalyRT) => RegisterCityAnomaly(nodeId, anomalyId, anomalyRT);

    public void RegisterCityAnomaly(string cityId, string anomalyDefId, RectTransform anomalyRT)
    {
        if (string.IsNullOrEmpty(cityId) || string.IsNullOrEmpty(anomalyDefId) || anomalyRT == null) return;
        _anomalyMarkers[BuildMarkerKey(cityId, anomalyDefId)] = anomalyRT;
    }

    private void SyncOffBaseAgents()
    {
        _offBaseAgents.Clear();

        var gc = GameController.I;
        var agents = gc?.State?.Agents;
        if (agents == null) return;

        for (int i = 0; i < agents.Count; i++)
        {
            var ag = agents[i];
            if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;

            if (ag.LocationKind != Core.AgentLocationKind.Base)
                _offBaseAgents.Add(ag.Id);
        }
    }

    private void EnsureCityRegistry()
    {
        if (_citiesScanned) return;
        _citiesScanned = true;

        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[MapUI] Dispatch city scan count={cities?.Length ?? 0}");

        if (cities == null || cities.Length == 0) return;

        foreach (var city in cities)
        {
            if (city == null || string.IsNullOrEmpty(city.CityId))
                continue;

            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                _cities[city.CityId] = rt;
                Debug.Log($"[MapUI] Dispatch city registered cityId={city.CityId}");
            }
        }
    }

    private void TryRegisterCityFromScene(string cityId)
    {
        if (string.IsNullOrEmpty(cityId)) return;

        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cities == null || cities.Length == 0) return;

        foreach (var city in cities)
        {
            if (city == null || !string.Equals(city.CityId, cityId, System.StringComparison.OrdinalIgnoreCase))
                continue;

            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                _cities[city.CityId] = rt;
                Debug.Log($"[MapUI] Dispatch city registered cityId={city.CityId}");
            }
            return;
        }
    }

    private bool TryGetCityLocalPoint(string cityId, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (string.IsNullOrEmpty(cityId)) return false;

        EnsureCityRegistry();

        if (!_cities.TryGetValue(cityId, out var cityRT) || cityRT == null)
        {
            if (_missingCityWarned.Add(cityId))
                Debug.LogWarning($"[MapUI] Dispatch city missing cityId={cityId}");
            return false;
        }

        return TryGetLocalPoint(cityRT, out localPoint);
    }

    private bool TryGetLocalPoint(RectTransform rt, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (rt == null || tokenLayer == null) return false;

        var screenPoint = RectTransformUtility.WorldToScreenPoint(null, rt.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(tokenLayer, screenPoint, null, out localPoint);
    }

    private static string BuildMarkerKey(string cityId, string anomalyDefId)
        => $"{cityId}:{anomalyDefId}";

    private Vector2 ApplyBaseSpawnOffset(string fromCityId, Vector2 origin)
    {
        var baseId = ResolveBaseCityId();
        if (string.IsNullOrEmpty(baseId) || !string.Equals(fromCityId, baseId, System.StringComparison.OrdinalIgnoreCase))
            return origin;

        if (baseSpawnRadius <= 0f) return origin;
        return origin + Random.insideUnitCircle * baseSpawnRadius;
    }

    private string ResolveBaseCityId()
    {
        if (!string.IsNullOrEmpty(_cachedBaseCityId))
            return _cachedBaseCityId;

        // ���ȴӳ������� base ���е㣨CityType == 0��
        var mapBase = FindBaseCityOnMap();
        if (!string.IsNullOrEmpty(mapBase))
        {
            _cachedBaseCityId = mapBase;
            return _cachedBaseCityId;
        }

        // �ٴ� state �ң�CityState.Type == 0��
        var gc = GameController.I;
        var baseCity = gc?.State?.Cities?.Find(c => c != null && c.Type == 0);
        if (baseCity == null)
        {
            if (_missingCityWarned.Add("__base__"))
                Debug.LogWarning("[MapUI] Dispatch base city missing (type=0)");
            return null;
        }

        if (!_cities.ContainsKey(baseCity.Id))
            TryRegisterCityFromScene(baseCity.Id);

        _cachedBaseCityId = baseCity.Id;
        return _cachedBaseCityId;
    }

    private string FindBaseCityOnMap()
    {
        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cities == null || cities.Length == 0) return null;

        City baseCity = null;
        foreach (var city in cities)
        {
            if (city == null || city.CityType != 0 || string.IsNullOrEmpty(city.CityId))
                continue;
            baseCity = city;
            break;
        }

        if (baseCity == null) return null;

        if (!_cities.ContainsKey(baseCity.CityId))
        {
            var rt = baseCity.transform as RectTransform;
            if (rt != null)
                _cities[baseCity.CityId] = rt;
        }

        return baseCity.CityId;
    }

    private Core.MovementToken FindFirstPendingToken(Core.GameState s)
    {
        if (s?.MovementTokens == null) return null;
        for (int i = 0; i < s.MovementTokens.Count; i++)
        {
            var t = s.MovementTokens[i];
            if (t != null && t.State == Core.MovementTokenState.Pending)
                return t;
        }
        return null;
    }

    private IEnumerator ConsumeTokenCoroutine(GameController gc, Core.MovementToken token)
    {
        _playingToken = token;
        token.State = Core.MovementTokenState.Playing;

        SetHudLocked(true);

        // 1) animation / fallback wait
        yield return PlayTokenAnimationOrFallback(gc, token);

        // 2) landing
        ApplyTokenLanding(gc, token);

        token.State = Core.MovementTokenState.Completed;

        if (gc.State != null && gc.State.MovementLockCount > 0)
            gc.State.MovementLockCount -= 1;

        SyncOffBaseAgents();
        gc?.Notify();

        SetHudLocked(false);

        _playingToken = null;
        _tokenCo = null;
    }

    

private bool TryResolveTravelAnchors(Core.GameState state, string anomalyInstanceId, out Vector2 baseLocal, out Vector2 anomLocal)
{
    baseLocal = Vector2.zero;
    anomLocal = Vector2.zero;

    if (state == null) return false;

    var baseCityId = ResolveBaseCityId();
    if (string.IsNullOrEmpty(baseCityId) || !TryGetCityLocalPoint(baseCityId, out baseLocal))
        return false;

    var anom = Core.DispatchSystem.FindAnomaly(state, anomalyInstanceId);
    var cityId = anom?.NodeId; // current NodeId == CityId anchor
    var anomalyDefIdForMarker = anom?.AnomalyDefId ?? anom?.ManagedState?.AnomalyDefId;

    bool haveAnom = false;

    // prefer anomaly marker; fallback to city anchor
    if (!string.IsNullOrEmpty(cityId) && !string.IsNullOrEmpty(anomalyDefIdForMarker))
    {
        var key = BuildMarkerKey(cityId, anomalyDefIdForMarker);
        if (_anomalyMarkers.TryGetValue(key, out var markerRT) && markerRT != null)
            haveAnom = TryGetLocalPoint(markerRT, out anomLocal);
    }
    if (!haveAnom && !string.IsNullOrEmpty(cityId))
        haveAnom = TryGetCityLocalPoint(cityId, out anomLocal);

    return haveAnom;
}

/// <summary>
/// Unified travel visual for both dispatch (Base->Anomaly) and recall (Anomaly->Base).
/// Pure visual: does NOT create tokens and does NOT change GameState.
/// </summary>
public IEnumerator PlayVisualTravelOne(string anomalyInstanceId, string agentId, bool toAnomaly, float durationOverride = -1f)
{
    var gc = GameController.I;
    var state = gc?.State;

    if (state == null)
    {
        yield return new WaitForSeconds(fallbackTravelSeconds);
        yield break;
    }

    if (!TryResolveTravelAnchors(state, anomalyInstanceId, out var baseLocal, out var anomLocal))
    {
        yield return new WaitForSeconds(fallbackTravelSeconds);
        yield break;
    }

    var sprite = ResolveAgentAvatarSprite(state, agentId);
    float duration = durationOverride > 0f ? durationOverride : fallbackTravelSeconds;

    // stable offsets (avoid overlap & keep dispatch/recall consistent)
    Vector2 baseOffset = StableOffset(agentId, Mathf.Max(0f, baseSpawnRadius));
    Vector2 anomOffset = StableOffset(agentId, 6f);

    Vector2 startPos = toAnomaly ? (baseLocal + baseOffset) : (anomLocal + anomOffset);
    Vector2 endPos   = toAnomaly ? (anomLocal + anomOffset) : (baseLocal + baseOffset);

    yield return AnimateAgent(
        debugId: $"travel:{(toAnomaly ? "toA" : "toB")}:{anomalyInstanceId}:{agentId}",
        index: 1,
        total: 1,
        startPos: startPos,
        toLocal: endPos,
        duration: Mathf.Max(0.01f, duration),
        onComplete: null,
        avatarSprite: sprite
    );
}

private IEnumerator PlayTokenAnimationOrFallback(GameController gc, Core.MovementToken token)
{
    if (gc == null || token == null)
    {
        yield return new WaitForSeconds(fallbackTravelSeconds);
        yield break;
    }

    bool toAnomaly = token.Type == Core.MovementTokenType.Dispatch;
    yield return PlayVisualTravelOne(token.AnomalyInstanceId, token.AgentId, toAnomaly, fallbackTravelSeconds);
}

private void ApplyTokenLanding(GameController gc, Core.MovementToken token)
    {
        var s = gc?.State;
        if (s == null || s.Agents == null) return;

        Core.AgentState ag = null;
        for (int i = 0; i < s.Agents.Count; i++)
        {
            var a = s.Agents[i];
            if (a != null && a.Id == token.AgentId) { ag = a; break; }
        }
        if (ag == null) return;

        if (token.Type == Core.MovementTokenType.Dispatch)
        {
            if (ag.LocationKind == Core.AgentLocationKind.TravellingToAnomaly &&
                ag.LocationAnomalyInstanceId == token.AnomalyInstanceId)
            {
                ag.LocationKind = Core.AgentLocationKind.AtAnomaly;
            }
            else
            {
                ag.LocationKind = Core.AgentLocationKind.AtAnomaly;
                ag.LocationAnomalyInstanceId = token.AnomalyInstanceId;
            }
            ag.LocationSlot = token.Slot;
        }
        else
        {
            ag.LocationKind = Core.AgentLocationKind.Base;
            ag.LocationAnomalyInstanceId = null;
            ag.LocationSlot = token.Slot;
        }
    }

    private IEnumerator AnimateAgent(string debugId, int index, int total, Vector2 startPos, Vector2 toLocal, float duration, System.Action onComplete, Sprite avatarSprite = null)
    {
        if (!tokenLayer || !agentPrefab)
        {
            yield return new WaitForSeconds(duration);
            onComplete?.Invoke();
            yield break;
        }

        var go = Instantiate(agentPrefab, tokenLayer);
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = startPos;
        var img = go.GetComponentInChildren<Image>(true) ?? go.GetComponent<Image>();
        if (img != null && avatarSprite != null)
            img.sprite = avatarSprite;


        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(startPos, toLocal, smooth);

            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = toLocal;
        Destroy(rt.gameObject);
        onComplete?.Invoke();
    }


    
public IEnumerator PlayVisualRecallOne(string anomalyInstanceId, string agentId, float durationOverride = -1f)
{
    yield return PlayVisualTravelOne(anomalyInstanceId, agentId, toAnomaly: false, durationOverride: durationOverride);
}

public IEnumerator PlayVisualDispatchOne(string anomalyInstanceId, string agentId, float durationOverride = -1f)
{
    yield return PlayVisualTravelOne(anomalyInstanceId, agentId, toAnomaly: true, durationOverride: durationOverride);
}

private static Vector2 StableOffset(string s, float radius)
    {
        int h = 23;
        unchecked
        {
            if (!string.IsNullOrEmpty(s))
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
        }
        // 0..1
        float u = Mathf.Abs(h % 1000) / 1000f;
        float ang = u * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Mathf.Max(0f, radius);
    }

    private static Sprite[] _avatarPool;
    
    private static Sprite ResolveAgentAvatarSprite(Core.GameState state, string agentId)
        {
            const string AvatarResourcePath = "Avatar";
            const string AvatarSpriteSheetName = "Avatar";

            if (state?.Agents == null || string.IsNullOrEmpty(agentId)) return null;

            var ag = state.Agents.Find(a => a != null && a.Id == agentId);
            if (ag == null) return null;

            // try direct sprite by id/name first (to match Anomaly UI)
            Sprite sprite = null;
            if (!string.IsNullOrEmpty(agentId))
                sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{agentId}");
            if (sprite == null && !string.IsNullOrEmpty(ag.Name))
                sprite = Resources.Load<Sprite>($"{AvatarResourcePath}/{ag.Name}");
            if (sprite != null) return sprite;

            if (_avatarPool == null)
            {
                _avatarPool = Resources.LoadAll<Sprite>(AvatarResourcePath) ?? System.Array.Empty<Sprite>();
                if (_avatarPool.Length == 0)
                    _avatarPool = Resources.LoadAll<Sprite>($"{AvatarResourcePath}/{AvatarSpriteSheetName}") ?? System.Array.Empty<Sprite>();
                if (_avatarPool.Length == 0)
                    _avatarPool = System.Array.Empty<Sprite>();
            }
            if (_avatarPool.Length == 0) return null;

            int seed = ag.AvatarSeed;
            if (seed < 0)
            {
                var key = !string.IsNullOrEmpty(agentId) ? agentId : ag.Name;
                seed = string.IsNullOrEmpty(key) ? 0 : key.GetHashCode();
                ag.AvatarSeed = seed;
            }

            int idx = Mathf.Abs(seed) % _avatarPool.Length;
            return _avatarPool[idx];
        }


    private void SetHudLocked(bool locked)
    {
        if (_hudLocked == locked) return;
        _hudLocked = locked;
        HUD.I?.SetControlsInteractable(!locked);
    }


}

