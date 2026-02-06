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
        // Recursion guard to prevent WebGL stack overflow
        private static bool _isGeneratingNews = false;
        
        // Track which facts have been reported by which media
        private static readonly Dictionary<string, HashSet<string>> _reportedByMedia = new();
        
        /// <summary>
        /// Generate news from unreported facts in the game state.
        /// Returns the number of news items generated.
        /// </summary>
        public static int GenerateNewsFromFacts(GameState state, DataRegistry registry, int maxCount = 5)
        {
            if (state?.FactSystem?.Facts == null || state.NewsLog == null)
                return 0;

            // Recursion guard
            if (_isGeneratingNews)
            {
                Debug.LogWarning("[FactNews] Recursion detected, skipping generation");
                return 0;
            }

            try
            {
                _isGeneratingNews = true;
                
                // Get available media profiles
                var mediaProfiles = GetAvailableMediaProfiles(registry);
                if (mediaProfiles == null || mediaProfiles.Count == 0)
                {
                    Debug.LogWarning("[FactNews] No media profiles available");
                    return 0;
                }

                // Get unreported facts ordered by severity (high to low) and day (recent first)
                var unreportedFacts = state.FactSystem.Facts
                    .Where(f => f != null && !IsFullyReported(f, mediaProfiles))
                    .OrderByDescending(f => f.Severity)
                    .ThenByDescending(f => f.Day)
                    .Take(maxCount)
                    .ToList();

                int generated = 0;
                foreach (var fact in unreportedFacts)
                {
                    // Generate news for each media profile
                    foreach (var profile in mediaProfiles)
                    {
                        // Check if this fact has already been reported by this media
                        if (IsReportedByMedia(fact, profile.profileId))
                            continue;
                        
                        // Check limit per media per day
                        int currentMediaCount = state.NewsLog
                            .Count(n => n != null && n.Day == state.Day && n.mediaProfileId == profile.profileId);
                        
                        if (currentMediaCount >= 5) // MaxFactNewsPerDayPerMedia
                            continue;

                        var newsInstance = GenerateNewsFromFact(state, fact, registry, profile);
                        if (newsInstance != null)
                        {
                            state.NewsLog.Add(newsInstance);
                            MarkReportedByMedia(fact, profile.profileId);
                            generated++;
                            
                            Debug.Log($"[FactNews] day={state.Day} factId={fact.FactId} media={profile.profileId} type={fact.Type} newsId={newsInstance.Id} severity={fact.Severity}");
                        }
                    }
                    
                    // Mark fact as fully reported if all media have covered it
                    if (IsFullyReported(fact, mediaProfiles))
                    {
                        fact.Reported = true;
                    }
                }

                return generated;
            }
            finally
            {
                _isGeneratingNews = false;
            }
        }
        
        /// <summary>
        /// Get available media profiles with fallback.
        /// </summary>
        private static List<MediaProfileDef> GetAvailableMediaProfiles(DataRegistry registry)
        {
            if (registry?.MediaProfiles != null && registry.MediaProfiles.Count > 0)
                return registry.MediaProfiles;
            
            return GetDefaultMediaProfiles();
        }
        
        /// <summary>
        /// Check if fact has been reported by specific media.
        /// </summary>
        private static bool IsReportedByMedia(FactInstance fact, string mediaProfileId)
        {
            if (fact == null || string.IsNullOrEmpty(mediaProfileId))
                return false;
            
            if (!_reportedByMedia.TryGetValue(fact.FactId, out var mediaSet))
                return false;
            
            return mediaSet.Contains(mediaProfileId);
        }
        
        /// <summary>
        /// Mark fact as reported by specific media.
        /// </summary>
        private static void MarkReportedByMedia(FactInstance fact, string mediaProfileId)
        {
            if (fact == null || string.IsNullOrEmpty(mediaProfileId))
                return;
            
            if (!_reportedByMedia.TryGetValue(fact.FactId, out var mediaSet))
            {
                mediaSet = new HashSet<string>();
                _reportedByMedia[fact.FactId] = mediaSet;
            }
            
            mediaSet.Add(mediaProfileId);
        }
        
        /// <summary>
        /// Check if fact has been reported by all media.
        /// </summary>
        private static bool IsFullyReported(FactInstance fact, List<MediaProfileDef> mediaProfiles)
        {
            if (fact == null || mediaProfiles == null || mediaProfiles.Count == 0)
                return false;
            
            foreach (var profile in mediaProfiles)
            {
                if (!IsReportedByMedia(fact, profile.profileId))
                    return false;
            }
            
            return true;
        }

        /// <summary>
        /// Convert a single fact to a news instance using templates.
        /// </summary>
        private static NewsInstance GenerateNewsFromFact(GameState state, FactInstance fact, DataRegistry registry, MediaProfileDef profile)
        {
            if (fact == null || profile == null) return null;

            // Generate title and description using templates
            string title = GenerateTitle(fact, profile, state);
            string desc = GenerateDescription(fact, profile, state);

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(desc))
                return null;

            // Create a synthetic news def ID based on fact type and media
            string newsDefId = $"FACT_{fact.Type}_{profile.profileId}_{fact.FactId}";

            var newsInstance = new NewsInstance
            {
                Id = $"NEWS_{Guid.NewGuid():N}",
                NewsDefId = newsDefId,
                NodeId = fact.NodeId,
                SourceAnomalyId = fact.AnomalyId,
                CauseType = "FactTriggered",
                AgeDays = 0,
                Day = state.Day,
                Title = title,
                Description = desc,
                mediaProfileId = profile.profileId
            };

            // Store the generated content in the legacy News list for display
            state.News.Add($"[新闻] {title}");

            return newsInstance;
        }

        /// <summary>
        /// Get default media profiles (fallback when table is empty).
        /// Normally loaded from MediaProfiles table via DataRegistry.
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
        /// Generate news title from fact using templates.
        /// Uses fallback templates to ensure no FACT_... placeholders appear.
        /// Each media profile gets distinct wording.
        /// </summary>
        private static string GenerateTitle(FactInstance fact, MediaProfileDef profile, GameState state)
        {
            string nodeName = GetNodeName(state, fact.NodeId);

            // Map fact types (handle variations)
            string factType = fact.Type;
            
            switch (factType)
            {
                case "AnomalySpawned":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"基金会通报：{nodeName}出现异常活动",
                        "SENSATIONAL" => $"怪事爆发！{nodeName}疑现"异象"",
                        "INVESTIGATIVE" => $"调查线索：{nodeName}异常事件的共同点",
                        _ => $"{nodeName}发现异常现象"
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
                
                case "ContainmentSuccess":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【成功】{nodeName}收容行动成功",
                        "SENSATIONAL" => $"【大捷】{nodeName}威胁已被控制！",
                        "INVESTIGATIVE" => $"【分析】{nodeName}收容行动全程回顾",
                        _ => $"{nodeName}收容成功"
                    };
                
                case "ContainmentFailed":
                case "ContainmentBreach":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【警报】{nodeName}收容失败",
                        "SENSATIONAL" => $"【危机】{nodeName}形势失控！",
                        "INVESTIGATIVE" => $"【检讨】{nodeName}收容失败原因分析",
                        _ => $"{nodeName}收容失败"
                    };

                default:
                    // Generic fallback for unknown fact types
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"【通报】{nodeName}发生事件",
                        "SENSATIONAL" => $"【突发】{nodeName}情况异常！",
                        "INVESTIGATIVE" => $"【关注】{nodeName}事件追踪",
                        _ => $"{nodeName}相关事件"
                    };
            }
        }

        /// <summary>
        /// Generate news description from fact using templates.
        /// Uses fallback templates to ensure no FACT_... placeholders appear.
        /// Each media profile gets distinct wording and tone.
        /// </summary>
        private static string GenerateDescription(FactInstance fact, MediaProfileDef profile, GameState state)
        {
            string nodeName = GetNodeName(state, fact.NodeId);
            object anomalyClass = fact.Payload?.GetValueOrDefault("anomalyClass", "未知");
            
            string factType = fact.Type;
            
            switch (factType)
            {
                case "AnomalySpawned":
                    int severity = fact.Severity;
                    string threat = severity >= 4 ? "极高威胁" : severity >= 3 ? "高度警戒" : "需要关注";
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"基金会已启动标准收容流程。{nodeName}地区出现异常现象，等级：{anomalyClass}。建议公众遵循当地指引，避免传播未经证实信息。",
                        "SENSATIONAL" => $"目击者称现场出现无法解释现象。街头传言四起，相关区域疑似被迅速封锁。{nodeName}情况{threat}！",
                        "INVESTIGATIVE" => $"我们整理了过去记录与本次迹象的重合项。封锁范围与行动节奏与以往案例一致。{nodeName}异常等级：{anomalyClass}。",
                        _ => $"{nodeName}出现异常活动，等级：{anomalyClass}。"
                    };

                case "InvestigateCompleted":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"经过深入调查，{nodeName}异常事件已确认为{anomalyClass}类型。调查组已完成现场取证工作，相关信息将依规公开。",
                        "SENSATIONAL" => $"独家！{nodeName}真相大白！原来是{anomalyClass}在作祟！知情人士透露更多惊人内幕，事态比预想严重！",
                        "INVESTIGATIVE" => $"经本报调查核实，{nodeName}事件确认为{anomalyClass}异常。深度分析显示，此类事件具有典型特征，可能与此前案例相关。",
                        _ => $"{nodeName}调查完成，发现{anomalyClass}异常。"
                    };

                case "InvestigateNoResult":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}的调查工作已告一段落，暂未发现明确异常迹象。相关部门将继续保持关注，必要时重新启动调查。",
                        "SENSATIONAL" => $"谜团重重！{nodeName}调查无果，真相依然扑朔迷离！是否有人刻意隐瞒？民众质疑声四起。",
                        "INVESTIGATIVE" => $"本报跟踪报道：{nodeName}调查尚无定论。专家认为可能需要更多时间和资源深入研究，不排除重新开展调查的可能。",
                        _ => $"{nodeName}调查暂无结果。"
                    };

                case "ContainCompleted":
                    object reward = fact.Payload?.GetValueOrDefault("reward", 0);
                    object relief = fact.Payload?.GetValueOrDefault("panicRelief", 0);
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}异常已成功收容，{anomalyClass}级威胁得到控制。行动获得资金奖励{reward}，社会恐慌指数下降{relief}点。",
                        "SENSATIONAL" => $"大胜利！{nodeName}危机解除！英雄们成功收容{anomalyClass}，民众终于可以安心了！街头庆祝活动持续到深夜。",
                        "INVESTIGATIVE" => $"收容行动详情：{nodeName}的{anomalyClass}异常已被妥善处置。此次行动展现了专业团队的应对能力，值得深入分析。",
                        _ => $"{nodeName}异常收容成功，{anomalyClass}得到控制。"
                    };
                
                case "ContainmentSuccess":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}收容行动成功，异常已被妥善处置。专业团队展现了高水平的应对能力。",
                        "SENSATIONAL" => $"{nodeName}大捷！威胁已被彻底控制，民众可以安心了！现场一片欢腾。",
                        "INVESTIGATIVE" => $"本报全程跟踪{nodeName}收容行动，从部署到执行展现了专业水准，为类似行动提供了宝贵经验。",
                        _ => $"{nodeName}收容成功。"
                    };
                
                case "ContainmentFailed":
                case "ContainmentBreach":
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}收容行动未能成功，异常仍处于活跃状态。相关部门正在制定新的应对方案。",
                        "SENSATIONAL" => $"警报！{nodeName}形势失控，收容失败！民众恐慌情绪急剧升温，当局紧急应对！",
                        "INVESTIGATIVE" => $"深度分析：{nodeName}收容失败的原因可能包括资源不足、情报偏差等因素。我们将持续追踪后续进展。",
                        _ => $"{nodeName}收容失败。"
                    };

                default:
                    // Generic fallback for unknown fact types
                    return profile?.profileId switch
                    {
                        "FORMAL" => $"{nodeName}发生相关事件，具体情况正在核实中。官方将及时通报进展。",
                        "SENSATIONAL" => $"{nodeName}情况异常！现场目击者称事态不明，民众密切关注事态发展！",
                        "INVESTIGATIVE" => $"本报关注{nodeName}事件进展。我们正在收集更多信息，以便为读者提供全面分析。",
                        _ => $"{nodeName}发生事件：{fact.Type}"
                    };
            }
        }

        /// <summary>
        /// Clean up tracking data for pruned facts.
        /// Should be called after PruneFacts to prevent memory leaks.
        /// </summary>
        public static void CleanupPrunedFactTracking(GameState state)
        {
            if (state?.FactSystem?.Facts == null)
                return;
            
            // Get current fact IDs
            var currentFactIds = new HashSet<string>();
            foreach (var fact in state.FactSystem.Facts)
            {
                if (fact != null && !string.IsNullOrEmpty(fact.FactId))
                {
                    currentFactIds.Add(fact.FactId);
                }
            }
            
            // Remove tracking for facts that no longer exist
            var keysToRemove = new List<string>();
            foreach (var key in _reportedByMedia.Keys)
            {
                if (!currentFactIds.Contains(key))
                {
                    keysToRemove.Add(key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _reportedByMedia.Remove(key);
            }
            
            if (keysToRemove.Count > 0)
            {
                Debug.Log($"[FactNews] Cleaned up {keysToRemove.Count} pruned fact tracking entries");
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
