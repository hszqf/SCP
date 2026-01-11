// <EXPORT_BLOCK>
using System;
using System.Linq;
using Core;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public static GameController I { get; private set; }

    public GameState State { get; private set; } = new GameState();
    public event Action OnStateChanged;

    private System.Random _rng;

    [Header("Debug Seed (same seed => same run)")]
    [SerializeField] private int seed = 12345;

    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        _rng = new System.Random(seed);

        // ---- 初始干员（先写死，后面再换 Data/ScriptableObject）----
        State.Agents.Add(new AgentState { Id = "A1", Name = "Alice", Perception = 6, Operation = 5, Resistance = 5, Power = 4 });
        State.Agents.Add(new AgentState { Id = "A2", Name = "Bob",   Perception = 4, Operation = 7, Resistance = 5, Power = 6 });
        State.Agents.Add(new AgentState { Id = "A3", Name = "Chen",  Perception = 5, Operation = 5, Resistance = 7, Power = 4 });

        // ---- 初始节点（先写死）----
        State.Nodes.Add(new NodeState { Id = "N1", Name = "节点1", X = 0.35f, Y = 0.42f });
        State.Nodes.Add(new NodeState { Id = "N2", Name = "节点2", X = 0.62f, Y = 0.33f });
        State.Nodes.Add(new NodeState { Id = "N3", Name = "节点3", X = 0.48f, Y = 0.58f });

        Notify();
    }

    public void Notify() => OnStateChanged?.Invoke();

    public NodeState GetNode(string nodeId)
        => State.Nodes.FirstOrDefault(n => n.Id == nodeId);

    public AgentState GetAgent(string agentId)
        => State.Agents.FirstOrDefault(a => a.Id == agentId);

    public void EndDay()
    {
        Sim.StepDay(State, _rng);
        Notify();
    }

    public void AssignInvestigate(string nodeId, string agentId)
    {
        var n = GetNode(nodeId);
        if (n == null) return;

        n.Status = NodeStatus.Investigating;
        n.AssignedAgentId = agentId;
        n.InvestigateProgress = 0f;

        Notify();
    }

    public void AssignContain(string nodeId, string agentId)
    {
        var n = GetNode(nodeId);
        if (n == null) return;

        n.Status = NodeStatus.Containing;
        n.AssignedAgentId = agentId;
        n.ContainProgress = 0f;

        Notify();
    }

    public (bool success, string text) ResolveEvent(string eventId, string optionId)
    {
        var res = Sim.ResolveEvent(State, eventId, optionId, _rng);
        Notify();
        return res;
    }
}

// <FORCE_WITHDRAW_EXT>
/// <summary>
/// Dispatch rules (extensions):
/// - Progress==0: can freely change assignee / switch Investigate<->Contain.
/// - Progress>0: cannot change; must ForceWithdraw first (treated as failure; no extra penalty, only time/progress lost).
/// - Prevent assigning same agent to multiple nodes (if other node is Investigating/Containing).
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

    public static bool IsTaskStarted(this GameController gc, string nodeId)
    {
        var n = gc?.GetNode(nodeId);
        if (n == null) return false;
        if (n.Status == NodeStatus.Investigating) return n.InvestigateProgress > 0.0001f;
        if (n.Status == NodeStatus.Containing) return n.ContainProgress > 0.0001f;
        return false;
    }

    static bool HasTask(NodeState n)
        => n != null && (n.Status == NodeStatus.Investigating || n.Status == NodeStatus.Containing);

    static float GetProgress(NodeState n)
    {
        if (n == null) return 0f;
        if (n.Status == NodeStatus.Investigating) return n.InvestigateProgress;
        if (n.Status == NodeStatus.Containing) return n.ContainProgress;
        return 0f;
    }

    static bool IsAgentBusyOnOtherNode(GameController gc, string agentId, string currentNodeId)
    {
        if (gc == null || gc.State == null || gc.State.Nodes == null) return false;

        foreach (var node in gc.State.Nodes)
        {
            if (node == null) continue;
            if (node.Id == currentNodeId) continue;
            if (node.AssignedAgentId != agentId) continue;

            // 只要在 Investigating/Containing，就算占用（即使进度==0，也避免一人多派）
            if (node.Status == NodeStatus.Investigating || node.Status == NodeStatus.Containing)
                return true;
        }
        return false;
    }

    public static bool ForceWithdraw(this GameController gc, string nodeId)
    {
        if (gc == null || string.IsNullOrEmpty(nodeId)) return false;
        var n = gc.GetNode(nodeId);
        if (!HasTask(n)) return false;

        // 撤回=任务失败：清空派遣与进度；不额外惩罚（时间/过程中影响已体现在别处）
        n.AssignedAgentId = null;
        n.InvestigateProgress = 0f;
        n.ContainProgress = 0f;
        n.Status = default(NodeStatus);

        gc.Notify();
        return true;
    }

    public static AssignResult TryAssignInvestigate(this GameController gc, string nodeId, string agentId)
        => TryAssign(gc, nodeId, agentId, NodeStatus.Investigating);

    public static AssignResult TryAssignContain(this GameController gc, string nodeId, string agentId)
        => TryAssign(gc, nodeId, agentId, NodeStatus.Containing);

    static AssignResult TryAssign(GameController gc, string nodeId, string agentId, NodeStatus targetStatus)
    {
        if (gc == null) return AssignResult.Fail("GameController is null");
        if (string.IsNullOrEmpty(nodeId)) return AssignResult.Fail("nodeId 为空");
        if (string.IsNullOrEmpty(agentId)) return AssignResult.Fail("agentId 为空");

        var n = gc.GetNode(nodeId);
        if (n == null) return AssignResult.Fail("节点不存在");

        // 忙于别的节点则不允许
        if (IsAgentBusyOnOtherNode(gc, agentId, nodeId))
            return AssignResult.Fail("该干员正在其他节点执行任务，不能重复派遣");

        bool hasTask = HasTask(n);

        if (hasTask)
        {
            bool started = GetProgress(n) > 0.0001f;

            // 已开始：禁止直接换人/换类型（只允许重复点击同人同类型=OK）
            if (started)
            {
                if (n.AssignedAgentId == agentId && n.Status == targetStatus)
                    return AssignResult.Ok();

                return AssignResult.Fail("任务已开始：只能强制撤回后再更换派遣");
            }

            // 未开始：允许自由换人、允许切换任务类型
            n.Status = targetStatus;
            n.AssignedAgentId = agentId;
            n.InvestigateProgress = 0f;
            n.ContainProgress = 0f;

            gc.Notify();
            return AssignResult.Ok();
        }

        // Idle：直接调用原有接口开始任务
        if (targetStatus == NodeStatus.Investigating)
            gc.AssignInvestigate(nodeId, agentId);
        else
            gc.AssignContain(nodeId, agentId);

        return AssignResult.Ok();
    }
}
// </FORCE_WITHDRAW_EXT>

// </EXPORT_BLOCK>
