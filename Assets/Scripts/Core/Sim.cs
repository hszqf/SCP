// Canvas-maintained file: Core/Sim (v3 - N tasks)
// Source: Assets/Scripts/Core/Sim.cs
// Goal: StepDay progresses ALL active tasks in NodeState.Tasks (unlimited investigate/contain tasks).
// Notes:
// - Node events now attach to NodeState.PendingEvents.
// - PendingEvent (legacy) is kept for compatibility but not used by this MVP flow.
// <EXPORT_BLOCK>

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace Core
{
    public static class Sim
    {
        private const int LOCAL_PANIC_HIGH_THRESHOLD = 6;
        private const double RANDOM_EVENT_CHANCE = 0.45; // 开发期调高，便于验收
        private const int FIXED_EVENT_DAY = 3;
        private const string FIXED_EVENT_NODE_ID = "N1";

        public static void StepDay(GameState s, Random rng)
        {
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
                    TryGenerateEvent(s, rng, EventSource.Fixed, FIXED_EVENT_NODE_ID, "FixedDayTrigger");
                }
            }

            // 1) 异常生成（节点维度）
            foreach (var n in s.Nodes)
            {
                if (n.Status == NodeStatus.Calm && !n.HasAnomaly)
                {
                    if (rng.NextDouble() < 0.15)
                    {
                        n.HasAnomaly = true;
                        n.AnomalyLevel = Math.Max(1, n.AnomalyLevel);
                        s.News.Add($"- {n.Name} 出现异常迹象");
                    }
                }
            }

            // 2) 推进任务（任务维度：同节点可并行 N 个任务）
            foreach (var n in s.Nodes)
            {
                if (n.Tasks == null || n.Tasks.Count == 0) continue;

                // 如果该节点有待处理事件：本日暂停该节点所有任务推进（保持原先阻塞语义）
                if (HasPendingNodeEvents(n))
                    continue;

                // 推进所有 Active 任务
                for (int i = 0; i < n.Tasks.Count; i++)
                {
                    var t = n.Tasks[i];
                    if (t == null) continue;
                    if (t.State != TaskState.Active) continue;
                    if (t.AssignedAgentIds == null || t.AssignedAgentIds.Count == 0) continue;

                    var squad = GetAssignedAgents(s, t.AssignedAgentIds);
                    if (squad.Count == 0) continue;

                    float delta = CalcDailyProgressDelta(t, squad, rng);

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
                        CompleteTask(s, n, t, rng);
                    }
                }
            }

            // 2.5) 收容后管理（负熵产出）
            StepManageTasks(s, rng);

            // 3) 事件来源（过天触发）：本地恐慌高 / 随机
            foreach (var node in s.Nodes)
            {
                if (node == null) continue;

                int pendingBefore = node.PendingEvents?.Count ?? 0;

                if (node.LocalPanic >= LOCAL_PANIC_HIGH_THRESHOLD)
                {
                    bool allowLocalPanicHigh = pendingBefore == 0;
                    string localPanicReason = allowLocalPanicHigh ? "trigger" : "pendingEvents";
                    Debug.Log($"[EventGenCheck] day={s.Day} node={node.Id} source=LocalPanicHigh panic={node.LocalPanic} threshold={LOCAL_PANIC_HIGH_THRESHOLD} allow={allowLocalPanicHigh} reason={localPanicReason}");

                    if (allowLocalPanicHigh)
                    {
                        TryGenerateEvent(s, rng, EventSource.LocalPanicHigh, node.Id, $"LocalPanicHigh>={LOCAL_PANIC_HIGH_THRESHOLD}");
                        pendingBefore = node.PendingEvents?.Count ?? pendingBefore;
                    }
                }

                double roll = rng.NextDouble();
                bool allowRandom = pendingBefore == 0 && roll < RANDOM_EVENT_CHANCE;
                string randomReason = pendingBefore > 0 ? "pendingEvents" : (roll < RANDOM_EVENT_CHANCE ? "trigger" : "rollTooHigh");
                Debug.Log($"[EventGenCheck] day={s.Day} node={node.Id} source=Random roll={roll:0.00} p={RANDOM_EVENT_CHANCE:0.00} allow={allowRandom} reason={randomReason} pendingBefore={pendingBefore}");

                if (allowRandom)
                {
                    TryGenerateEvent(s, rng, EventSource.Random, node.Id, $"RandomRoll<{RANDOM_EVENT_CHANCE:0.00}");
                }
            }

            // 4) 不处理的后果（事件不会自动清除）
            ApplyIgnorePenaltyOnDayEnd(s);

            // 5) 恐慌 & 收入（全局）
            int activeAnomaly = s.Nodes.Count(n => n.HasAnomaly && n.Status != NodeStatus.Secured);
            s.Panic = ClampInt(s.Panic + activeAnomaly, 0, 100);
            s.Money += 50;

            s.News.Add($"Day {s.Day} 结束");
        }

        public static (bool success, string text) ResolveEvent(GameState s, string nodeId, string eventId, string optionId, Random rng)
        {
            var node = s.Nodes.FirstOrDefault(n => n != null && n.Id == nodeId);
            if (node == null) return (false, "节点不存在");
            if (node.PendingEvents == null || node.PendingEvents.Count == 0) return (false, "节点无事件");

            var ev = node.PendingEvents.FirstOrDefault(e => e != null && e.EventId == eventId);
            if (ev == null) return (false, "事件不存在");

            var opt = ev.Options?.FirstOrDefault(o => o != null && o.OptionId == optionId);
            if (opt == null) return (false, "选项不存在");

            ApplyEffect(s, node, opt.Effects);

            node.PendingEvents.Remove(ev);

            int pendingAfter = node.PendingEvents.Count;
            Debug.Log($"[EventResolve] day={s.Day} node={node.Id} eventId={ev.EventId} option={opt.OptionId} effects={opt.Effects} pendingCountAfter={pendingAfter}");

            s.News.Add($"- {node.Name} 事件处理：{ev.Title} -> {opt.Text}");

            var resultText = string.IsNullOrEmpty(opt.ResultText) ? BuildEffectSummary(opt.Effects) : opt.ResultText;
            if (string.IsNullOrEmpty(resultText)) resultText = opt.Text;
            if (string.IsNullOrEmpty(resultText)) resultText = "事件已处理";
            return (true, resultText);
        }

        public static bool TryGenerateEvent(GameState s, Random rng, EventSource source, string nodeId, string reason)
        {
            var node = s.Nodes.FirstOrDefault(n => n != null && n.Id == nodeId);
            if (node == null) return false;

            var instance = EventTemplates.CreateInstance(source, nodeId, s.Day, rng);
            if (instance == null) return false;

            AddEventToNode(node, instance);

            int pendingCount = node.PendingEvents.Count;
            Debug.Log($"[EventGen] day={s.Day} source={source} node={nodeId} eventId={instance.EventId} pendingCount={pendingCount} reason={reason}");
            s.News.Add($"- {node.Name} 发生事件：{instance.Title}");
            return true;
        }

        public static void ApplyIgnorePenaltyOnDayEnd(GameState s)
        {
            foreach (var node in s.Nodes)
            {
                if (!HasPendingNodeEvents(node)) continue;

                foreach (var ev in node.PendingEvents)
                {
                    if (ev == null || ev.IgnorePenalty == null) continue;
                    ApplyEffect(s, node, ev.IgnorePenalty);
                    Debug.Log($"[EventIgnore] day={s.Day} node={node.Id} eventId={ev.EventId} penalty={ev.IgnorePenalty} localPanic={node.LocalPanic} pop={node.Population}");
                }
            }
        }

        private static bool HasPendingNodeEvents(NodeState node)
        {
            return node != null && node.PendingEvents != null && node.PendingEvents.Count > 0;
        }

        private static void AddEventToNode(NodeState node, EventInstance ev)
        {
            if (node.PendingEvents == null) node.PendingEvents = new List<EventInstance>();
            node.PendingEvents.Add(ev);
        }

        private static void ApplyEffect(GameState s, NodeState node, EventEffect eff)
        {
            if (eff == null) return;

            node.LocalPanic = Math.Max(0, node.LocalPanic + eff.LocalPanicDelta);
            node.Population = Math.Max(0, node.Population + eff.PopulationDelta);

            s.Panic = ClampInt(s.Panic + eff.GlobalPanicDelta, 0, 100);
            s.Money += eff.MoneyDelta;
            s.NegEntropy = Math.Max(0, s.NegEntropy + eff.NegEntropyDelta);
        }

        private static string BuildEffectSummary(EventEffect eff)
        {
            if (eff == null) return string.Empty;

            var parts = new List<string>();
            AddDelta(parts, "本地恐慌", eff.LocalPanicDelta);
            AddDelta(parts, "人口", eff.PopulationDelta);
            AddDelta(parts, "全局恐慌", eff.GlobalPanicDelta);
            AddDelta(parts, "资金", eff.MoneyDelta);
            AddDelta(parts, "负熵", eff.NegEntropyDelta);

            return parts.Count == 0 ? string.Empty : string.Join("，", parts);
        }

        private static void AddDelta(List<string> parts, string label, int delta)
        {
            if (delta == 0) return;
            var sign = delta > 0 ? "+" : string.Empty;
            parts.Add($"{label} {sign}{delta}");
        }

        // =====================
        // Task completion rules
        // =====================

        static void CompleteTask(GameState s, NodeState node, NodeTask task, Random rng)
        {
            task.Progress = 1f;
            task.State = TaskState.Completed;
            task.CompletedDay = s.Day;

            // Release squad
            if (task.AssignedAgentIds != null) task.AssignedAgentIds.Clear();

            if (task.Type == TaskType.Investigate)
            {
                if (node.Containables == null) node.Containables = new List<ContainableItem>();

                // 每完成一次调查，都产出一个可收容目标（支持无限调查）
                var item = new ContainableItem
                {
                    Id = $"SCP_{node.Id}_{Guid.NewGuid().ToString("N")[..6]}",
                    Name = $"未编号异常（{node.Name}）",
                    Level = Math.Max(1, node.AnomalyLevel)
                };
                node.Containables.Add(item);

                // 调查完成不会自动收容
                node.Status = NodeStatus.Calm;
                node.HasAnomaly = true;

                s.News.Add($"- {node.Name} 调查完成：新增可收容目标 x1");
                TryGenerateEvent(s, rng, EventSource.Investigate, node.Id, "InvestigateComplete");
            }
            else
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

                int level = (target != null) ? Math.Max(1, target.Level) : Math.Max(1, node.AnomalyLevel);
                int reward = 200 + 50 * level;

                s.Money += reward;
                s.Panic = Math.Max(0, s.Panic - 5);

                s.News.Add($"- {node.Name} 收容成功（+$ {reward}, -Panic 5）");
                TryGenerateEvent(s, rng, EventSource.Contain, node.Id, "ContainComplete");

                // 收容成功：将该可收容目标加入“已收藏异常”（用于后续管理）。
                // 注意：在并行收容/目标缺失等情况下，target 可能为 null，但我们仍然需要记录一个“已收容异常”，否则管理系统无法进入。
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
                            Name = $"已收容异常（{node.Name}）",
                            Level = level
                        };

                        s.News.Add($"- {node.Name} 收容成功：目标信息缺失，已用占位记录写入收藏列表");
                    }

                    EnsureManagedAnomalyRecorded(node, recordItem);
                }

                // 若已无可收容目标且无进行中的收容任务，则节点可视为“清空异常”。
                // 但如果还有进行中的调查（有小队）在该节点跑动，则不应标记为 Secured，避免 UI/体验出现“已收容但还在调查”的割裂。
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
                    node.Status = hasActiveInvestigateWithSquad ? NodeStatus.Calm : NodeStatus.Secured;
                }
                else
                {
                    node.Status = NodeStatus.Calm;
                    node.HasAnomaly = true;
                }
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

        static float CalcDailyProgressDelta(NodeTask t, List<AgentState> squad, Random rng)
        {
            // Base daily progress.
            // - Investigate/Contain: normal completion loop.
            // - Manage: progress is only a "started" flag; keep delta small to avoid hitting 1.

            float baseDelta = (t.Type == TaskType.Manage) ? 0.02f : 0.10f;

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

        static void StepManageTasks(GameState s, Random rng)
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
                    TryGenerateEvent(s, rng, EventSource.SecuredManage, node.Id, "SecuredManageYield");
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

        static void EnsureManagedAnomalyRecorded(NodeState node, ContainableItem item)
        {
            if (node == null || item == null) return;
            if (node.ManagedAnomalies == null) node.ManagedAnomalies = new List<ManagedAnomalyState>();

            bool exists = node.ManagedAnomalies.Any(x => x != null && x.Id == item.Id);
            if (exists) return;

            node.ManagedAnomalies.Add(new ManagedAnomalyState
            {
                Id = item.Id,
                Name = item.Name,
                Level = Math.Max(1, item.Level),
                Favorited = true,
                StartDay = 0,
                TotalNegEntropy = 0
            });
        }

        static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
// </EXPORT_BLOCK>
