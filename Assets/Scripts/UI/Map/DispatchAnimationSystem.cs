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
    [SerializeField] private string baseNodeId = "BASE";
    [SerializeField] private float travelSeconds = 10f;
    [SerializeField] private float baseSpawnRadius = 5f;
    [SerializeField] private float launchInterval = 0.5f;
    [SerializeField] private float arrivalProgressStepPercent = 0.01f;
    [SerializeField] private float arrivalProgressInterval = 0.12f;

    private readonly Dictionary<string, RectTransform> _nodes = new();
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
    private bool _hudLocked;
    private float _lastBindAttempt;
    private readonly HashSet<string> _rollingTasks = new();
    private readonly HashSet<string> _lockedVisualTasks = new();

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
    }

    public void RegisterNode(string nodeId, RectTransform nodeRT)
    {
        if (string.IsNullOrEmpty(nodeId) || nodeRT == null) return;
        _nodes[nodeId] = nodeRT;
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
        if (gc?.State?.Nodes == null) return result;

        foreach (var node in gc.State.Nodes)
        {
            if (node?.Tasks == null) continue;
            foreach (var task in node.Tasks)
            {
                if (task == null || string.IsNullOrEmpty(task.Id)) continue;
                var agents = task.AssignedAgentIds ?? new List<string>();
                result[task.Id] = new TaskSnapshot
                {
                    NodeId = node.Id,
                    State = task.State,
                    AgentIds = new List<string>(agents),
                    Progress = task.Progress
                };
            }
        }

        return result;
    }

    private void EnqueueTransitions()
    {
        var current = BuildTaskSnapshotMap();

        foreach (var kvp in current)
        {
            var taskId = kvp.Key;
            var snapshot = kvp.Value;
            if (snapshot.State != TaskState.Active || snapshot.AgentIds.Count == 0 || snapshot.Progress <= 0f)
                continue;

            if (!_taskCache.TryGetValue(taskId, out var previous) || previous.Progress <= 0f)
                EnqueueDispatch(DispatchMode.Go, taskId, baseNodeId, snapshot.NodeId, snapshot.AgentIds, previous?.Progress ?? 0f);
        }

        foreach (var kvp in _taskCache)
        {
            var taskId = kvp.Key;
            var snapshot = kvp.Value;
            if (snapshot.State != TaskState.Active) continue;

            if (!current.TryGetValue(taskId, out var currentSnapshot) || currentSnapshot.State != TaskState.Active)
            {
                if (snapshot.Progress > 0f)
                    EnqueueDispatch(DispatchMode.Return, taskId, snapshot.NodeId, baseNodeId, snapshot.AgentIds);
            }
        }

        _taskCache.Clear();
        foreach (var kvp in current)
            _taskCache[kvp.Key] = kvp.Value;
    }

    private void EnqueueDispatch(DispatchMode mode, string taskId, string fromNodeId, string toNodeId, List<string> agentIds, float previousProgress = 0f)
    {
        string key = $"{mode}:{taskId}";
        if (!_pendingKeys.Add(key)) return;

        _pending.Enqueue(new DispatchEvent
        {
            Mode = mode,
            TaskId = taskId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            AgentIds = agentIds != null ? new List<string>(agentIds) : new List<string>(),
            Key = key,
            PreviousProgress = previousProgress
        });

        Debug.Log($"[MapUI] Dispatch enqueue mode={mode} taskId={taskId} from={fromNodeId} to={toNodeId} agents={agentIds?.Count ?? 0}");
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

            if (!TryGetNodeLocalPoint(ev.FromNodeId, out var fromLocal) || !TryGetNodeLocalPoint(ev.ToNodeId, out var toLocal))
                continue;

            int agentCount = ev.AgentIds?.Count ?? 0;
            if (agentCount <= 0) continue;

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
                StartCoroutine(AnimateAgent(ev.TaskId, index, agentCount, startPos, toLocal, duration, () =>
                {
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
                StartProgressRoll(ev.TaskId);
        }

        _playRoutine = null;
    }

    private void StartProgressRoll(string taskId)
    {
        if (string.IsNullOrEmpty(taskId) || _rollingTasks.Contains(taskId)) return;
        if (GameController.I == null) return;
        if (!GameController.I.TryGetTask(taskId, out var node, out var task) || task == null) return;
        if (task.Type != TaskType.Investigate) return;
        if (task.Progress <= 0f) return;

        int baseDays = GetTaskBaseDays(task);
        if (baseDays <= 0) return;

        float step = Mathf.Max(0f, arrivalProgressStepPercent);
        float target = Mathf.Clamp01(task.Progress / baseDays);
        float interval = Mathf.Max(0.01f, arrivalProgressInterval);

        _lockedVisualTasks.Remove(taskId);
        _rollingTasks.Add(taskId);
        StartCoroutine(ProgressRollRoutine(taskId, baseDays, step, target, interval));
    }

    private IEnumerator ProgressRollRoutine(string taskId, int baseDays, float stepPercent, float targetPercent, float interval)
    {
        float target = Mathf.Clamp01(targetPercent);
        float current = 0f;
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
    }

    private void SyncVisualProgress()
    {
        var gc = GameController.I;
        if (gc?.State?.Nodes == null) return;

        foreach (var node in gc.State.Nodes)
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
        if (task.Type != TaskType.Investigate) return;
        if (task.VisualProgress >= 0f) return;
        task.VisualProgress = Mathf.Max(0f, previousProgress);
        _lockedVisualTasks.Add(taskId);
        GameController.I.Notify();
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

    private bool TryGetNodeLocalPoint(string nodeId, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (string.IsNullOrEmpty(nodeId)) return false;
        if (!_nodes.TryGetValue(nodeId, out var nodeRT) || nodeRT == null)
        {
            if (_missingNodeWarned.Add(nodeId))
                Debug.LogWarning($"[MapUI] Dispatch node missing nodeId={nodeId}");
            return false;
        }
        return TryGetNodeLocalPoint(nodeRT, out localPoint);
    }

    private bool TryGetNodeLocalPoint(RectTransform nodeRT, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (nodeRT == null || tokenLayer == null) return false;

        var screenPoint = RectTransformUtility.WorldToScreenPoint(null, nodeRT.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(tokenLayer, screenPoint, null, out localPoint);
    }

    private Vector2 ApplyBaseSpawnOffset(string fromNodeId, Vector2 origin)
    {
        if (!string.Equals(fromNodeId, baseNodeId, System.StringComparison.OrdinalIgnoreCase))
            return origin;

        if (baseSpawnRadius <= 0f) return origin;
        return origin + Random.insideUnitCircle * baseSpawnRadius;
    }
}
