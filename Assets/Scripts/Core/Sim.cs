// Canvas-maintained file: Core/Sim (v4 - data driven events)
// Source: Assets/Scripts/Core/Sim.cs
// Goal: Load all game data from DataRegistry and drive events/effects via config.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using UnityEngine;
using Random = System.Random;

namespace Core
{
    public static class Sim
    {
        private const string RequirementAny = "ANY";
        private const string RandomDailySource = "RandomDaily";

        public static event Action OnIgnorePenaltyApplied;

        private static readonly HashSet<TaskType> WarnedMissingYieldFields = new();
        private static readonly HashSet<TaskType> WarnedUnknownYieldKey = new();
        private static readonly HashSet<string> WarnedDifficultyAnomalies = new();

        public static void StepDay(GameState s, Random rng)
        {
            var registry = DataRegistry.Instance;

            s.Day += 1;
            s.News.Add($"Day {s.Day}: 日结算开始");

            // 0) 异常生成（节点维度）
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                if (n.Status == NodeStatus.Calm && !n.HasAnomaly)
                {
                    if (rng.NextDouble() < 0.15)
                    {
                        var anomalyId = PickRandomAnomalyId(registry, rng);
                        if (!string.IsNullOrEmpty(anomalyId))
                        {
                            EnsureActiveAnomaly(n, anomalyId, registry);
                            s.News.Add($"- {n.Name} 出现异常迹象：{anomalyId}");
                        }
                    }
                }
            }

            // 1) 推进任务（任务维度：同节点可并行 N 个任务）
            foreach (var n in s.Nodes)
            {
                if (n?.Tasks == null || n.Tasks.Count == 0) continue;

                // 推进所有 Active 任务（按事件阻塞策略判断）
                for (int i = 0; i < n.Tasks.Count; i++)
                {
                    var t = n.Tasks[i];
                    if (t == null) continue;
                    if (t.State != TaskState.Active) continue;
                    if (t.AssignedAgentIds == null || t.AssignedAgentIds.Count == 0) continue;

                    if (IsTaskBlockedByEvents(s, n, t, registry))
                        continue;

                    var squad = GetAssignedAgents(s, t.AssignedAgentIds);
                    if (squad.Count == 0) continue;

                    float baseDelta = CalcDailyProgressDelta(t, squad, rng, registry);
                    string anomalyId = GetTaskAnomalyId(n, t);
                    int diff = 1;
                    if (t.Type == TaskType.Investigate || t.Type == TaskType.Contain)
                    {
                        diff = GetTaskDifficulty(anomalyId, t.Type, registry);
                    }

                    float effDelta = baseDelta / Math.Max(1, diff);
                    int manageRisk = (t.Type == TaskType.Manage) ? GetManageRisk(anomalyId, registry) : 0;

                    // Manage tasks are LONG-RUNNING: progress is only used as a "started" flag (0 vs >0).
                    // They should never auto-complete.
                    if (t.Type == TaskType.Manage)
                    {
                        float beforeManage = t.Progress;
                        t.Progress = Math.Max(t.Progress, 0f);
                        t.Progress = Clamp01(t.Progress + effDelta);
                        if (t.Progress >= 1f) t.Progress = 0.99f;
                        if (Math.Abs(t.Progress - beforeManage) > 0.0001f)
                        {
                            Debug.Log($"[TaskProgress] day={s.Day} taskId={t.Id} type={t.Type} anomalyId={anomalyId} diff={diff} baseDelta={baseDelta:0.00} effDelta={effDelta:0.00} risk={manageRisk} progress={t.Progress:0.00}/1 (baseDays=1)");
                        }

                        var defId = t.SourceAnomalyId;
                        if (string.IsNullOrEmpty(defId))
                        {
                            Debug.LogWarning($"[ManageDailySkip] day={s.Day} taskId={t.Id} node={n.Id} reason=MissingSourceAnomalyId target={t.TargetManagedAnomalyId ?? "none"}");
                            continue;
                        }

                        if (!registry.AnomaliesById.TryGetValue(defId, out var manageDef) || manageDef == null)
                        {
                            Debug.LogWarning($"[ManageDailySkip] day={s.Day} taskId={t.Id} node={n.Id} reason=UnknownAnomalyDef target={t.TargetManagedAnomalyId ?? "none"} anomaly={defId}");
                            continue;
                        }
                        var impact = ComputeImpact(s, TaskType.Manage, manageDef, t.AssignedAgentIds);
                        var req = NormalizeIntArray4(manageDef?.manReq);
                        float magSan = (manageDef?.sanDmg ?? 0) * impact.sanMul * impact.S * impact.sanRand;
                        Debug.Log(
                            $"[ImpactCalc] day={s.Day} type=Manage node={n.Id} anomaly={defId ?? "unknown"} base=({manageDef?.hpDmg ?? 0},{manageDef?.sanDmg ?? 0}) " +
                            $"mul=({impact.hpMul:0.###},{impact.sanMul:0.###}) rand=({impact.hpRand:0.###},{impact.sanRand:0.###}) " +
                            $"req={FormatIntArray(req)} team={FormatIntArray(impact.team)} D={impact.D:0.###} S={impact.S:0.###} magSan={magSan:0.###} final=({impact.hpDelta},{impact.sanDelta})");
                        foreach (var agentId in t.AssignedAgentIds)
                        {
                            string reason = $"ManageDaily:node={n.Id},anomaly={defId ?? "unknown"},dayTick={s.Day}";
                            ApplyAgentImpact(s, agentId, 0, impact.sanDelta, reason);
                        }
                        continue;
                    }

                    int baseDays = Math.Max(1, registry.GetTaskBaseDaysWithWarn(t.Type, 1));
                    float before = t.Progress;
                    t.Progress = Mathf.Clamp(t.Progress + effDelta, 0f, baseDays);
                    if (Math.Abs(t.Progress - before) > 0.0001f)
                    {
                        Debug.Log($"[TaskProgress] day={s.Day} taskId={t.Id} type={t.Type} anomalyId={anomalyId} diff={diff} baseDelta={baseDelta:0.00} effDelta={effDelta:0.00} progress={t.Progress:0.00}/{baseDays} (baseDays={baseDays})");
                    }

                    if (t.Progress >= baseDays)
                    {
                        CompleteTask(s, n, t, rng, registry);
                    }
                }
            }

            // 1.5) 收容后管理（负熵产出）
            StepManageTasks(s, rng, registry);

            // 1.75) 任务日结算产出（TaskDefs.yieldKey / yieldPerDay）
            StepTaskDailyYield(s, registry);

            // 2) 不处理的后果（按 IgnoreApplyMode 执行）
            ApplyIgnorePenaltyOnDayEnd(s, registry);

            // 2.5) 事件自动关闭（按 autoResolveAfterDays）
            AutoResolvePendingEventsOnDayEnd(s, registry);

            // 3) RandomDaily 事件生成
            GenerateRandomDailyEvents(s, rng, registry);

            // 3.5) RandomDaily 新闻生成
            GenerateRandomDailyNews(s, rng, registry);

            // 4) 经济 & 世界恐慌（全局）
            float popToMoneyRate = registry.GetBalanceFloatWithWarn("PopToMoneyRate", 0f);
            int wagePerAgentPerDay = registry.GetBalanceIntWithWarn("WagePerAgentPerDay", 0);
            int maintenanceDefault = registry.GetBalanceIntWithWarn("ContainedAnomalyMaintenanceDefault", 0);
            int clampMoneyMin = registry.GetBalanceIntWithWarn("ClampMoneyMin", 0);
            float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);

            int moneyBefore = s.Money;
            int income = 0;
            int maintenance = 0;
            int safeNodeCount = 0;
            float worldPanicAdd = 0f;

            foreach (var node in s.Nodes)
            {
                if (node == null) continue;

                income += Mathf.FloorToInt(node.Population * popToMoneyRate);

                // Safe node definition (current): no uncontained anomalies on this node.
                bool hasUncontained = node.Status != NodeStatus.Secured && node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0;
                if (!hasUncontained) safeNodeCount++;

                if (hasUncontained)
                {
                    foreach (var anomalyId in node.ActiveAnomalyIds)
                    {
                        if (string.IsNullOrEmpty(anomalyId))
                        {
                            Debug.LogWarning("[WARN] Missing anomalyId for world panic calculation. Using fallback=0.");
                            continue;
                        }
                        worldPanicAdd += registry.GetAnomalyFloatWithWarn(anomalyId, "worldPanicPerDayUncontained", 0f);
                    }
                }

                if (node.ManagedAnomalies == null) continue;
                foreach (var managed in node.ManagedAnomalies)
                {
                    if (managed == null) continue;
                    if (string.IsNullOrEmpty(managed.AnomalyId))
                    {
                        Debug.LogWarning($"[WARN] Managed anomaly {managed.Id} missing anomalyId. Using default maintenance={maintenanceDefault}.");
                        maintenance += maintenanceDefault;
                        continue;
                    }
                    maintenance += registry.GetAnomalyIntWithWarn(managed.AnomalyId, "maintenanceCostPerDay", maintenanceDefault);
                }
            }

            int wage = (s.Agents?.Count ?? 0) * wagePerAgentPerDay;
            int optionCost = 0; // TODO: hook event option costs if/when EffectOps expose them.
            int moneyAfter = moneyBefore + income - wage - maintenance - optionCost;
            if (moneyAfter < clampMoneyMin) moneyAfter = clampMoneyMin;
            s.Money = moneyAfter;

            Debug.Log($"[Economy] day={s.Day} income={income} wage={wage} maint={maintenance} option={optionCost} moneyBefore={moneyBefore} moneyAfter={moneyAfter}");

            float dailyDecay = registry.GetBalanceFloatWithWarn("DailyWorldPanicDecay", 0f);
            float decayPerSafeNode = registry.GetBalanceFloatWithWarn("WorldPanicDecayPerSafeNodePerDay", 0f);
            float worldPanicDecay = dailyDecay + safeNodeCount * decayPerSafeNode;
            float worldPanicBefore = s.WorldPanic;
            float worldPanicAfter = worldPanicBefore + worldPanicAdd - worldPanicDecay;
            if (worldPanicAfter < clampWorldPanicMin) worldPanicAfter = clampWorldPanicMin;
            s.WorldPanic = worldPanicAfter;

            float failThreshold = registry.GetBalanceFloatWithWarn("WorldPanicFailThreshold", 0f);
            Debug.Log($"[WorldPanic] day={s.Day} add={worldPanicAdd:0.##} decay={worldPanicDecay:0.##} safe={safeNodeCount} before={worldPanicBefore:0.##} after={worldPanicAfter:0.##} threshold={failThreshold:0.##}");

            if (s.WorldPanic >= failThreshold && GameController.I != null)
            {
                GameController.I.MarkGameOver($"reason=WorldPanic day={s.Day} value={s.WorldPanic:0.##} threshold={failThreshold:0.##}");
            }

            s.News.Add($"Day {s.Day} 结束");
        }

        public static (bool success, string text) ResolveEvent(GameState s, string nodeId, string eventInstanceId, string optionId, Random rng)
        {
            var registry = DataRegistry.Instance;
            var node = s.Nodes.FirstOrDefault(n => n != null && n.Id == nodeId);
            if (node == null) return (false, "节点不存在");
            if (node.PendingEvents == null || node.PendingEvents.Count == 0) return (false, "节点无事件");

            var ev = node.PendingEvents.FirstOrDefault(e => e != null && e.EventInstanceId == eventInstanceId);
            if (ev == null) return (false, "事件不存在");
            if (!registry.TryGetEvent(ev.EventDefId, out var eventDef)) return (false, "事件配置不存在");
            if (!registry.TryGetOption(ev.EventDefId, optionId, out var optionDef)) return (false, "选项不存在");

            var originTask = TryGetOriginTask(s, ev.SourceTaskId, out var originNode) ? originNode?.Tasks?.FirstOrDefault(t => t != null && t.Id == ev.SourceTaskId) : null;

            var affects = ResolveAffects(eventDef, optionDef, registry);
            var ctx = new EffectContext
            {
                State = s,
                Node = node,
                OriginTask = originTask,
                EventDefId = ev.EventDefId,
                OptionId = optionId,
            };
            int effectsApplied = EffectOpExecutor.ApplyEffect(optionDef.effectId, ctx, affects);

            node.PendingEvents.Remove(ev);

            Debug.Log($"[EventResolve] node={node.Id} inst={ev.EventInstanceId} def={ev.EventDefId} option={optionDef.optionId} effectsApplied={effectsApplied}");

            // ===== HP/SAN Impact for Events (hardcoded examples) =====
            // Apply impacts to agents assigned to the origin task, if any
            if (originTask != null && originTask.AssignedAgentIds != null && originTask.AssignedAgentIds.Count > 0)
            {
                int hpDelta = 0;
                int sanDelta = 0;

                // Hardcoded examples for specific events
                if (ev.EventDefId == "EV_001") // Example event 1
                {
                    hpDelta = -(1 + rng.Next(3)); // -1 to -3
                    sanDelta = -(2 + rng.Next(3)); // -2 to -4
                }
                else if (ev.EventDefId == "EV_002") // Example event 2
                {
                    hpDelta = -(2 + rng.Next(4)); // -2 to -5
                    sanDelta = -(1 + rng.Next(2)); // -1 to -2
                }
                else if (ev.EventDefId == "EV_003") // Example event 3
                {
                    hpDelta = 0;
                    sanDelta = -(3 + rng.Next(3)); // -3 to -5
                }

                // Apply to all agents on the task if any delta was set
                if (hpDelta != 0 || sanDelta != 0)
                {
                    foreach (var agentId in originTask.AssignedAgentIds)
                    {
                        string reason = $"EventResolve:event={ev.EventDefId},option={optionId},node={node.Id}";
                        ApplyAgentImpact(s, agentId, hpDelta, sanDelta, reason);
                    }
                }
            }

            s.News.Add($"- {node.Name} 事件处理：{eventDef.title} -> {optionDef.text}");

            var resultText = string.IsNullOrEmpty(optionDef.resultText) ? BuildEffectSummary(optionDef.effectId, affects) : optionDef.resultText;
            if (string.IsNullOrEmpty(resultText)) resultText = optionDef.text;
            if (string.IsNullOrEmpty(resultText)) resultText = "事件已处理";
            return (true, resultText);
        }

        private static void GenerateRandomDailyEvents(GameState s, Random rng, DataRegistry registry)
        {
            if (s == null || s.Nodes == null) return;

            var randomDailyDefs = registry.EventsById.Values
                .Where(def => def != null &&
                              !string.IsNullOrEmpty(def.eventDefId) &&
                              string.Equals(def.source, RandomDailySource, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int taskCtxChecked = 0;
            int taskFired = 0;
            int nodeCtxChecked = 0;
            int nodeFired = 0;

            var firedCounts = s.EventFiredCounts ??= new Dictionary<string, int>();
            var lastFiredDay = s.EventLastFiredDay ??= new Dictionary<string, int>();

            foreach (var node in s.Nodes)
            {
                if (node?.Tasks == null || node.Tasks.Count == 0) continue;

                foreach (var task in node.Tasks)
                {
                    if (task == null || task.State != TaskState.Active) continue;
                    if (task.Type != TaskType.Investigate && task.Type != TaskType.Contain && task.Type != TaskType.Manage) continue;

                    taskCtxChecked += 1;
                    int pendingBefore = node.PendingEvents?.Count ?? 0;
                    string ctxTaskTypeKey = GetTaskTypeKey(task.Type);
                    string ctxAnomalyId = GetTaskAnomalyId(node, task);

                    if (pendingBefore > 0)
                    {
                        Debug.Log($"[EventGenCheck] day={s.Day} ctx=Task node={node.Id} taskId={task.Id} taskType={ctxTaskTypeKey} anomaly={ctxAnomalyId ?? "none"} roll=0 p=0 allow=false reason=pendingEvents pendingBefore={pendingBefore}");
                        continue;
                    }

                    var matched = GetRandomDailyMatches(randomDailyDefs, s.Day, node.Id, ctxTaskTypeKey, ctxAnomalyId, firedCounts, lastFiredDay, requireAnomalyAny: false);
                    Debug.Log($"[EventPool] day={s.Day} ctx=Task node={node.Id} taskId={task.Id} candidates={randomDailyDefs.Count} matched={matched.Count}");

                    if (matched.Count == 0)
                    {
                        Debug.Log($"[EventGenCheck] day={s.Day} ctx=Task node={node.Id} taskId={task.Id} taskType={ctxTaskTypeKey} anomaly={ctxAnomalyId ?? "none"} roll=0 p=0 allow=false reason=noMatch pendingBefore={pendingBefore}");
                        continue;
                    }

                    float pContext = Mathf.Clamp01(matched.Max(def => def.p));
                    double roll = rng.NextDouble();
                    bool allow = roll <= pContext;
                    string reason = allow ? "trigger" : "rollTooHigh";
                    Debug.Log($"[EventGenCheck] day={s.Day} ctx=Task node={node.Id} taskId={task.Id} taskType={ctxTaskTypeKey} anomaly={ctxAnomalyId ?? "none"} roll={roll:0.00} p={pContext:0.00} allow={allow} reason={reason} pendingBefore={pendingBefore}");

                    if (!allow) continue;
                    if (!TryPickWeighted(matched, rng, out var picked)) continue;

                    var instance = EventInstanceFactory.Create(picked.eventDefId, node.Id, s.Day, task.Id, ctxAnomalyId, RandomDailySource);
                    AddEventToNode(node, instance);
                    UpdateEventFireTracking(s.Day, picked.eventDefId, firedCounts, lastFiredDay);
                    taskFired += 1;

                    Debug.Log($"[EventGen] day={s.Day} ctx=Task node={node.Id} eventDefId={picked.eventDefId} inst={instance.EventInstanceId} cause={RandomDailySource} taskId={task.Id} anomalyId={ctxAnomalyId ?? "none"}");
                    s.News.Add($"- {node.Name} 发生事件：{picked.title}");
                }
            }

            foreach (var node in s.Nodes)
            {
                if (node == null) continue;
                nodeCtxChecked += 1;

                int pendingBefore = node.PendingEvents?.Count ?? 0;
                if (pendingBefore > 0)
                {
                    Debug.Log($"[EventGenCheck] day={s.Day} ctx=Node node={node.Id} taskId=none taskType=ANY anomaly=none roll=0 p=0 allow=false reason=pendingEvents pendingBefore={pendingBefore}");
                    continue;
                }

                var matched = GetRandomDailyMatches(randomDailyDefs, s.Day, node.Id, RequirementAny, null, firedCounts, lastFiredDay, requireAnomalyAny: true);
                Debug.Log($"[EventPool] day={s.Day} ctx=Node node={node.Id} taskId=none candidates={randomDailyDefs.Count} matched={matched.Count}");

                if (matched.Count == 0)
                {
                    Debug.Log($"[EventGenCheck] day={s.Day} ctx=Node node={node.Id} taskId=none taskType=ANY anomaly=none roll=0 p=0 allow=false reason=noMatch pendingBefore={pendingBefore}");
                    continue;
                }

                float pContext = Mathf.Clamp01(matched.Max(def => def.p));
                double roll = rng.NextDouble();
                bool allow = roll <= pContext;
                string reason = allow ? "trigger" : "rollTooHigh";
                Debug.Log($"[EventGenCheck] day={s.Day} ctx=Node node={node.Id} taskId=none taskType=ANY anomaly=none roll={roll:0.00} p={pContext:0.00} allow={allow} reason={reason} pendingBefore={pendingBefore}");

                if (!allow) continue;
                if (!TryPickWeighted(matched, rng, out var picked)) continue;

                var instance = EventInstanceFactory.Create(picked.eventDefId, node.Id, s.Day, null, null, RandomDailySource);
                AddEventToNode(node, instance);
                UpdateEventFireTracking(s.Day, picked.eventDefId, firedCounts, lastFiredDay);
                nodeFired += 1;

                Debug.Log($"[EventGen] day={s.Day} ctx=Node node={node.Id} eventDefId={picked.eventDefId} inst={instance.EventInstanceId} cause={RandomDailySource} taskId=none anomalyId=none");
                s.News.Add($"- {node.Name} 发生事件：{picked.title}");
            }

            Debug.Log($"[RandomDailySummary] day={s.Day} taskCtxChecked={taskCtxChecked} taskFired={taskFired} nodeCtxChecked={nodeCtxChecked} nodeFired={nodeFired}");
        }

        private static void GenerateRandomDailyNews(GameState s, Random rng, DataRegistry registry)
        {
            if (s == null || s.Nodes == null) return;

            var randomDailyDefs = registry.NewsDefsById.Values
                .Where(def => def != null &&
                              !string.IsNullOrEmpty(def.newsDefId) &&
                              string.Equals(def.source, RandomDailySource, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int nodeAnomCtxChecked = 0;
            int nodeAnomPicked = 0;
            int nodeAnomEmitted = 0;
            int nodeCtxChecked = 0;
            int nodePicked = 0;
            int nodeEmitted = 0;

            var firedCounts = s.NewsFiredCounts ??= new Dictionary<string, int>();
            var lastFiredDay = s.NewsLastFiredDay ??= new Dictionary<string, int>();

            foreach (var node in s.Nodes)
            {
                if (node?.ActiveAnomalyIds == null || node.ActiveAnomalyIds.Count == 0) continue;

                foreach (var anomalyId in node.ActiveAnomalyIds)
                {
                    if (string.IsNullOrEmpty(anomalyId)) continue;
                    nodeAnomCtxChecked += 1;

                    var matched = GetRandomDailyNewsMatches(randomDailyDefs, s.Day, node.Id, anomalyId, firedCounts, lastFiredDay, requireAnomalyAny: false);
                    if (matched.Count == 0) continue;

                    if (!TryPickWeightedNews(matched, rng, out var picked)) continue;
                    nodeAnomPicked += 1;

                    double roll = rng.NextDouble();
                    bool emit = roll <= picked.p;
                    string reason = emit ? "Picked" : "RolledAboveP";
                    Debug.Log($"[NewsPick] day={s.Day} ctx=NodeAnom nodeId={node.Id} anomalyId={anomalyId} newsDefId={picked.newsDefId} weight={picked.weight} p={picked.p:0.00} roll={roll:0.00} emit={(emit ? 1 : 0)} reason={reason}");
                    if (!emit) continue;

                    string sourceAnomalyId = IsRequirementAny(picked.requiresAnomalyId) ? null : anomalyId;
                    var instance = NewsInstanceFactory.Create(picked.newsDefId, node.Id, sourceAnomalyId, RandomDailySource);
                    AddNewsToLog(s, instance);
                    UpdateNewsFireTracking(s.Day, picked.newsDefId, firedCounts, lastFiredDay);
                    nodeAnomEmitted += 1;

                    Debug.Log($"[NewsGen] day={s.Day} ctx=NodeAnom nodeId={node.Id} anomalyId={anomalyId} newsDefId={picked.newsDefId} instId={instance.Id} cause={RandomDailySource}");
                }
            }

            foreach (var node in s.Nodes)
            {
                if (node == null) continue;
                nodeCtxChecked += 1;

                var matched = GetRandomDailyNewsMatches(randomDailyDefs, s.Day, node.Id, null, firedCounts, lastFiredDay, requireAnomalyAny: true);
                if (matched.Count == 0) continue;

                if (!TryPickWeightedNews(matched, rng, out var picked)) continue;
                nodePicked += 1;

                double roll = rng.NextDouble();
                bool emit = roll <= picked.p;
                string reason = emit ? "Picked" : "RolledAboveP";
                Debug.Log($"[NewsPick] day={s.Day} ctx=Node nodeId={node.Id} anomalyId=none newsDefId={picked.newsDefId} weight={picked.weight} p={picked.p:0.00} roll={roll:0.00} emit={(emit ? 1 : 0)} reason={reason}");
                if (!emit) continue;

                var instance = NewsInstanceFactory.Create(picked.newsDefId, node.Id, null, RandomDailySource);
                AddNewsToLog(s, instance);
                UpdateNewsFireTracking(s.Day, picked.newsDefId, firedCounts, lastFiredDay);
                nodeEmitted += 1;

                Debug.Log($"[NewsGen] day={s.Day} ctx=Node nodeId={node.Id} anomalyId=none newsDefId={picked.newsDefId} instId={instance.Id} cause={RandomDailySource}");
            }

            Debug.Log($"[RandomDailyNewsSummary] day={s.Day} nodeAnomCtxChecked={nodeAnomCtxChecked} nodeAnomPicked={nodeAnomPicked} nodeAnomEmitted={nodeAnomEmitted} nodeCtxChecked={nodeCtxChecked} nodePicked={nodePicked} nodeEmitted={nodeEmitted} newsTotal={(s.NewsLog?.Count ?? 0)}");
        }

        public static bool ApplyIgnorePenaltyOnDayEnd(GameState s, DataRegistry registry)
        {
            bool anyApplied = false;
            foreach (var node in s.Nodes)
            {
                if (node?.PendingEvents == null || node.PendingEvents.Count == 0) continue;

                var toRemove = new List<EventInstance>();
                foreach (var ev in node.PendingEvents)
                {
                    if (ev == null) continue;
                    if (!registry.TryGetEvent(ev.EventDefId, out var def)) continue;

                    var ignoreMode = registry.GetIgnoreApplyMode(def);
                    if (ignoreMode == IgnoreApplyMode.NeverAuto) continue;
                    if (string.IsNullOrEmpty(def.ignoreEffectId)) continue;

                    var originTask = TryGetOriginTask(s, ev.SourceTaskId, out var originNode)
                        ? originNode?.Tasks?.FirstOrDefault(t => t != null && t.Id == ev.SourceTaskId)
                        : null;

                    var affects = GetDefaultAffects(def);
                    var ctx = new EffectContext
                    {
                        State = s,
                        Node = node,
                        OriginTask = originTask,
                        EventDefId = ev.EventDefId,
                        OptionId = "__ignore__",
                    };

                    if (ignoreMode == IgnoreApplyMode.ApplyOnceThenRemove)
                    {
                        if (ev.IgnoreAppliedOnce) continue;
                        EffectOpExecutor.ApplyEffect(def.ignoreEffectId, ctx, affects);
                        ev.IgnoreAppliedOnce = true;
                        toRemove.Add(ev);
                        anyApplied = true;
                        Debug.Log($"[EventIgnore] day={s.Day} node={node.Id} eventInstanceId={ev.EventInstanceId} mode={ignoreMode} removed=true");
                    }
                    else if (ignoreMode == IgnoreApplyMode.ApplyDailyKeep)
                    {
                        EffectOpExecutor.ApplyEffect(def.ignoreEffectId, ctx, affects);
                        anyApplied = true;
                        Debug.Log($"[EventIgnore] day={s.Day} node={node.Id} eventInstanceId={ev.EventInstanceId} mode={ignoreMode} removed=false");
                    }
                }

                if (toRemove.Count > 0)
                {
                    foreach (var ev in toRemove) node.PendingEvents.Remove(ev);
                }
            }

            if (anyApplied) OnIgnorePenaltyApplied?.Invoke();
            return anyApplied;
        }

        private static void AutoResolvePendingEventsOnDayEnd(GameState s, DataRegistry registry)
        {
            int scanned = 0;
            int removed = 0;

            foreach (var node in s.Nodes)
            {
                if (node?.PendingEvents == null || node.PendingEvents.Count == 0) continue;

                var toRemove = new List<EventInstance>();
                foreach (var ev in node.PendingEvents)
                {
                    if (ev == null) continue;
                    scanned += 1;
                    ev.AgeDays += 1;

                    if (!registry.TryGetEvent(ev.EventDefId, out var def)) continue;
                    int limit = def.autoResolveAfterDays;
                    if (limit <= 0) continue;
                    if (ev.AgeDays < limit) continue;

                    toRemove.Add(ev);
                    removed += 1;
                    Debug.Log($"[EventAutoResolve] day={s.Day} nodeId={node.Id} eventDefId={ev.EventDefId} age={ev.AgeDays} limit={limit} reason=AutoResolveAfterDays");
                }

                if (toRemove.Count > 0)
                {
                    foreach (var ev in toRemove) node.PendingEvents.Remove(ev);
                }
            }

            Debug.Log($"[EventAutoResolve] day={s.Day} scanned={scanned} removed={removed}");
        }

        private static bool IsTaskBlockedByEvents(GameState state, NodeState node, NodeTask task, DataRegistry registry)
        {
            if (node?.PendingEvents == null || node.PendingEvents.Count == 0) return false;

            foreach (var ev in node.PendingEvents)
            {
                if (ev == null) continue;
                if (!registry.TryGetEvent(ev.EventDefId, out var def)) continue;
                if (!DataRegistry.TryParseBlockPolicy(def.blockPolicy, out var policy, out _)) continue;

                if (policy == BlockPolicy.BlockAllTasksOnNode) return true;
                if (policy == BlockPolicy.BlockOriginTask && !string.IsNullOrEmpty(ev.SourceTaskId) && ev.SourceTaskId == task.Id)
                    return true;
            }

            return false;
        }

        private static void AddEventToNode(NodeState node, EventInstance ev)
        {
            if (node.PendingEvents == null) node.PendingEvents = new List<EventInstance>();
            node.PendingEvents.Add(ev);
        }

        private static void AddNewsToLog(GameState s, NewsInstance news)
        {
            if (s.NewsLog == null) s.NewsLog = new List<NewsInstance>();
            s.NewsLog.Add(news);
        }

        private static List<AffectScope> ResolveAffects(EventDef eventDef, EventOptionDef optionDef, DataRegistry registry)
        {
            if (optionDef?.affects != null && optionDef.affects.Count > 0 &&
                DataRegistry.TryParseAffectScopes(optionDef.affects, out var optionScopes, out _))
            {
                return optionScopes;
            }

            return GetDefaultAffects(eventDef);
        }

        private static List<AffectScope> GetDefaultAffects(EventDef eventDef)
        {
            if (eventDef?.defaultAffects != null && eventDef.defaultAffects.Count > 0 &&
                DataRegistry.TryParseAffectScopes(eventDef.defaultAffects, out var scopes, out _))
            {
                return scopes;
            }

            return new List<AffectScope> { new(AffectScopeKind.Node) };
        }

        private static string BuildEffectSummary(string effectId, IReadOnlyCollection<AffectScope> affects)
        {
            if (string.IsNullOrEmpty(effectId)) return string.Empty;
            var registry = DataRegistry.Instance;
            if (!registry.EffectOpsByEffectId.TryGetValue(effectId, out var ops) || ops == null || ops.Count == 0)
                return string.Empty;

            HashSet<string> allowed = affects != null && affects.Count > 0
                ? new HashSet<string>(affects.Select(a => a.Raw))
                : null;

            var parts = new List<string>();
            foreach (var op in ops)
            {
                if (op == null) continue;
                if (allowed != null && !allowed.Contains(op.Scope.Raw)) continue;

                string label = op.StatKey switch
                {
                    var k when string.Equals(k, "LocalPanic", StringComparison.OrdinalIgnoreCase) => "本地恐慌",
                    var k when string.Equals(k, "Population", StringComparison.OrdinalIgnoreCase) => "人口",
                    var k when string.Equals(k, "WorldPanic", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "Panic", StringComparison.OrdinalIgnoreCase) => "全局恐慌",
                    var k when string.Equals(k, "Money", StringComparison.OrdinalIgnoreCase) => "资金",
                    var k when string.Equals(k, "NegEntropy", StringComparison.OrdinalIgnoreCase) => "负熵",
                    var k when string.Equals(k, "TaskProgressDelta", StringComparison.OrdinalIgnoreCase) => "任务进度",
                    _ => op.StatKey,
                };

                string scopeLabel = op.Scope.Kind switch
                {
                    AffectScopeKind.Node => string.Empty,
                    AffectScopeKind.Global => "(全局)",
                    AffectScopeKind.OriginTask => "(来源任务)",
                    AffectScopeKind.TaskType when op.Scope.TaskType.HasValue => $"(任务类型:{op.Scope.TaskType.Value})",
                    _ => string.Empty,
                };

                string valueLabel = op.Op == EffectOpType.Set
                    ? $"={op.Value:+0;-0;0}"
                    : $"{(op.Value >= 0 ? "+" : string.Empty)}{op.Value:0}";

                parts.Add($"{label}{scopeLabel} {valueLabel}");
            }

            return parts.Count == 0 ? string.Empty : string.Join("，", parts);
        }

        private static List<EventDef> GetRandomDailyMatches(
            IReadOnlyList<EventDef> candidates,
            int day,
            string nodeId,
            string taskTypeKey,
            string anomalyId,
            Dictionary<string, int> firedCounts,
            Dictionary<string, int> lastFiredDay,
            bool requireAnomalyAny)
        {
            var matched = new List<EventDef>();
            if (candidates == null || candidates.Count == 0) return matched;

            foreach (var ev in candidates)
            {
                if (ev == null || string.IsNullOrEmpty(ev.eventDefId)) continue;
                if (ev.weight <= 0) continue;
                if (!IsWithinDayWindow(ev, day)) continue;
                if (!RequirementMatches(ev.requiresNodeId, nodeId)) continue;
                if (!RequirementMatches(ev.requiresTaskType, taskTypeKey)) continue;

                if (requireAnomalyAny)
                {
                    if (!IsRequirementAny(ev.requiresAnomalyId)) continue;
                }
                else
                {
                    if (!IsRequirementAny(ev.requiresAnomalyId))
                    {
                        if (string.IsNullOrEmpty(anomalyId)) continue;
                        if (!string.Equals(ev.requiresAnomalyId, anomalyId, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                }

                if (ev.limitNum > 0 && firedCounts.TryGetValue(ev.eventDefId, out var fired) && fired >= ev.limitNum)
                    continue;

                if (ev.cd > 0 && lastFiredDay.TryGetValue(ev.eventDefId, out var lastDay) && day - lastDay < ev.cd)
                    continue;

                matched.Add(ev);
            }

            return matched;
        }

        private static List<NewsDef> GetRandomDailyNewsMatches(
            IReadOnlyList<NewsDef> candidates,
            int day,
            string nodeId,
            string anomalyId,
            Dictionary<string, int> firedCounts,
            Dictionary<string, int> lastFiredDay,
            bool requireAnomalyAny)
        {
            var matched = new List<NewsDef>();
            if (candidates == null || candidates.Count == 0) return matched;

            foreach (var news in candidates)
            {
                if (news == null || string.IsNullOrEmpty(news.newsDefId)) continue;
                if (news.weight <= 0) continue;
                if (!IsWithinDayWindow(news, day)) continue;
                if (!RequirementMatches(news.requiresNodeId, nodeId)) continue;

                if (requireAnomalyAny)
                {
                    if (!IsRequirementAny(news.requiresAnomalyId)) continue;
                }
                else
                {
                    if (!IsRequirementAny(news.requiresAnomalyId))
                    {
                        if (string.IsNullOrEmpty(anomalyId)) continue;
                        if (!string.Equals(news.requiresAnomalyId, anomalyId, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                }

                if (news.limitNum > 0 && firedCounts.TryGetValue(news.newsDefId, out var fired) && fired >= news.limitNum)
                    continue;

                if (news.cd > 0 && lastFiredDay.TryGetValue(news.newsDefId, out var lastDay) && day - lastDay < news.cd)
                    continue;

                matched.Add(news);
            }

            return matched;
        }

        private static bool TryPickWeighted(IReadOnlyList<EventDef> candidates, Random rng, out EventDef picked)
        {
            picked = null;
            if (candidates == null || candidates.Count == 0) return false;

            int totalWeight = 0;
            foreach (var ev in candidates)
            {
                if (ev == null || ev.weight <= 0) continue;
                totalWeight += ev.weight;
            }

            if (totalWeight <= 0) return false;

            int roll = rng.Next(totalWeight);
            foreach (var ev in candidates)
            {
                if (ev == null || ev.weight <= 0) continue;
                roll -= ev.weight;
                if (roll < 0)
                {
                    picked = ev;
                    return true;
                }
            }

            picked = candidates.FirstOrDefault(ev => ev != null && ev.weight > 0);
            return picked != null;
        }

        private static bool TryPickWeightedNews(IReadOnlyList<NewsDef> candidates, Random rng, out NewsDef picked)
        {
            picked = null;
            if (candidates == null || candidates.Count == 0) return false;

            int totalWeight = 0;
            foreach (var news in candidates)
            {
                if (news == null || news.weight <= 0) continue;
                totalWeight += news.weight;
            }

            if (totalWeight <= 0) return false;

            int roll = rng.Next(totalWeight);
            foreach (var news in candidates)
            {
                if (news == null || news.weight <= 0) continue;
                roll -= news.weight;
                if (roll < 0)
                {
                    picked = news;
                    return true;
                }
            }

            picked = candidates.FirstOrDefault(news => news != null && news.weight > 0);
            return picked != null;
        }

        private static void UpdateEventFireTracking(int day, string eventDefId, Dictionary<string, int> firedCounts, Dictionary<string, int> lastFiredDay)
        {
            if (string.IsNullOrEmpty(eventDefId)) return;
            if (firedCounts != null)
            {
                firedCounts.TryGetValue(eventDefId, out var fired);
                firedCounts[eventDefId] = fired + 1;
            }

            lastFiredDay?.TryGetValue(eventDefId, out _);
            if (lastFiredDay != null) lastFiredDay[eventDefId] = day;
        }

        private static void UpdateNewsFireTracking(int day, string newsDefId, Dictionary<string, int> firedCounts, Dictionary<string, int> lastFiredDay)
        {
            if (string.IsNullOrEmpty(newsDefId)) return;
            if (firedCounts != null)
            {
                firedCounts.TryGetValue(newsDefId, out var fired);
                firedCounts[newsDefId] = fired + 1;
            }

            lastFiredDay?.TryGetValue(newsDefId, out _);
            if (lastFiredDay != null) lastFiredDay[newsDefId] = day;
        }

        private static bool IsWithinDayWindow(EventDef def, int day)
        {
            if (def == null) return false;
            if (def.minDay > 0 && day < def.minDay) return false;
            if (def.maxDay > 0 && day > def.maxDay) return false;
            return true;
        }

        private static bool IsWithinDayWindow(NewsDef def, int day)
        {
            if (def == null) return false;
            if (def.minDay > 0 && day < def.minDay) return false;
            if (def.maxDay > 0 && day > def.maxDay) return false;
            return true;
        }

        private static bool RequirementMatches(string requirement, string value)
        {
            if (IsRequirementAny(requirement)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            return string.Equals(requirement, value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRequirementAny(string requirement)
            => string.IsNullOrWhiteSpace(requirement) || string.Equals(requirement, RequirementAny, StringComparison.OrdinalIgnoreCase);

        private static string GetTaskTypeKey(TaskType type)
        {
            return type switch
            {
                TaskType.Investigate => "TaskInvestigate",
                TaskType.Contain => "TaskContain",
                TaskType.Manage => "TaskManage",
                _ => RequirementAny,
            };
        }

        // =====================
        // Task completion rules
        // =====================

        static void CompleteTask(GameState s, NodeState node, NodeTask task, Random rng, DataRegistry registry)
        {
            int baseDays = Math.Max(1, registry.GetTaskBaseDaysWithWarn(task.Type, 1));
            task.Progress = baseDays;
            task.State = TaskState.Completed;
            task.CompletedDay = s.Day;

            // Store assigned agents before clearing for HP/SAN impact
            var assignedAgents = task.AssignedAgentIds != null ? new List<string>(task.AssignedAgentIds) : new List<string>();

            // Release squad
            if (task.AssignedAgentIds != null) task.AssignedAgentIds.Clear();

            if (task.Type == TaskType.Investigate)
            {
                // 只记录已知 anomalyDefId，不再产出 ContainableItem
                string anomalyId = GetOrCreateAnomalyForNode(node, registry, rng);
                var anomaly = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level = anomaly != null ? Math.Max(1, anomaly.baseThreat) : Math.Max(1, node.AnomalyLevel);

                // 调查完成不会自动收容
                node.Status = NodeStatus.Calm;
                node.HasAnomaly = true;

                s.News.Add($"- {node.Name} 调查完成：发现异常 {anomalyId}");

                if (!string.IsNullOrEmpty(task.TargetNewsId) && s.NewsLog != null)
                {
                    var news = s.NewsLog.FirstOrDefault(n => n != null && n.Id == task.TargetNewsId);
                    if (news != null)
                    {
                        news.IsResolved = true;
                        news.ResolvedDay = s.Day;
                        Debug.Log($"[NewsResolve] day={s.Day} newsId={news.Id} nodeId={news.NodeId} anomalyId={news.SourceAnomalyId} resolved=1");
                    }
                }

                // ===== Anomaly Discovery Logic =====
                const float discoverP = 0.35f;
                string discovered = null;

                // A) via News (guaranteed)
                if (!string.IsNullOrEmpty(task.TargetNewsId))
                {
                    var news = s.NewsLog?.FirstOrDefault(x => x != null && x.Id == task.TargetNewsId);
                    if (news != null && !string.IsNullOrEmpty(news.SourceAnomalyId))
                    {
                        discovered = news.SourceAnomalyId;
                        bool added = AddKnown(node, discovered);
                        Debug.Log($"[AnomalyDiscovered] day={s.Day} nodeId={node.Id} anomalyDefId={discovered} via=News newsId={news.Id} added={(added ? 1 : 0)}");
                    }
                }

                // B) random investigate
                if (string.IsNullOrEmpty(discovered))
                {
                    var pool = node.ActiveAnomalyIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                    if (pool != null && pool.Count > 0)
                    {
                        double roll = rng.NextDouble();
                        if (roll <= discoverP)
                        {
                            string pick = pool[rng.Next(pool.Count)];
                            bool added = AddKnown(node, pick);
                            Debug.Log($"[AnomalyDiscovered] day={s.Day} nodeId={node.Id} anomalyDefId={pick} via=RandomInvestigate roll={roll:0.00} p={discoverP:0.00} added={(added ? 1 : 0)}");
                        }
                        else
                        {
                            Debug.Log($"[AnomalyDiscoverCheck] day={s.Day} nodeId={node.Id} via=RandomInvestigate roll={roll:0.00} p={discoverP:0.00} discovered=0");
                        }
                    }
                }

                // ===== HP/SAN Impact for Investigate =====
                var defId = !string.IsNullOrEmpty(task.SourceAnomalyId) ? task.SourceAnomalyId : anomalyId;
                var def = !string.IsNullOrEmpty(defId) && registry.AnomaliesById.TryGetValue(defId, out var defModel) ? defModel : null;
                if (assignedAgents.Count > 0)
                {
                    var impact = ComputeImpact(s, TaskType.Investigate, def, assignedAgents);
                    var req = NormalizeIntArray4(def?.invReq);
                    Debug.Log(
                        $"[ImpactCalc] day={s.Day} type=Investigate node={node.Id} anomaly={defId ?? "unknown"} base=({def?.hpDmg ?? 0},{def?.sanDmg ?? 0}) " +
                        $"mul=({impact.hpMul:0.###},{impact.sanMul:0.###}) rand=({impact.hpRand:0.###},{impact.sanRand:0.###}) " +
                        $"req={FormatIntArray(req)} team={FormatIntArray(impact.team)} D={impact.D:0.###} S={impact.S:0.###} final=({impact.hpDelta},{impact.sanDelta})");
                    foreach (var agentId in assignedAgents)
                    {
                        string reason = $"InvestigateComplete:node={node.Id},anomaly={defId ?? "unknown"}";
                        ApplyAgentImpact(s, agentId, impact.hpDelta, impact.sanDelta, reason);
                    }
                }
            }
            else if (task.Type == TaskType.Contain)
            {
                // 只用 anomalyId 进行收容
                string anomalyId = !string.IsNullOrEmpty(task.SourceAnomalyId)
                    ? task.SourceAnomalyId
                    : GetOrCreateAnomalyForNode(node, registry, rng);
                var anomaly = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level = anomaly != null ? Math.Max(1, anomaly.baseThreat) : Math.Max(1, node.AnomalyLevel);
                int reward = 200 + 50 * level;

                s.Money += reward;
                int relief = registry.GetBalanceIntWithWarn("ContainReliefFixed", 0);
                float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);
                float beforePanic = s.WorldPanic;
                s.WorldPanic = Math.Max(clampWorldPanicMin, s.WorldPanic - relief);

                Debug.Log($"[WorldPanic] day={s.Day} source=ContainComplete relief={relief} before={beforePanic:0.##} after={s.WorldPanic:0.##}");

                s.News.Add($"- {node.Name} 收容成功（+$ {reward}, WorldPanic -{relief}）");
                // 收容成功：将该 anomalyId 加入“已收藏异常”（用于后续管理）。
                EnsureManagedAnomalyRecorded(node, anomalyId, anomaly);

                // 若无进行中的收容任务，则节点可视为“清空异常”。
                bool hasActiveContainTask = node.Tasks != null && node.Tasks.Any(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain);
                bool hasActiveInvestigateWithSquad = node.Tasks != null && node.Tasks.Any(t =>
                    t != null &&
                    t.State == TaskState.Active &&
                    t.Type == TaskType.Investigate &&
                    t.AssignedAgentIds != null &&
                    t.AssignedAgentIds.Count > 0);

                if (!hasActiveContainTask)
                {
                    node.HasAnomaly = false;
                    node.ActiveAnomalyIds?.Clear();
                    node.Status = hasActiveInvestigateWithSquad ? NodeStatus.Calm : NodeStatus.Secured;
                }

                // ===== HP/SAN Impact for Contain =====
                var defId = task.SourceAnomalyId;
                var def = !string.IsNullOrEmpty(defId) && registry.AnomaliesById.TryGetValue(defId, out var defModel) ? defModel : null;
                if (assignedAgents.Count > 0)
                {
                    var impact = ComputeImpact(s, TaskType.Contain, def, assignedAgents);
                    var req = NormalizeIntArray4(def?.conReq);
                    Debug.Log(
                        $"[ImpactCalc] day={s.Day} type=Contain node={node.Id} anomaly={defId ?? "unknown"} base=({def?.hpDmg ?? 0},{def?.sanDmg ?? 0}) " +
                        $"mul=({impact.hpMul:0.###},{impact.sanMul:0.###}) rand=({impact.hpRand:0.###},{impact.sanRand:0.###}) " +
                        $"req={FormatIntArray(req)} team={FormatIntArray(impact.team)} D={impact.D:0.###} S={impact.S:0.###} final=({impact.hpDelta},{impact.sanDelta})");
                    foreach (var agentId in assignedAgents)
                    {
                        string reason = $"ContainComplete:node={node.Id},anomaly={defId ?? "unknown"}";
                        ApplyAgentImpact(s, agentId, impact.hpDelta, impact.sanDelta, reason);
                    }
                }
            }
            node.Status = NodeStatus.Calm;
            node.HasAnomaly = true;
        }

        private static (int hpDelta, int sanDelta, float D, float S, int[] team, float hpMul, float sanMul, float hpRand, float sanRand) ComputeImpact(GameState state, TaskType type, AnomalyDef def, List<string> agentIds)
        {
            var team = new int[4];
            if (state?.Agents != null && agentIds != null && agentIds.Count > 0)
            {
                foreach (var agentId in agentIds)
                {
                    var agent = state.Agents.FirstOrDefault(a => a != null && a.Id == agentId);
                    if (agent == null) continue;
                    team[0] += agent.Perception;
                    team[1] += agent.Resistance;
                    team[2] += agent.Operation;
                    team[3] += agent.Power;
                }
            }

            var req = type switch
            {
                TaskType.Investigate => NormalizeIntArray4(def?.invReq),
                TaskType.Contain => NormalizeIntArray4(def?.conReq),
                TaskType.Manage => NormalizeIntArray4(def?.manReq),
                _ => new int[4]
            };

            var weights = type switch
            {
                TaskType.Contain => new[] { 0.15f, 0.40f, 0.05f, 0.40f },
                TaskType.Manage => new[] { 0.10f, 0.45f, 0.35f, 0.10f },
                _ => new[] { 0.45f, 0.35f, 0.10f, 0.10f },
            };

            float weighted = 0f;
            float weightSum = 0f;
            for (var i = 0; i < 4; i++)
            {
                if (req[i] <= 0) continue;
                float deficit = Math.Max(0f, (req[i] - team[i]) / (float)req[i]);
                var w = weights[i];
                weighted += deficit * w;
                weightSum += w;
            }

            float D = weightSum > 0f ? weighted / weightSum : 0f;
            float S = Mathf.Clamp(D, 0f, 1.5f);

            float hpMul = type switch
            {
                TaskType.Investigate => 0.2f,
                TaskType.Manage => 0f,
                _ => 1.0f,
            };
            float sanMul = type switch
            {
                TaskType.Contain => 0.7f,
                TaskType.Manage => 0.5f,
                _ => 1.0f,
            };
            float hpRand = 1f;
            float sanRand = 1f;
            int hpDelta = 0;
            int sanDelta = 0;
            if (def != null && S > 0f)
            {
                hpRand = UnityEngine.Random.Range(0.8f, 1.2f);
                sanRand = UnityEngine.Random.Range(0.8f, 1.2f);
                float hpMag = def.hpDmg * hpMul * S * hpRand;
                float sanMag = def.sanDmg * sanMul * S * sanRand;
                hpDelta = -Mathf.RoundToInt(hpMag);

                if (type == TaskType.Manage && sanMag > 0f)
                {
                    int sanLoss = Mathf.CeilToInt(sanMag);
                    sanDelta = -sanLoss;
                }
                else
                {
                    sanDelta = -Mathf.RoundToInt(sanMag);
                }
            }

            return (hpDelta, sanDelta, D, S, team, hpMul, sanMul, hpRand, sanRand);
        }

        private static int[] NormalizeIntArray4(int[] input)
        {
            var result = new int[4];
            if (input == null) return result;
            var count = Math.Min(input.Length, 4);
            Array.Copy(input, result, count);
            return result;
        }

        private static string FormatIntArray(int[] values)
        {
            if (values == null) return "null";
            return $"[{string.Join(",", values)}]";
        }

        // =====================
        // Helpers
        // =====================

        static List<AgentState> GetAssignedAgents(GameState s, List<string> assignedIds)
        {
            if (assignedIds == null || assignedIds.Count == 0) return new List<AgentState>();
            return s.Agents.Where(a => a != null && assignedIds.Contains(a.Id)).ToList();
        }

        static float CalcDailyProgressDelta(NodeTask t, List<AgentState> squad, Random rng, DataRegistry registry)
        {
            // Base daily progress.
            // - Investigate/Contain: normal completion loop.
            // - Manage: progress is only a "started" flag; keep delta small to avoid hitting 1.

            float baseDelta = (t.Type == TaskType.Manage) ? 0.02f : registry.GetTaskProgressPerDay(t.Type, 0.10f);

            int totalStat = 0;
            foreach (var a in squad)
            {
                if (t.Type == TaskType.Investigate) totalStat += a.Perception;
                else if (t.Type == TaskType.Contain) totalStat += a.Operation;
                else totalStat += (a.Resistance + a.Perception) / 2; // Manage
            }

            float statBonus = totalStat * ((t.Type == TaskType.Manage) ? 0.002f : 0.01f);
            float noise = (float)(rng.NextDouble() * 0.02 - 0.01);

            float min = (t.Type == TaskType.Manage) ? 0.005f : 0.05f;
            return Math.Max(min, baseDelta + statBonus + noise);
        }

        static string GetTaskAnomalyId(NodeState node, NodeTask task)
        {
            if (node == null || task == null) return null;

            if (task.Type == TaskType.Manage)
            {
                if (node.ManagedAnomalies == null) return null;
                var managed = node.ManagedAnomalies.FirstOrDefault(x => x != null && x.Id == task.TargetManagedAnomalyId);
                return managed?.AnomalyId;
            }

            if (task.Type == TaskType.Contain)
            {
                // 直接返回 task.SourceAnomalyId
                if (!string.IsNullOrEmpty(task.SourceAnomalyId))
                    return task.SourceAnomalyId;
            }

            return node.ActiveAnomalyIds?.FirstOrDefault(id => !string.IsNullOrEmpty(id));
        }

        static int GetTaskDifficulty(string anomalyId, TaskType type, DataRegistry registry)
        {
            int raw = 0;
            if (!string.IsNullOrEmpty(anomalyId) && registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly))
            {
                raw = type == TaskType.Investigate ? anomaly.investigateDifficulty : anomaly.containDifficulty;
            }

            if (raw > 0) return raw;

            var key = string.IsNullOrEmpty(anomalyId) ? "<unknown>" : anomalyId;
            if (WarnedDifficultyAnomalies.Add(key))
            {
                Debug.LogWarning($"[WARN] Anomaly difficulty missing or <=0: anomalyId={key}. Using fallback=1.");
            }

            return 1;
        }

        static int GetManageRisk(string anomalyId, DataRegistry registry)
        {
            if (string.IsNullOrEmpty(anomalyId)) return 0;
            return registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly) ? anomaly.manageRisk : 0;
        }

        /// <summary>
        /// Unified entry point for applying HP/SAN impacts to agents.
        /// Clamps values to [0, max] and logs the impact.
        /// </summary>
        public static void ApplyAgentImpact(GameState s, string agentId, int hpDelta, int sanDelta, string reason)
        {
            if (s == null || s.Agents == null) return;

            var agent = s.Agents.FirstOrDefault(a => a != null && a.Id == agentId);
            if (agent == null)
            {
                Debug.LogWarning($"[AgentImpact] day={s.Day} agent={agentId} NOTFOUND reason={reason}");
                return;
            }

            int hpBefore = agent.HP;
            int sanBefore = agent.SAN;

            agent.HP = Math.Max(0, Math.Min(agent.MaxHP, agent.HP + hpDelta));
            agent.SAN = Math.Max(0, Math.Min(agent.MaxSAN, agent.SAN + sanDelta));

            Debug.Log($"[AgentImpact] day={s.Day} agent={agent.Id} hp={hpDelta:+0;-#} ({hpBefore}->{agent.HP}) san={sanDelta:+0;-#} ({sanBefore}->{agent.SAN}) reason={reason}");
        }

        // =====================
        // Management (NegEntropy) - formalized as NodeTask.Manage
        // =====================

        static void StepManageTasks(GameState s, Random rng, DataRegistry registry)
        {
            if (s == null || s.Nodes == null || s.Nodes.Count == 0) return;

            int totalAllNodes = 0;

            foreach (var node in s.Nodes)
            {
                if (node == null) continue;
                if (node.Tasks == null || node.Tasks.Count == 0) continue;
                if (node.ManagedAnomalies == null || node.ManagedAnomalies.Count == 0) continue;

                int nodeTotal = 0;

                foreach (var t in node.Tasks)
                {
                    if (t == null) continue;
                    if (t.State != TaskState.Active) continue;
                    if (t.Type != TaskType.Manage) continue;
                    if (t.AssignedAgentIds == null || t.AssignedAgentIds.Count == 0) continue;
                    if (string.IsNullOrEmpty(t.TargetManagedAnomalyId)) continue;

                    var m = node.ManagedAnomalies.FirstOrDefault(x => x != null && x.Id == t.TargetManagedAnomalyId);
                    if (m == null) continue; // dangling task

                    // First day of management
                    if (m.StartDay <= 0) m.StartDay = s.Day;

                    var squad = GetAssignedAgents(s, t.AssignedAgentIds);
                    if (squad.Count == 0) continue;

                    int yield = CalcDailyNegEntropyYield(m, squad, rng);
                    if (yield <= 0) continue;

                    nodeTotal += yield;
                    m.TotalNegEntropy += yield;
                }

                if (nodeTotal > 0)
                {
                    totalAllNodes += nodeTotal;
                    s.News.Add($"- {node.Name} 管理产出：+{nodeTotal} 负熵");
                }

                if (node.Status == NodeStatus.Secured && nodeTotal > 0)
                {
                    // RandomDaily handles per-day event generation.
                }
            }

            if (totalAllNodes > 0)
                s.NegEntropy += totalAllNodes;
        }

        static void StepTaskDailyYield(GameState s, DataRegistry registry)
        {
            if (s == null || s.Nodes == null || s.Nodes.Count == 0) return;

            int moneyDeltaSum = 0;
            float worldPanicDeltaSum = 0f;
            int intelDeltaSum = 0;
            int yieldedTasks = 0;

            foreach (var node in s.Nodes)
            {
                if (node == null || node.Tasks == null || node.Tasks.Count == 0) continue;


                foreach (var task in node.Tasks)
                {
                    if (task == null) continue;
                    if (task.State != TaskState.Active) continue;

                    // 获取任务定义
                    if (!registry.TryGetTaskDef(task.Type, out var def) || def == null)
                        continue;
                    float yieldPerDay = def.yieldPerDay;

                    if (!def.hasYieldKey || !def.hasYieldPerDay)
                    {
                        if (WarnedMissingYieldFields.Add(task.Type))
                        {
                            Debug.LogWarning($"[TaskYield] Missing yieldKey/yieldPerDay for taskType={task.Type}. Skipping daily yield.");
                        }
                        continue;
                    }

                    switch (def.yieldKey)
                    {
                        case "Money":
                            {
                                int delta = Mathf.RoundToInt(yieldPerDay);
                                if (delta == 0) continue;
                                int before = s.Money;
                                s.Money = before + delta;
                                int after = s.Money;
                                moneyDeltaSum += delta;
                                yieldedTasks++;
                                Debug.Log($"[TaskYield] day={s.Day} taskId={task.Id} type={task.Type} key=Money delta={delta} before={before} after={after}");
                                break;
                            }
                        case "WorldPanic":
                            {
                                float delta = yieldPerDay;
                                float before = s.WorldPanic;
                                s.WorldPanic = before + delta;
                                float after = s.WorldPanic;
                                worldPanicDeltaSum += delta;
                                yieldedTasks++;
                                Debug.Log($"[TaskYield] day={s.Day} taskId={task.Id} type={task.Type} key=WorldPanic delta={delta:0.##} before={before:0.##} after={after:0.##}");
                                break;
                            }
                        case "Intel":
                            {
                                int delta = Mathf.RoundToInt(yieldPerDay);
                                if (delta == 0) continue;
                                int before = s.Intel;
                                s.Intel = before + delta;
                                int after = s.Intel;
                                intelDeltaSum += delta;
                                yieldedTasks++;
                                Debug.Log($"[TaskYield] day={s.Day} taskId={task.Id} type={task.Type} key=Intel delta={delta} before={before} after={after}");
                                break;
                            }
                        default:
                            if (WarnedUnknownYieldKey.Add(task.Type))
                            {
                                Debug.LogWarning($"[TaskYield] Unknown yieldKey={def.yieldKey} for taskType={task.Type}. Skipping daily yield.");
                            }
                            break;
                    }
                }

                if (yieldedTasks > 0)
                {
                    Debug.Log($"[TaskYieldSummary] day={s.Day} moneyDelta={moneyDeltaSum} worldPanicDelta={worldPanicDeltaSum:0.##} intelDelta={intelDeltaSum} tasks={yieldedTasks}");
                }
            }
        }

        static int CalcDailyNegEntropyYield(ManagedAnomalyState m, List<AgentState> managers, Random rng)
        {
            int level = Math.Max(1, m.Level);

            // 基础产出：与异常等级相关
            int baseYield = 2 + level;

            // 管理干员加成：偏向 Resistance + Perception
            int stat = 0;
            foreach (var a in managers)
                stat += a.Resistance + a.Perception;

            int bonus = stat / 10; // 每 10 点合计属性 +1

            // 轻微波动（避免过于机械）
            int noise = (rng.NextDouble() < 0.5) ? 0 : 1;

            return Math.Max(1, baseYield + bonus + noise);
        }

        static void EnsureManagedAnomalyRecorded(NodeState node, string anomalyId, AnomalyDef anomaly)
        {
            if (node.ManagedAnomalies == null) node.ManagedAnomalies = new List<ManagedAnomalyState>();
            if (string.IsNullOrEmpty(anomalyId)) return;

            var existing = node.ManagedAnomalies.FirstOrDefault(m => m != null && m.AnomalyId == anomalyId);
            if (existing != null)
            {
                existing.Level = Math.Max(existing.Level, anomaly?.baseThreat ?? 1);
                if (!string.IsNullOrEmpty(anomaly?.@class)) existing.AnomalyClass = anomaly.@class;
                return;
            }

            node.ManagedAnomalies.Add(new ManagedAnomalyState
            {
                Id = $"MANAGED_{anomalyId}_{Guid.NewGuid().ToString("N")[..6]}",
                Name = anomaly != null ? anomaly.name : $"已收容异常（{node.Name}）",
                Level = Math.Max(1, anomaly?.baseThreat ?? 1),
                AnomalyId = anomalyId,
                AnomalyClass = anomaly?.@class,
                Favorited = true,
                StartDay = 0,
                TotalNegEntropy = 0,
            });
        }

        private static bool TryGetOriginTask(GameState s, string taskId, out NodeState node)
        {
            node = null;
            if (string.IsNullOrEmpty(taskId) || s?.Nodes == null) return false;

            foreach (var n in s.Nodes)
            {
                if (n?.Tasks == null) continue;
                if (n.Tasks.Any(t => t != null && t.Id == taskId))
                {
                    node = n;
                    return true;
                }
            }

            return false;
        }

        private static void EnsureActiveAnomaly(NodeState node, string anomalyId, DataRegistry registry)
        {
            if (node.ActiveAnomalyIds == null) node.ActiveAnomalyIds = new List<string>();
            if (!node.ActiveAnomalyIds.Contains(anomalyId)) node.ActiveAnomalyIds.Add(anomalyId);
            node.HasAnomaly = node.ActiveAnomalyIds.Count > 0;

            if (registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly))
            {
                node.AnomalyLevel = Math.Max(node.AnomalyLevel, Math.Max(1, anomaly.baseThreat));
            }
        }

        private static string GetOrCreateAnomalyForNode(NodeState node, DataRegistry registry, Random rng)
        {
            if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0)
                return node.ActiveAnomalyIds[0];

            var anomalyId = PickRandomAnomalyId(registry, rng);
            if (!string.IsNullOrEmpty(anomalyId))
            {
                EnsureActiveAnomaly(node, anomalyId, registry);
                return anomalyId;
            }

            return null;
        }

        private static string PickRandomAnomalyId(DataRegistry registry, Random rng)
        {
            var all = registry.AnomaliesById.Keys.ToList();
            if (all.Count == 0) return null;
            int idx = rng.Next(all.Count);
            return all[idx];
        }

        // =====================
        // Anomaly discovery helper
        // =====================

        private static bool AddKnown(NodeState node, string anomalyDefId)
        {
            if (node == null || string.IsNullOrEmpty(anomalyDefId)) return false;
            node.KnownAnomalyDefIds ??= new List<string>();
            if (node.KnownAnomalyDefIds.Contains(anomalyDefId)) return false;
            node.KnownAnomalyDefIds.Add(anomalyDefId);
            return true;
        }

        // =====================
        // Agent Busy Text
        // =====================

        /// <summary>
        /// Builds a descriptive text for what an agent is currently doing.
        /// Returns empty string if the agent is idle (not assigned to any active task).
        /// </summary>
        public static string BuildAgentBusyText(GameState state, string agentId)
        {
            if (state?.Nodes == null || string.IsNullOrEmpty(agentId))
                return string.Empty;

            var registry = DataRegistry.Instance;

            // Traverse all nodes and tasks to find where this agent is assigned
            foreach (var node in state.Nodes)
            {
                if (node?.Tasks == null) continue;

                foreach (var task in node.Tasks)
                {
                    if (task == null || task.State != TaskState.Active) continue;
                    if (task.AssignedAgentIds == null || !task.AssignedAgentIds.Contains(agentId)) continue;

                    // Found the task this agent is working on
                    string busyText = string.Empty;

                    switch (task.Type)
                    {
                        case TaskType.Investigate:
                            if (!string.IsNullOrEmpty(task.TargetNewsId))
                            {
                                // Has a specific news target
                                var newsDef = registry?.GetNewsDefById(task.TargetNewsId);
                                string newsTitle = newsDef?.title ?? task.TargetNewsId;
                                busyText = $"在{node.Name}调查《{newsTitle}》";
                            }
                            else
                            {
                                // Generic investigation
                                busyText = $"在{node.Name}随意调查";
                            }
                            break;

                        case TaskType.Contain:
                            {
                                // Get anomaly name from SourceAnomalyId or TargetContainableId
                                string anomalyId = task.SourceAnomalyId ?? task.TargetContainableId;
                                string anomalyName = anomalyId;
                                if (!string.IsNullOrEmpty(anomalyId) && registry?.AnomaliesById != null)
                                {
                                    if (registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef))
                                    {
                                        anomalyName = anomalyDef.name;
                                    }
                                }
                                busyText = $"在{node.Name}收容 {anomalyName}";
                            }
                            break;

                        case TaskType.Manage:
                            {
                                // Get managed anomaly name
                                string anomalyId = task.TargetManagedAnomalyId;
                                string anomalyName = anomalyId;

                                // Try to find the managed anomaly to get its name
                                if (!string.IsNullOrEmpty(anomalyId) && node.ManagedAnomalies != null)
                                {
                                    var managed = node.ManagedAnomalies.FirstOrDefault(m => m.Id == anomalyId);
                                    if (managed != null)
                                    {
                                        anomalyName = managed.Name;
                                    }
                                    else if (registry?.AnomaliesById != null && registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef))
                                    {
                                        anomalyName = anomalyDef.name;
                                    }
                                }
                                busyText = $"在{node.Name}管理 {anomalyName}";
                            }
                            break;

                        default:
                            Debug.LogWarning($"[AgentBusy] Unhandled task type: {task.Type} for agent {agentId}");
                            busyText = $"在{node.Name}执行任务";
                            break;
                    }

                    // Optional debug log (only when agent is busy)
                    if (!string.IsNullOrEmpty(busyText))
                    {
                        Debug.Log($"[AgentBusy] agent={agentId} task={task.Id} text={busyText}");
                    }

                    return busyText;
                }
            }

            // Agent not found in any active task - idle
            return string.Empty;
        }

        // =====================
        // Math helpers
        // =====================

        static float Clamp01(float v)
        {
            return Mathf.Clamp01(v);
        }
    }
}
