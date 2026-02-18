// Canvas-maintained file: Runtime/GameController (v3 - N tasks backend, legacy UI compatibility)
// Source target: Assets/Scripts/Runtime/GameController.cs
//
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
            var cityState = new CityState
            {
                Id = city.CityId,
                Name = city.CityName,
                Unlocked = city.Unlocked,
                Type = city.CityType,
                LocalPanic = 0,
                Population = Math.Max(0, city.Population),
            };

            // ===== BEGIN M2 MapPos (InitGame city write) =====
            var rt = city.transform as RectTransform;
            if (rt != null)
            {
                // M2: MapPos is the ONLY settlement coordinate.
                // Use anchoredPosition in the map root space (NodeLayer-local).
                cityState.MapPos = rt.anchoredPosition;
            }
            else
            {
                // Fallback: should not happen for UI nodes. Keep deterministic default.
                cityState.MapPos = Vector2.zero;
                Debug.LogWarning($"[Boot] City has no RectTransform, MapPos defaulted to (0,0). cityId={city.CityId}");
            }
            // ===== END M2 MapPos (InitGame city write) =====


            State.Cities.Add(cityState);
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

    public void Notify()
    {
        if (State != null)
        {
            State.Index ??= new GameStateIndex();
            State.Index.Rebuild(State); // ✅ 强制刷新，杜绝 stale
        }
        OnStateChanged?.Invoke();
    }

    public CityState GetCity(string cityId)
    {
        if (State == null || string.IsNullOrEmpty(cityId)) return null;
        State.EnsureIndex();
        return State.Index.GetCity(cityId);
    }


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
        // after pipeline.Run(this)
        if (result != null && result.Logs != null)
        {
            for (int i = 0; i < result.Logs.Count; i++)
                Debug.Log(result.Logs[i]);
        }
    }
    
    // --- EndDay stages (private, minimal; contain original EndDay logic) ---
    private sealed class Stage_EndDay_Core : IDaySettlementStage
    {
        public string Name => "EndDay.Core";
        public void Execute(GameController gc, GameState state, DayEndResult result)
        {
            if (state == null) return;

            // Pipeline path: do NOT call Sim.StepDay
            // Use Sim.AdvanceDay_Only to centralize day increment and light initialization
            Sim.AdvanceDay_Only(state);

            // 1..5 严格顺序
            Settlement.AnomalyWorkSystem.Apply(gc, state, result);
            Settlement.AnomalyBehaviorSystem.Apply(gc, state, result);
            Settlement.CityEconomySystem.Apply(gc, state, result);
            Settlement.BaseRecoverySystem.Apply(gc, state, result);
            Settlement.SettlementCleanupSystem.Apply(gc, state, result);

            // 完成判定与自动召回（你现在放哪都行，但至少不要在 Sim.StepDay 之前）
            Core.PhaseCompletionRecallSystem.Apply(gc);

            // 仅 pipeline 下：结算完成后再生成当日计划异常（避免同日被处理）
            Core.Sim.GenerateScheduledAnomalies_Public(state, gc._rng);

            result?.Log("Stage_EndDay_Core pipeline executed");
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

        // Fallback: if any agent is still travelling, block EndDay
        if (State.Agents != null)
        {
            foreach (var agent in State.Agents)
            {
                if (agent == null) continue;
                if (agent.LocationKind == Core.AgentLocationKind.TravellingToAnomaly ||
                    agent.LocationKind == Core.AgentLocationKind.TravellingToBase ||
                    agent.IsTravelling)
                {
                    reason = "有人仍在路上";
                    return false;
                }
            }
        }

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



    private void RefreshMapNodes()
    {
        AnomalySpawner.I?.RefreshMapNodes();
        //UIPanelRoot.I?.RefreshNodePanel();
    }


    private static void EnsureCityIds(List<City> cities)
    {
        if (cities == null || cities.Count == 0) return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < cities.Count; i++)
        {
            var city = cities[i];
            if (city == null) continue;

            string oldId = city.CityId;
            string id = SanitizeId(oldId);

            bool needFix = string.IsNullOrEmpty(id) || used.Contains(id);

            if (needFix)
            {
                // 1) 优先用 CityName（如果它不是回退到 CityId 的那种）
                string candidate = SanitizeId(city.CityName);
                if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase))
                    candidate = SanitizeId(city.gameObject != null ? city.gameObject.name : null);

                if (string.IsNullOrEmpty(candidate))
                    candidate = "City";

                string final = candidate;
                int suffix = 1;
                while (used.Contains(final))
                    final = $"{candidate}_{suffix++}";

                // 尽量把旧 key 从 registry 里移除（如果之前注册过）
                MapEntityRegistry.I?.UnregisterCity(city);

                city.SetCityId(final, true);

                // 重新注册到 registry（OnEnable 不会自动重跑）
                MapEntityRegistry.I?.RegisterCity(city);

                Debug.Log($"[Boot] CityId fixed: '{oldId}' -> '{final}' (name='{city.CityName}', go='{city.gameObject?.name}')");

                id = final;
            }

            if (!string.IsNullOrEmpty(id))
                used.Add(id);
        }
    }

    private static string SanitizeId(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim();
        if (s.Length == 0) return null;

        // 避免空格/不可见字符造成“看似不同实际相同”的问题
        s = s.Replace(" ", "_");
        return s;
    }


}

/// <summary>

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
        if (gc != null && gc.State != null && gc.State.UseSettlement_Pipeline)
            return false;


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


    // ---------- Busy derivation & debug ----------

    public static HashSet<string> DeriveBusyAgentIdsFromTasks(GameController gc)
    {
        var result = new HashSet<string>();
        var agents = gc?.State?.Agents;
        if (agents == null) return result;

        foreach (var a in agents)
        {
            if (a == null || string.IsNullOrEmpty(a.Id)) continue;
            if (a.IsDead || a.IsInsane) continue;
            if (a.LocationKind != AgentLocationKind.Base)
                result.Add(a.Id);
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
