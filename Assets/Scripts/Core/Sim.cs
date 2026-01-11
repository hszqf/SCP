using System;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    public static class Sim
    {
        public static void StepDay(GameState s, Random rng)
        {
            s.Day += 1;
            s.News.Add($"Day {s.Day}: 日结算开始");

            // 1) 异常生成
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

            // 2) 推进进度
            foreach (var n in s.Nodes)
            {
                if (n.Status != NodeStatus.Investigating && n.Status != NodeStatus.Containing) continue;
                if (s.PendingEvents.Any(e => e.NodeId == n.Id)) continue;

                // 12% 概率事件
                if (rng.NextDouble() < 0.12)
                {
                    var ev = GenerateEvent(n);
                    s.PendingEvents.Add(ev);
                    s.News.Add($"- {n.Name} 发生突发事件：{ev.Title}");
                    continue;
                }

                // --- Squad Logic ---
                var agents = GetAssignedAgents(s, n);
                if (agents.Count == 0) continue; // 无人执勤

                float delta = CalcDailyProgressDelta(n, agents, rng);

                if (n.Status == NodeStatus.Investigating)
                {
                    n.InvestigateProgress = Clamp01(n.InvestigateProgress + delta);
                    if (n.InvestigateProgress >= 1f)
                    {
                        n.Status = NodeStatus.Containing;
                        n.ContainProgress = 0f;
                        s.News.Add($"- {n.Name} 调查完成，小队转入收容阶段");
                    }
                }
                else
                {
                    n.ContainProgress = Clamp01(n.ContainProgress + delta);
                    if (n.ContainProgress >= 1f)
                    {
                        n.Status = NodeStatus.Secured;
                        n.AssignedAgentIds.Clear(); // 释放小队

                        int reward = 200 + 50 * n.AnomalyLevel;
                        s.Money += reward;
                        s.Panic = Math.Max(0, s.Panic - 5);
                        s.News.Add($"- {n.Name} 收容成功（+$ {reward}, -Panic 5）");
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

            var node = s.Nodes.First(n => n.Id == ev.NodeId);
            var agents = GetAssignedAgents(s, node);

            // 取小队中属性最高的人来判定 (Best Man for the Job)
            float rate = CalcSuccessRate(opt, agents);
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
            s.News.Add($"- {node.Name} 事件结果：{resultText}");

            s.PendingEvents.Remove(ev);
            return (success, resultText);
        }

        // --- Helpers ---

        static List<AgentState> GetAssignedAgents(GameState s, NodeState n)
        {
            if (n.AssignedAgentIds == null || n.AssignedAgentIds.Count == 0) return new List<AgentState>();
            return s.Agents.Where(a => n.AssignedAgentIds.Contains(a.Id)).ToList();
        }

        static float CalcDailyProgressDelta(NodeState n, List<AgentState> squad, Random rng)
        {
            float baseDelta = 0.10f; // 基础值稍降
            
            // 核心机制：人多力量大，属性叠加
            int totalStat = 0;
            foreach(var agent in squad)
            {
                totalStat += (n.Status == NodeStatus.Investigating) ? agent.Perception : agent.Operation;
            }

            // 3个人, 属性平均5 => total 15 => bonus +0.15 => total 0.25/day
            float statBonus = totalStat * 0.01f;
            float noise = (float)(rng.NextDouble() * 0.02 - 0.01);
            
            return Math.Max(0.05f, baseDelta + statBonus + noise);
        }

        static float CalcSuccessRate(DecisionOption opt, List<AgentState> squad)
        {
            if (squad == null || squad.Count == 0) return opt.BaseSuccess;

            // 判定取全队最高值
            int maxVal = 0;
            foreach(var a in squad)
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

        static PendingEvent GenerateEvent(NodeState n)
        {
             // (简化的事件生成，和之前逻辑一致)
            bool inv = n.Status == NodeStatus.Investigating;
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