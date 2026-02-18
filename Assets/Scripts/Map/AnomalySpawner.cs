using System.Collections.Generic;
using Core;
using UnityEngine;

public class AnomalySpawner : MonoBehaviour
{
    public static AnomalySpawner I { get; private set; }

    [SerializeField] private RectTransform nodeLayer;    // NodeLayer
    public RectTransform NodeLayer => nodeLayer;

    [Header("Anomalies")]
    [SerializeField] private RectTransform anomalyLayer;
    [SerializeField] private GameObject anomalyPrefab;
    [SerializeField] private float anomalySpawnRadiusMin = 8f;
    [SerializeField] private float anomalySpawnRadiusMax = 24f;

    private Canvas _anomalyCanvas;

    //private readonly Dictionary<string, RectTransform> _cityRects = new();
    private readonly Dictionary<string, RectTransform> _cityRects =
    new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Anomaly> _anomalies = new();
    private readonly Dictionary<string, Vector2> _anomalyOffsets = new();
    private readonly Dictionary<string, Vector2> _anomalyAnchorPositions = new();
    private readonly Dictionary<string, string> _anomalyAnchorNodeIds = new();

    private void Awake()
    {
        I = this;
        _anomalyCanvas = anomalyLayer != null ? anomalyLayer.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
    }

    private void OnDestroy()
    {
        if (GameController.I != null)
        {
            GameController.I.OnInitialized -= OnGameControllerInitialized;
        }
    }

    private void OnGameControllerInitialized()
    {
        Debug.Log("[AnomalySpawner] GameController initialized, building nodes now");
        if (GameController.I != null)
            GameController.I.OnInitialized -= OnGameControllerInitialized;
        Build();
    }

    private System.Collections.IEnumerator WaitForGameControllerAndBuild()
    {
        int maxRetries = 60; // Wait up to 3 seconds (60 frames at 50fps)
        int retries = 0;

        while (GameController.I == null && retries < maxRetries)
        {
            retries++;
            yield return null;
        }

        if (GameController.I == null)
        {
            Debug.LogError("[AnomalySpawner] GameController.I is still null after waiting");
            yield break;
        }

        Debug.Log($"[AnomalySpawner] GameController found after {retries} frame(s)");

        if (GameController.I.IsInitialized)
        {
            Debug.Log("[AnomalySpawner] GameController already initialized, building immediately");
            Build();
        }
        else
        {
            Debug.Log("[AnomalySpawner] Subscribing to OnInitialized event");
            GameController.I.OnInitialized += OnGameControllerInitialized;
        }
    }

    private void Start()
    {
        if (!nodeLayer)
        {
            Debug.LogError("[AnomalySpawner] Missing refs: nodeLayer", this);
            enabled = false;
            return;
        }

        // Wait for GameController to initialize before building nodes
        if (GameController.I != null)
        {
            if (GameController.I.IsInitialized)
            {
                Debug.Log("[AnomalySpawner] GameController already initialized, building immediately");
                Build();
            }
            else
            {
                Debug.Log("[AnomalySpawner] Waiting for GameController to initialize...");
                GameController.I.OnInitialized += OnGameControllerInitialized;
            }
        }
        else
        {
            Debug.LogWarning("[AnomalySpawner] GameController.I is null, will retry in next frame");
            StartCoroutine(WaitForGameControllerAndBuild());
        }
    }

    private void Build()
    {
        if (GameController.I == null || GameController.I.State?.Cities == null) return;

        _cityRects.Clear();

        var cities = nodeLayer.GetComponentsInChildren<City>(true);
        foreach (var city in cities)
        {
            if (city == null || string.IsNullOrEmpty(city.CityId)) continue;
            var cityRt = city.transform as RectTransform;
            if (cityRt != null)
                _cityRects[city.CityId] = cityRt;
        }


        SyncCityMapPosToState();

        RefreshAnomalies();
    }
    // ===== BEGIN Hotfix MapPos (SyncCityMapPosToState) =====
    private void SyncCityMapPosToState()
    {
        var state = GameController.I != null ? GameController.I.State : null;
        if (state == null || anomalyLayer == null) return;

        var cam = _anomalyCanvas != null ? _anomalyCanvas.worldCamera : null;

        foreach (var city in state.Cities)
        {
            if (city == null) continue;

            if (_cityRects.TryGetValue(city.Id, out var cityRt) && cityRt != null)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(cam, cityRt.position);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(anomalyLayer, screen, cam, out var local))
                    city.MapPos = local;
                else
                    city.MapPos = anomalyLayer.InverseTransformPoint(cityRt.position);
            }
        }
    }
    // ===== END Hotfix MapPos (SyncCityMapPosToState) =====

    public void RefreshMapNodes()
    {
        Build();
    }

    private void RefreshAnomalies()
    {
        if (!anomalyLayer || !anomalyPrefab) return;
        if (GameController.I == null || GameController.I.State?.Cities == null) return;

        var gc = GameController.I;
        var state = gc.State;

        var toKeep = new HashSet<string>();
        var offsetKeysToKeep = new HashSet<string>();

        // 只从 state.Anomalies 生成地图异常（唯一真相）
        if (state.Anomalies != null)
        {
            for (int i = 0; i < state.Anomalies.Count; i++)
            {
                var anom = state.Anomalies[i];
                if (anom == null) continue;

                var nodeId = anom.NodeId;
                var defId = anom.AnomalyDefId;
                var instanceId = anom.Id;

                if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(defId) || string.IsNullOrEmpty(instanceId))
                    continue;

                state.EnsureIndex();
                var node = state.Index.GetCity(nodeId);
                if (node == null || !node.Unlocked) continue;


                Vector2 fallbackPos = ResolveNodeAnchoredPosition(node);

                // Key：node:def:instance（避免同 def 碰撞）
                string key = BuildAnomalyKey(nodeId, defId, instanceId);
                // OffsetKey：node:def（保持同 def 稳定簇）
                var offsetKey = BuildAnomalyOffsetKey(nodeId, defId);

                toKeep.Add(key);
                offsetKeysToKeep.Add(offsetKey);

                if (!_anomalies.TryGetValue(key, out var anomaly) || anomaly == null)
                {
                    var go = Instantiate(anomalyPrefab, anomalyLayer);
                    anomaly = go.GetComponent<Anomaly>();
                    if (anomaly == null) anomaly = go.AddComponent<Anomaly>();
                    _anomalies[key] = anomaly;
                }

                var rt = anomaly.transform as RectTransform;
                if (rt != null)
                {
                    var anchorPos = GetOrCreateAnomalyAnchorPosition(offsetKey, fallbackPos);
                    rt.anchoredPosition = anchorPos + GetOrCreateAnomalyOffset(offsetKey);
                    LogAnomalyPlacement(nodeId, defId, offsetKey, anchorPos, rt.anchoredPosition);
                    // ===== BEGIN Hotfix MapPos (write anomaly MapPos) =====
                    // MapPos now uses anomalyLayer-local (same as marker anchoredPosition)
                    anom.MapPos = rt.anchoredPosition;
                    // ===== END Hotfix MapPos (write anomaly MapPos) =====


                }

                // ✅ 永远携带 instanceId
                anomaly.Bind(defId, instanceId);

                if (rt != null)
                    DispatchAnimationSystem.I?.RegisterAnomaly(nodeId, defId, rt);
            }
        }

        // Remove stale anomaly objects
        var toRemove = new List<string>();
        foreach (var kvp in _anomalies)
        {
            if (kvp.Value == null || !toKeep.Contains(kvp.Key))
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value.gameObject);
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
            _anomalies.Remove(key);

        // Remove stale offsets
        var offsetsToRemove = new List<string>();
        foreach (var kvp in _anomalyOffsets)
        {
            if (!offsetKeysToKeep.Contains(kvp.Key))
                offsetsToRemove.Add(kvp.Key);
        }
        foreach (var key in offsetsToRemove)
            _anomalyOffsets.Remove(key);

        // Remove stale anchors
        var anchorsToRemove = new List<string>();
        foreach (var kvp in _anomalyAnchorPositions)
        {
            if (!offsetKeysToKeep.Contains(kvp.Key))
                anchorsToRemove.Add(kvp.Key);
        }
        foreach (var key in anchorsToRemove)
        {
            _anomalyAnchorPositions.Remove(key);
            _anomalyAnchorNodeIds.Remove(key);
        }
    }

    // ===== BEGIN M2 MapPos (ResolveNodeAnchoredPosition FULL) =====
    private Vector2 ResolveNodeAnchoredPosition(CityState node)
    {
        if (node == null || nodeLayer == null || anomalyLayer == null) return Vector2.zero;

        // 主路径：有视图时，以视图位置为准（更稳，避免层级/缩放差异）
        if (_cityRects.TryGetValue(node.Id, out var cityRt) && cityRt != null)
        {
            var world = cityRt.position;
            var cam = _anomalyCanvas != null ? _anomalyCanvas.worldCamera : null;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(anomalyLayer, screen, cam, out var local))
                return local;

            return anomalyLayer.InverseTransformPoint(world);
        }

        //M2 统一用 State.MapPos（NodeLayer-local）
        return node.MapPos;
    }
    // ===== END M2 MapPos (ResolveNodeAnchoredPosition FULL) =====



    private static string BuildAnomalyKey(string nodeId, string anomalyId, string managedId)
    {
        var managedSuffix = string.IsNullOrEmpty(managedId) ? string.Empty : $":{managedId}";
        return $"{nodeId}:{anomalyId}{managedSuffix}";
    }

    private static string BuildAnomalyOffsetKey(string nodeId, string anomalyId)
    {
        return $"{nodeId}:{anomalyId}";
    }

    private Vector2 GetOrCreateAnomalyOffset(string key)
    {
        if (string.IsNullOrEmpty(key)) return Vector2.zero;

        float min = Mathf.Max(0f, anomalySpawnRadiusMin);
        float max = Mathf.Max(min, anomalySpawnRadiusMax);
        if (max <= 0f)
        {
            _anomalyOffsets[key] = Vector2.zero;
            return Vector2.zero;
        }

        if (_anomalyOffsets.TryGetValue(key, out var offset))
        {
            float magnitude = offset.magnitude;
            if (magnitude >= min - 0.01f && magnitude <= max + 0.01f)
                return offset;
        }

        // 取单位圆向量，长度在[min, max]之间
        Vector2 dir = Random.insideUnitCircle.normalized;
        float len = Random.Range(min, max);
        offset = dir * len;
        _anomalyOffsets[key] = offset;
        return offset;
    }

    private void LogAnomalyPlacement(string nodeId, string anomalyId, string offsetKey, Vector2 anchorPos, Vector2 anomalyPos)
    {
        string anchorNodeId = _anomalyAnchorNodeIds.TryGetValue(offsetKey, out var value) ? value : "<none>";
        string anchorNodeName = ResolveNodeName(anchorNodeId);
        var cityAnchored = ResolveCityAnchoredPosition(anchorNodeId);
        float distance = Vector2.Distance(anchorPos, anomalyPos);
        Debug.Log($"[AnomalySpawnPos] node={nodeId} anomaly={anomalyId} anchorNode={anchorNodeId} anchorName={anchorNodeName} cityAnchored=({cityAnchored.x:0.##},{cityAnchored.y:0.##}) anchor=({anchorPos.x:0.##},{anchorPos.y:0.##}) pos=({anomalyPos.x:0.##},{anomalyPos.y:0.##}) distance={distance:0.##} radiusMin={anomalySpawnRadiusMin:0.##} radiusMax={anomalySpawnRadiusMax:0.##}");
    }

    private static string ResolveNodeName(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return string.Empty;

        var state = GameController.I?.State;
        if (state == null) return nodeId;

        state.EnsureIndex();
        var node = state.Index.GetCity(nodeId);
        return node?.Name ?? nodeId;
    }

    private Vector2 GetOrCreateAnomalyAnchorPosition(string key, Vector2 fallbackPos)
    {
        if (string.IsNullOrEmpty(key)) return fallbackPos;

        if (_anomalyAnchorPositions.TryGetValue(key, out var anchor))
            return anchor;

        var nodes = GameController.I?.State?.Cities;
        if (nodes != null)
        {
            var candidates = new List<CityState>();
            foreach (var n in nodes)
            {
                if (n == null || !n.Unlocked) continue;
                if (n.Type != 1) continue;
                candidates.Add(n);
            }

            if (candidates.Count > 0)
            {
                var pick = candidates[Random.Range(0, candidates.Count)];
                anchor = ResolveNodeAnchoredPosition(pick);
                _anomalyAnchorPositions[key] = anchor;
                _anomalyAnchorNodeIds[key] = pick.Id;
                return anchor;
            }
        }

        _anomalyAnchorPositions[key] = fallbackPos;
        _anomalyAnchorNodeIds[key] = "<fallback>";
        return fallbackPos;
    }

    private Vector2 ResolveCityAnchoredPosition(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return Vector2.zero;
        if (_cityRects.TryGetValue(nodeId, out var cityRt) && cityRt != null)
            return cityRt.anchoredPosition;
        return Vector2.zero;
    }
}
