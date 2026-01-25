// Canvas-maintained file: Core/GameState (v3 - N tasks)
// Source target: Assets/Scripts/Core/GameState.cs
// Goal: Support unlimited per-node tasks (investigate/contain) via NodeState.Tasks.
// Notes:
// - Legacy single-task fields are kept temporarily for compatibility with existing UI/code.
// - New systems should only use NodeState.Tasks.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;

namespace Core
{
    // Keep for legacy UI and map display. In N-task model, task state is derived from NodeState.Tasks.
    public enum NodeStatus { Calm, Secured }

    // Legacy event kind (PendingEvent). New node events use EventSource/EventInstance.
    public enum EventKind { Investigate, Contain }

    public enum TaskType { Investigate, Contain, Manage }
    public enum TaskState { Active, Completed, Cancelled }

    [Serializable]
    public class AgentState
    {
        public string Id;
        public string Name;

        public int Perception = 5;
        public int Operation = 5;
        public int Resistance = 5;
        public int Power = 5;
    }

    [Serializable]
    public class ContainableItem
    {
        public string Id;
        public string Name;
        public int Level = 1;
        public string AnomalyId;
    }

    // 收容后进入“已收藏异常”，可被分配干员进行长期管理，按天产出负熵。
    [Serializable]
    public class ManagedAnomalyState
    {
        public string Id;
        public string Name;
        public int Level = 1;
        public string AnomalyId;
        public string AnomalyClass;

        // 左侧“已收藏异常”列表使用（后续可做收藏/取消收藏筛选）
        public bool Favorited = true;

        // 第一次开始管理的日期（用于统计/成长）
        public int StartDay;

        // 累计产出
        public int TotalNegEntropy;
    }

    [Serializable]
    public class NodeTask
    {
        public string Id;
        public TaskType Type;
        public TaskState State = TaskState.Active;

        // 0..1
        public float Progress = 0f;

        // 预定占用：progress==0 也算占用。
        public List<string> AssignedAgentIds = new();

        // Only for containment tasks: which containable we are trying to contain.
        public string TargetContainableId;

        // Only for management tasks: which managed anomaly we are managing.
        public string TargetManagedAnomalyId;

        public int CreatedDay;
        public int CompletedDay;

        public bool IsStarted => Progress > 0.0001f;
    }

    [Serializable]
    public class NodeState
    {
        public string Id;
        public string Name;
        public List<string> Tags = new();

        // 0..1 百分比坐标：左下(0,0) 右上(1,1)
        public float X;
        public float Y;

        // Node-level status is now coarse. Tasks are in Tasks list.
        public NodeStatus Status = NodeStatus.Calm;

        public bool HasAnomaly = false;
        public int AnomalyLevel = 0;
        public List<string> ActiveAnomalyIds = new();

        // 调查产出：可收容目标列表（调查完成后写入）
        public List<ContainableItem> Containables = new();
        // 收容产出：已收容目标列表（收容完成后写入）
        public List<ManagedAnomalyState> ManagedAnomalies = new List<ManagedAnomalyState>();


        // ===== NEW: Unlimited tasks =====
        public List<NodeTask> Tasks = new();

        // ===== Node-scoped state =====
        public int LocalPanic = 0;
        public int Population = 10;
        public List<EventInstance> PendingEvents = new();
        public bool HasPendingEvent => PendingEvents != null && PendingEvents.Count > 0;

        // ===== Legacy fields (temporary) =====
        // Kept so existing UI/code can compile during migration. Do not use for new features.
        public List<string> AssignedAgentIds = new List<string>(); // legacy squad
        public float InvestigateProgress = 0f; // legacy 0..1
        public float ContainProgress = 0f;     // legacy 0..1
    }

    [Serializable]
    public class DecisionOption
    {
        public string Id;
        public string Text;

        // "Perception" / "Operation" / "Resistance" / "Power"
        public string CheckAttr;
        public int Threshold;
        public float BaseSuccess = 0.5f;

        public int MoneyOnSuccess = 0;
        public int MoneyOnFail = 0;
        public int PanicOnSuccess = 0;
        public int PanicOnFail = 0;

        public float ProgressDeltaOnSuccess = 0.02f;
        public float ProgressDeltaOnFail = -0.06f;
    }

    [Serializable]
    public class PendingEvent
    {
        public string Id;
        public string NodeId;
        public EventKind Kind;

        public string Title;
        public string Desc;

        public List<DecisionOption> Options = new();
    }

    [Serializable]
    public class GameState
    {
        public int Day = 1;
        public int Money = 0;
        public float WorldPanic = 0f;

        // 预留字段：Intel（暂不结算）
        public int Intel = 0;

        // 新货币：负熵（由“管理异常”系统每日产出）
        public int NegEntropy = 0;

        // 已收藏/已收容异常的长期管理状态（Legacy/Deprecated）
        // Node-scoped source of truth is NodeState.ManagedAnomalies.
        public List<ManagedAnomalyState> ManagedAnomalies = new List<ManagedAnomalyState>();

        public List<NodeState> Nodes = new();
        public List<AgentState> Agents = new();

        public List<PendingEvent> PendingEvents = new();
        public List<string> News = new();
    }
}
// </EXPORT_BLOCK>
