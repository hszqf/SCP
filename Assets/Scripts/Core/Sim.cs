using System;
using System.Linq;

namespace Core
{
    public static class Sim
    {
        public static void StepDay(GameState s, Random rng)
        {
            s.Day += 1;
            s.News.Add($"Day {s.Day}: 日结算开始");

            // 1) 平静节点 15% 生成异常（示例）
            foreach (var n in s.Nodes)
            {
                if (n.Status == NodeStatus.Calm && !n.HasAnomaly)
                {
                    if (rng.NextDouble() < 0.15)
                    {
                        n.HasAnomaly = true;
                        n.AnomalyLevel = 1;
                        s.News.Add($"- {n.Name} 出现异常迹象");
                    }
                }
            }

            // 2) 推进调查/收容（有事件则暂停推进）
            foreach (var n in s.Nodes)
            {
                if (n.Status != NodeStatus.Investigating && n.Status != NodeStatus.Containing) continue;
                if (s.PendingEvents.Any(e => e.NodeId == n.Id)) continue;

                // 12% 概率生成事件
                if (rng.NextDouble() < 0.12)
                {
                    var ev = GenerateEvent(n);
                    s.PendingEvents.Add(ev);
                    s.News.Add($"- {n.Name} 发生突发事件：{ev.Title}");
                    continue;
                }

                var agent = GetAssignedAgent(s, n);
                float delta = CalcDailyProgressDelta(n, agent, rng);

                if (n.Status == NodeStatus.Investigating)
                {
                    n.InvestigateProgress = Clamp01(n.InvestigateProgress + delta);
                    if (n.InvestigateProgress >= 1f)
                    {
                        n.Status = NodeStatus.Containing;
                        n.ContainProgress = 0f;
                        s.News.Add($"- {n.Name} 调查完成，转入收容阶段");
                    }
                }
                else
                {
                    n.ContainProgress = Clamp01(n.ContainProgress + delta);
                    if (n.ContainProgress >= 1f)
                    {
                        n.Status = NodeStatus.Secured;
                        n.AssignedAgentId = null;

                        int reward = 200 + 50 * n.AnomalyLevel;
                        s.Money += reward;
                        s.Panic = Math.Max(0, s.Panic - 5);
                        s.News.Add($"- {n.Name} 收容成功（+$ {reward}, -Panic 5）");
                    }
                }
            }

            // 3) 恐慌按未收容异常数增长
            int activeAnomaly = s.Nodes.Count(n => n.HasAnomaly && n.Status != NodeStatus.Secured);
            s.Panic = ClampInt(s.Panic + activeAnomaly, 0, 100);

            // 4) 基础收入
            s.Money += 50;

            s.News.Add($"Day {s.Day}: 日结算结束（Money={s.Money}, Panic={s.Panic}）");
        }

        public static (bool success, string text) ResolveEvent(GameState s, string eventId, string optionId, Random rng)
        {
            var ev = s.PendingEvents.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) return (false, "事件不存在");

            var opt = ev.Options.FirstOrDefault(o => o.Id == optionId);
            if (opt == null) return (false, "选项不存在");

            var node = s.Nodes.First(n => n.Id == ev.NodeId);
            var agent = GetAssignedAgent(s, node);

            float rate = CalcSuccessRate(opt, agent);
            bool success = rng.NextDouble() < rate;

            if (success)
            {
                s.Money += opt.MoneyOnSuccess;
                s.Panic = ClampInt(s.Panic + opt.PanicOnSuccess, 0, 100);
                ApplyProgressDelta(ev.Kind, node, opt.ProgressDeltaOnSuccess);
            }
            else
            {
                s.Money += opt.MoneyOnFail;
                s.Panic = ClampInt(s.Panic + opt.PanicOnFail, 0, 100);
                ApplyProgressDelta(ev.Kind, node, opt.ProgressDeltaOnFail);
            }

            string resultText = $"{(success ? "成功" : "失败")}（成功率 {Math.Round(rate * 100)}%）: {opt.Text}";
            s.News.Add($"- {node.Name} 事件结果：{ev.Title} → {resultText}");

            s.PendingEvents.Remove(ev);
            return (success, resultText);
        }

        static PendingEvent GenerateEvent(NodeState n)
        {
            bool inv = n.Status == NodeStatus.Investigating;
            var ev = new PendingEvent
            {
                Id = "EV_" + Guid.NewGuid().ToString("N")[..8],
                NodeId = n.Id,
                Kind = inv ? EventKind.Investigate : EventKind.Contain,
                Title = inv ? "目击者失控" : "收容设施异常震动",
                Desc = inv ? "当地目击者情绪激动，可能引发恐慌扩散。" : "设施监测到异常震动，继续操作可能扩大损失。"
            };

            ev.Options.Add(new DecisionOption
            {
                Id = "A",
                Text = inv ? "安抚并隔离目击者" : "暂缓推进，优先加固",
                CheckAttr = inv ? "Perception" : "Resistance",
                Threshold = 6,
                BaseSuccess = 0.55f,
                PanicOnSuccess = -2,
                PanicOnFail = +4,
                ProgressDeltaOnSuccess = +0.02f,
                ProgressDeltaOnFail = -0.06f
            });

            ev.Options.Add(new DecisionOption
            {
                Id = "B",
                Text = inv ? "强制驱散，快速清场" : "继续推进，赌它只是误报",
                CheckAttr = inv ? "Power" : "Operation",
                Threshold = 7,
                BaseSuccess = 0.45f,
                MoneyOnSuccess = +50,
                MoneyOnFail = -80,
                PanicOnSuccess = +1,
                PanicOnFail = +6,
                ProgressDeltaOnSuccess = +0.05f,
                ProgressDeltaOnFail = -0.10f
            });

            return ev;
        }

        static AgentState GetAssignedAgent(GameState s, NodeState n)
        {
            if (string.IsNullOrEmpty(n.AssignedAgentId)) return null;
            return s.Agents.FirstOrDefault(a => a.Id == n.AssignedAgentId);
        }

        static float CalcDailyProgressDelta(NodeState n, AgentState agent, Random rng)
        {
            float baseDelta = 0.12f;
            int stat = 0;
            if (agent != null)
                stat = (n.Status == NodeStatus.Investigating) ? agent.Perception : agent.Operation;

            float statBonus = stat * 0.01f; // 5 => +0.05
            float noise = (float)(rng.NextDouble() * 0.02 - 0.01); // -0.01~+0.01
            return Math.Max(0.02f, baseDelta + statBonus + noise);
        }

        static float CalcSuccessRate(DecisionOption opt, AgentState agent)
        {
            int val = 0;
            if (agent != null)
            {
                val = opt.CheckAttr switch
                {
                    "Perception" => agent.Perception,
                    "Operation" => agent.Operation,
                    "Resistance" => agent.Resistance,
                    "Power" => agent.Power,
                    _ => 0
                };
            }

            float rate = opt.BaseSuccess + (val - opt.Threshold) * 0.05f;
            return Clamp01(rate);
        }

        static void ApplyProgressDelta(EventKind kind, NodeState node, float delta)
        {
            if (kind == EventKind.Investigate)
                node.InvestigateProgress = Clamp01(node.InvestigateProgress + delta);
            else
                node.ContainProgress = Clamp01(node.ContainProgress + delta);
        }

        static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
