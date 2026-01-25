// Canvas-maintained file: Runtime/GameController (v3 - N tasks backend, legacy UI compatibility)
// Source target: Assets/Scripts/Runtime/GameController.cs
//
// What changed vs v2:
// - NodeStatus no longer has Investigating/Containing; tasks are stored in NodeState.Tasks (unlimited).
// - Assign/Busy/Cancel/Retreat all operate on NodeTask.
// - Legacy APIs (TryAssignInvestigate/TryAssignContain/ForceWithdraw(nodeId)) are kept so existing UI can compile.
//   They intentionally operate on a single "current" task per type to avoid creating invisible tasks before UI is upgraded.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Data;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public static GameController I { get; private set; }

    public GameState State { get; private set; } = new GameState();
    public event Action OnStateChanged;
    public bool IsGameOver { get; private set; }

    private System.Random _rng;

    [Header("Debug Seed (same seed => same run)")]
    [SerializeField] private int seed = 12345;


    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        _rng = new System.Random(seed);
        var registry = DataRegistry.Instance;
        Sim.OnIgnorePenaltyApplied += HandleIgnorePenaltyApplied;

        // ---- 初始全局状态（从 Balance 表读取）----
        int startMoney = registry.GetBalanceIntWithWarn("StartMoney", 0);
        float startWorldPanic = registry.GetBalanceFloatWithWarn("StartWorldPanic", 0f);
        int clampMoneyMin = registry.GetBalanceIntWithWarn("ClampMoneyMin", 0);
        float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);
        Debug.Log($"[Balance] StartMoney={startMoney} StartWorldPanic={startWorldPanic} ClampMoneyMin={clampMoneyMin} ClampWorldPanicMin={clampWorldPanicMin}");
        State.Money = Math.Max(clampMoneyMin, startMoney);
        State.WorldPanic = Math.Max(clampWorldPanicMin, startWorldPanic);

        // ---- 初始干员（先写死，后面再换 Data/ScriptableObject）----
        State.Agents.Add(new AgentState { Id = "A1", Name = "Alice", Perception = 6, Operation = 5, Resistance = 5, Power = 4 });
        State.Agents.Add(new AgentState { Id = "A2", Name = "Bob", Perception = 4, Operation = 7, Resistance = 5, Power = 6 });
        State.Agents.Add(new AgentState { Id = "A3", Name = "Chen", Perception = 5, Operation = 5, Resistance = 7, Power = 4 });

        // ---- 初始节点（从 DataRegistry 读取，坐标仍由代码提供）----
        var nodeCoords = new Dictionary<string, (float x, float y)>
        {
            ["N1"] = (0.35f, 0.42f),
            ["N2"] = (0.62f, 0.33f),
            ["N3"] = (0.48f, 0.58f),
        };

        foreach (var nodeDef in DataRegistry.Instance.NodesById.Values.OrderBy(n => n.nodeId))
        {
            var coord = nodeCoords.TryGetValue(nodeDef.nodeId, out var c) ? c : (x: 0.5f, y: 0.5f);
            var nodeState = new NodeState
            {
                Id = nodeDef.nodeId,
                Name = nodeDef.name,
                X = coord.x,
                Y = coord.y,
                LocalPanic = Math.Max(0, nodeDef.startLocalPanic),
                Population = Math.Max(0, nodeDef.startPopulation),
                Tags = nodeDef.tags ?? new List<string>(),
                ActiveAnomalyIds = nodeDef.startAnomalyIds ?? new List<string>(),
            };
            nodeState.HasAnomaly = nodeState.ActiveAnomalyIds.Count > 0;
            if (nodeState.HasAnomaly)
            {
                foreach (var anomalyId in nodeState.ActiveAnomalyIds)
                {
                    if (DataRegistry.Instance.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef))
                    {
                        nodeState.AnomalyLevel = Math.Max(nodeState.AnomalyLevel, Math.Max(1, anomalyDef.baseThreat));
                    }
                }
            }

            State.Nodes.Add(nodeState);
        }

        Notify();
    }

    private void OnDestroy()
    {
        if (I == this)
        {
            Sim.OnIgnorePenaltyApplied -= HandleIgnorePenaltyApplied;
        }
    }

    public void Notify() => OnStateChanged?.Invoke();

    public NodeState GetNode(string nodeId)
        => State.Nodes.FirstOrDefault(n => n.Id == nodeId);

    public AgentState GetAgent(string agentId)
        => State.Agents.FirstOrDefault(a => a.Id == agentId);

    public void EndDay()
    {
        if (IsGameOver)
        {
            Debug.LogWarning("[GameOver] EndDay ignored because the game is already over.");
            return;
        }

        Sim.StepDay(State, _rng);
        Notify();
        RefreshMapNodes();
    }

    public void MarkGameOver(string reason)
    {
        if (IsGameOver) return;
        IsGameOver = true;
        Debug.Log($"[GameOver] {reason}");
    }

    public (bool success, string text) ResolveEvent(string nodeId, string eventId, string optionId)
    {
        var res = Sim.ResolveEvent(State, nodeId, eventId, optionId, _rng);
        Notify();
        RefreshMapNodes();
        return res;
    }

    // Legacy wrapper: locate node by eventId across all nodes.
    public (bool success, string text) ResolveEvent(string eventId, string optionId)
    {
        var node = State.Nodes.FirstOrDefault(n => n?.PendingEvents != null && n.PendingEvents.Any(e => e != null && e.EventInstanceId == eventId));
        if (node == null) return (false, "事件不存在");
        return ResolveEvent(node.Id, eventId, optionId);
    }

    public bool TryHireAgent(int cost, out AgentState agent)
    {
        agent = null;
        if (cost < 0) cost = 0;
        if (State == null) return false;
        if (State.Money < cost) return false;

        int clampMoneyMin = DataRegistry.Instance.GetBalanceIntWithWarn("ClampMoneyMin", 0);
        int moneyAfter = Math.Max(clampMoneyMin, State.Money - cost);
        State.Money = moneyAfter;

        string agentId = GenerateNextAgentId();
        agent = new AgentState
        {
            Id = agentId,
            Name = $"Agent {agentId}",
            Perception = 5,
            Operation = 5,
            Resistance = 5,
            Power = 5,
        };

        State.Agents.Add(agent);
        Debug.Log($"[Hire] cost={cost} moneyAfter={moneyAfter} agentId={agentId}");
        Notify();
        RefreshMapNodes();
        return true;
    }

    private string GenerateNextAgentId()
    {
        int max = 0;
        if (State?.Agents != null)
        {
            foreach (var a in State.Agents)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                if (!a.Id.StartsWith("A", StringComparison.OrdinalIgnoreCase)) continue;
                var raw = a.Id.Substring(1);
                if (int.TryParse(raw, out var value))
                    max = Math.Max(max, value);
            }
        }

        int next = max + 1;
        string candidate = $"A{next}";
        while (State?.Agents?.Any(a => a != null && a.Id == candidate) == true)
        {
            next++;
            candidate = $"A{next}";
        }

        return candidate;
    }

    // =====================
    // N-task APIs (new)
    // =====================

    public NodeTask CreateInvestigateTask(string nodeId)
    {
        var n = GetNode(nodeId);
        if (n == null) return null;
        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        var t = new NodeTask
        {
            Id = "T_" + Guid.NewGuid().ToString("N")[..10],
            Type = TaskType.Investigate,
            State = TaskState.Active,
            CreatedDay = State.Day,
            Progress = 0f,
        };
        n.Tasks.Add(t);
        LogTaskDefSummary(TaskType.Investigate);
        GameControllerTaskExt.LogBusySnapshot(this, $"CreateInvestigateTask(node:{nodeId})");
        return t;
    }

    public NodeTask CreateContainTask(string nodeId, string containableId)
    {
        var n = GetNode(nodeId);
        if (n == null) return null;
        if (n.Containables == null || n.Containables.Count == 0) return null;

        // Validate target; fallback to first
        string target = containableId;
        if (string.IsNullOrEmpty(target) || !n.Containables.Any(c => c != null && c.Id == target))
            target = n.Containables[0].Id;

        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        var t = new NodeTask
        {
            Id = "T_" + Guid.NewGuid().ToString("N")[..10],
            Type = TaskType.Contain,
            State = TaskState.Active,
            CreatedDay = State.Day,
            Progress = 0f,
            TargetContainableId = target,
        };
        n.Tasks.Add(t);
        LogTaskDefSummary(TaskType.Contain);
        GameControllerTaskExt.LogBusySnapshot(this, $"CreateContainTask(node:{nodeId}, target:{target})");
        return t;
    }

    public NodeTask CreateManageTask(string nodeId, string managedAnomalyId)
    {
        var n = GetNode(nodeId);
        if (n == null) return null;
        if (n.ManagedAnomalies == null || n.ManagedAnomalies.Count == 0) return null;

        // Validate target
        string target = managedAnomalyId;
        if (string.IsNullOrEmpty(target) || !n.ManagedAnomalies.Any(m => m != null && m.Id == target))
            return null;

        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        // If already has an active manage task for this anomaly, reuse (idempotent)
        var existing = n.Tasks.LastOrDefault(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == target);
        if (existing != null) return existing;

        var t = new NodeTask
        {
            Id = "T_" + Guid.NewGuid().ToString("N")[..10],
            Type = TaskType.Manage,
            State = TaskState.Active,
            CreatedDay = State.Day,
            Progress = 0f,
            TargetManagedAnomalyId = target,
        };
        n.Tasks.Add(t);
        LogTaskDefSummary(TaskType.Manage);
        GameControllerTaskExt.LogBusySnapshot(this, $"CreateManageTask(node:{nodeId}, anomaly:{target})");
        return t;
    }

    public bool TryGetTask(string taskId, out NodeState node, out NodeTask task)
    {
        node = null;
        task = null;
        if (string.IsNullOrEmpty(taskId) || State?.Nodes == null) return false;

        foreach (var n in State.Nodes)
        {
            if (n?.Tasks == null) continue;
            var t = n.Tasks.FirstOrDefault(x => x != null && x.Id == taskId);
            if (t != null)
            {
                node = n;
                task = t;
                return true;
            }
        }
        return false;
    }

    public bool CancelOrRetreatTask(string taskId)
    {
        if (!TryGetTask(taskId, out var n, out var t)) return false;
        if (t.State != TaskState.Active) return false;

        // Cancel == progress==0 ; Retreat == progress>0 (same effect for now: mark cancelled + release squad)
        if (t.AssignedAgentIds != null) t.AssignedAgentIds.Clear();
        t.Progress = 0f;
        t.State = TaskState.Cancelled;

        // Node status: only coarse
        if (n != null && n.Status != NodeStatus.Secured)
            n.Status = NodeStatus.Calm;

        GameControllerTaskExt.LogBusySnapshot(this, $"CancelOrRetreatTask(task:{taskId})");
        Notify();
        return true;
    }

    public void AssignTask(string taskId, List<string> agentIds)
    {
        if (!TryGetTask(taskId, out var n, out var t)) return;
        if (t.State != TaskState.Active) return;

        t.AssignedAgentIds = new List<string>(agentIds);

        // Node-level bookkeeping
        if (n != null && n.Status != NodeStatus.Secured)
            n.Status = NodeStatus.Calm;


        GameControllerTaskExt.LogBusySnapshot(this, $"AssignTask(task:{taskId}, agents:{string.Join(",", agentIds)})");
        Notify();
    }

    private void LogTaskDefSummary(TaskType type)
    {
        var registry = DataRegistry.Instance;
        int baseDays = registry.GetTaskBaseDaysWithWarn(type, 1);
        var (minSlots, maxSlots) = registry.GetTaskAgentSlotRangeWithWarn(type, 1, int.MaxValue);
        Debug.Log($"[TaskDef] taskType={type} baseDays={baseDays} slotsMin={minSlots} slotsMax={maxSlots}");
    }

    private void RefreshMapNodes()
    {
        MapNodeSpawner.I?.RefreshMapNodes();
        UIPanelRoot.I?.RefreshNodePanel();
    }

    private void HandleIgnorePenaltyApplied()
    {
        RefreshMapNodes();
    }

    // =====================
    // Legacy APIs (temporary)
    // =====================

    // Old UI calls these; keep them as wrappers.
    public void AssignInvestigate(string nodeId, List<string> agentIds)
    {
        var t = CreateInvestigateTask(nodeId);
        if (t == null) return;
        AssignTask(t.Id, agentIds);
    }

    public void AssignContain(string nodeId, List<string> agentIds)
    {
        var n = GetNode(nodeId);
        if (n == null) return;
        if (n.Containables == null || n.Containables.Count == 0) return;

        // Legacy: default to first containable
        var t = CreateContainTask(nodeId, n.Containables[0].Id);
        if (t == null) return;
        AssignTask(t.Id, agentIds);
    }

    // Optional convenience API for UI: assign management squad to a managed anomaly.
    public void AssignManage(string nodeId, string managedAnomalyId, List<string> agentIds)
    {
        var t = CreateManageTask(nodeId, managedAnomalyId);
        if (t == null) return;
        AssignTask(t.Id, agentIds);
    }
}

/// <summary>
/// Dispatch rules (extensions) - v3:
/// - 预定占用：派遣=占用（progress==0 也占用）。busy 判定遍历所有节点所有 Active 任务。
/// - progress==0：不允许在选人面板改派；必须先取消该任务。
/// - progress>0：不允许更换；必须撤退该任务。
/// - 收容前置：必须有可收容目标（Containables > 0）。
///
/// 注意：为了兼容旧 UI（尚未支持任务列表），TryAssignInvestigate/TryAssignContain 只操作“当前任务”（每类取一条）。
/// 等 NodePanelView 升级为任务列表后，再引入基于 taskId 的 UI 调度。
/// </summary>
public static class GameControllerTaskExt
{
    public struct AssignResult
    {
        public bool ok;
        public string reason;

        public static AssignResult Ok() => new AssignResult { ok = true, reason = "" };
        public static AssignResult Fail(string r) => new AssignResult { ok = false, reason = r };
    }

    // ---------- Busy ----------

    public static bool AreAgentsBusy(GameController gc, List<string> agentIds, string _unusedCurrentNodeId = null)
    {
        if (gc == null || gc.State?.Nodes == null) return false;
        if (agentIds == null || agentIds.Count == 0) return false;

        var busy = DeriveBusyAgentIdsFromTasks(gc);
        bool anyBusy = agentIds.Any(id => !string.IsNullOrEmpty(id) && busy.Contains(id));
        if (anyBusy)

            LogBusySnapshot(gc, $"AreAgentsBusy(check:{string.Join(",", agentIds)})");
        return anyBusy;
    }

    // ---------- Legacy: task started? ----------

    public static bool IsTaskStarted(this GameController gc, string nodeId)
    {
        var n = gc?.GetNode(nodeId);
        if (n?.Tasks == null) return false;
        // any active task with progress>0
        return n.Tasks.Any(t => t != null && t.State == TaskState.Active && t.Progress > 0.0001f);
    }

    // ---------- Legacy: withdraw all on node ----------

    public static bool ForceWithdraw(this GameController gc, string nodeId)
    {
        if (gc == null || string.IsNullOrEmpty(nodeId)) return false;
        var n = gc.GetNode(nodeId);
        if (n == null || n.Tasks == null || n.Tasks.Count == 0) return false;

        bool any = false;
        foreach (var t in n.Tasks)
        {
            if (t == null) continue;
            if (t.State != TaskState.Active) continue;

            // Cancel == progress==0 ; Retreat == progress>0 (same effect for legacy node-level withdraw)
            if (t.AssignedAgentIds != null) t.AssignedAgentIds.Clear();
            t.Progress = 0f;
            t.State = TaskState.Cancelled;
            any = true;
        }

        if (any && n.Status != NodeStatus.Secured)
            n.Status = NodeStatus.Calm;

        if (any)
        {
            GameControllerTaskExt.LogBusySnapshot(gc, $"ForceWithdraw(node:{nodeId})");
            gc.Notify();
        }
        return any;
    }

    // ---------- Legacy: per-type assignment ----------

    public static AssignResult TryAssignInvestigate(this GameController gc, string nodeId, List<string> agentIds)
        => TryAssignLegacyCurrentTask(gc, nodeId, agentIds, TaskType.Investigate);

    public static AssignResult TryAssignContain(this GameController gc, string nodeId, List<string> agentIds)
        => TryAssignLegacyCurrentTask(gc, nodeId, agentIds, TaskType.Contain);

    static AssignResult TryAssignLegacyCurrentTask(GameController gc, string nodeId, List<string> agentIds, TaskType type)
    {
        if (gc == null) return AssignResult.Fail("GameController is null");
        if (string.IsNullOrEmpty(nodeId)) return AssignResult.Fail("nodeId 为空");
        if (agentIds == null || agentIds.Count == 0) return AssignResult.Fail("未选择干员");

        var n = gc.GetNode(nodeId);
        if (n == null) return AssignResult.Fail("节点不存在");
        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        // 收容前置：必须有调查产出的可收容目标
        if (type == TaskType.Contain)
        {
            int c = (n.Containables != null) ? n.Containables.Count : 0;
            if (c <= 0)
                return AssignResult.Fail("未发现可收容目标：请先派遣调查完成后再进行收容");
        }

        // Busy check (global)
        if (AreAgentsBusy(gc, agentIds))
            return AssignResult.Fail("部分干员正在其他任务执行中");

        // Pick current task for this type (legacy behavior: only operate one visible task per type)
        var current = n.Tasks.LastOrDefault(t => t != null && t.State == TaskState.Active && t.Type == type);
        if (current == null)
        {
            // Create one
            if (type == TaskType.Investigate)
                current = gc.CreateInvestigateTask(nodeId);
            else
            {
                // default to first containable
                string target = n.Containables[0].Id;
                current = gc.CreateContainTask(nodeId, target);
            }
        }

        if (current == null) return AssignResult.Fail("创建任务失败");

        // Validate containment target still exists
        if (type == TaskType.Contain)
        {
            if (n.Containables == null || n.Containables.Count == 0)
                return AssignResult.Fail("可收容目标已为空");

            if (string.IsNullOrEmpty(current.TargetContainableId) || !n.Containables.Any(c => c != null && c.Id == current.TargetContainableId))
                current.TargetContainableId = n.Containables[0].Id;
        }

        // Enforce "no repick" per task
        bool hasSquad = (current.AssignedAgentIds != null && current.AssignedAgentIds.Count > 0);
        if (hasSquad)
        {
            bool started = current.Progress > 0.0001f;
            bool sameSquad = current.AssignedAgentIds.Count == agentIds.Count && !current.AssignedAgentIds.Except(agentIds).Any();

            if (sameSquad) return AssignResult.Ok();
            return started
                ? AssignResult.Fail("任务已开始：只能撤退后再更换派遣")
                : AssignResult.Fail("任务已预定：请先取消任务后再重新派遣");
        }

        // Assign squad
        current.AssignedAgentIds = new List<string>(agentIds);

        LogBusySnapshot(gc, $"TryAssignLegacyCurrentTask(node:{nodeId}, type:{type}, agents:{string.Join(",", agentIds)})");

        return AssignResult.Ok();
    }

    // ---------- Busy derivation & debug ----------

    public static HashSet<string> DeriveBusyAgentIdsFromTasks(GameController gc)
    {
        var result = new HashSet<string>();
        if (gc?.State?.Nodes == null) return result;

        foreach (var node in gc.State.Nodes)
        {
            if (node?.Tasks == null) continue;
            foreach (var t in node.Tasks)
            {
                if (t == null || t.State != TaskState.Active) continue;
                if (t.AssignedAgentIds == null) continue;
                foreach (var id in t.AssignedAgentIds)
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
        }

        return result;
    }

    public static void LogBusySnapshot(GameController gc, string context)
    {
        if (gc == null) return;
        var busy = DeriveBusyAgentIdsFromTasks(gc);
        var list = string.Join(",", busy.OrderBy(x => x));
        Debug.Log($"[BusySnapshot] {context} => count={busy.Count} ids=[{list}]");
    }
}
// </EXPORT_BLOCK>
