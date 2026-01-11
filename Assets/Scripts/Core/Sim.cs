// Canvas-maintained file: Core/Sim (v3 - N tasks)
// Source: Assets/Scripts/Core/Sim.cs
// Goal: StepDay progresses ALL active tasks in NodeState.Tasks (unlimited investigate/contain tasks).
// Notes:
// - Random incident generation is currently disabled via ENABLE_RANDOM_EVENTS.
// - Events (PendingEvent) apply progress deltas to the most recent matching active task on the node.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    public static class Sim
    {
        // Toggle: pause random incident generation during StepDay.
        // Set to true to re-enable. (Existing pending events are still resolved via EventPanel.)
        private const bool ENABLE_RANDOM_EVENTS = false;

        public static void StepDay(GameState s, Random rng)
        {
            s.Day += 1;
            s.News.Add($"Day {s.Day}: 日结算开始");

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

                // 如果该节点有待处理事件：本日暂停该节点所有任务推进（保持你原先的阻塞语义）
                if (s.PendingEvents.Any(e => e.NodeId == n.Id))
                    continue;

                // 12% 概率事件（可开关）
                if (ENABLE_RANDOM_EVENTS && rng.NextDouble() < 0.12)
                {
                    var ev = GenerateEvent(n);
                    s.PendingEvents.Add(ev);
                    s.News.Add($"- {n.Name} 发生突发事件：{ev.Title}");
                    continue;
                }

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
                    t.Progress = Clamp01(t.Progress + delta);

                    if (t.Progress >= 1f)
                    {
                        CompleteTask(s, n, t);
                    }
                }
            }

            // 3) 恐慌 & 收入
            int activeAnomaly = s.Nodes.Count(n => n.HasAnomaly && n.Status != NodeStatus.Secured);
            s.Panic = ClampInt(s.Panic + activeAnomaly, 0, 100);
            s.Money += 50;

            s.News.Add($"Day {s.Day} 结束");
        }

        public static (bool success, string text) ResolveEvent(GameState s, string eventId, string optionId, Random rng)
        {
            var ev = s.PendingEvents.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) return (false, "事件不存在");

            var opt = ev.Options.FirstOrDefault(o => o.Id == optionId);
            if (opt == null) return (false, "选项不存在");

            var node = s.Nodes.FirstOrDefault(n => n.Id == ev.NodeId);
            if (node == null) return (false, "节点不存在");

            // 优先找与事件 Kind 匹配、且本节点最近创建的一条 Active 任务
            var task = PickTaskForEvent(node, ev.Kind);

            // 若无匹配任务，则事件只结算资源/恐慌，不动进度（可接受的最小语义）
            var agents = (task != null)
                ? GetAssignedAgents(s, task.AssignedAgentIds)
                : new List<AgentState>();

            float rate = CalcSuccessRate(opt, agents);
            bool success = rng.NextDouble() < rate;

            if (success)
            {
                s.Money += opt.MoneyOnSuccess;
                s.Panic = ClampInt(s.Panic + opt.PanicOnSuccess, 0, 100);
                if (task != null) task.Progress = Clamp01(task.Progress + opt.ProgressDeltaOnSuccess);
            }
            else
            {
                s.Money += opt.MoneyOnFail;
                s.Panic = ClampInt(s.Panic + opt.PanicOnFail, 0, 100);
                if (task != null) task.Progress = Clamp01(task.Progress + opt.ProgressDeltaOnFail);
            }

            string resultText = $"{(success ? "成功" : "失败")}（成功率 {Math.Round(rate * 100)}%）: {opt.Text}";
            s.News.Add($"- {node.Name} 事件结果：{resultText}");

            // 若事件导致进度直接完成，则同日立即完成该任务
            if (task != null && task.State == TaskState.Active && task.Progress >= 1f)
                CompleteTask(s, node, task);

            s.PendingEvents.Remove(ev);
            return (success, resultText);
        }

        // =====================
        // Task completion rules
        // =====================

        static void CompleteTask(GameState s, NodeState node, NodeTask task)
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

                // 若已无可收容目标且无进行中的收容任务，则节点视为清空异常
                bool hasMoreContainables = node.Containables != null && node.Containables.Count > 0;
                bool hasActiveContainTask = node.Tasks != null && node.Tasks.Any(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Contain);

                if (!hasMoreContainables && !hasActiveContainTask)
                {
                    node.HasAnomaly = false;
                    node.Status = NodeStatus.Secured;
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
            float baseDelta = 0.10f;

            int totalStat = 0;
            foreach (var a in squad)
            {
                if (t.Type == TaskType.Investigate) totalStat += a.Perception;
                else totalStat += a.Operation;
            }

            float statBonus = totalStat * 0.01f;
            float noise = (float)(rng.NextDouble() * 0.02 - 0.01);

            return Math.Max(0.05f, baseDelta + statBonus + noise);
        }

        static float CalcSuccessRate(DecisionOption opt, List<AgentState> squad)
        {
            // 没人：用 base
            if (squad == null || squad.Count == 0) return opt.BaseSuccess;

            int maxVal = 0;
            foreach (var a in squad)
            {
                int val = opt.CheckAttr switch
                {
                    "Perception" => a.Perception,
                    "Operation" => a.Operation,
                    "Resistance" => a.Resistance,
                    "Power" => a.Power,
                    _ => 0
                };
                if (val > maxVal) maxVal = val;
            }

            float rate = opt.BaseSuccess + (maxVal - opt.Threshold) * 0.05f;
            return Clamp01(rate);
        }

        static NodeTask PickTaskForEvent(NodeState node, EventKind kind)
        {
            if (node == null || node.Tasks == null) return null;
            TaskType want = (kind == EventKind.Investigate) ? TaskType.Investigate : TaskType.Contain;

            // 最近创建的优先（CreatedDay 越大越新；若没填则按列表末尾）
            NodeTask best = null;
            int bestDay = int.MinValue;

            foreach (var t in node.Tasks)
            {
                if (t == null) continue;
                if (t.State != TaskState.Active) continue;
                if (t.Type != want) continue;

                int cd = t.CreatedDay;
                if (cd >= bestDay)
                {
                    bestDay = cd;
                    best = t;
                }
            }

            // fallback：列表从后往前找
            if (best == null)
            {
                for (int i = node.Tasks.Count - 1; i >= 0; i--)
                {
                    var t = node.Tasks[i];
                    if (t != null && t.State == TaskState.Active && t.Type == want)
                        return t;
                }
            }

            return best;
        }

        static PendingEvent GenerateEvent(NodeState n)
        {
            // 选择事件类型：优先调查任务，其次收容任务
            bool hasInv = n.Tasks != null && n.Tasks.Any(t => t != null && t.State == TaskState.Active && t.Type == TaskType.Investigate);
            bool inv = hasInv;

            var ev = new PendingEvent
            {
                Id = "EV_" + Guid.NewGuid().ToString("N")[..8],
                NodeId = n.Id,
                Kind = inv ? EventKind.Investigate : EventKind.Contain,
                Title = inv ? "目击者失控" : "收容设施异常震动",
                Desc = inv ? "当地目击者情绪激动，可能引发恐慌扩散。" : "设施监测到异常震动，继续操作可能扩大损失。"
            };
            ev.Options.Add(new DecisionOption { Id = "A", Text = inv ? "安抚" : "加固", CheckAttr = inv ? "Perception" : "Resistance", Threshold = 6 });
            ev.Options.Add(new DecisionOption { Id = "B", Text = inv ? "驱散" : "推进", CheckAttr = inv ? "Power" : "Operation", Threshold = 7 });
            return ev;
        }

        static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
