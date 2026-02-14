using System.Collections.Generic;
using Core;
using UnityEngine;

public class AnomalySpawner : MonoBehaviour
{
    public static AnomalySpawner I { get; private set; }

    [SerializeField] private RectTransform nodeLayer;    // NodeLayer

    [Header("Anomalies")]
    [SerializeField] private RectTransform anomalyLayer;
    [SerializeField] private GameObject anomalyPrefab;
    [SerializeField] private float anomalySpawnRadiusMin = 8f;
    [SerializeField] private float anomalySpawnRadiusMax = 24f;

    private Canvas _anomalyCanvas;

    private readonly Dictionary<string, RectTransform> _cityRects = new();
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
            GameController.I.OnStateChanged -= Refresh;
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

        GameController.I.OnStateChanged += Refresh;
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
            GameController.I.OnStateChanged += Refresh;
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

        RefreshAnomalies();
    }

    public void RefreshMapNodes()
    {
        Build();
    }

    private void Refresh()
    {
        RefreshMapNodes();
    }

    private void RefreshAnomalies()
    {
        if (!anomalyLayer || !anomalyPrefab) return;
        if (GameController.I == null || GameController.I.State?.Cities == null) return;

        var toKeep = new HashSet<string>();
        var offsetKeysToKeep = new HashSet<string>();

        foreach (var node in GameController.I.State.Cities)
        {
            if (node == null || !node.Unlocked) continue;

            var entries = new List<(string anomalyId, string managedId)>();

            if (node.ActiveAnomalyIds != null)
            {
                foreach (var anomalyId in node.ActiveAnomalyIds)
                {
                    if (string.IsNullOrEmpty(anomalyId)) continue;
                    entries.Add((anomalyId, null));
                }
            }

            if (node.ManagedAnomalies != null)
            {
                foreach (var managed in node.ManagedAnomalies)
                {
                    if (managed == null || string.IsNullOrEmpty(managed.AnomalyId)) continue;
                    entries.Add((managed.AnomalyId, managed.Id));
                }
            }

            if (entries.Count == 0) continue;

            Vector2 fallbackPos = ResolveNodeAnchoredPosition(node);
            foreach (var entry in entries)
            {
                string key = BuildAnomalyKey(node.Id, entry.anomalyId, entry.managedId);
                var offsetKey = BuildAnomalyOffsetKey(node.Id, entry.anomalyId);
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
                    LogAnomalyPlacement(node.Id, entry.anomalyId, offsetKey, anchorPos, rt.anchoredPosition);
                }

                anomaly.Bind(node.Id, entry.anomalyId, entry.managedId);

                if (rt != null)
                    DispatchAnimationSystem.I?.RegisterAnomaly(node.Id, entry.anomalyId, rt);
            }
        }

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
        {
            _anomalies.Remove(key);
        }

        var offsetsToRemove = new List<string>();
        foreach (var kvp in _anomalyOffsets)
        {
            if (!offsetKeysToKeep.Contains(kvp.Key))
                offsetsToRemove.Add(kvp.Key);
        }

        foreach (var key in offsetsToRemove)
        {
            _anomalyOffsets.Remove(key);
        }

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

    private Vector2 ResolveNodeAnchoredPosition(NodeState node)
    {
        if (node == null || nodeLayer == null) return Vector2.zero;

        if (_cityRects.TryGetValue(node.Id, out var cityRt) && cityRt != null && anomalyLayer != null)
        {
            var world = cityRt.position;
            var cam = _anomalyCanvas != null ? _anomalyCanvas.worldCamera : null;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(anomalyLayer, screen, cam, out var local))
                return local;

            return anomalyLayer.InverseTransformPoint(world);
        }

        var size = nodeLayer.rect.size;
        var location = ResolveNodeLocation(node);
        var anchored = new Vector2((location.x - 0.5f) * size.x, (location.y - 0.5f) * size.y);

        return ConvertAnchoredPosition(nodeLayer, anomalyLayer, anchored);
    }

    private static Vector2 ConvertAnchoredPosition(RectTransform from, RectTransform to, Vector2 anchoredPosition)
    {
        if (from == null || to == null || from == to) return anchoredPosition;

        var world = from.TransformPoint(anchoredPosition);
        return to.InverseTransformPoint(world);
    }

    private static string BuildAnomalyKey(string nodeId, string anomalyId, string managedId)
    {
        var managedSuffix = string.IsNullOrEmpty(managedId) ? string.Empty : $":{managedId}";
        return $"{nodeId}:{anomalyId}{managedSuffix}";
    }

    private static string BuildAnomalyOffsetKey(string nodeId, string anomalyId)
    {
        return $"{nodeId}:{anomalyId}";
    }

    private static Vector2 ResolveNodeLocation(NodeState node)
    {
        if (node?.Location != null && node.Location.Length >= 2)
            return new Vector2(node.Location[0], node.Location[1]);

        if (node != null && node.Type == 0 && Mathf.Abs(node.X) < 0.0001f && Mathf.Abs(node.Y) < 0.0001f)
            return new Vector2(0.5f, 0.5f);

        return node != null ? new Vector2(node.X, node.Y) : new Vector2(0.5f, 0.5f);
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
        var nodes = GameController.I?.State?.Cities;
        if (nodes == null) return nodeId;
        var node = nodes.Find(n => n != null && n.Id == nodeId);
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
            var candidates = new List<NodeState>();
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
