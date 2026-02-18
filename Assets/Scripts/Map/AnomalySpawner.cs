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
                // ===== BEGIN M2: anchor follows NodeId city (no random anchor) =====
                if (rt != null)
                {
                    // anchor = NodeId 对应城市位置（anomalyLayer-local）
                    var anchorPos = fallbackPos;

                    // 记录 anchor，方便日志/排错
                    _anomalyAnchorPositions[offsetKey] = anchorPos;
                    _anomalyAnchorNodeIds[offsetKey] = nodeId;

                    // offset 必须稳定（见下方 GetOrCreateAnomalyOffset 的替换）
                    rt.anchoredPosition = anchorPos + GetOrCreateAnomalyOffset(offsetKey);

                    LogAnomalyPlacement(nodeId, defId, offsetKey, anchorPos, rt.anchoredPosition);

                    // MapPos 与 marker 坐标同口径（anomalyLayer-local），结算用它当中心
                    anom.MapPos = rt.anchoredPosition;
                }
                // ===== END M2: anchor follows NodeId city (no random anchor) =====


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

    // ===== BEGIN M2: Stable offset (no UnityEngine.Random) FULL =====
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

        // ✅ 稳定：由 key 计算出方向+半径（半径落在[min,max]）
        offset = StableOffsetInAnnulus(key, min, max);
        _anomalyOffsets[key] = offset;
        return offset;
    }

    private static Vector2 StableOffsetInAnnulus(string key, float min, float max)
    {
        // 两个独立 hash 生成 u/v
        uint h1 = Fnv1a32(key);
        uint h2 = Fnv1a32(key + "|2");

        float u = To01(h1);
        float v = To01(h2);

        // 面积均匀：r = sqrt(lerp(min^2, max^2, u))
        float min2 = min * min;
        float max2 = max * max;
        float r = Mathf.Sqrt(Mathf.Lerp(min2, max2, u));

        float ang = v * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
    }

    private static float To01(uint h)
    {
        // 取 24-bit 保证 float 精度稳定，范围 [0,1)
        return (h & 0x00FFFFFFu) / 16777216f;
    }

    private static uint Fnv1a32(string s)
    {
        unchecked
        {
            const uint FNV_OFFSET = 2166136261u;
            const uint FNV_PRIME = 16777619u;

            uint hash = FNV_OFFSET;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= FNV_PRIME;
                }
            }
            return hash;
        }
    }
    // ===== END M2: Stable offset (no UnityEngine.Random) FULL =====

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

    // ===== BEGIN M2: ResolveCityAnchoredPosition uses anomalyLayer-local =====
    private Vector2 ResolveCityAnchoredPosition(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return Vector2.zero;
        var state = GameController.I?.State;
        if (state == null) return Vector2.zero;

        state.EnsureIndex();
        var node = state.Index.GetCity(nodeId);
        if (node == null) return Vector2.zero;

        return ResolveNodeAnchoredPosition(node); // anomalyLayer-local
    }
    // ===== END M2: ResolveCityAnchoredPosition uses anomalyLayer-local =====
}
