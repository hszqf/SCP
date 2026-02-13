using System.Collections.Generic;
using Core;
using Data;
using UnityEngine;

public class MapNodeSpawner : MonoBehaviour
{
    public static MapNodeSpawner I { get; private set; }

    [SerializeField] private RectTransform mapRect;      // MapImage 的 RectTransform
    [SerializeField] private RectTransform nodeLayer;    // NodeLayer
    [SerializeField] private NodeButton cityPrefab;
    [SerializeField] private NodeButton basePrefab; // 也可以用同一个 prefab 但换样式

    [Header("Anomalies")]
    [SerializeField] private RectTransform anomalyLayer;
    [SerializeField] private GameObject anomalyPrefab;
    [SerializeField] private float anomalySpawnRadius = 24f;
    [SerializeField] private Sprite unknownAnomalySprite;
    [SerializeField] private List<Sprite> anomalySprites = new();
    [SerializeField] private string anomalySpritesResourcePath = "Anomalies";
    [SerializeField] private bool loadAnomalySpritesFromResources = true;

    private readonly Dictionary<string, NodeButton> _nodeButtons = new();
    private readonly Dictionary<string, AnomalyMarker> _anomalyMarkers = new();
    private readonly Dictionary<string, Vector2> _anomalyOffsets = new();
    private readonly Dictionary<string, Sprite> _anomalySpriteLookup = new(System.StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        I = this;
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
        Debug.Log("[MapNodeSpawner] GameController initialized, building nodes now");
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
            Debug.LogError("[MapNodeSpawner] GameController.I is still null after waiting");
            yield break;
        }

        Debug.Log($"[MapNodeSpawner] GameController found after {retries} frame(s)");
        
        if (GameController.I.IsInitialized)
        {
            Debug.Log("[MapNodeSpawner] GameController already initialized, building immediately");
            Build();
        }
        else
        {
            Debug.Log("[MapNodeSpawner] Subscribing to OnInitialized event");
            GameController.I.OnInitialized += OnGameControllerInitialized;
        }
        
        GameController.I.OnStateChanged += Refresh;
    }

    private void Start()
    {
        if (!mapRect || !nodeLayer || !cityPrefab || !basePrefab)
        {
            Debug.LogError("[MapNodeSpawner] Missing refs: mapRect/nodeLayer/cityPrefab/basePrefab", this);
            enabled = false;
            return;
        }

        BuildAnomalySpriteLookup();

        // Wait for GameController to initialize before building nodes
        if (GameController.I != null)
        {
            if (GameController.I.IsInitialized)
            {
                Debug.Log("[MapNodeSpawner] GameController already initialized, building immediately");
                Build();
            }
            else
            {
                Debug.Log("[MapNodeSpawner] Waiting for GameController to initialize...");
                GameController.I.OnInitialized += OnGameControllerInitialized;
            }
            GameController.I.OnStateChanged += Refresh;
        }
        else
        {
            Debug.LogWarning("[MapNodeSpawner] GameController.I is null, will retry in next frame");
            StartCoroutine(WaitForGameControllerAndBuild());
        }
    }

    private void Build()
    {
        if (GameController.I == null || GameController.I.State?.Nodes == null) return;

        var toRemove = new List<string>();
        foreach (var kvp in _nodeButtons)
        {
            if (kvp.Value == null) toRemove.Add(kvp.Key);
        }

        foreach (var n in GameController.I.State.Nodes)
        {
            if (n == null) continue;

            if (!n.Unlocked)
            {
                if (_nodeButtons.TryGetValue(n.Id, out var existingLocked) && existingLocked != null)
                {
                    Destroy(existingLocked.gameObject);
                    toRemove.Add(n.Id);
                }
                continue;
            }

            if (!_nodeButtons.TryGetValue(n.Id, out var btn) || btn == null)
            {
                var prefab = n.Type == 0 ? basePrefab : cityPrefab;
                btn = Instantiate(prefab, nodeLayer);
                _nodeButtons[n.Id] = btn;
            }

            btn.Set(n.Id, n.Name);

            var rt = (RectTransform)btn.transform;
            var size = nodeLayer.rect.size;
            var location = ResolveNodeLocation(n);
            rt.anchoredPosition = new Vector2((location.x - 0.5f) * size.x, (location.y - 0.5f) * size.y);

            DispatchAnimationSystem.I?.RegisterNode(n.Id, rt);
        }

        foreach (var nodeId in toRemove)
            _nodeButtons.Remove(nodeId);

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
        if (GameController.I == null || GameController.I.State?.Nodes == null) return;

        var registry = DataRegistry.Instance;
        var toKeep = new HashSet<string>();

        foreach (var node in GameController.I.State.Nodes)
        {
            if (node == null || !node.Unlocked) continue;

            var entries = new List<(string anomalyId, string managedId, bool contained)>();

            if (node.ActiveAnomalyIds != null)
            {
                foreach (var anomalyId in node.ActiveAnomalyIds)
                {
                    if (string.IsNullOrEmpty(anomalyId)) continue;
                    entries.Add((anomalyId, null, false));
                }
            }

            if (node.ManagedAnomalies != null)
            {
                foreach (var managed in node.ManagedAnomalies)
                {
                    if (managed == null || string.IsNullOrEmpty(managed.AnomalyId)) continue;
                    entries.Add((managed.AnomalyId, managed.Id, true));
                }
            }

            if (entries.Count == 0) continue;

            Vector2 nodePos = ResolveNodeAnchoredPosition(node);
            foreach (var entry in entries)
            {
                string key = BuildAnomalyKey(node.Id, entry.anomalyId, entry.managedId);
                toKeep.Add(key);

                if (!_anomalyMarkers.TryGetValue(key, out var marker) || marker == null)
                {
                    var go = Instantiate(anomalyPrefab, anomalyLayer);
                    marker = go.GetComponent<AnomalyMarker>();
                    if (marker == null) marker = go.AddComponent<AnomalyMarker>();
                    _anomalyMarkers[key] = marker;
                }

                var rt = marker.transform as RectTransform;
                if (rt != null)
                {
                    rt.anchoredPosition = nodePos + GetOrCreateAnomalyOffset(key);
                }

                bool isKnown = entry.contained || (node.KnownAnomalyDefIds != null && node.KnownAnomalyDefIds.Contains(entry.anomalyId));
                bool displayKnown = isKnown;
                float progress01 = 0f;
                string progressPrefix = string.Empty;
                string nameSuffix = string.Empty;

                bool hideNameWhileProgress = false;

                if (entry.contained)
                {
                    nameSuffix = "(已收容)";
                }
                else if (isKnown)
                {
                    nameSuffix = "(未收容)";
                }

                if (isKnown && TryGetRevealProgress01(node, entry.anomalyId, out var revealProgress))
                {
                    progress01 = revealProgress;
                    progressPrefix = "调查中：";
                    displayKnown = revealProgress >= 1f;
                }
                else if (!isKnown)
                {
                    progress01 = GetUnknownAnomalyProgress01(node, entry.anomalyId);
                    if (progress01 > 0f)
                        progressPrefix = "调查中：";
                }
                else
                {
                    progress01 = GetContainProgress01(node, entry.anomalyId);
                    if (progress01 > 0f)
                    {
                        progressPrefix = "收容中：";
                        hideNameWhileProgress = true;
                    }
                }

                var sprite = ResolveAnomalySprite(registry, entry.anomalyId, displayKnown);
                marker.Bind(node.Id, entry.anomalyId, entry.managedId, sprite, isKnown, displayKnown, entry.contained, progress01, progressPrefix, nameSuffix, hideNameWhileProgress);

                if (rt != null)
                    DispatchAnimationSystem.I?.RegisterAnomaly(node.Id, entry.anomalyId, rt);
            }
        }

        var toRemove = new List<string>();
        foreach (var kvp in _anomalyMarkers)
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
            _anomalyMarkers.Remove(key);
            _anomalyOffsets.Remove(key);
        }
    }

    private Vector2 ResolveNodeAnchoredPosition(NodeState node)
    {
        if (node == null || nodeLayer == null) return Vector2.zero;

        if (_nodeButtons.TryGetValue(node.Id, out var button) && button != null)
        {
            var btnRt = button.transform as RectTransform;
            if (btnRt != null) return btnRt.anchoredPosition;
        }

        var size = nodeLayer.rect.size;
        var location = ResolveNodeLocation(node);
        return new Vector2((location.x - 0.5f) * size.x, (location.y - 0.5f) * size.y);
    }

    private Vector2 GetOrCreateAnomalyOffset(string key)
    {
        if (string.IsNullOrEmpty(key)) return Vector2.zero;
        if (_anomalyOffsets.TryGetValue(key, out var offset)) return offset;

        if (anomalySpawnRadius <= 0f)
        {
            _anomalyOffsets[key] = Vector2.zero;
            return Vector2.zero;
        }

        offset = Random.insideUnitCircle * anomalySpawnRadius;
        _anomalyOffsets[key] = offset;
        return offset;
    }

    private void BuildAnomalySpriteLookup()
    {
        _anomalySpriteLookup.Clear();
        if (loadAnomalySpritesFromResources && !string.IsNullOrEmpty(anomalySpritesResourcePath))
        {
            var loaded = Resources.LoadAll<Sprite>(anomalySpritesResourcePath);
            if (loaded != null && loaded.Length > 0)
            {
                anomalySprites = new List<Sprite>(loaded);
            }
        }
        if (anomalySprites == null) return;
        foreach (var sprite in anomalySprites)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
            if (!_anomalySpriteLookup.ContainsKey(sprite.name))
                _anomalySpriteLookup[sprite.name] = sprite;
        }
    }

    private Sprite ResolveAnomalySprite(DataRegistry registry, string anomalyId, bool isKnown)
    {
        if (!isKnown) return unknownAnomalySprite;
        if (!string.IsNullOrEmpty(anomalyId) && _anomalySpriteLookup.TryGetValue(anomalyId, out var direct))
            return direct;
        if (registry != null && !string.IsNullOrEmpty(anomalyId) && registry.AnomaliesById.TryGetValue(anomalyId, out var def))
        {
            var name = def?.name;
            if (!string.IsNullOrEmpty(name) && _anomalySpriteLookup.TryGetValue(name, out var sprite))
                return sprite;
        }
        return unknownAnomalySprite;
    }

    private static string BuildAnomalyKey(string nodeId, string anomalyId, string managedId)
    {
        var managedSuffix = string.IsNullOrEmpty(managedId) ? string.Empty : $":{managedId}";
        return $"{nodeId}:{anomalyId}{managedSuffix}";
    }

    private static Vector2 ResolveNodeLocation(NodeState node)
    {
        if (node?.Location != null && node.Location.Length >= 2)
            return new Vector2(node.Location[0], node.Location[1]);

        if (node != null && node.Type == 0 && Mathf.Abs(node.X) < 0.0001f && Mathf.Abs(node.Y) < 0.0001f)
            return new Vector2(0.5f, 0.5f);

        return node != null ? new Vector2(node.X, node.Y) : new Vector2(0.5f, 0.5f);
    }

    private static float GetUnknownAnomalyProgress01(NodeState node, string anomalyId)
    {
        if (node == null) return 0f;
        if (string.IsNullOrEmpty(anomalyId)) return 0f;
        if (node.Tasks == null) return 0f;

        float best = 0f;
        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Investigate) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;

            float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
            if (progress <= 0f) continue;

            int baseDays = GetTaskBaseDays(task);
            float progress01 = baseDays > 0 ? Mathf.Clamp01(progress / baseDays) : 0f;
            if (progress01 > best) best = progress01;
        }

        return best;
    }

    private static bool TryGetRevealProgress01(NodeState node, string anomalyId, out float progress01)
    {
        progress01 = 0f;
        if (node == null || string.IsNullOrEmpty(anomalyId)) return false;
        if (node.Tasks == null) return false;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.Type != TaskType.Investigate) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (task.VisualProgress < 0f) continue;

            int baseDays = GetTaskBaseDays(task);
            progress01 = baseDays > 0 ? Mathf.Clamp01(task.VisualProgress / baseDays) : 0f;
            return true;
        }

        return false;
    }

    private static bool IsManagingAnomaly(NodeState node, string anomalyId)
    {
        if (node == null || string.IsNullOrEmpty(anomalyId)) return false;
        if (node.Tasks == null) return false;

        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Manage) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;

            var managed = node.ManagedAnomalies?.Find(m => m != null && m.Id == task.TargetManagedAnomalyId);
            var taskAnomalyId = managed?.AnomalyId ?? task.SourceAnomalyId;
            if (string.Equals(taskAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static float GetContainProgress01(NodeState node, string anomalyId)
    {
        if (node == null || string.IsNullOrEmpty(anomalyId)) return 0f;
        if (node.Tasks == null) return 0f;

        float best = 0f;
        foreach (var task in node.Tasks)
        {
            if (task == null || task.State != TaskState.Active || task.Type != TaskType.Contain) continue;
            if (!string.Equals(task.SourceAnomalyId, anomalyId, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) continue;

            float progress = task.VisualProgress >= 0f ? task.VisualProgress : task.Progress;
            if (progress <= 0f) continue;
            int baseDays = GetTaskBaseDays(task);
            float progress01 = baseDays > 0 ? Mathf.Clamp01(progress / baseDays) : 0f;
            if (progress01 > best) best = progress01;
        }

        return best;
    }

    private static int GetTaskBaseDays(NodeTask task)
    {
        if (task == null) return 1;
        var registry = DataRegistry.Instance;
        if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            return task.InvestigateNoResultBaseDays;
        string anomalyId = task.SourceAnomalyId;
        if (string.IsNullOrEmpty(anomalyId) || registry == null) return 1;
        return Mathf.Max(1, registry.GetAnomalyBaseDaysWithWarn(anomalyId, 1));
    }
}
