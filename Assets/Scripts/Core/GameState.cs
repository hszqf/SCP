using System;
using System.Collections.Generic;

namespace Core
{
    public enum NodeStatus { Calm, Investigating, Containing, Secured }
    public enum EventKind { Investigate, Contain }

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
    public class NodeState
    {
        public string Id;
        public string Name;

        // 0..1 百分比坐标：左下(0,0) 右上(1,1)
        public float X;
        public float Y;

        public NodeStatus Status = NodeStatus.Calm;

        public bool HasAnomaly = false;
        public int AnomalyLevel = 0;

        public List<string> AssignedAgentIds = new List<string>(); // Squad System
        public float InvestigateProgress = 0f; // 0..1
        public float ContainProgress = 0f;     // 0..1
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
        public int Money = 1000;
        public int Panic = 0;

        public List<NodeState> Nodes = new();
        public List<AgentState> Agents = new();

        public List<PendingEvent> PendingEvents = new();
        public List<string> News = new();
    }
}
