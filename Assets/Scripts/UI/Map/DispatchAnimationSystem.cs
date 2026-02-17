using System.Collections;
using System.Collections.Generic;
using Core;
using Data;
using UnityEngine;

public class DispatchAnimationSystem : MonoBehaviour
{
    public static DispatchAnimationSystem I { get; private set; }

    [Header("Refs")]
    [SerializeField] private RectTransform tokenLayer;
    [SerializeField] private GameObject agentPrefab;

    [Header("Config")]
    [SerializeField] private float travelSeconds = 10f;
    [SerializeField] private float baseSpawnRadius = 5f;
    [SerializeField] private float launchInterval = 0.5f;
    [SerializeField] private float arrivalProgressStepPercent = 0.01f;
    [SerializeField] private float arrivalProgressInterval = 0.12f;
    [SerializeField] private float _fallbackTravelSeconds = 0.45f; // placeholder travel duration

    private readonly Dictionary<string, RectTransform> _nodes = new();
    private readonly Dictionary<string, RectTransform> _anomalies = new();
    private readonly Dictionary<string, TaskSnapshot> _taskCache = new();
    private readonly Queue<DispatchEvent> _pending = new();
    private readonly HashSet<string> _pendingKeys = new();
    private readonly HashSet<string> _missingNodeWarned = new();
    private Coroutine _playRoutine;
    private int _lastReportedStarted = -1;
    private int _lastReportedPending = -1;
    private bool _isSubscribed;
    private GameController _boundController;
    private int _activeAgents;
    private int _activeProgressRolls;
    private bool _hudLocked;
    private float _lastBindAttempt;
    private readonly HashSet<string> _rollingTasks = new();
    private readonly HashSet<string> _lockedVisualTasks = new();
    private readonly HashSet<string> _inTransitTasks = new();
    private readonly HashSet<string> _offBaseAgents = new();
    private string _cachedBaseNodeId;
    private bool _nodesScanned;
    private readonly Dictionary<string, List<string>> _taskAgentHistory = new();

    // Token-based interaction lock: non-null when a MovementToken coroutine is playing
    private Coroutine _tokenCo;
    private Core.MovementToken _playingToken;

    public bool IsInteractionLocked => _tokenCo != null;

    public bool IsTaskInTransit(string taskId)
        => !string.IsNullOrEmpty(taskId) && _inTransitTasks.Contains(taskId);

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

    private class TaskSnapshot
    {
        public string NodeId;
        public TaskState State;
        public List<string> AgentIds;
        public float Progress;
    }

    private enum DispatchMode
    {
        Go,
        Return
    }

    private class DispatchEvent
    {
        public DispatchMode Mode;
        public string TaskId;
        public string FromNodeId;
        public string ToNodeId;
        public List<string> AgentIds;
        public string Key;
        public float PreviousProgress;
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
        TryBindGameController();
        Debug.Log("[MapUI] DispatchAnimationSystem OnEnable");
    }

    private void OnDisable()
    {
        UnbindGameController();

        Debug.Log("[MapUI] DispatchAnimationSystem OnDisable");
    }

    private void Update()
    {
        if (!_isSubscribed)
            TryBindGameController();

        if (_tokenCo != null) return; // already playing a token

        var gc = GameController.I;
        if (gc == null || gc.State == null) return;

        var token = FindFirstPendingToken(gc.State);
        if (token == null) return;

        _tokenCo = StartCoroutine(ConsumeTokenCoroutine(gc, token));
    }

    public void RegisterNode(string nodeId, RectTransform nodeRT)
    {
        if (string.IsNullOrEmpty(nodeId) || nodeRT == null) return;
        _nodes[nodeId] = nodeRT;

        if (!string.IsNullOrEmpty(_cachedBaseNodeId) &&
            string.Equals(_cachedBaseNodeId, nodeId, System.StringComparison.OrdinalIgnoreCase))
        {
            _missingNodeWarned.Remove("__base__");
        }
    }

    public void RegisterAnomaly(string nodeId, string anomalyId, RectTransform anomalyRT)
    {
        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(anomalyId) || anomalyRT == null) return;
        _anomalies[BuildAnomalyKey(nodeId, anomalyId)] = anomalyRT;
    }

    public void PlayPending()
    {
        if (_playRoutine != null || _pending.Count == 0) return;
        _playRoutine = StartCoroutine(PlayPendingRoutine());
    }

    private void HandleStateChanged()
    {
        EnqueueTransitions();
        SyncVisualProgress();
        ReportDispatchState();
        if (_pending.Count > 0)
        {
            Debug.Log($"[MapUI] Dispatch queued count={_pending.Count}");
            PlayPending();
        }
    }

    private void TryBindGameController()
    {
        if (_isSubscribed) return;
        if (Time.unscaledTime - _lastBindAttempt < 0.5f) return;
        _lastBindAttempt = Time.unscaledTime;

        var controller = GameController.I;
        if (controller == null)
            controller = FindFirstObjectByType<GameController>();
        if (controller == null) return;

        controller.OnStateChanged += HandleStateChanged;
        _boundController = controller;
        _isSubscribed = true;
        CacheCurrentTasks();
        SyncOffBaseAgents();
        EnsureNodeRegistry();
        Debug.Log("[MapUI] DispatchAnimationSystem bound to GameController");
    }

    private void UnbindGameController()
    {
        if (!_isSubscribed || _boundController == null) return;
        _boundController.OnStateChanged -= HandleStateChanged;
        _boundController = null;
        _isSubscribed = false;
    }

    private void CacheCurrentTasks()
    {
        var current = BuildTaskSnapshotMap();
        _taskCache.Clear();
        foreach (var kvp in current)
            _taskCache[kvp.Key] = kvp.Value;
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

            // off-base = any location not Base (including AtAnomaly/TravellingToAnomaly/TravellingToBase)
            if (ag.LocationKind != Core.AgentLocationKind.Base)
                _offBaseAgents.Add(ag.Id);
        }
    }

    private void ReportDispatchState()
    {
        var current = BuildTaskSnapshotMap();
        int started = 0;
        foreach (var snapshot in current.Values)
        {
            if (snapshot.State == TaskState.Active && snapshot.AgentIds.Count > 0 && snapshot.Progress > 0f)
                started += 1;
        }

        if (started != _lastReportedStarted || _pending.Count != _lastReportedPending)
        {
            Debug.Log($"[MapUI] Dispatch state startedTasks={started} pending={_pending.Count}");
            _lastReportedStarted = started;
            _lastReportedPending = _pending.Count;
        }
    }

    private Dictionary<string, TaskSnapshot> BuildTaskSnapshotMap()
    {
        var result = new Dictionary<string, TaskSnapshot>();
        var gc = GameController.I;
        if (gc?.State?.Cities == null) return result;

        foreach (var node in gc.State.Cities)
        {
            if (node?.Tasks == null) continue;
            foreach (var t in node.Tasks)
            {
                if (t == null || t.State != TaskState.Active) continue;
                if (string.IsNullOrEmpty(t.Id)) continue;
                result.Add(t.Id, new TaskSnapshot
                {
                    NodeId = node.Id,
                    State = t.State,
                    AgentIds = t.AssignedAgentIds ?? new List<string>(),
                    Progress = t.Progress
                });
            }
        }

        return result;
    }

    private void EnqueueTransitions()
    {
        var baseId = ResolveBaseNodeId();
        if (string.IsNullOrEmpty(baseId))
            return;

        var gc = GameController.I;
        bool useMovementTokens = (gc != null && gc.State != null && gc.State.MovementTokens != null);

        var current = BuildTaskSnapshotMap();

        if (!useMovementTokens)
        {
            foreach (var kvp in current)
            {
                var taskId = kvp.Key;
                var snapshot = kvp.Value;
                if (snapshot.State != TaskState.Active || snapshot.AgentIds.Count == 0 || snapshot.Progress <= 0f)
                    continue;

                if (!_taskCache.TryGetValue(taskId, out var previous) || previous.Progress <= 0f)
                    EnqueueDispatch(DispatchMode.Go, taskId, baseId, snapshot.NodeId, snapshot.AgentIds, previous?.Progress ?? 0f);
            }

            foreach (var kvp in _taskCache)
            {
                var taskId = kvp.Key;
                var snapshot = kvp.Value;
                if (snapshot.State != TaskState.Active) continue;

                if (!current.TryGetValue(taskId, out var currentSnapshot) || currentSnapshot.State != TaskState.Active)
                {
                    if (snapshot.Progress > 0f || HasOffBaseAgents(snapshot.AgentIds) || _inTransitTasks.Contains(taskId))
                        EnqueueDispatch(DispatchMode.Return, taskId, snapshot.NodeId, baseId, snapshot.AgentIds);
                }
            }
        }

        RollProgressDeltas(current);

        _taskCache.Clear();
        foreach (var kvp in current)
            _taskCache[kvp.Key] = kvp.Value;
    }

    private void EnqueueDispatch(DispatchMode mode, string taskId, string fromNodeId, string toNodeId, List<string> agentIds, float previousProgress = 0f)
    {
        var resolvedAgents = ResolveDispatchAgents(mode, taskId, agentIds);
        if (resolvedAgents.Count == 0)
            return;

        string key = $"{mode}:{taskId}";
        if (!_pendingKeys.Add(key)) return;

        if (mode == DispatchMode.Go && !string.IsNullOrEmpty(taskId))
            _inTransitTasks.Add(taskId);

        _pending.Enqueue(new DispatchEvent
        {
            Mode = mode,
            TaskId = taskId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            AgentIds = resolvedAgents,
            Key = key,
            PreviousProgress = previousProgress
        });

        Debug.Log($"[MapUI] Dispatch enqueue mode={mode} taskId={taskId} from={fromNodeId} to={toNodeId} agents={resolvedAgents.Count}");
    }

    private List<string> ResolveDispatchAgents(DispatchMode mode, string taskId, List<string> agentIds)
    {
        List<string> resolved;
        if (agentIds != null && agentIds.Count > 0)
        {
            resolved = new List<string>();
            foreach (var id in agentIds)
            {
                if (!string.IsNullOrEmpty(id))
                    resolved.Add(id);
            }
        }
        else if (!string.IsNullOrEmpty(taskId) && _taskAgentHistory.TryGetValue(taskId, out var cached))
        {
            resolved = new List<string>(cached);
        }
        else
        {
            resolved = new List<string>();
        }

        if (mode == DispatchMode.Go && !string.IsNullOrEmpty(taskId) && resolved.Count > 0)
            _taskAgentHistory[taskId] = new List<string>(resolved);

        return resolved;
    }

    private IEnumerator PlayPendingRoutine()
    {
        if (!tokenLayer || !agentPrefab)
        {
            Debug.LogError("[MapUI] Dispatch tokenLayer/agentPrefab missing", this);
            _playRoutine = null;
            yield break;
        }

        while (_pending.Count > 0)
        {
            var ev = _pending.Dequeue();
            _pendingKeys.Remove(ev.Key);

            if (!TryGetDispatchPoint(ev.TaskId, ev.FromNodeId, out var fromLocal) || !TryGetDispatchPoint(ev.TaskId, ev.ToNodeId, out var toLocal))
            {
                if (ev.Mode == DispatchMode.Go && !string.IsNullOrEmpty(ev.TaskId))
                    _inTransitTasks.Remove(ev.TaskId);
                continue;
            }

            int agentCount = ev.AgentIds?.Count ?? 0;
            if (agentCount <= 0)
            {
                if (ev.Mode == DispatchMode.Go && !string.IsNullOrEmpty(ev.TaskId))
                    _inTransitTasks.Remove(ev.TaskId);
                continue;
            }

            Debug.Log($"[MapUI] Dispatch start mode={ev.Mode} taskId={ev.TaskId} from={ev.FromNodeId} to={ev.ToNodeId} agents={agentCount}");

            if (ev.Mode == DispatchMode.Go)
                PrepareVisualProgressForRoll(ev.TaskId, ev.PreviousProgress);

            float duration = Mathf.Max(0.01f, travelSeconds);
            int remaining = agentCount;
            for (int i = 0; i < agentCount; i++)
            {
                _activeAgents += 1;
                SetHudLocked(true);

                int index = i + 1;
                var startPos = ApplyBaseSpawnOffset(ev.FromNodeId, fromLocal);
                string agentId = ev.AgentIds[i];
                StartCoroutine(AnimateAgent(ev.TaskId, index, agentCount, startPos, toLocal, duration, () =>
                {
                    if (ev.Mode == DispatchMode.Go)
                        MarkAgentOffBase(agentId);
                    else
                        MarkAgentOnBase(agentId);
                    remaining -= 1;
                    _activeAgents = Mathf.Max(0, _activeAgents - 1);
                    if (_activeAgents == 0)
                        SetHudLocked(false);
                }));

                if (launchInterval > 0f)
                    yield return new WaitForSeconds(launchInterval);
            }

            while (remaining > 0)
                yield return null;

            if (ev.Mode == DispatchMode.Go)
                _inTransitTasks.Remove(ev.TaskId);
            if (ev.Mode == DispatchMode.Go)
                StartProgressRoll(ev.TaskId);
            if (ev.Mode == DispatchMode.Return && !string.IsNullOrEmpty(ev.TaskId))
                _taskAgentHistory.Remove(ev.TaskId);
        }

        _playRoutine = null;
    }

    private void StartProgressRoll(string taskId)
    {
        if (string.IsNullOrEmpty(taskId) || _rollingTasks.Contains(taskId)) return;
        if (GameController.I == null) return;
        if (!GameController.I.TryGetTask(taskId, out var node, out var task) || task == null) return;
        if (task.Type != TaskType.Investigate && task.Type != TaskType.Contain) return;
        if (task.Progress <= 0f) return;

        int baseDays = GetTaskBaseDays(task);
        if (baseDays <= 0) return;

        float step = Mathf.Max(0f, arrivalProgressStepPercent);
        float target = Mathf.Clamp01(task.Progress / baseDays);
        float interval = Mathf.Max(0.01f, arrivalProgressInterval);

        float startPercent = 0f;
        if (task.VisualProgress >= 0f)
            startPercent = Mathf.Clamp01(task.VisualProgress / baseDays);

        _lockedVisualTasks.Remove(taskId);
        _rollingTasks.Add(taskId);
        _activeProgressRolls += 1;
        SetHudLocked(true);
        StartCoroutine(ProgressRollRoutine(taskId, baseDays, step, target, interval, startPercent));
    }

    private IEnumerator ProgressRollRoutine(string taskId, int baseDays, float stepPercent, float targetPercent, float interval, float startPercent)
    {
        float target = Mathf.Clamp01(targetPercent);
        float current = Mathf.Clamp01(startPercent);
        while (current < target)
        {
            if (GameController.I == null || !GameController.I.TryGetTask(taskId, out var node, out var task) || task == null || task.State != TaskState.Active)
                break;

            current = Mathf.Min(target, current + stepPercent);
            task.VisualProgress = baseDays * current;
            GameController.I.Notify();
            yield return new WaitForSeconds(interval);
        }

        if (GameController.I != null && GameController.I.TryGetTask(taskId, out var _, out var finalTask) && finalTask != null)
            finalTask.VisualProgress = -1f;
        _rollingTasks.Remove(taskId);
        _activeProgressRolls = Mathf.Max(0, _activeProgressRolls - 1);
        if (_activeProgressRolls == 0 && _activeAgents == 0)
            SetHudLocked(false);
    }

    private void SyncVisualProgress()
    {
        var gc = GameController.I;
        if (gc?.State?.Cities == null) return;

        foreach (var node in gc.State.Cities)
        {
            if (node?.Tasks == null) continue;
            foreach (var task in node.Tasks)
            {
                if (task == null || task.VisualProgress < 0f) continue;
                if (!string.IsNullOrEmpty(task.Id) && _rollingTasks.Contains(task.Id)) continue;
                if (!string.IsNullOrEmpty(task.Id) && _lockedVisualTasks.Contains(task.Id)) continue;
                if (task.Progress > 0f && task.Progress >= task.VisualProgress)
                    task.VisualProgress = -1f;
            }
        }
    }

    private void PrepareVisualProgressForRoll(string taskId, float previousProgress)
    {
        if (string.IsNullOrEmpty(taskId) || GameController.I == null) return;
        if (!GameController.I.TryGetTask(taskId, out var node, out var task) || task == null) return;
        if (task.Type != TaskType.Investigate && task.Type != TaskType.Contain) return;
        if (task.VisualProgress >= 0f) return;
        task.VisualProgress = Mathf.Max(0f, previousProgress);
        _lockedVisualTasks.Add(taskId);
        GameController.I.Notify();
    }

    private void RollProgressDeltas(Dictionary<string, TaskSnapshot> current)
    {
        if (current == null || current.Count == 0) return;

        foreach (var kvp in current)
        {
            var taskId = kvp.Key;
            var snapshot = kvp.Value;
            if (snapshot == null || snapshot.State != TaskState.Active) continue;
            if (snapshot.AgentIds == null || snapshot.AgentIds.Count == 0) continue;

            if (!_taskCache.TryGetValue(taskId, out var previous)) continue;
            if (previous.State != TaskState.Active) continue;
            if (previous.Progress <= 0f) continue;
            if (snapshot.Progress <= previous.Progress) continue;

            PrepareVisualProgressForRoll(taskId, previous.Progress);
            StartProgressRoll(taskId);
        }
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

    private IEnumerator AnimateAgent(string taskId, int index, int total, Vector2 startPos, Vector2 toLocal, float duration, System.Action onComplete)
    {
        var go = Instantiate(agentPrefab, tokenLayer);
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = startPos;
        Debug.Log($"[MapUI] Agent launch taskId={taskId} index={index}/{total} start={startPos} to={toLocal}");

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
        Debug.Log($"[MapUI] Agent arrive taskId={taskId} index={index}/{total} to={toLocal}");
        onComplete?.Invoke();
    }

    private void SetHudLocked(bool locked)
    {
        if (_hudLocked == locked) return;
        _hudLocked = locked;
        HUD.I?.SetControlsInteractable(!locked);
        Debug.Log($"[MapUI] HUD {(locked ? "locked" : "unlocked")} during dispatch");
    }

    private void MarkAgentOffBase(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;
        if (_offBaseAgents.Add(agentId))
            GameController.I?.Notify();
    }

    private void MarkAgentOnBase(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;
        if (_offBaseAgents.Remove(agentId))
            GameController.I?.Notify();
    }

    private void TryRegisterNodeFromScene(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (cities == null || cities.Length == 0) return;

        foreach (var city in cities)
        {
            if (city == null || !string.Equals(city.CityId, nodeId, System.StringComparison.OrdinalIgnoreCase))
                continue;

            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                _nodes[city.CityId] = rt;
                Debug.Log($"[MapUI] Dispatch node registered cityId={city.CityId}");
            }
            return;
        }
    }

    private void EnsureNodeRegistry()
    {
        if (_nodesScanned) return;
        _nodesScanned = true;

        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[MapUI] Dispatch node scan count={cities?.Length ?? 0}");

        if (cities == null || cities.Length == 0) return;

        foreach (var city in cities)
        {
            if (city == null || string.IsNullOrEmpty(city.CityId))
                continue;

            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                _nodes[city.CityId] = rt;
                Debug.Log($"[MapUI] Dispatch node registered cityId={city.CityId}");
            }
        }
    }

    private bool TryGetNodeLocalPoint(string nodeId, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (string.IsNullOrEmpty(nodeId)) return false;
        EnsureNodeRegistry();
        if (!_nodes.TryGetValue(nodeId, out var nodeRT) || nodeRT == null)
        {
            if (_missingNodeWarned.Add(nodeId))
                Debug.LogWarning($"[MapUI] Dispatch node missing nodeId={nodeId}");
            return false;
        }
        return TryGetNodeLocalPoint(nodeRT, out localPoint);
    }

    private bool TryGetDispatchPoint(string taskId, string nodeId, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (string.IsNullOrEmpty(nodeId)) return false;

        EnsureNodeRegistry();
        var baseId = ResolveBaseNodeId();
        Debug.Log($"[MapUI] Dispatch point lookup taskId={taskId} nodeId={nodeId} baseId={baseId ?? "null"} nodes={_nodes.Count}");
        if (!string.IsNullOrEmpty(baseId) && string.Equals(nodeId, baseId, System.StringComparison.OrdinalIgnoreCase))
            return TryGetNodeLocalPoint(nodeId, out localPoint);

        if (TryGetTaskAnomalyLocalPoint(taskId, nodeId, out localPoint))
            return true;

        return TryGetNodeLocalPoint(nodeId, out localPoint);
    }

    private bool TryGetTaskAnomalyLocalPoint(string taskId, string nodeId, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (string.IsNullOrEmpty(taskId) || GameController.I == null) return false;
        if (!GameController.I.TryGetTask(taskId, out var node, out var task) || task == null) return false;
        if (node == null || !string.Equals(node.Id, nodeId, System.StringComparison.OrdinalIgnoreCase)) return false;

        var anomalyId = ResolveTaskAnomalyId(node, task);
        if (string.IsNullOrEmpty(anomalyId)) return false;

        var key = BuildAnomalyKey(node.Id, anomalyId);
        if (!_anomalies.TryGetValue(key, out var anomalyRT) || anomalyRT == null)
            return false;

        return TryGetNodeLocalPoint(anomalyRT, out localPoint);
    }

    private static string ResolveTaskAnomalyId(CityState node, NodeTask task)
    {
        if (node == null || task == null) return null;

        if (task.Type == TaskType.Manage)
        {
            var managed = node.ManagedAnomalies?.Find(m => m != null && m.AnomalyInstanceId == task.TargetManagedAnomalyId);
            return managed?.AnomalyDefId ?? task.SourceAnomalyId;
        }

        if (!string.IsNullOrEmpty(task.SourceAnomalyId))
            return task.SourceAnomalyId;

        if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0)
            return node.ActiveAnomalyIds[0];

        return null;
    }

    private static string BuildAnomalyKey(string nodeId, string anomalyId)
        => $"{nodeId}:{anomalyId}";

    private bool TryGetNodeLocalPoint(RectTransform nodeRT, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (nodeRT == null || tokenLayer == null) return false;

        var screenPoint = RectTransformUtility.WorldToScreenPoint(null, nodeRT.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(tokenLayer, screenPoint, null, out localPoint);
    }

    private Vector2 ApplyBaseSpawnOffset(string fromNodeId, Vector2 origin)
    {
        var baseId = ResolveBaseNodeId();
        if (string.IsNullOrEmpty(baseId) || !string.Equals(fromNodeId, baseId, System.StringComparison.OrdinalIgnoreCase))
            return origin;

        if (baseSpawnRadius <= 0f) return origin;
        return origin + Random.insideUnitCircle * baseSpawnRadius;
    }

    private string ResolveBaseNodeId()
    {
        if (!string.IsNullOrEmpty(_cachedBaseNodeId))
            return _cachedBaseNodeId;

        if (_nodes.Count > 0)
        {
            var mapBase = FindBaseCityOnMap();
            if (!string.IsNullOrEmpty(mapBase))
            {
                Debug.Log($"[MapUI] Dispatch base resolved from map cityId={mapBase}");
                _cachedBaseNodeId = mapBase;
                return _cachedBaseNodeId;
            }
        }

        var gc = GameController.I;
        var baseNode = gc?.State?.Cities?.Find(node => node != null && node.Type == 0);
        if (baseNode == null)
        {
            if (_missingNodeWarned.Add("__base__"))
                Debug.LogWarning("[MapUI] Dispatch base node missing (type=0)");
            return null;
        }

        if (!_nodes.ContainsKey(baseNode.Id))
            TryRegisterNodeFromScene(baseNode.Id);

        Debug.Log($"[MapUI] Dispatch base resolved from state nodeId={baseNode.Id}");
        _cachedBaseNodeId = baseNode.Id;
        return _cachedBaseNodeId;
    }

    private string FindBaseCityOnMap()
    {
        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[MapUI] Dispatch base map scan count={cities?.Length ?? 0} nodes={_nodes.Count}");
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

        if (!_nodes.ContainsKey(baseCity.CityId))
        {
            var rt = baseCity.transform as RectTransform;
            if (rt != null)
            {
                _nodes[baseCity.CityId] = rt;
                Debug.Log($"[MapUI] Dispatch base node registered cityId={baseCity.CityId}");
            }
        }

        return baseCity.CityId;
    }

    private bool HasOffBaseAgents(List<string> agentIds)
    {
        if (agentIds == null || agentIds.Count == 0) return false;
        foreach (var agentId in agentIds)
        {
            if (string.IsNullOrEmpty(agentId)) continue;
            if (_offBaseAgents.Contains(agentId)) return true;
        }
        return false;
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

    private System.Collections.IEnumerator ConsumeTokenCoroutine(GameController gc, Core.MovementToken token)
    {
        _playingToken = token;
        token.State = Core.MovementTokenState.Playing;

        // 1) Play animation or fallback wait
        yield return PlayTokenAnimationOrFallback(gc, token);

        // 2) On animation end: apply landing and complete token
        ApplyTokenLanding(gc, token);

        token.State = Core.MovementTokenState.Completed;

        if (gc.State != null && gc.State.MovementLockCount > 0)
            gc.State.MovementLockCount -= 1;

        // Sync off-base agents according to agent LocationKind after token landing
        SyncOffBaseAgents();

        // Notify the controller that state has changed after token completion/unlock
        gc?.Notify();

        _playingToken = null;
        _tokenCo = null;
    }

    private System.Collections.IEnumerator PlayTokenAnimationOrFallback(GameController gc, Core.MovementToken token)
    {
        // Try to resolve anomaly and map points for a meaningful dispatch/recall animation.
        if (gc == null || token == null)
        {
            yield return new UnityEngine.WaitForSeconds(_fallbackTravelSeconds);
            yield break;
        }

        var anom = Core.DispatchSystem.FindAnomaly(gc.State, token.AnomalyKey);
        var nodeId = anom?.NodeId;
        var anomalyIdForMarker = anom?.AnomalyDefId ?? anom?.ManagedState?.AnomalyDefId ?? anom?.Id;

        var baseId = ResolveBaseNodeId();

        // Resolve base local point
        Vector2 baseLocal = Vector2.zero;
        bool haveBase = !string.IsNullOrEmpty(baseId) && TryGetNodeLocalPoint(baseId, out baseLocal);

        // Resolve anomaly marker/local point (prefer explicit anomaly marker, fallback to node point)
        Vector2 anomLocal = Vector2.zero;
        bool haveAnom = false;

        if (!string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(anomalyIdForMarker))
        {
            var key = BuildAnomalyKey(nodeId, anomalyIdForMarker);
            if (_anomalies.TryGetValue(key, out var anomalyRT) && anomalyRT != null)
            {
                haveAnom = TryGetNodeLocalPoint(anomalyRT, out anomLocal);
            }
        }

        if (!haveAnom && !string.IsNullOrEmpty(nodeId))
        {
            haveAnom = TryGetNodeLocalPoint(nodeId, out anomLocal);
        }

        // Need both points to play animation (from/to). Otherwise fallback to wait.
        if (!haveBase || !haveAnom)
        {
            yield return new UnityEngine.WaitForSeconds(_fallbackTravelSeconds);
            yield break;
        }

        Vector2 fromLocal, toLocal;
        if (token.Type == Core.MovementTokenType.Dispatch)
        {
            fromLocal = ApplyBaseSpawnOffset(baseId, baseLocal);
            toLocal = anomLocal;
        }
        else
        {
            fromLocal = anomLocal;
            toLocal = baseLocal;
        }

        // Play the single-agent animate coroutine and yield until complete.
        yield return AnimateAgent("token:" + token.TokenId, 1, 1, fromLocal, toLocal, Mathf.Max(0.01f, _fallbackTravelSeconds), null);
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
            // TravellingToAnomaly -> AtAnomaly
            if (ag.LocationKind == Core.AgentLocationKind.TravellingToAnomaly &&
                ag.LocationAnomalyKey == token.AnomalyKey)
            {
                ag.LocationKind = Core.AgentLocationKind.AtAnomaly;
            }
            else
            {
                // Tolerant: if state drifted, still land at anomaly
                ag.LocationKind = Core.AgentLocationKind.AtAnomaly;
                ag.LocationAnomalyKey = token.AnomalyKey;
            }
            ag.LocationSlot = token.Slot;
        }
        else
        {
            // Recall -> Base
            ag.LocationKind = Core.AgentLocationKind.Base;
            ag.LocationAnomalyKey = null;
            ag.LocationSlot = token.Slot;
        }
    }
}
