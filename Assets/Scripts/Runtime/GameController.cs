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
using System.Collections;
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
    public event Action OnInitialized;
    public bool IsGameOver { get; private set; }
    public bool IsInitialized => _initialized;

    private System.Random _rng;
    private bool _initialized;

    [Header("Debug Seed (same seed => same run)")]
    [SerializeField] private int seed = 12345;


    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        _rng = new System.Random(seed);
    }

    // Remote URLs: Primary = main branch, Fallback = copilot branch
    private static readonly string[] RemoteGameDataUrls = new[]
    {
        "https://raw.githubusercontent.com/hszqf/SCP/main/GameData/Published/game_data.json",
        "https://raw.githubusercontent.com/hszqf/SCP/copilot/GameData/Published/game_data.json"
    };

    private IEnumerator Start()
    {
        if (_initialized) yield break;

        Debug.Log($"[Boot] Platform={Application.platform} StreamingAssetsPath={Application.streamingAssetsPath}");

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string json = null;
            bool loadSuccess = false;

            // Try each remote URL in order
            for (int i = 0; i < RemoteGameDataUrls.Length; i++)
            {
                var baseUrl = RemoteGameDataUrls[i];
                var url = $"{baseUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                Debug.Log($"[Boot] Try remote URL #{i + 1}: {url}");
                
                Exception error = null;

                yield return DataRegistry.LoadJsonTextCoroutine(
                    url,
                    text => json = text,
                    ex => error = ex
                );

                if (error != null)
                {
                    Debug.LogError($"[Boot] Remote URL failed #{i + 1}: {error.Message}");
                    Debug.LogException(error);
                    continue; // Try next URL
                }

                int jsonLen = json?.Length ?? 0;
                string jsonHead = json?.Length > 80 ? json.Substring(0, 80) : (json ?? "");
                Debug.Log($"[Boot] Remote URL OK #{i + 1}: length={jsonLen} head={jsonHead}");
                loadSuccess = true;
                break; // Success, stop trying other URLs
            }

            if (!loadSuccess)
            {
                Debug.LogError("[Boot] FAILED to load JSON from all remote URLs");
                yield break;
            }

            try
            {
                DataRegistry.InitFromJson(json);
                Debug.Log("[Boot] InitFromJson succeeded");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Boot] InitFromJson FAILED: {ex.Message}");
                Debug.LogException(ex);
                yield break;
            }

            Debug.Log("[Boot] Calling InitGame");
            InitGame();
            Debug.Log("[Boot] InitGame completed");
            yield break;
        }

        try
        {
            DataRegistry.Instance.Reload();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Boot] Reload failed: {ex.Message}");
            Debug.LogException(ex);
            yield break;
        }

        Debug.Log("[Boot] Non-WebGL path");
        Debug.Log("[Boot] Calling InitGame");
        InitGame();
        Debug.Log("[Boot] InitGame completed");
    }

    private void InitGame()
    {
        if (_initialized) return;
        _initialized = true;

        var registry = DataRegistry.Instance;

        // ---- 鍒濆鍏ㄥ眬鐘舵€侊紙浠?Balance 琛ㄨ鍙栵級----
        int startMoney = registry.GetBalanceIntWithWarn("StartMoney", 0);
        float startWorldPanic = registry.GetBalanceFloatWithWarn("StartWorldPanic", 0f);
        int clampMoneyMin = registry.GetBalanceIntWithWarn("ClampMoneyMin", 0);
        float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);
        Debug.Log($"[Balance] StartMoney={startMoney} StartWorldPanic={startWorldPanic} ClampMoneyMin={clampMoneyMin} ClampWorldPanicMin={clampWorldPanicMin}");
        State.Money = Math.Max(clampMoneyMin, startMoney);
        State.WorldPanic = Math.Max(clampWorldPanicMin, startWorldPanic);

        // ---- 初始干员（先写死，后面再换 Data/ScriptableObject）----
        State.Agents.Add(new AgentState { Id = "A1", Name = "Alice", Perception = 6, Operation = 5, Resistance = 5, Power = 4, AvatarSeed = "A1".GetHashCode() });
        State.Agents.Add(new AgentState { Id = "A2", Name = "Bob", Perception = 4, Operation = 7, Resistance = 5, Power = 6, AvatarSeed = "A2".GetHashCode() });
        State.Agents.Add(new AgentState { Id = "A3", Name = "Chen", Perception = 5, Operation = 5, Resistance = 7, Power = 4, AvatarSeed = "A3".GetHashCode() });

        // ---- 初始节点（从场景 City 组件读取）----
        var cities = FindObjectsByType<City>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c != null)
            .ToList();

        EnsureCityIds(cities);

        cities = cities
            .Where(c => c != null && !string.IsNullOrEmpty(c.CityId))
            .OrderBy(c => c.CityId)
            .ToList();

        if (cities.Count == 0)
        {
            Debug.LogWarning("[Boot] No City components found in scene; no nodes initialized.");
        }

        foreach (var city in cities)
        {
            var nodeState = new CityState
            {
                Id = city.CityId,
                Name = city.CityName,
                Unlocked = city.Unlocked,
                Type = city.CityType,
                LocalPanic = 0,
                Population = Math.Max(0, city.Population),
                ActiveAnomalyIds = new List<string>(),
            };

            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                nodeState.X = rt.anchoredPosition.x;
                nodeState.Y = rt.anchoredPosition.y;
            }

            nodeState.HasAnomaly = nodeState.ActiveAnomalyIds.Count > 0;

            State.Cities.Add(nodeState);
        }

        // ---- 初始异常生成（AnomaliesGen day=1）----
        Sim.GenerateScheduledAnomalies(State, _rng, registry, State.Day);

        Notify();
        OnInitialized?.Invoke();
        Debug.Log("[Boot] GameController initialized and OnInitialized event fired");
    }

    private void OnDestroy()
    {
        if (I == this)
        {
            I = null;
        }
    }

    public void Notify() => OnStateChanged?.Invoke();

    public CityState GetNode(string nodeId)
        => State.Cities.FirstOrDefault(n => n.Id == nodeId);

    public AgentState GetAgent(string agentId)
        => State.Agents.FirstOrDefault(a => a.Id == agentId);

    public void EndDay()
    {
        if (IsGameOver)
        {
            Debug.LogWarning("[GameOver] EndDay ignored because the game is already over.");
            return;
        }

        // Guard: prevent ending day while movement/dispatch in progress
        if (!CanEndDay(out var canReason))
        {
            Debug.LogWarning($"[Day] Blocked: {canReason}");
            return;
        }
        
        // Run end-of-day pipeline (stages contain the original EndDay logic, moved verbatim)
        var stages = new List<IDaySettlementStage>
        {
            new Stage_EndDay_Core(),
            new Stage_EndDay_RefreshNotify(),
        };

        var pipeline = new DaySettlementPipeline(stages);
        var result = pipeline.Run(this);

        // Emit any pipeline logs to Unity console for visibility (no behavior change)
        if (result != null && result.Logs != null)
        {
            foreach (var l in result.Logs)
                Debug.Log(l);
        }
    }
    
    // --- EndDay stages (private, minimal; contain original EndDay logic) ---
    private sealed class Stage_EndDay_Core : IDaySettlementStage
    {
        public string Name => "EndDay.Core";
        public void Execute(GameController gc, GameState state, DayEndResult result)
        {
            // Original core settlement logic
            Sim.StepDay(state, gc._rng);
            // T6.6: After main settlement is applied (progress updated), recall agents for completed phases.
            // Must run before Notify() so UI sees recall tokens / Travelling state in the same frame.
            Core.PhaseCompletionRecallSystem.Apply(gc);
            result?.Log("Stage_EndDay_Core executed");
        }
    }

    private sealed class Stage_EndDay_RefreshNotify : IDaySettlementStage
    {
        public string Name => "EndDay.RefreshNotify";
        public void Execute(GameController gc, GameState state, DayEndResult result)
        {
            gc.Notify();
            gc.RefreshMapNodes();
            result?.Log("Stage_EndDay_RefreshNotify executed");
        }
    }
    
    // Check whether user can proceed to end the day. Returns false with reason if blocked.
    public bool CanEndDay(out string reason)
    {
        reason = null;
        if (State == null)
        {
            reason = "State null";
            return false;
        }

        if (State.MovementLockCount > 0)
        {
            reason = "MovementLockCount>0";
            return false;
        }

        if (State.PendingMovementCount > 0)
        {
            reason = "PendingMovementCount>0";
            return false;
        }

        if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsInteractionLocked)
        {
            reason = "DispatchAnimationSystem locked";
            return false;
        }

        return true;
    }

    public void MarkGameOver(string reason)
    {
        if (IsGameOver) return;
        IsGameOver = true;
        Debug.Log($"[GameOver] {reason}");
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
            AvatarSeed = _rng.Next(),
        };

        State.Agents.Add(agent);
        Debug.Log($"[Hire] cost={cost} moneyAfter={moneyAfter} agentId={agentId}");
        Notify();
        RefreshMapNodes();
        return true;
    }

    public RecruitCandidate GenerateRecruitCandidate()
    {
        if (State == null || _rng == null) return null;

        int level = RollRecruitLevel(_rng);
        int extraPoints = Math.Max(0, (level - 1) * 3 + _rng.Next(0, 3));

        int[] stats = { 5, 5, 5, 5 };
        for (int i = 0; i < extraPoints; i++)
        {
            int idx = _rng.Next(0, 4);
            stats[idx] += 1;
        }

        var agent = new AgentState
        {
            Name = string.Empty,
            Perception = stats[0],
            Operation = stats[1],
            Resistance = stats[2],
            Power = stats[3],
            Level = level,
            AvatarSeed = _rng.Next(),
        };

        int propSum = agent.Perception + agent.Operation + agent.Resistance + agent.Power;
        int baseCost = 100;
        int levelCost = (level - 1) * 120;
        int statBonus = Math.Max(0, propSum - 20) * 10;
        int cost = baseCost + levelCost + statBonus;

        return new RecruitCandidate { cid = Guid.NewGuid().ToString("N"), agent = agent, cost = cost };
    }

    public bool TryHireAgent(RecruitCandidate candidate, out AgentState agent)
    {
        agent = null;
        if (candidate == null || candidate.agent == null) return false;
        if (State == null) return false;

        int cost = Math.Max(0, candidate.cost);
        if (State.Money < cost) return false;

        int moneyBefore = State.Money;
        int clampMoneyMin = DataRegistry.Instance.GetBalanceIntWithWarn("ClampMoneyMin", 0);
        int moneyAfter = Math.Max(clampMoneyMin, State.Money - cost);
        State.Money = moneyAfter;

        string agentId = GenerateNextAgentId();
        agent = candidate.agent;
        agent.Id = agentId;
        agent.Name = $"Agent {agentId}";
        int offerLevel = candidate.agent.Level;
        agent.Level = Math.Max(1, offerLevel);
        agent.Exp = 0;

        State.Agents.Add(agent);
        Debug.Log($"[Recruit] day={State.Day} agent={agentId} lv={agent.Level} cost={cost} money={moneyBefore}->{moneyAfter}");
        Notify();
        RefreshMapNodes();
        return true;
    }

    private static int RollRecruitLevel(System.Random rng)
    {
        double roll = rng.NextDouble();
        if (roll < 0.70) return 1;
        if (roll < 0.90) return 2;
        if (roll < 0.98) return 3;
        return 4;
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
        WireTaskDefOnCreate(t);
        n.Tasks.Add(t);
        LogTaskDefSummary(TaskType.Investigate);
        GameControllerTaskExt.LogBusySnapshot(this, $"CreateInvestigateTask(node:{nodeId})");
        return t;
    }

    public NodeTask CreateContainTask(string nodeId, string containableId)
    {
        var n = GetNode(nodeId);
        if (n == null) return null;
        if (n.KnownAnomalyDefIds == null || n.KnownAnomalyDefIds.Count == 0) return null;

        HashSet<string> contained = null;
        if (n.ManagedAnomalies != null && n.ManagedAnomalies.Count > 0)
        {
            contained = new HashSet<string>(n.ManagedAnomalies
                .Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                .Select(m => m.AnomalyId));
        }

        // Validate target; fallback to first
        string target = containableId;
        if (string.IsNullOrEmpty(target))
            target = n.KnownAnomalyDefIds.FirstOrDefault(id => !string.IsNullOrEmpty(id) && (contained == null || !contained.Contains(id)));
        else if (!n.KnownAnomalyDefIds.Contains(target) || (contained != null && contained.Contains(target)))
            return null;
        if (string.IsNullOrEmpty(target)) return null;

        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        var t = new NodeTask
        {
            Id = "T_" + Guid.NewGuid().ToString("N")[..10],
            Type = TaskType.Contain,
            State = TaskState.Active,
            CreatedDay = State.Day,
            Progress = 0f,
            SourceAnomalyId = target,
        };
        WireTaskDefOnCreate(t);
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

        var managed = n.ManagedAnomalies.FirstOrDefault(m => m != null && m.Id == target);
        if (managed == null)
            return null;

        string defId = managed.AnomalyId;
        if (string.IsNullOrEmpty(defId))
        {
            Debug.LogWarning($"[ManageTask] managedAnomalyId={target} missing AnomalyId; ManageDaily impacts will be skipped.");
        }

        if (n.Tasks == null) n.Tasks = new List<NodeTask>();

        // If already has an active manage task for this anomaly, reuse (idempotent)
        var existing = n.Tasks.LastOrDefault(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Manage && t.TargetManagedAnomalyId == target);
        if (existing != null)
        {
            if (string.IsNullOrEmpty(existing.SourceAnomalyId) && !string.IsNullOrEmpty(defId))
            {
                existing.SourceAnomalyId = defId;
                Debug.Log($"[ManageTask] taskId={existing.Id} restored SourceAnomalyId={defId} from managed anomaly {target}.");
            }
            return existing;
        }

        var t = new NodeTask
        {
            Id = "T_" + Guid.NewGuid().ToString("N")[..10],
            Type = TaskType.Manage,
            State = TaskState.Active,
            CreatedDay = State.Day,
            Progress = 0f,
            TargetManagedAnomalyId = target,
            SourceAnomalyId = string.IsNullOrEmpty(defId) ? null : defId,
        };
        WireTaskDefOnCreate(t);
        n.Tasks.Add(t);
        LogTaskDefSummary(TaskType.Manage);
        GameControllerTaskExt.LogBusySnapshot(this, $"CreateManageTask(node:{nodeId}, anomaly:{target})");
        return t;
    }

    public bool TryGetTask(string taskId, out CityState node, out NodeTask task)
    {
        node = null;
        task = null;
        if (string.IsNullOrEmpty(taskId) || State?.Cities == null) return false;

        foreach (var n in State.Cities)
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

        if (!GameControllerTaskExt.AreAgentsUsable(this, agentIds, out var _))
            return;

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
        var (minSlots, maxSlots) = registry.GetTaskAgentSlotRangeWithWarn(type, 1, int.MaxValue);
        Debug.Log($"[TaskDef] taskType={type} slotsMin={minSlots} slotsMax={maxSlots}");
    }

    private void WireTaskDefOnCreate(NodeTask task)
    {
        if (task == null) return;
        TaskDef def = null;
        var registry = DataRegistry.Instance;
        if (registry != null && registry.TryGetTaskDefForType(task.Type, out def))
        {
            task.TaskDefId = def.taskDefId;
        }
        LogTaskCreate(task, def);
    }

    private static void LogTaskCreate(NodeTask task, TaskDef def)
    {
        if (task == null) return;
        string taskDefId = def?.taskDefId ?? task.TaskDefId ?? "";
        string taskDefName = def?.name ?? "";
        Debug.Log($"[TaskCreate] taskId={task.Id} type={task.Type} taskDefId={taskDefId} name={taskDefName}");
    }

    private void RefreshMapNodes()
    {
        AnomalySpawner.I?.RefreshMapNodes();
        UIPanelRoot.I?.RefreshNodePanel();
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
        if (n.KnownAnomalyDefIds == null || n.KnownAnomalyDefIds.Count == 0) return;

        HashSet<string> contained = null;
        if (n.ManagedAnomalies != null && n.ManagedAnomalies.Count > 0)
        {
            contained = new HashSet<string>(n.ManagedAnomalies
                .Where(m => m != null && !string.IsNullOrEmpty(m.AnomalyId))
                .Select(m => m.AnomalyId));
        }

        string target = n.KnownAnomalyDefIds.FirstOrDefault(id => !string.IsNullOrEmpty(id) && (contained == null || !contained.Contains(id)));
        if (string.IsNullOrEmpty(target)) return;

        var t = CreateContainTask(nodeId, target);
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

    private static void EnsureCityIds(List<City> cities)
    {
        if (cities == null || cities.Count == 0) return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int counter = 1;

        foreach (var city in cities.OrderBy(c => c?.CityId))
        {
            if (city == null) continue;

            var id = city.CityId;
            if (string.IsNullOrEmpty(id) || used.Contains(id))
            {
                id = GenerateNextCityId(used, ref counter);
                city.SetCityId(id, true);
                Debug.Log($"[Boot] CityId assigned: {id}");
            }

            used.Add(id);
        }
    }

    private static string GenerateNextCityId(HashSet<string> usedIds, ref int counter)
    {
        while (true)
        {
            string id = $"N{counter}";
            if (!usedIds.Contains(id))
            {
                counter++;
                return id;
            }
            counter++;
        }
    }
}

/// <summary>
/// Dispatch rules (extensions) - v3:
/// - 预定占用：派遣=占用（progress==0 也占用）。busy 判定遍历所有节点所有 Active 任务。
/// - progress==0：不允许在选人面板改派；必须先取消该任务。
/// - progress>0：不允许更换；必须撤退该任务。
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
        if (gc == null || gc.State?.Cities == null) return false;
        if (agentIds == null || agentIds.Count == 0) return false;

        var busy = DeriveBusyAgentIdsFromTasks(gc);
        bool anyBusy = agentIds.Any(id => !string.IsNullOrEmpty(id) && busy.Contains(id));
        if (anyBusy)

            LogBusySnapshot(gc, $"AreAgentsBusy(check:{string.Join(",", agentIds)})");
        return anyBusy;
    }

    public static bool AreAgentsUsable(GameController gc, List<string> agentIds, out string reason)
    {
        reason = string.Empty;
        if (gc?.State?.Agents == null || agentIds == null || agentIds.Count == 0) return true;

        foreach (var id in agentIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var agent = gc.State.Agents.FirstOrDefault(a => a != null && a.Id == id);
            if (agent == null) continue;
            if (agent.IsDead)
            {
                reason = "干员已死亡";
                return false;
            }
            if (agent.IsInsane)
            {
                reason = "干员已疯狂";
                return false;
            }
        }

        return true;
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
            int c = (n.KnownAnomalyDefIds != null) ? n.KnownAnomalyDefIds.Count : 0;
            if (c <= 0)
                return AssignResult.Fail("未发现可收容目标：请先派遣调查完成后再进行收容");
        }

        if (!AreAgentsUsable(gc, agentIds, out var usableReason))
            return AssignResult.Fail(usableReason);

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
                // default to first known anomaly
                string target = (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Count > 0) ? n.KnownAnomalyDefIds[0] : null;
                current = gc.CreateContainTask(nodeId, target);
            }
        }

        if (current == null) return AssignResult.Fail("创建任务失败");

        // Validate containment target still exists
        if (type == TaskType.Contain)
        {
            if (n.KnownAnomalyDefIds == null || n.KnownAnomalyDefIds.Count == 0)
                return AssignResult.Fail("可收容目标已为空");

            if (string.IsNullOrEmpty(current.SourceAnomalyId) || !n.KnownAnomalyDefIds.Contains(current.SourceAnomalyId))
                current.SourceAnomalyId = n.KnownAnomalyDefIds[0];
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

    public static HashSet<String> DeriveBusyAgentIdsFromTasks(GameController gc)
    {
        var result = new HashSet<String>();
        if (gc?.State?.Cities == null) return result;

        foreach (var node in gc.State.Cities)
        {
            if (node?.Tasks == null) continue;
            foreach (var t in node.Tasks)
            {
                if (t == null || t.State != TaskState.Active) continue;
                if (t.AssignedAgentIds == null) continue;
                foreach (var id in t.AssignedAgentIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    var agent = gc.State?.Agents?.FirstOrDefault(a => a != null && a.Id == id);
                    if (agent != null && (agent.IsDead || agent.IsInsane)) continue;
                    result.Add(id);
                }
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
