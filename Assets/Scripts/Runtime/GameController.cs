// Canvas-maintained file: Runtime/GameController
// Source: Assets/Scripts/Runtime/GameController.cs

// <EXPORT_BLOCK>
using System;
using System.Collections.Generic;
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
        State.Agents.Add(new AgentState { Id = "A2", Name = "Bob", Perception = 4, Operation = 7, Resistance = 5, Power = 6 });
        State.Agents.Add(new AgentState { Id = "A3", Name = "Chen", Perception = 5, Operation = 5, Resistance = 7, Power = 4 });

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

    public void AssignInvestigate(string nodeId, List<string> agentIds)
    {
        var n = GetNode(nodeId);
        if (n == null) return;

        n.Status = NodeStatus.Investigating;
        n.AssignedAgentIds = new List<string>(agentIds);
        n.InvestigateProgress = 0f;

        Notify();
    }

    public void AssignContain(string nodeId, List<string> agentIds)
    {
        var n = GetNode(nodeId);
        if (n == null) return;

        n.Status = NodeStatus.Containing;
        n.AssignedAgentIds = new List<string>(agentIds);
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

    // [Updated] 检查小队中是否有人忙碌
    public static bool AreAgentsBusy(GameController gc, List<string> agentIds, string currentNodeId)
    {
        // currentNodeId is kept for call-site compatibility.
        // Rule-set: 预定占用 —— assigned to ANY node (including current node) counts as busy.
        _ = currentNodeId;

        if (gc == null || gc.State == null || gc.State.Nodes == null) return false;
        if (agentIds == null || agentIds.Count == 0) return false;

        foreach (var node in gc.State.Nodes)
        {
            if (node == null) continue;

            // 若节点处于调查/收容（包含“待开始/已推进”）且有人员
            if ((node.Status == NodeStatus.Investigating || node.Status == NodeStatus.Containing) &&
                node.AssignedAgentIds != null)
            {
                if (node.AssignedAgentIds.Intersect(agentIds).Any())
                    return true;
            }
        }
        return false;
    }

    public static bool ForceWithdraw(this GameController gc, string nodeId)
    {
        if (gc == null || string.IsNullOrEmpty(nodeId)) return false;
        var n = gc.GetNode(nodeId);
        if (!HasTask(n)) return false;

        // 撤回：清空列表
        if (n.AssignedAgentIds != null) n.AssignedAgentIds.Clear();
        n.InvestigateProgress = 0f;
        n.ContainProgress = 0f;
        n.Status = default(NodeStatus);

        gc.Notify();
        return true;
    }

    // [Updated] 接口升级为 List<string>
    public static AssignResult TryAssignInvestigate(this GameController gc, string nodeId, List<string> agentIds)
        => TryAssign(gc, nodeId, agentIds, NodeStatus.Investigating);

    public static AssignResult TryAssignContain(this GameController gc, string nodeId, List<string> agentIds)
        => TryAssign(gc, nodeId, agentIds, NodeStatus.Containing);

    static AssignResult TryAssign(GameController gc, string nodeId, List<string> agentIds, NodeStatus targetStatus)
    {
        if (gc == null) return AssignResult.Fail("GameController is null");
        if (string.IsNullOrEmpty(nodeId)) return AssignResult.Fail("nodeId 为空");
        if (agentIds == null || agentIds.Count == 0) return AssignResult.Fail("未选择干员");

        var n = gc.GetNode(nodeId);
        if (n == null) return AssignResult.Fail("节点不存在");

        if (AreAgentsBusy(gc, agentIds, nodeId))
            return AssignResult.Fail("部分干员正在其他节点执行任务");

        bool hasTask = HasTask(n);

        if (hasTask)
        {
            bool started = GetProgress(n) > 0.0001f;

            // 已开始：仅允许完全相同的人员组合重复确认，否则需撤回
            if (started)
            {
                bool sameSquad = (n.AssignedAgentIds != null &&
                                  n.AssignedAgentIds.Count == agentIds.Count &&
                                  !n.AssignedAgentIds.Except(agentIds).Any());

                if (sameSquad && n.Status == targetStatus)
                    return AssignResult.Ok();

                return AssignResult.Fail("任务已开始：只能强制撤回后再更换派遣");
            }

            // 未推进（progress==0）：在“预定占用”规则下，不允许在选人面板里改派。
            // 必须通过节点任务卡片的【取消】释放后再重新派遣。
            bool sameSquad2 = (n.AssignedAgentIds != null &&
                              n.AssignedAgentIds.Count == agentIds.Count &&
                              !n.AssignedAgentIds.Except(agentIds).Any());

            if (sameSquad2 && n.Status == targetStatus)
                return AssignResult.Ok();

            return AssignResult.Fail("任务已预定：请先取消任务后再重新派遣");
        }

        // Idle
        if (targetStatus == NodeStatus.Investigating)
            gc.AssignInvestigate(nodeId, agentIds);
        else
            gc.AssignContain(nodeId, agentIds);

        return AssignResult.Ok();
    }
}
// </FORCE_WITHDRAW_EXT>

// </EXPORT_BLOCK>
