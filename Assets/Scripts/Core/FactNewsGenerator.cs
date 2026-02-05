using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Generates news from facts using media profiles and templates.
    /// Provides structured payload + template assembly for news generation.
    /// </summary>
    public static class FactNewsGenerator
    {
        /// <summary>
        /// Generate news from unreported facts in the game state.
        /// Returns the number of news items generated.
        /// </summary>
        public static int GenerateNewsFromFacts(GameState state, DataRegistry registry, int maxCount = 5)
        {
            if (state?.FactSystem?.Facts == null || state.NewsLog == null)
                return 0;

            // Get unreported facts ordered by severity (high to low) and day (recent first)
            var unreportedFacts = state.FactSystem.Facts
                .Where(f => f != null && !f.Reported)
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.Day)
                .Take(maxCount)
                .ToList();

            int generated = 0;
            foreach (var fact in unreportedFacts)
            {
                var newsInstance = GenerateNewsFromFact(state, fact, registry);
                if (newsInstance != null)
                {
                    state.NewsLog.Add(newsInstance);
                    fact.Reported = true;
                    generated++;
                    
                    Debug.Log($"[FactNews] day={state.Day} factId={fact.FactId} type={fact.Type} newsId={newsInstance.Id} severity={fact.Severity}");
                }
            }

            return generated;
        }

        /// <summary>
        /// Convert a single fact to a news instance using templates.
        /// </summary>
        private static NewsInstance GenerateNewsFromFact(GameState state, FactInstance fact, DataRegistry registry)
        {
            if (fact == null) return null;

            // Get default media profiles (hardcoded for now, can be moved to JSON later)
            var mediaProfiles = GetDefaultMediaProfiles();
            var profile = SelectMediaProfile(mediaProfiles, fact);
            
            // Generate title and description using templates
            string title = GenerateTitle(fact, profile, state);
            string desc = GenerateDescription(fact, profile, state);

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(desc))
                return null;

            // Create a synthetic news def ID based on fact type
            string newsDefId = $"FACT_{fact.Type}_{fact.FactId}";

            var newsInstance = new NewsInstance
            {
                Id = $"NEWS_{Guid.NewGuid():N}",
                NewsDefId = newsDefId,
                NodeId = fact.NodeId,
                SourceAnomalyId = fact.AnomalyId,
                CauseType = "FactTriggered",
                AgeDays = 0,
                Day = state.Day
            };

            // Store the generated content in the legacy News list for display
            state.News.Add($"[新闻] {title}");

            return newsInstance;
        }

        /// <summary>
        /// Get default media profiles (hardcoded for now).
        /// Can be moved to JSON/DataRegistry later.
        /// </summary>
        private static List<MediaProfileDef> GetDefaultMediaProfiles()
        {
            return new List<MediaProfileDef>
            {
                new MediaProfileDef
                {
                    profileId = "FORMAL",
                    name = "正式报道",
                    tone = "neutral",
                    weight = 1
                },
                new MediaProfileDef
                {
                    profileId = "SENSATIONAL",
                    name = "耸人听闻",
                    tone = "alarmist",
                    weight = 1
                },
                new MediaProfileDef
                {
                    profileId = "INVESTIGATIVE",
                    name = "调查报道",
                    tone = "analytical",
                    weight = 1
                }
            };
        }

        /// <summary>
        /// Select a media profile based on fact characteristics.
        /// High severity facts more likely to use sensational tone.
        /// </summary>
        private static MediaProfileDef SelectMediaProfile(List<MediaProfileDef> profiles, FactInstance fact)
        {
            if (profiles == null || profiles.Count == 0)
                return null;

            // Higher severity -> prefer sensational
            if (fact.Severity >= 4)
            {
                var sensational = profiles.FirstOrDefault(p => p.profileId == "SENSATIONAL");
                if (sensational != null) return sensational;
            }

            // Lower severity -> prefer formal
            if (fact.Severity <= 2)
            {
                var formal = profiles.FirstOrDefault(p => p.profileId == "FORMAL");
                if (formal != null) return formal;
            }

            // Default to investigative
            var investigative = profiles.FirstOrDefault(p => p.profileId == "INVESTIGATIVE");
            return investigative ?? profiles[0];
        }

        /// <summary>
        /// Generate news title from fact using templates.
        /// </summary>
        private static string GenerateTitle(FactInstance fact, MediaProfileDef profile, GameState state)
        {
            string nodeName = GetNodeName(state, fact.NodeId);
            object anomalyClass = fact.Payload?.GetValueOrDefault("anomalyClass", "未知");

            switch (fact.Type)
            {
                case "AnomalySpawned":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【快讯】{nodeName}发现异常现象",
                        "SENSATIONAL" => $"【紧急】{nodeName}出现神秘事件！",
                        "INVESTIGATIVE" => $"【调查】{nodeName}地区异常活动报告",
                        _ => $"{nodeName}出现异常迹象"
                    };

                case "InvestigateCompleted":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【官方】{nodeName}调查工作完成",
                        "SENSATIONAL" => $"【独家】{nodeName}惊人真相曝光！",
                        "INVESTIGATIVE" => $"【深度】{nodeName}异常真相揭秘",
                        _ => $"{nodeName}调查完成"
                    };

                case "InvestigateNoResult":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【通报】{nodeName}调查暂无结果",
                        "SENSATIONAL" => $"【疑云】{nodeName}谜团仍未解开！",
                        "INVESTIGATIVE" => $"【追踪】{nodeName}案件调查持续中",
                        _ => $"{nodeName}调查未发现异常"
                    };

                case "ContainCompleted":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【成功】{nodeName}异常收容完成",
                        "SENSATIONAL" => $"【胜利】{nodeName}危机解除！民众欢呼",
                        "INVESTIGATIVE" => $"【跟进】{nodeName}异常处置行动结束",
                        _ => $"{nodeName}收容成功"
                    };

                default:
                    return $"{nodeName}相关事件";
            }
        }

        /// <summary>
        /// Generate news description from fact using templates.
        /// </summary>
        private static string GenerateDescription(FactInstance fact, MediaProfileDef profile, GameState state)
        {
            string nodeName = GetNodeName(state, fact.NodeId);
            object anomalyClass = fact.Payload?.GetValueOrDefault("anomalyClass", "未知");
            
            switch (fact.Type)
            {
                case "AnomalySpawned":
                    int severity = fact.Severity;
                    string threat = severity >= 4 ? "极高威胁" : severity >= 3 ? "高度警戒" : "需要关注";
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"根据官方通报，{nodeName}地区出现异常现象，等级：{anomalyClass}。相关部门已介入调查，建议居民保持警惕。",
                        "SENSATIONAL" => $"震惊！{nodeName}惊现神秘事件！目击者称现场情况{threat}，恐慌情绪蔓延。当局呼吁民众不要恐慌。",
                        "INVESTIGATIVE" => $"本报记者获悉，{nodeName}地区发生异常活动，专家分析为{anomalyClass}级别事件。我们将持续跟踪报道。",
                        _ => $"{nodeName}出现异常活动，等级：{anomalyClass}。"
                    };

                case "InvestigateCompleted":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"经过深入调查，{nodeName}异常事件已确认为{anomalyClass}类型。调查组已完成现场取证工作。",
                        "SENSATIONAL" => $"独家！{nodeName}真相大白！原来是{anomalyClass}在作祟！知情人士透露更多惊人内幕...",
                        "INVESTIGATIVE" => $"经本报调查核实，{nodeName}事件确认为{anomalyClass}异常。深度分析显示，此类事件具有典型特征。",
                        _ => $"{nodeName}调查完成，发现{anomalyClass}异常。"
                    };

                case "InvestigateNoResult":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}的调查工作已告一段落，暂未发现明确异常迹象。相关部门将继续保持关注。",
                        "SENSATIONAL" => $"谜团重重！{nodeName}调查无果，真相依然扑朔迷离！是否有人刻意隐瞒？",
                        "INVESTIGATIVE" => $"本报跟踪报道：{nodeName}调查尚无定论。专家认为可能需要更多时间和资源深入研究。",
                        _ => $"{nodeName}调查暂无结果。"
                    };

                case "ContainCompleted":
                    object reward = fact.Payload?.GetValueOrDefault("reward", 0);
                    object relief = fact.Payload?.GetValueOrDefault("panicRelief", 0);
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}异常已成功收容，{anomalyClass}级威胁得到控制。行动获得资金奖励{reward}，社会恐慌指数下降{relief}点。",
                        "SENSATIONAL" => $"大胜利！{nodeName}危机解除！英雄们成功收容{anomalyClass}，民众终于可以安心了！",
                        "INVESTIGATIVE" => $"收容行动详情：{nodeName}的{anomalyClass}异常已被妥善处置。此次行动展现了专业团队的应对能力。",
                        _ => $"{nodeName}异常收容成功，{anomalyClass}得到控制。"
                    };

                default:
                    return $"{nodeName}发生事件：{fact.Type}";
            }
        }

        /// <summary>
        /// Get node name from state, with fallback.
        /// </summary>
        private static string GetNodeName(GameState state, string nodeId)
        {
            if (state?.Nodes == null || string.IsNullOrEmpty(nodeId))
                return "未知地点";

            var node = state.Nodes.FirstOrDefault(n => n != null && n.Id == nodeId);
            return node?.Name ?? nodeId;
        }
    }
}
