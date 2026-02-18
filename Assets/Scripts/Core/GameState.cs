// Canvas-maintained file: Core/GameState (v3 - N tasks)
// Source target: Assets/Scripts/Core/GameState.cs
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    public enum NodeStatus { Calm, Secured }

    public enum TaskType { Investigate, Contain, Manage }

    public enum AgentLocationKind
    {
        Base,
        TravellingToAnomaly,
        AtAnomaly,
        TravellingToBase
    }

    [Serializable]
    public class AgentState
    {
        public string Id;
        public string Name;

        public int AvatarSeed = -1;

        public int Perception = 5;
        public int Operation = 5;
        public int Resistance = 5;
        public int Power = 5;
        public int HP = 20;
        public int MaxHP = 20;
        public int SAN = 20;
        public int MaxSAN = 20;
        public int Level = 1;
        public int Exp = 0;

        public bool IsDead = false;
        public bool IsInsane = false;

        // new-arch: agent location (single source of truth for “arrived / travelling / base”)
        public AgentLocationKind LocationKind = AgentLocationKind.Base;

        // When LocationKind is AtAnomaly / TravellingToAnomaly / TravellingToBase, this stores the anomaly key.
        // IMPORTANT: for now use AnomalyState.Id as the key (legacy stable id).
        public string LocationAnomalyInstanceId = null;

        // Which roster slot this agent is assigned to while at an anomaly (for UI tag/debug).
        // Default Operate; meaningful only when LocationKind != Base.
        public AssignmentSlot LocationSlot = AssignmentSlot.Operate;

        // Convenience (not serialized)
        public bool IsTravelling =>
            LocationKind == AgentLocationKind.TravellingToAnomaly || LocationKind == AgentLocationKind.TravellingToBase;
    }

    [Serializable]
    public class RecruitCandidate
    {
        public string cid;
        public AgentState agent;
        public int cost;
        public bool isHired;
        public string hiredAgentId;
        public string hiredName;
    }

    [Serializable]
    public class RecruitPoolState
    {
        public int day = -1;
        public int refreshUsedToday = 0;
        public List<RecruitCandidate> candidates = new();
    }


    // 收容后进入“已收藏异常”，可被分配干员进行长期管理，按天产出负熵。
    [Serializable]
    public class ManagedAnomalyState
    {
        public string AnomalyInstanceId;
        public string Name;
        public int Level = 1;
        public string AnomalyDefId;
        public string AnomalyClass;

        // 左侧“已收藏异常”列表使用（后续可做收藏/取消收藏筛选）
        public bool Favorited = true;

        // 第一次开始管理的日期（用于统计/成长）
        public int StartDay;

        // 累计产出
        public int TotalNegEntropy;
    }

    [Serializable]
    public class CityState
    {
        public string Id;
        public string Name;

        public bool Unlocked = true;
        public int Type = 1;
        // ===== BEGIN M2 MapPos (CityState fields) =====

        // M2: unified settlement coordinate (MapRoot-local; same space as map nodes)
        // Simulation/Settlement MUST only use MapPos for distance.
        public Vector2 MapPos;

        // Legacy (DO NOT use for new logic; kept for migration/compat/debug)
        public float[] Location;
        public Vector2 Position;
        public float X;
        public float Y;

        // ===== END M2 MapPos (CityState fields) =====

        // Legacy: node-scoped status during migration.
        public NodeStatus Status = NodeStatus.Calm;

        public List<string> KnownAnomalyDefIds = new();
        public List<ManagedAnomalyState> ManagedAnomalies = new();

        public int LocalPanic = 0;
        public int Population = 10;
    }


    public enum AnomalyPhase
    {
        Investigate,
        Contain,
        Operate
    }


    [Serializable]
    public class AnomalyState
    {
        public string Id; // 实例唯一ID
        public string AnomalyDefId;
        public string NodeId;
        public bool IsKnown;
        public bool IsContained;
        public bool IsManaged;
        public ManagedAnomalyState ManagedState;
        // ===== BEGIN M2 MapPos (AnomalyState fields) =====

        // M2: unified map coordinate (MapRoot-local). Written once when the view is placed/spawned.
        // Future settlement distance (if needed) should use MapPos.
        public Vector2 MapPos;

        // Legacy coords (DO NOT use for new logic; kept during migration)
        public float X;
        public float Y;

        // ===== END M2 MapPos (AnomalyState fields) =====

        public int SpawnDay;

        // existing fields (kept)
        public float InvestigateProgress;
        public float ContainProgress;

        // new-arch: identity & spatial
        //public string AnomalyId;        // 实例唯一ID（将来替代/区分与配置ID）
        public Vector2 Position;        // 世界坐标/生成位置（用于影响范围计算）

        // new-arch: lifecycle & reveal
        public AnomalyPhase Phase;      // 生命周期阶段
        public int RevealLevel;         // 0=小方块名；1=已发现名字（desc0）；>=2逐步解锁desc1..n

        // new-arch: rosters (single source of truth in future)
        public List<string> InvestigatorIds = new List<string>();
        public List<string> ContainmentIds = new List<string>();
        public List<string> OperateIds = new List<string>();

        // S1: Spawn sequence key for deterministic ordering of anomaly actions
        public int SpawnSeq;

        public List<string> GetRoster(AssignmentSlot slot)
        {
            switch (slot)
            {
                case AssignmentSlot.Investigate: return InvestigatorIds;
                case AssignmentSlot.Contain:     return ContainmentIds;
                case AssignmentSlot.Operate:     return OperateIds;
                default:                         return OperateIds;
            }
        }
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

        public List<CityState> Cities = new();
        public List<AnomalyState> Anomalies = new();

        public List<AgentState> Agents = new();

        public RecruitPoolState RecruitPool = new RecruitPoolState();

        // Movement tokens for dispatch/recall animation & sequencing
        public List<Core.MovementToken> MovementTokens = new List<Core.MovementToken>();

        // UI/过天锁：当 >0 时，不允许 EndDay/NextDay
        public int MovementLockCount = 0;

        [NonSerialized] public GameStateIndex Index = new GameStateIndex();

        public void EnsureIndex()
        {
            if (Index == null) Index = new GameStateIndex();
            Index.EnsureUpToDate(this);
        }

        // Convenience: number of pending movement tokens (not serialized)
        public int PendingMovementCount
        {
            get
            {
                if (MovementTokens == null) return 0;
                int c = 0;
                for (int i = 0; i < MovementTokens.Count; i++)
                {
                    var t = MovementTokens[i];
                    if (t != null && t.State != Core.MovementTokenState.Completed) c++;
                }
                return c;
            }
        }

        // S1: deterministic spawn sequence counter for anomalies
        public int NextAnomalySpawnSeq = 0;

        // New flag: if true, Settlement.AnomalyBehaviorSystem performs actual city population deduction
        // Default true = enable new settlement-driven population deduction. When false, Sim performs legacy deductions.
        public bool UseSettlement_AnomalyCityPop = true;

        // New flag: when true, anomaly generation is deferred until after the Settlement pipeline runs.
        // Default true preserves the corrected pipeline behavior where newly spawned anomalies do not participate in the same day's settlement.
        public bool UseSettlement_Pipeline = true;

        // New flag: when true, Settlement.AnomalyWorkSystem performs anomaly work (Investigate/Contain/Operate)
        // Default true = enable settlement-driven anomaly work progression. When false, legacy Sim per-task progression runs.
        public bool UseSettlement_AnomalyWork = true;
    }
}
// </EXPORT_BLOCK>
