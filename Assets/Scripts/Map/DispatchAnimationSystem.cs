using System.Collections;
using System.Collections.Generic;
using Core;
using UnityEngine;

public class DispatchAnimationSystem : MonoBehaviour
{
    public static DispatchAnimationSystem I { get; private set; }

    [Header("Refs")]
    [SerializeField] private RectTransform tokenLayer;
    [SerializeField] private GameObject agentPrefab;

    [Header("Config")]
    [SerializeField] private float baseSpawnRadius = 5f;
    [SerializeField] private float fallbackTravelSeconds = 0.45f; // token travel duration

    // “Node” 在这里等同 “City anchor”（地图上的城市点）
    private readonly Dictionary<string, RectTransform> _cities = new();
    // marker key 仍为 cityId:defId（与你当前 AnomalySpawner.RegisterAnomaly(nodeId, defId, rt) 一致）
    private readonly Dictionary<string, RectTransform> _anomalyMarkers = new();

    private readonly HashSet<string> _offBaseAgents = new();
    private readonly HashSet<string> _missingCityWarned = new();

    private string _cachedBaseCityId;
    private bool _citiesScanned;

    private bool _hudLocked;

    // Token-based interaction lock
    private Coroutine _tokenCo;
    private Core.MovementToken _playingToken;

    public bool IsInteractionLocked => _tokenCo != null;

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
        Debug.Log("[MapUI] DispatchAnimationSystem OnEnable");
    }

    private void OnDisable()
    {
        SetHudLocked(false);
        Debug.Log("[MapUI] DispatchAnimationSystem OnDisable");
    }

    private void Update()
    {
        if (_tokenCo != null) return;

        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        var token = FindFirstPendingToken(gc.State);
        if (token == null) return;

        _tokenCo = StartCoroutine(ConsumeTokenCoroutine(gc, token));
    }

    // HUD 在 EndDay 后会调用它。现在无 task 队列，改为：尝试立刻播放一个 pending token。
    public void PlayPending()
    {
        if (_tokenCo != null) return;

        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        var token = FindFirstPendingToken(gc.State);
        if (token == null) return;

        _tokenCo = StartCoroutine(ConsumeTokenCoroutine(gc, token));
    }

    // 兼容旧调用：City.cs 仍在调用 RegisterNode(cityId, rt)
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

        // 优先从场景里找 base 城市点（CityType == 0）
        var mapBase = FindBaseCityOnMap();
        if (!string.IsNullOrEmpty(mapBase))
        {
            _cachedBaseCityId = mapBase;
            return _cachedBaseCityId;
        }

        // 再从 state 找（CityState.Type == 0）
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

    private IEnumerator PlayTokenAnimationOrFallback(GameController gc, Core.MovementToken token)
    {
        if (gc == null || token == null)
        {
            yield return new WaitForSeconds(fallbackTravelSeconds);
            yield break;
        }

        var anom = Core.DispatchSystem.FindAnomaly(gc.State, token.AnomalyInstanceId);
        var cityId = anom?.NodeId; // 这里的 NodeId 当前就是“锚点城市 Id”（M2 会坐标统一）
        var anomalyDefIdForMarker = anom?.AnomalyDefId ?? anom?.ManagedState?.AnomalyDefId;

        var baseCityId = ResolveBaseCityId();

        Vector2 baseLocal = Vector2.zero;
        bool haveBase = !string.IsNullOrEmpty(baseCityId) && TryGetCityLocalPoint(baseCityId, out baseLocal);

        Vector2 anomLocal = Vector2.zero;
        bool haveAnom = false;

        // 优先找 anomaly marker（cityId:defId），找不到就退回 city anchor 点
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

        Vector2 fromLocal, toLocal;
        if (token.Type == Core.MovementTokenType.Dispatch)
        {
            fromLocal = ApplyBaseSpawnOffset(baseCityId, baseLocal);
            toLocal = anomLocal;
        }
        else
        {
            fromLocal = anomLocal;
            toLocal = baseLocal;
        }

        yield return AnimateAgent(
            "token:" + token.TokenId,
            1, 1,
            fromLocal,
            toLocal,
            Mathf.Max(0.01f, fallbackTravelSeconds),
            null
        );
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

    private IEnumerator AnimateAgent(string debugId, int index, int total, Vector2 startPos, Vector2 toLocal, float duration, System.Action onComplete)
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

    private void SetHudLocked(bool locked)
    {
        if (_hudLocked == locked) return;
        _hudLocked = locked;
        HUD.I?.SetControlsInteractable(!locked);
    }
}
