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
        private const int FIXED_EVENT_DAY = 3;
        private const string FIXED_EVENT_NODE_ID = "N1";

        public static event Action OnIgnorePenaltyApplied;

        public static void StepDay(GameState s, Random rng)
        {
            var registry = DataRegistry.Instance;

            s.Day += 1;
            s.News.Add($"Day {s.Day}: 日结算开始");

            // 0) 固定事件（最小 stub：固定日期投放到固定节点）
            if (s.Day == FIXED_EVENT_DAY)
            {
                var fixedNode = s.Nodes.FirstOrDefault(n => n != null && n.Id == FIXED_EVENT_NODE_ID);
                int pendingBefore = fixedNode?.PendingEvents?.Count ?? 0;
                bool allow = fixedNode != null && pendingBefore == 0;
                string checkReason = fixedNode == null ? "nodeMissing" : (allow ? "trigger" : "pendingEvents");
                Debug.Log($"[EventGenCheck] day={s.Day} node={FIXED_EVENT_NODE_ID} source=Fixed allow={allow} reason={checkReason}");

                if (allow)
                {
                    TryGenerateEvent(s, rng, EventSource.Fixed, FIXED_EVENT_NODE_ID, reason: "FixedDayTrigger");
                }
            }

            // 1) 异常生成（节点维度）
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

            // 2) 推进任务（任务维度：同节点可并行 N 个任务）
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

                    float delta = CalcDailyProgressDelta(t, squad, rng, registry);

                    // Manage tasks are LONG-RUNNING: progress is only used as a "started" flag (0 vs >0).
                    // They should never auto-complete.
                    if (t.Type == TaskType.Manage)
                    {
                        t.Progress = Math.Max(t.Progress, 0f);
                        t.Progress = Clamp01(t.Progress + delta);
                        if (t.Progress >= 1f) t.Progress = 0.99f;
                        continue;
                    }

                    t.Progress = Clamp01(t.Progress + delta);

                    if (t.Progress >= 1f)
                    {
                        CompleteTask(s, n, t, rng, registry);
                    }
                }
            }

            // 2.5) 收容后管理（负熵产出）
            StepManageTasks(s, rng, registry);

            // 3) 事件来源（过天触发）：本地恐慌高 / 随机
            foreach (var node in s.Nodes)
            {
                if (node == null) continue;

                int pendingBefore = node.PendingEvents?.Count ?? 0;

                if (node.LocalPanic >= registry.LocalPanicHighThreshold)
                {
                    bool allowLocalPanicHigh = pendingBefore == 0;
                    string localPanicReason = allowLocalPanicHigh ? "trigger" : "pendingEvents";
                    Debug.Log($"[EventGenCheck] day={s.Day} node={node.Id} source=LocalPanicHigh panic={node.LocalPanic} threshold={registry.LocalPanicHighThreshold} allow={allowLocalPanicHigh} reason={localPanicReason}");

                    if (allowLocalPanicHigh)
                    {
                        TryGenerateEvent(s, rng, EventSource.LocalPanicHigh, node.Id, reason: $"LocalPanicHigh>={registry.LocalPanicHighThreshold}");
                        pendingBefore = node.PendingEvents?.Count ?? pendingBefore;
                    }
                }

                double roll = rng.NextDouble();
                bool allowRandom = pendingBefore == 0 && roll < registry.RandomEventBaseProb;
                string randomReason = pendingBefore > 0 ? "pendingEvents" : (roll < registry.RandomEventBaseProb ? "trigger" : "rollTooHigh");
                Debug.Log($"[EventGenCheck] day={s.Day} node={node.Id} source=Random roll={roll:0.00} p={registry.RandomEventBaseProb:0.00} allow={allowRandom} reason={randomReason} pendingBefore={pendingBefore}");

                if (allowRandom)
                {
                    TryGenerateEvent(s, rng, EventSource.Random, node.Id, reason: $"RandomRoll<{registry.RandomEventBaseProb:0.00}");
                }
            }

            // 4) 不处理的后果（按 IgnoreApplyMode 执行）
            ApplyIgnorePenaltyOnDayEnd(s, registry);

            // 5) 恐慌 & 收入（全局）
            int activeAnomaly = s.Nodes.Count(n => n.HasAnomaly && n.Status != NodeStatus.Secured);
            s.Panic = ClampInt(s.Panic + activeAnomaly, 0, 100);
            s.Money += 50;

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

            s.News.Add($"- {node.Name} 事件处理：{eventDef.title} -> {optionDef.text}");

            var resultText = string.IsNullOrEmpty(optionDef.resultText) ? BuildEffectSummary(optionDef.effectId, affects) : optionDef.resultText;
            if (string.IsNullOrEmpty(resultText)) resultText = optionDef.text;
            if (string.IsNullOrEmpty(resultText)) resultText = "事件已处理";
            return (true, resultText);
        }

        public static bool TryGenerateEvent(GameState s, Random rng, EventSource source, string nodeId, string reason, NodeTask sourceTask = null, string sourceAnomalyId = null)
        {
            var registry = DataRegistry.Instance;
            var node = s.Nodes.FirstOrDefault(n => n != null && n.Id == nodeId);
            if (node == null) return false;

            var eventDef = PickEventDef(registry, s, node, source, sourceTask, rng);
            if (eventDef == null) return false;

            var instance = EventInstanceFactory.Create(eventDef.eventDefId, nodeId, s.Day, sourceTask?.Id, sourceAnomalyId);
            AddEventToNode(node, instance);

            Debug.Log($"[EventGen] day={s.Day} node={nodeId} def={eventDef.eventDefId} inst={instance.EventInstanceId} sourceTask={sourceTask?.Id ?? "none"} sourceAnomaly={sourceAnomalyId ?? "none"} reason={reason}");
            s.News.Add($"- {node.Name} 发生事件：{eventDef.title}");
            return true;
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

        private static EventDef PickEventDef(DataRegistry registry, GameState state, NodeState node, EventSource source, NodeTask sourceTask, Random rng)
        {
            var candidates = new List<(EventDef def, int weight)>();
            int sourceCount = 0;

            foreach (var ev in registry.Root.events ?? new List<EventDef>())
            {
                if (ev == null || string.IsNullOrEmpty(ev.eventDefId)) continue;
                if (!DataRegistry.TryParseEventSource(ev.source, out var evSource, out _)) continue;
                if (evSource != source) continue;
                sourceCount++;

                if (!IsEventEligible(registry, state, node, ev, sourceTask)) continue;
                candidates.Add((ev, Math.Max(1, ev.weight)));
            }

            Debug.Log($"[EventPool] day={state.Day} node={node.Id} source={source} candidates={sourceCount} matched={candidates.Count}");
            if (candidates.Count == 0) return null;

            int totalWeight = candidates.Sum(c => c.weight);
            int roll = rng.Next(totalWeight);
            foreach (var candidate in candidates)
            {
                roll -= candidate.weight;
                if (roll < 0) return candidate.def;
            }

            return candidates[0].def;
        }

        private static bool IsEventEligible(DataRegistry registry, GameState state, NodeState node, EventDef ev, NodeTask sourceTask)
        {
            if (!registry.TriggersByEventDefId.TryGetValue(ev.eventDefId, out var triggers) || triggers == null || triggers.Count == 0)
                return true;

            foreach (var trigger in triggers)
            {
                if (trigger == null) continue;
                if (DoesTriggerMatch(registry, state, node, trigger, sourceTask)) return true;
            }

            return false;
        }

        private static bool DoesTriggerMatch(DataRegistry registry, GameState state, NodeState node, EventTrigger trigger, NodeTask sourceTask)
        {
            if (node == null) return false;

            if (trigger.MinDay.HasValue && state.Day < trigger.MinDay.Value) return false;
            if (trigger.MaxDay.HasValue && state.Day > trigger.MaxDay.Value) return false;
            if (trigger.MinLocalPanic.HasValue && node.LocalPanic < trigger.MinLocalPanic.Value) return false;
            if (trigger.RequiresSecured.HasValue && (node.Status == NodeStatus.Secured) != trigger.RequiresSecured.Value) return false;

            if (trigger.TaskType.HasValue)
            {
                if (sourceTask == null || sourceTask.Type != trigger.TaskType.Value) return false;
            }

            if (trigger.OnlyAffectOriginTask == true && sourceTask == null) return false;

            if (!MatchesTagRequirement(node.Tags, trigger.RequiresNodeTagsAll, requireAll: true)) return false;
            if (!MatchesTagRequirement(node.Tags, trigger.RequiresNodeTagsAny, requireAll: false)) return false;

            if (trigger.RequiresAnomalyTagsAny != null && trigger.RequiresAnomalyTagsAny.Count > 0)
            {
                var anomalyTags = GetActiveAnomalyTags(node, registry);
                if (!MatchesTagRequirement(anomalyTags, trigger.RequiresAnomalyTagsAny, requireAll: false)) return false;
            }

            return true;
        }

        private static bool MatchesTagRequirement(IEnumerable<string> subjectTags, List<string> requiredTags, bool requireAll)
        {
            if (requiredTags == null || requiredTags.Count == 0) return true;
            var set = new HashSet<string>((subjectTags ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)));
            if (set.Count == 0) return false;

            if (requireAll)
            {
                return requiredTags.All(set.Contains);
            }

            return requiredTags.Any(set.Contains);
        }

        private static List<string> GetActiveAnomalyTags(NodeState node, DataRegistry registry)
        {
            var tags = new HashSet<string>();
            foreach (var anomalyId in node.ActiveAnomalyIds ?? new List<string>())
            {
                if (string.IsNullOrEmpty(anomalyId)) continue;
                if (!registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly)) continue;
                foreach (var tag in anomaly.tags ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }
            }

            return tags.ToList();
        }

        // =====================
        // Task completion rules
        // =====================

        static void CompleteTask(GameState s, NodeState node, NodeTask task, Random rng, DataRegistry registry)
        {
            task.Progress = 1f;
            task.State = TaskState.Completed;
            task.CompletedDay = s.Day;

            // Release squad
            if (task.AssignedAgentIds != null) task.AssignedAgentIds.Clear();

            if (task.Type == TaskType.Investigate)
            {
                if (node.Containables == null) node.Containables = new List<ContainableItem>();

                string anomalyId = GetOrCreateAnomalyForNode(node, registry, rng);
                var anomaly = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level = anomaly != null ? Math.Max(1, anomaly.baseThreat) : Math.Max(1, node.AnomalyLevel);

                // 每完成一次调查，都产出一个可收容目标（支持无限调查）
                var item = new ContainableItem
                {
                    Id = $"SCP_{node.Id}_{Guid.NewGuid().ToString("N")[..6]}",
                    Name = anomaly != null ? $"{anomaly.name} 线索（{node.Name}）" : $"未编号异常（{node.Name}）",
                    Level = level,
                    AnomalyId = anomalyId,
                };
                node.Containables.Add(item);

                // 调查完成不会自动收容
                node.Status = NodeStatus.Calm;
                node.HasAnomaly = true;

                s.News.Add($"- {node.Name} 调查完成：新增可收容目标 x1 ({anomalyId})");
                TryGenerateEvent(s, rng, EventSource.Investigate, node.Id, reason: "InvestigateComplete", sourceTask: task, sourceAnomalyId: anomalyId);
            }
            else if (task.Type == TaskType.Contain)
            {
                // Containment consumes one containable
                ContainableItem target = null;
                if (node.Containables != null && node.Containables.Count > 0)
                {
                    if (!string.IsNullOrEmpty(task.TargetContainableId))
                        target = node.Containables.FirstOrDefault(c => c != null && c.Id == task.TargetContainableId);

                    if (target == null)
                        target = node.Containables[0];

                    if (target != null)
                        node.Containables.Remove(target);
                }

                string anomalyId = target?.AnomalyId ?? GetOrCreateAnomalyForNode(node, registry, rng);
                var anomaly = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level = anomaly != null ? Math.Max(1, anomaly.baseThreat) : Math.Max(1, node.AnomalyLevel);
                int reward = 200 + 50 * level;

                s.Money += reward;
                s.Panic = Math.Max(0, s.Panic - 5);

                s.News.Add($"- {node.Name} 收容成功（+$ {reward}, -Panic 5）");
                TryGenerateEvent(s, rng, EventSource.Contain, node.Id, reason: "ContainComplete", sourceTask: task, sourceAnomalyId: anomalyId);

                // 收容成功：将该可收容目标加入“已收藏异常”（用于后续管理）。
                {
                    ContainableItem recordItem = target;
                    if (recordItem == null)
                    {
                        string rid = !string.IsNullOrEmpty(task.TargetContainableId)
                            ? task.TargetContainableId
                            : $"SCP_{node.Id}_{Guid.NewGuid().ToString("N")[..6]}";

                        recordItem = new ContainableItem
                        {
                            Id = rid,
                            Name = anomaly != null ? anomaly.name : $"已收容异常（{node.Name}）",
                            Level = level,
                            AnomalyId = anomalyId,
                        };

                        s.News.Add($"- {node.Name} 收容成功：目标信息缺失，已用占位记录写入收藏列表");
                    }

                    EnsureManagedAnomalyRecorded(node, recordItem, anomaly);
                }

                // 若已无可收容目标且无进行中的收容任务，则节点可视为“清空异常”。
                bool hasMoreContainables = node.Containables != null && node.Containables.Count > 0;
                bool hasActiveContainTask = node.Tasks != null && node.Tasks.Any(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain);
                bool hasActiveInvestigateWithSquad = node.Tasks != null && node.Tasks.Any(t =>
                    t != null &&
                    t.State == TaskState.Active &&
                    t.Type == TaskType.Investigate &&
                    t.AssignedAgentIds != null &&
                    t.AssignedAgentIds.Count > 0);

                if (!hasMoreContainables && !hasActiveContainTask)
                {
                    node.HasAnomaly = false;
                    node.ActiveAnomalyIds?.Clear();
                    node.Status = hasActiveInvestigateWithSquad ? NodeStatus.Calm : NodeStatus.Secured;
                }
                else
                {
                    node.Status = NodeStatus.Calm;
                    node.HasAnomaly = true;
                }
            }
            else if (task.Type == TaskType.Manage)
            {
                TryGenerateEvent(s, rng, EventSource.Manage, node.Id, reason: "ManageTick", sourceTask: task, sourceAnomalyId: task.TargetManagedAnomalyId);
            }
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
                    TryGenerateEvent(s, rng, EventSource.SecuredManage, node.Id, reason: "SecuredManageYield");
                }
            }

            if (totalAllNodes > 0)
                s.NegEntropy += totalAllNodes;
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

        static void EnsureManagedAnomalyRecorded(NodeState node, ContainableItem item, AnomalyDef anomaly)
        {
            if (node.ManagedAnomalies == null) node.ManagedAnomalies = new List<ManagedAnomalyState>();
            if (item == null) return;

            var existing = node.ManagedAnomalies.FirstOrDefault(m => m != null && m.Id == item.Id);
            if (existing != null)
            {
                existing.Level = Math.Max(existing.Level, item.Level);
                if (!string.IsNullOrEmpty(item.AnomalyId)) existing.AnomalyId = item.AnomalyId;
                if (!string.IsNullOrEmpty(anomaly?.@class)) existing.AnomalyClass = anomaly.@class;
                return;
            }

            node.ManagedAnomalies.Add(new ManagedAnomalyState
            {
                Id = item.Id,
                Name = item.Name,
                Level = Math.Max(1, item.Level),
                AnomalyId = item.AnomalyId,
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
        // Math helpers
        // =====================

        static float Clamp01(float v) => Mathf.Clamp01(v);
        static int ClampInt(int v, int min, int max) => Mathf.Clamp(v, min, max);
    }
}
// </EXPORT_BLOCK>
