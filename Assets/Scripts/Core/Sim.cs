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
        // Agent Level/Exp helpers
        public static int ExpToNext(int level)
        {
            return 20 + (level - 1) * 10;
        }

        // Returns true if level up occurred
        public static bool AddExpAndTryLevelUp(Core.AgentState a, int addExp, System.Random rng)
        {
            if (addExp > 0)
            {
                Debug.Log($"[AgentLevel] agent={a.Id} lv={a.Level} exp={a.Exp}->{a.Exp + addExp}");
            }
            a.Exp += addExp;
            bool leveled = false;
            while (a.Exp >= ExpToNext(a.Level))
            {
                int oldLv = a.Level;
                a.Exp -= ExpToNext(a.Level);
                a.Level += 1;
                int grow = rng.Next(0, 4);
                string growStr = "";
                switch (grow)
                {
                    case 0:
                        a.Perception += 1;
                        growStr = "Perception+1";
                        break;
                    case 1:
                        a.Resistance += 1;
                        growStr = "Resistance+1";
                        break;
                    case 2:
                        a.Operation += 1;
                        growStr = "Operation+1";
                        break;
                    case 3:
                        a.Power += 1;
                        growStr = "Power+1";
                        break;
                }
                Debug.Log($"[AgentLevel] agent={a.Id} lv={oldLv}->{a.Level} exp={a.Exp} grow={growStr}");
                leveled = true;
            }
            return leveled;
        }

        private const string RequirementAny = "ANY";

        public static void StepDay(GameState s, Random rng)
        {
            var registry = DataRegistry.Instance;

            s.Day += 1;
            if (s.RecruitPool != null)
            {
                s.RecruitPool.day = -1;
                s.RecruitPool.refreshUsedToday = 0;
                s.RecruitPool.candidates?.Clear();
            }

            // Legacy logging limiter: per-anomaly per-day
            var legacyWorkLogged = new HashSet<string>();

            // 1) 推进任务（任务维度：同节点可并行 N 个任务）
            if (!s.UseSettlement_AnomalyWork)
            {
                foreach (var n in s.Cities)
                {
                    if (n == null || n.Type == 0) continue;
                    if (n?.Tasks == null || n.Tasks.Count == 0) continue;

                    // 推进所有 Active 任务
                    for (int i = 0; i < n.Tasks.Count; i++)
                    {
                        var t = n.Tasks[i];
                        if (t == null) continue;
                        if (t.State != TaskState.Active) continue;
                        if (t.AssignedAgentIds == null || t.AssignedAgentIds.Count == 0) continue;

                        var squad = GetAssignedAgents(s, t.AssignedAgentIds);
                        if (squad.Count == 0) continue;

                        if (t.Type == TaskType.Investigate && !t.InvestigateTargetLocked)
                        {
                            if (!string.IsNullOrEmpty(t.SourceAnomalyId))
                            {
                                t.InvestigateNoResultBaseDays = 0;
                                t.InvestigateTargetLocked = true;
                            }
                            else
                            {
                                TryLockGenericInvestigateTarget(s, n, t, squad, registry, rng);
                            }
                        }

                        string anomalyId = GetTaskAnomalyId(n, t);
                        var team = ComputeTeamAvgProps(squad);
                        AnomalyDef anomalyDef = null;
                        if (!string.IsNullOrEmpty(anomalyId))
                        {
                            registry.AnomaliesById.TryGetValue(anomalyId, out anomalyDef);
                        }

                        int[] req = t.Type switch
                        {
                            TaskType.Investigate => NormalizeIntArray4(anomalyDef?.invReq),
                            TaskType.Contain => NormalizeIntArray4(anomalyDef?.conReq),
                            TaskType.Manage => NormalizeIntArray4(anomalyDef?.manReq),
                            _ => new int[4]
                        };

                        float sMatch = ComputeMatchS_NoWeight(team, req);
                        float progressScale = MapSToMult(sMatch);

                        float effDelta = progressScale;

                        // Manage tasks are LONG-RUNNING: progress is only used as a "started" flag (0 vs >0).
                        // They should never auto-complete.
                        if (t.Type == TaskType.Manage)
                        {
                            float beforeManage = t.Progress;
                            t.Progress = Math.Max(t.Progress, 0f);
                            t.Progress = Clamp01(t.Progress + effDelta);
                            if (t.Progress >= 1f) t.Progress = 0.99f;
                            Debug.Log($"[TaskProgress] day={s.Day} taskId={t.Id} type={t.Type} anomalyId={anomalyId ?? "none"} team={FormatFloatArray(team)} req={FormatIntArray(req)} s={sMatch:0.###} scale={progressScale:0.###} effDelta={effDelta:0.00} progress={t.Progress:0.00}/1 (baseDays=1)");

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
                            var manageReq = NormalizeIntArray4(manageDef?.manReq);
                            float magSan = (manageDef?.mansanDmg ?? 0) * impact.sanMul;
                            Debug.Log(
                                $"[ImpactCalc] day={s.Day} type=Manage node={n.Id} anomaly={defId ?? "unknown"} base=({manageDef?.manhpDmg ?? 0},{manageDef?.mansanDmg ?? 0}) " +
                                $"mul=({impact.hpMul:0.###},{impact.sanMul:0.###}) " +
                                $"req={FormatIntArray(manageReq)} team={FormatIntArray(impact.team)} D={impact.D:0.###} S={impact.S:0.###} magSan={magSan:0.###} final=({impact.hpDelta},{impact.sanDelta})");
                            foreach (var agentId in t.AssignedAgentIds)
                            {
                                string reason = $"ManageDaily:node={n.Id},anomaly={defId ?? "unknown"},dayTick={s.Day}";
                                ApplyAgentImpact(s, agentId, 0, impact.sanDelta, reason);
                            }
                            continue;
                        }

                        int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(anomalyDef));
                        if (t.Type == TaskType.Investigate && t.InvestigateTargetLocked && string.IsNullOrEmpty(t.SourceAnomalyId) && t.InvestigateNoResultBaseDays > 0)
                        {
                            requiredDays = t.InvestigateNoResultBaseDays;
                        }

                        // legacy task progress (0..requiredDays) kept for compatibility/logging
                        float before = t.Progress;
                        t.Progress = Mathf.Clamp(t.Progress + effDelta, 0f, requiredDays);

                        // new truth: persist normalized progress (0..1) onto AnomalyState
                        if (!string.IsNullOrEmpty(anomalyId))
                        {
                            var anomalyState = GetOrCreateAnomalyState(s, n, anomalyId);
                            if (anomalyState != null)
                            {
                                float delta01 = effDelta / Mathf.Max(1f, (float)requiredDays);

                                if (t.Type == TaskType.Investigate)
                                {
                                    anomalyState.InvestigateProgress = Mathf.Clamp01(anomalyState.InvestigateProgress + delta01);
                                }
                                else if (t.Type == TaskType.Contain)
                                {
                                    anomalyState.ContainProgress = Mathf.Clamp01(anomalyState.ContainProgress + delta01);
                                }

                                // Legacy work logging (limit to 1 per anomaly per day)
                                try
                                {
                                    string logKey = $"{t.Type}|{anomalyState.Id}|{s.Day}";
                                    if (!legacyWorkLogged.Contains(logKey))
                                    {
                                        if (t.Type == TaskType.Investigate)
                                            Debug.Log($"[Sim][LegacyWork] type=Investigate anom={anomalyState.Id} node={n.Id} delta01={delta01:0.###} after={anomalyState.InvestigateProgress:0.###}");
                                        else if (t.Type == TaskType.Contain)
                                            Debug.Log($"[Sim][LegacyWork] type=Contain anom={anomalyState.Id} node={n.Id} delta01={delta01:0.###} after={anomalyState.ContainProgress:0.###}");

                                        legacyWorkLogged.Add(logKey);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogException(ex);
                                }
                            }
                        }


                        if (t.Type == TaskType.Investigate || t.Type == TaskType.Contain)
                        {
                            var a = !string.IsNullOrEmpty(anomalyId) ? GetOrCreateAnomalyState(s, n, anomalyId) : null;
                            float ap = (a == null) ? -1f : (t.Type == TaskType.Investigate ? a.InvestigateProgress : a.ContainProgress);
                            Debug.Log($"[ReachDbg] day={s.Day} task={t.Id} type={t.Type} anomalyId={anomalyId} taskProg={t.Progress:0.###} requiredDays={requiredDays} anomalyProg01={ap:0.###}");
                        }

                        ApplyDailyTaskImpact(s, n, t, anomalyDef, anomalyId);

                        bool reached = false;

                        Core.AnomalyState anomalyStateForReach = null;
                        if (!string.IsNullOrEmpty(anomalyId))
                            anomalyStateForReach = GetOrCreateAnomalyState(s, n, anomalyId);

                        if (anomalyStateForReach != null)
                        {
                            if (t.Type == TaskType.Investigate)
                                reached = anomalyStateForReach.InvestigateProgress >= 1f;
                            else if (t.Type == TaskType.Contain)
                                reached = anomalyStateForReach.ContainProgress >= 1f;
                        }

                        if (reached)
                        {
                            // When a task reaches completion, immediately recall assigned agents from the
                            // corresponding anomaly roster (generate Recall movement tokens) so UI/dispatch
                            // systems see TravellingToBase/Recall tokens in the same frame.
                            if (anomalyStateForReach != null)
                            {
                                try
                                {
                                    string err;
                                    if (t.Type == TaskType.Investigate)
                                    {
                                        DispatchSystem.TrySetRoster(s, anomalyStateForReach.Id, AssignmentSlot.Investigate, Array.Empty<string>(), out err);
                                        if (!string.IsNullOrEmpty(err))
                                            Debug.LogWarning($"[RecallRoster] Investigate recall failed anomaly={anomalyStateForReach.Id} err={err}");

                                        // Advance anomaly phase if applicable: Investigating -> Containing
                                        if (anomalyStateForReach.Phase == AnomalyPhase.Investigate)
                                            anomalyStateForReach.Phase = AnomalyPhase.Contain;
                                    }
                                    else if (t.Type == TaskType.Contain)
                                    {
                                        DispatchSystem.TrySetRoster(s, anomalyStateForReach.Id, AssignmentSlot.Contain, Array.Empty<string>(), out err);
                                        if (!string.IsNullOrEmpty(err))
                                            Debug.LogWarning($"[RecallRoster] Contain recall failed anomaly={anomalyStateForReach.Id} err={err}");

                                        // Advance anomaly phase: Containing -> Contained
                                        if (anomalyStateForReach.Phase == AnomalyPhase.Contain || anomalyStateForReach.Phase == AnomalyPhase.Investigate)
                                            anomalyStateForReach.Phase = AnomalyPhase.Operate;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogException(ex);
                                }
                            }

                            // Sync legacy task system: ensure no other active tasks for the same anomaly keep agents busy.
                            if (n?.Tasks != null)
                            {
                                for (int ti2 = 0; ti2 < n.Tasks.Count; ti2++)
                                {
                                    var other = n.Tasks[ti2];
                                    if (other == null) continue;
                                    if (other == t) continue; // skip the task we're about to complete
                                    if (other.State != TaskState.Active) continue;

                                    // If the other task targets the same anomaly (by SourceAnomalyId), cancel it to avoid busy residue.
                                    if (!string.IsNullOrEmpty(anomalyId) && string.Equals(other.SourceAnomalyId, anomalyId, StringComparison.OrdinalIgnoreCase)
                                        && (other.Type == TaskType.Investigate || other.Type == TaskType.Contain))
                                    {
                                        if (other.AssignedAgentIds != null) other.AssignedAgentIds.Clear();
                                        other.Progress = 0f;
                                        other.State = TaskState.Cancelled;
                                        Debug.Log($"[LegacySync] day={s.Day} node={n.Id} cancelled task={other.Id} type={other.Type} reason=DuplicateAnomalyCompletion");
                                    }
                                }
                            }

                            CompleteTask(s, n, t, rng, registry);
                        }
                    }
                }

                // 1.5) 收容后管理（负熵产出）
                StepManageTasks(s, rng, registry, legacyWorkLogged);
            }

            // 1.75) Idle agents recover HP/SAN (10% max per day)
            ApplyIdleAgentRecovery(s);


            // 4) 经济 & 世界恐慌（全局）
            float popToMoneyRate = registry.GetBalanceFloatWithWarn("PopToMoneyRate", 0f);
            int wagePerAgentPerDay = registry.GetBalanceIntWithWarn("WagePerAgentPerDay", 0);
            int maintenanceDefault = registry.GetBalanceIntWithWarn("ContainedAnomalyMaintenanceDefault", 0);
            int clampMoneyMin = registry.GetBalanceIntWithWarn("ClampMoneyMin", 0);
            float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);

            int moneyBefore = s.Money;
            int maintenance = 0;
            float worldPanicAdd = 0f;

            foreach (var node in s.Cities)
            {
                if (node == null) continue;

                // Uncontained anomalies on this node.
                bool hasUncontained = node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0;

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

            int totalPopLoss = 0;
            var popLossByNode = new Dictionary<string, int>();

            foreach (var originNode in s.Cities)
            {
                if (originNode == null || originNode.ActiveAnomalyIds == null || originNode.ActiveAnomalyIds.Count == 0)
                    continue;

                foreach (var anomalyId in originNode.ActiveAnomalyIds)
                {
                    if (string.IsNullOrEmpty(anomalyId)) continue;
                    int kill = registry.GetAnomalyIntWithWarn(anomalyId, "actPeopleKill", 0);
                    if (kill <= 0) continue;

                    float range = registry.GetAnomalyFloatWithWarn(anomalyId, "range", 0f);
                    // Track per-anomaly legacy log prints (limit to avoid spam)
                    int printed = 0;
                    const int PrintLimitPerAnom = 3;

                    foreach (var targetNode in s.Cities)
                    {
                        if (targetNode == null || targetNode.Type == 0) continue;
                        if (!IsNodeWithinRange(originNode, targetNode, range)) continue;

                        if (popLossByNode.TryGetValue(targetNode.Id, out var current))
                            popLossByNode[targetNode.Id] = current + kill;
                        else
                            popLossByNode[targetNode.Id] = kill;

                        // Legacy logging: limited per anomaly
                        if (printed < PrintLimitPerAnom && !s.UseSettlement_AnomalyCityPop)
                        {
                            Debug.Log($"[Sim][LegacyPop] anom={anomalyId} city={targetNode.Id} deltaPop={kill}");
                            printed++;
                        }
                    }
                }
            }

            // Only perform legacy population deduction when flag is disabled
            if (!s.UseSettlement_AnomalyCityPop)
            {
                foreach (var node in s.Cities)
                {
                    if (node == null) continue;
                    if (!popLossByNode.TryGetValue(node.Id, out var popLoss) || popLoss <= 0) continue;

                    int before = node.Population;
                    node.Population = Mathf.Max(0, before - popLoss);
                    int applied = before - node.Population;
                    if (applied > 0)
                    {
                        totalPopLoss += applied;
                        Debug.Log($"[AnomalyPopLoss] day={s.Day} node={node.Id} loss={applied} before={before} after={node.Population}");
                    }
                }

                if (totalPopLoss > 0)
                {
                    Debug.Log($"[AnomalyPopLossTotal] day={s.Day} totalLoss={totalPopLoss}");
                }
            }

            int income = 0;
            foreach (var node in s.Cities)
            {
                if (node == null) continue;
                income += Mathf.FloorToInt(node.Population * popToMoneyRate);
            }

            int wage = (s.Agents?.Count ?? 0) * wagePerAgentPerDay;
            int optionCost = 0; // TODO: hook event option costs if/when EffectOps expose them.
            int moneyAfter = moneyBefore + income - wage - maintenance - optionCost;
            if (moneyAfter < clampMoneyMin) moneyAfter = clampMoneyMin;
            s.Money = moneyAfter;

            Debug.Log($"[Economy] day={s.Day} income={income} wage={wage} maint={maintenance} option={optionCost} moneyBefore={moneyBefore} moneyAfter={moneyAfter}");

            float worldPanicBefore = s.WorldPanic;
            float worldPanicAfter = worldPanicBefore + worldPanicAdd;
            if (worldPanicAfter < clampWorldPanicMin) worldPanicAfter = clampWorldPanicMin;
            s.WorldPanic = worldPanicAfter;

            float failThreshold = registry.GetBalanceFloatWithWarn("WorldPanicFailThreshold", 0f);
            Debug.Log($"[WorldPanic] day={s.Day} add={worldPanicAdd:0.##} before={worldPanicBefore:0.##} after={worldPanicAfter:0.##} threshold={failThreshold:0.##}");

            if (s.WorldPanic >= failThreshold && GameController.I != null)
            {
                GameController.I.MarkGameOver($"reason=WorldPanic day={s.Day} value={s.WorldPanic:0.##} threshold={failThreshold:0.##}");
            }

            // 5) 异常生成（按 AnomaliesGen 表调度）
            if (!s.UseSettlement_Pipeline)
            {
                GenerateScheduledAnomalies(s, rng, registry, s.Day);
            }
        }

        /// <summary>
        /// Public wrapper to generate scheduled anomalies for the current day using the provided RNG.
        /// This exists so callers outside Sim.StepDay can control when anomaly generation happens
        /// (e.g. after settlement pipeline completes).
        /// </summary>
        public static void GenerateScheduledAnomalies_Public(GameState s, System.Random rng)
        {
            var registry = Data.DataRegistry.Instance;
            if (s == null || registry == null || rng == null) return;
            GenerateScheduledAnomalies(s, rng, registry, s.Day);
        }

        // Advance day only: increment day counter and perform lightweight per-day initializations
        // (used by pipeline path where full Sim.StepDay is executed via settlement systems)
        public static void AdvanceDay_Only(GameState s)
        {
            if (s == null) return;
            s.Day += 1;

            if (s.RecruitPool != null)
            {
                s.RecruitPool.day = -1;
                s.RecruitPool.refreshUsedToday = 0;
                s.RecruitPool.candidates?.Clear();
            }
        }


        // =====================
        // Task completion rules
        // =====================

        static void CompleteTask(GameState s, CityState node, NodeTask task, Random rng, DataRegistry registry)
        {
            string baseAnomalyId = GetTaskAnomalyId(node, task);
            AnomalyDef baseAnomalyDef = null;
            if (!string.IsNullOrEmpty(baseAnomalyId))
            {
                registry.AnomaliesById.TryGetValue(baseAnomalyId, out baseAnomalyDef);
            }
            int baseDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(baseAnomalyDef));
            if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            {
                baseDays = task.InvestigateNoResultBaseDays;
            }
            task.Progress = baseDays;
            task.State = TaskState.Completed;
            task.CompletedDay = s.Day;

            if (!string.IsNullOrEmpty(baseAnomalyId))
            {
                var anomalyState = GetOrCreateAnomalyState(s, node, baseAnomalyId);
                if (anomalyState != null)
                {
                    if (task.Type == TaskType.Investigate)
                        anomalyState.IsKnown = true;
                    if (task.Type == TaskType.Contain)
                        anomalyState.IsContained = true;
                }
            }

            // Store assigned agents before clearing for HP/SAN impact
            var assignedAgents = task.AssignedAgentIds != null ? new List<string>(task.AssignedAgentIds) : new List<string>();

            // Release squad
            if (task.AssignedAgentIds != null) task.AssignedAgentIds.Clear();

            if (task.Type == TaskType.Investigate)
            {
                // 只记录已知 anomalyDefId，不再产出 ContainableItem
                bool hasTarget = !string.IsNullOrEmpty(task.SourceAnomalyId);
                string anomalyId = hasTarget ? task.SourceAnomalyId : null;
                var anomaly = hasTarget && registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level = Math.Max(1, node.AnomalyLevel);

                // 调查完成不会自动收容
                node.Status = NodeStatus.Calm;
                node.HasAnomaly = true;

                if (hasTarget)
                {
                    bool added = AddKnown(node, task.SourceAnomalyId);
                    Debug.Log($"[AnomalyDiscovered] day={s.Day} nodeId={node.Id} anomalyDefId={task.SourceAnomalyId} via=InvestigateTarget added={(added ? 1 : 0)}");

                }
                else
                {
                    Debug.Log("no target");
                }

                // ===== EXP Reward for Investigate =====
                var defId = !string.IsNullOrEmpty(task.SourceAnomalyId) ? task.SourceAnomalyId : anomalyId;
                var def = !string.IsNullOrEmpty(defId) && registry.AnomaliesById.TryGetValue(defId, out var defModel) ? defModel : null;
                if (assignedAgents.Count > 0)
                {
                    int totalExp = def?.invExp ?? 0;
                    int perAgentExp = (int)Math.Ceiling(totalExp / (double)assignedAgents.Count);
                    if (totalExp > 0 && perAgentExp > 0)
                    {
                        foreach (var agentId in assignedAgents)
                        {
                            var a = s.Agents?.FirstOrDefault(x => x != null && x.Id == agentId);
                            if (a == null) continue;
                            int expBefore = a.Exp;
                            int lvBefore = a.Level;
                            AddExpAndTryLevelUp(a, perAgentExp, rng);
                            Debug.Log($"[AgentExp] day={s.Day} type=Investigate node={node.Id} anomaly={defId ?? "unknown"} agent={a.Id} +exp={perAgentExp} exp={expBefore}->{a.Exp} lv={lvBefore}->{a.Level}");
                        }
                    }
                }
            }
            else if (task.Type == TaskType.Contain)
            {
                // 只用 anomalyId 进行收容
                string anomalyId = !string.IsNullOrEmpty(task.SourceAnomalyId)
                    ? task.SourceAnomalyId
                    : GetOrCreateAnomalyForNode(s, node, registry, rng);
                var anomaly = registry.AnomaliesById.TryGetValue(anomalyId, out var anomalyDef) ? anomalyDef : null;
                int level =  Math.Max(1, node.AnomalyLevel);
                int reward = 200 + 50 * level;

                s.Money += reward;
                int relief = registry.GetBalanceIntWithWarn("ContainReliefFixed", 0);
                float clampWorldPanicMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);
                float beforePanic = s.WorldPanic;
                s.WorldPanic = Math.Max(clampWorldPanicMin, s.WorldPanic - relief);

                Debug.Log($"[WorldPanic] day={s.Day} source=ContainComplete relief={relief} before={beforePanic:0.##} after={s.WorldPanic:0.##}");

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
                    if (!string.IsNullOrEmpty(anomalyId))
                    {
                        node.ActiveAnomalyIds?.Remove(anomalyId);
                    }

                    node.HasAnomaly = node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0;
                    node.Status = node.HasAnomaly || hasActiveInvestigateWithSquad ? NodeStatus.Calm : NodeStatus.Secured;
                }

                // ===== EXP Reward for Contain =====
                var defId = task.SourceAnomalyId;
                var def = !string.IsNullOrEmpty(defId) && registry.AnomaliesById.TryGetValue(defId, out var defModel) ? defModel : null;
                if (assignedAgents.Count > 0)
                {
                    int totalExp = def?.conExp ?? 0;
                    int perAgentExp = (int)Math.Ceiling(totalExp / (double)assignedAgents.Count);
                    if (totalExp > 0 && perAgentExp > 0)
                    {
                        foreach (var agentId in assignedAgents)
                        {
                            var a = s.Agents?.FirstOrDefault(x => x != null && x.Id == agentId);
                            if (a == null) continue;
                            int expBefore = a.Exp;
                            int lvBefore = a.Level;
                            AddExpAndTryLevelUp(a, perAgentExp, rng);
                            Debug.Log($"[AgentExp] day={s.Day} type=Contain node={node.Id} anomaly={defId ?? "unknown"} agent={a.Id} +exp={perAgentExp} exp={expBefore}->{a.Exp} lv={lvBefore}->{a.Level}");
                        }
                    }
                }
            }
            node.Status = NodeStatus.Calm;
            node.HasAnomaly = true;
        }

        private static (int hpDelta, int sanDelta, float D, float S, int[] team, float hpMul, float sanMul) ComputeImpact(GameState state, TaskType type, AnomalyDef def, List<string> agentIds)
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

            float hpMul = ComputeAbilityDamageMod(team, req);
            float sanMul = hpMul;
            int hpDelta = 0;
            int sanDelta = 0;
            if (def != null)
            {
                int baseHp = type switch
                {
                    TaskType.Investigate => def.invhpDmg,
                    TaskType.Contain => def.conhpDmg,
                    TaskType.Manage => def.manhpDmg,
                    _ => 0,
                };
                int baseSan = type switch
                {
                    TaskType.Investigate => def.invsanDmg,
                    TaskType.Contain => def.consanDmg,
                    TaskType.Manage => def.mansanDmg,
                    _ => 0,
                };

                if (baseHp > 0)
                {
                    int hpLoss = Mathf.CeilToInt(baseHp * hpMul);
                    if (hpLoss < 1) hpLoss = 1;
                    hpDelta = -hpLoss;
                }

                if (baseSan > 0)
                {
                    int sanLoss = Mathf.CeilToInt(baseSan * sanMul);
                    if (sanLoss < 1) sanLoss = 1;
                    sanDelta = -sanLoss;
                }
            }

            return (hpDelta, sanDelta, D, S, team, hpMul, sanMul);
        }

        private static float ComputeAbilityDamageMod(int[] team, int[] req)
        {
            if (team == null || req == null) return 1f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < 4; i++)
            {
                if (req[i] <= 0) continue;
                int diff = team[i] - req[i];
                float mod = diff >= 0 ? 1f - 0.1f * diff : 1f + 0.1f * (-diff);
                if (mod < 0.1f) mod = 0.1f;
                sum += mod;
                count += 1;
            }

            if (count <= 0) return 1f;
            return sum / count;
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

        private static string FormatFloatArray(float[] values)
        {
            if (values == null) return "null";
            return $"[{string.Join(",", values.Select(v => v.ToString("0.###")))}]";
        }

        // =====================
        // Helpers
        // =====================

        private static bool IsNodeWithinRange(CityState origin, CityState target, float range)
        {
            if (origin == null || target == null) return false;
            if (range <= 0f)
                return string.Equals(origin.Id, target.Id, StringComparison.OrdinalIgnoreCase);

            var originPos = ResolveNodeLocation01(origin);
            var targetPos = ResolveNodeLocation01(target);
            return Vector2.Distance(originPos, targetPos) <= range;
        }

        // Calculate how many population to deduct for a given anomaly instance and target city
        // This is a pure function: it does NOT modify state.
        public static int CalcAnomalyCityPopDelta(GameState state, AnomalyState anom, CityState city)
        {
            if (state == null || anom == null || city == null) return 0;

            var registry = DataRegistry.Instance;
            if (registry == null) return 0;

            // Legacy field: actPeopleKill (per-anomaly flat kill value)
            int kill = registry.GetAnomalyIntWithWarn(anom.AnomalyDefId, "actPeopleKill", 0);
            if (kill <= 0) return 0;

            // Try to locate origin node by anomaly's NodeId
            CityState origin = null;
            if (!string.IsNullOrEmpty(anom.NodeId) && state.Cities != null)
                origin = state.Cities.FirstOrDefault(n => n != null && string.Equals(n.Id, anom.NodeId, StringComparison.OrdinalIgnoreCase));

            // If origin not found, we can't determine range; assume not affected
            if (origin == null) return 0;

            float range = registry.GetAnomalyFloatWithWarn(anom.AnomalyDefId, "range", 0f);
            if (!IsNodeWithinRange(origin, city, range)) return 0;

            return Math.Max(0, kill);
        }

        // Overload: compute delta when only anomaly def id and origin node are available (legacy Sim usage)
        public static int CalcAnomalyCityPopDelta(GameState state, string anomalyDefId, CityState originNode, CityState city)
        {
            if (state == null || string.IsNullOrEmpty(anomalyDefId) || originNode == null || city == null) return 0;
            var registry = DataRegistry.Instance;
            if (registry == null) return 0;
            int kill = registry.GetAnomalyIntWithWarn(anomalyDefId, "actPeopleKill", 0);
            if (kill <= 0) return 0;
            float range = registry.GetAnomalyFloatWithWarn(anomalyDefId, "range", 0f);
            if (!IsNodeWithinRange(originNode, city, range)) return 0;
            return Math.Max(0, kill);
        }

        private static Vector2 ResolveNodeLocation01(CityState node)
        {
            if (node?.Location != null && node.Location.Length >= 2)
                return new Vector2(node.Location[0], node.Location[1]);

            if (node != null && node.Type == 0 && Mathf.Abs(node.X) < 0.0001f && Mathf.Abs(node.Y) < 0.0001f)
                return new Vector2(0.5f, 0.5f);

            return node != null ? new Vector2(node.X, node.Y) : new Vector2(0.5f, 0.5f);
        }

        static List<AgentState> GetAssignedAgents(GameState s, List<string> assignedIds)
        {
            if (assignedIds == null || assignedIds.Count == 0) return new List<AgentState>();
            return s.Agents.Where(a => a != null && assignedIds.Contains(a.Id)).ToList();
        }

        private static float[] ComputeTeamAvgProps(List<AgentState> members)
        {
            if (members == null || members.Count == 0)
            {
                Debug.LogWarning("[TeamAvg] Empty members list. Using [0,0,0,0] to avoid divide-by-zero.");
                return new[] { 0f, 0f, 0f, 0f };
            }

            float p = 0f, r = 0f, o = 0f, pow = 0f;
            int count = 0;

            foreach (var m in members)
            {
                if (m == null) continue;
                p += m.Perception;
                r += m.Resistance;
                o += m.Operation;
                pow += m.Power;
                count += 1;
            }

            if (count <= 0)
            {
                Debug.LogWarning("[TeamAvg] All members were null. Using [0,0,0,0] to avoid divide-by-zero.");
                return new[] { 0f, 0f, 0f, 0f };
            }

            return new[] { p, r, o, pow };
        }

        private static void TryLockGenericInvestigateTarget(GameState s, CityState node, NodeTask task, List<AgentState> squad, DataRegistry registry, Random rng)
        {
            if (task == null || node == null || squad == null || registry == null || rng == null) return;

            int teamPerception = 0;
            foreach (var agent in squad)
            {
                if (agent == null) continue;
                teamPerception += agent.Perception;
            }

            var candidates = new List<(string id, float p)>();
            var active = node.ActiveAnomalyIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (active != null)
            {
                foreach (var anomalyId in active)
                {
                    if (!registry.AnomaliesById.TryGetValue(anomalyId, out var def) || def == null) continue;
                    int req = (def.invReq != null && def.invReq.Length > 0) ? def.invReq[0] : 0;
                    float p = CalcInvestigateDetectProb(teamPerception, req);
                    candidates.Add((anomalyId, p));
                }
            }

            candidates = candidates.OrderByDescending(c => c.p).ToList();
            foreach (var candidate in candidates)
            {
                if (candidate.p <= 0f) continue;
                double roll = rng.NextDouble();
                if (roll <= candidate.p)
                {
                    task.SourceAnomalyId = candidate.id;
                    task.InvestigateNoResultBaseDays = 0;
                    task.InvestigateTargetLocked = true;
                    Debug.Log($"[InvestigateTarget] day={s.Day} taskId={task.Id} node={node.Id} anomaly={candidate.id} teamP={teamPerception} reqP={(registry.AnomaliesById.TryGetValue(candidate.id, out var def) ? (def.invReq != null && def.invReq.Length > 0 ? def.invReq[0] : 0) : 0)} p={candidate.p:0.##} roll={roll:0.##} result=lock");
                    return;
                }
            }

            task.InvestigateNoResultBaseDays = rng.Next(2, 6);
            task.InvestigateTargetLocked = true;
            Debug.Log($"[InvestigateTarget] day={s.Day} taskId={task.Id} node={node.Id} teamP={teamPerception} result=none baseDays={task.InvestigateNoResultBaseDays}");
        }

        private static float CalcInvestigateDetectProb(int teamPerception, int reqPerception)
        {
            if (reqPerception <= 0) return 1f;
            float ratio = teamPerception / (float)reqPerception;
            float p = 0.5f * ratio;
            return Mathf.Clamp01(p);
        }

        private static float ComputeMatchS_NoWeight(float[] team, int[] req)
        {
            if (team == null || req == null) return 1f;

            float minR = float.PositiveInfinity;
            float sumR = 0f;
            int count = 0;

            for (int i = 0; i < 4; i++)
            {
                if (req[i] <= 0) continue;
                float ratio = req[i] > 0 ? team[i] / req[i] : 0f;
                ratio = Mathf.Clamp(ratio, 0f, 2f);
                if (ratio < minR) minR = ratio;
                sumR += ratio;
                count += 1;
            }

            if (count <= 0) return 1f;

            float avgR = sumR / count;
            return 0.5f * minR + 0.5f * avgR;
        }

        private static float MapSToMult(float s)
        {
            if (s < 0.7f) return 0.3f;
            if (s < 1.0f) return Mathf.Lerp(0.3f, 1.0f, (s - 0.7f) / 0.3f);
            if (s < 1.3f) return Mathf.Lerp(1.0f, 1.6f, (s - 1.0f) / 0.3f);
            return 1.6f;
        }

        private static int GetTaskBaseDaysFromAnomaly(AnomalyDef anomalyDef)
        {
            int baseDays = anomalyDef?.baseDays ?? 0;
            return baseDays > 0 ? baseDays : 1;
        }

        static string GetTaskAnomalyId(CityState node, NodeTask task)
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

            if (task.Type == TaskType.Investigate)
            {
                if (task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId))
                    return null;
                if (!string.IsNullOrEmpty(task.SourceAnomalyId))
                    return task.SourceAnomalyId;
            }

            return node.ActiveAnomalyIds?.FirstOrDefault(id => !string.IsNullOrEmpty(id));
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

            bool diedNow = false;
            bool insaneNow = false;
            if (agent.HP <= 0 && !agent.IsDead)
            {
                agent.IsDead = true;
                agent.IsInsane = false;
                diedNow = true;
            }
            if (!agent.IsDead && agent.SAN <= 0 && !agent.IsInsane)
            {
                agent.IsInsane = true;
                insaneNow = true;
            }

            if (diedNow || insaneNow)
            {
                HandleAgentUnusable(s, agent.Id, diedNow ? "Dead" : "Insane", reason);
            }
        }

        private static void HandleAgentUnusable(GameState s, string agentId, string state, string reason)
        {
            if (s == null || string.IsNullOrEmpty(agentId)) return;

            foreach (var node in s.Cities)
            {
                if (node?.Tasks == null) continue;
                foreach (var task in node.Tasks)
                {
                    if (task == null || task.State != TaskState.Active) continue;
                    if (task.AssignedAgentIds == null) continue;

                    if (task.AssignedAgentIds.Contains(agentId))
                    {
                        task.AssignedAgentIds.Remove(agentId);
                        Debug.Log($"[TaskAgentRemoved] day={s.Day} taskId={task.Id} agent={agentId} state={state} reason={reason}");
                    }

                    if (task.AssignedAgentIds.Count == 0)
                    {
                        task.State = TaskState.Cancelled;
                        task.Progress = 0f;
                        Debug.Log($"[TaskFailed] day={s.Day} taskId={task.Id} reason=NoUsableAgents");
                    }
                }
            }
        }

        // =====================
        // Management (NegEntropy) - formalized as NodeTask.Manage
        // =====================

        static void StepManageTasks(GameState s, Random rng, DataRegistry registry, HashSet<string> legacyWorkLogged)
        {
            if (s == null || s.Cities == null || s.Cities.Count == 0) return;

            int totalAllNodes = 0;

            foreach (var node in s.Cities)
            {
                if (node == null || node.Type == 0) continue;
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

                    string defId = t.SourceAnomalyId;
                    AnomalyDef manageDef = null;
                    if (!string.IsNullOrEmpty(defId))
                    {
                        registry.AnomaliesById.TryGetValue(defId, out manageDef);
                    }
                    if ( manageDef == null) continue;

                    int yield = CalcDailyNegEntropyYield(m, manageDef);
                    if (yield <= 0) continue;

                    nodeTotal += yield;
                    m.TotalNegEntropy += yield;

                    // Legacy pure-calc logging: per-managed-anomaly per-day (limit)
                    try
                    {
                        string logKey = $"Manage|{m.Id}|{s.Day}";
                        if (legacyWorkLogged == null || !legacyWorkLogged.Contains(logKey))
                        {
                            int baseDays = 0;
                            if (!string.IsNullOrEmpty(defId) && registry.AnomaliesById.TryGetValue(defId, out var defModel))
                            {
                                baseDays = GetTaskBaseDaysFromAnomaly(defModel);
                            }
                            Debug.Log($"[Sim][LegacyWork] type=Manage anom={m.Id} node={node.Id} deltaNegEntropy={yield} afterNegEntropy={m.TotalNegEntropy}");
                            legacyWorkLogged?.Add(logKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }

                    if (squad.Count > 0)
                    {
                        int totalExp = manageDef.manExpPerDay;
                        if (totalExp > 0)
                        {
                            int perAgentExp = (int)Math.Ceiling(totalExp / (double)squad.Count);
                            if (totalExp > 0 && perAgentExp > 0)
                            {
                                foreach (var a in squad)
                                {
                                    if (a == null) continue;
                                    int expBefore = a.Exp;
                                    int lvBefore = a.Level;
                                    AddExpAndTryLevelUp(a, perAgentExp, rng);
                                    Debug.Log($"[AgentExp] day={s.Day} type=Manage node={node.Id} anomaly={defId ?? "unknown"} agent={a.Id} +exp={perAgentExp} exp={expBefore}->{a.Exp} lv={lvBefore}->{a.Level}");
                                }
                            }
                        }
                    }
                }

                if (nodeTotal > 0)
                {
                    totalAllNodes += nodeTotal;
                }

                if (node.Status == NodeStatus.Secured && nodeTotal > 0)
                {
                    // RandomDaily handles per-day event generation.
                }
            }

            if (totalAllNodes > 0)
                s.NegEntropy += totalAllNodes;
        }

        private static void ApplyDailyTaskImpact(GameState s, CityState node, NodeTask task, AnomalyDef anomalyDef, string anomalyId)
        {
            if (s == null || node == null || task == null || anomalyDef == null) return;
            if (task.Type != TaskType.Investigate && task.Type != TaskType.Contain) return;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) return;

            var impact = ComputeImpact(s, task.Type, anomalyDef, task.AssignedAgentIds);
            var dailyReq = task.Type == TaskType.Investigate
                ? NormalizeIntArray4(anomalyDef?.invReq)
                : NormalizeIntArray4(anomalyDef?.conReq);
            float magSan = (task.Type == TaskType.Investigate ? (anomalyDef?.invsanDmg ?? 0) : (anomalyDef?.consanDmg ?? 0)) * impact.sanMul;
            Debug.Log(
                $"[ImpactCalc] day={s.Day} type={task.Type} ctx=Daily node={node.Id} anomaly={anomalyId ?? "unknown"} base=({(task.Type == TaskType.Investigate ? (anomalyDef?.invhpDmg ?? 0) : (anomalyDef?.conhpDmg ?? 0))},{(task.Type == TaskType.Investigate ? (anomalyDef?.invsanDmg ?? 0) : (anomalyDef?.consanDmg ?? 0))}) " +
                $"mul=({impact.hpMul:0.###},{impact.sanMul:0.###}) " +
                $"req={FormatIntArray(dailyReq)} team={FormatIntArray(impact.team)} D={impact.D:0.###} S={impact.S:0.###} magSan={magSan:0.###} final=({impact.hpDelta},{impact.sanDelta})");

            foreach (var agentId in task.AssignedAgentIds)
            {
                string reason = $"{task.Type}Daily:node={node.Id},anomaly={anomalyId ?? "unknown"},dayTick={s.Day}";
                ApplyAgentImpact(s, agentId, impact.hpDelta, impact.sanDelta, reason);
            }
        }

        static int CalcDailyNegEntropyYield(ManagedAnomalyState m, AnomalyDef def)
        {
            if (def == null) return 0;
            return Math.Max(0, def.manNegentropyPerDay);
        }
        private static int CalcDailyNegEntropyYield(AnomalyDef def)
        {
            if (def == null) return 0;
            return Math.Max(0, def.manNegentropyPerDay);
        }



        private static void ApplyIdleAgentRecovery(GameState s)
        {
            if (s?.Agents == null || s.Cities == null) return;

            var busy = new HashSet<string>();
            foreach (var node in s.Cities)
            {
                if (node?.Tasks == null) continue;
                foreach (var task in node.Tasks)
                {
                    if (task == null || task.State != TaskState.Active) continue;
                    if (task.AssignedAgentIds == null) continue;
                    foreach (var agentId in task.AssignedAgentIds)
                    {
                        if (!string.IsNullOrEmpty(agentId)) busy.Add(agentId);
                    }
                }
            }

            foreach (var agent in s.Agents)
            {
                if (agent == null) continue;
                if (agent.IsDead || agent.IsInsane) continue;
                if (busy.Contains(agent.Id)) continue;

                int hpHeal = Mathf.CeilToInt(agent.MaxHP * 0.1f);
                int sanHeal = Mathf.CeilToInt(agent.MaxSAN * 0.1f);
                if (hpHeal <= 0 && sanHeal <= 0) continue;

                ApplyAgentImpact(s, agent.Id, hpHeal, sanHeal, "IdleRecovery");
            }
        }

        static void EnsureManagedAnomalyRecorded(CityState node, string anomalyId, AnomalyDef anomaly)
        {
            if (node.ManagedAnomalies == null) node.ManagedAnomalies = new List<ManagedAnomalyState>();
            if (string.IsNullOrEmpty(anomalyId)) return;

            var existing = node.ManagedAnomalies.FirstOrDefault(m => m != null && m.AnomalyId == anomalyId);
            if (existing != null)
            {
                existing.Level = Math.Max(existing.Level, 1);
                if (!string.IsNullOrEmpty(anomaly?.@class)) existing.AnomalyClass = anomaly.@class;
                return;
            }

            node.ManagedAnomalies.Add(new ManagedAnomalyState
            {
                Id = $"MANAGED_{anomalyId}_{Guid.NewGuid().ToString("N")[..6]}",
                Name = anomaly != null ? anomaly.name : $"已收容异常（{node.Name}）",
                Level = 1,
                AnomalyId = anomalyId,
                AnomalyClass = anomaly?.@class,
                Favorited = true,
                StartDay = 0,
                TotalNegEntropy = 0,
            });
        }

        private static bool TryGetOriginTask(GameState s, string taskId, out CityState node)
        {
            node = null;
            if (string.IsNullOrEmpty(taskId) || s?.Cities == null) return false;

            foreach (var n in s.Cities)
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

        private static void EnsureActiveAnomaly(GameState state, CityState node, string anomalyId, DataRegistry registry)
        {
            if (node.ActiveAnomalyIds == null) node.ActiveAnomalyIds = new List<string>();
            if (!node.ActiveAnomalyIds.Contains(anomalyId)) node.ActiveAnomalyIds.Add(anomalyId);
            node.HasAnomaly = node.ActiveAnomalyIds.Count > 0;

            if (registry.AnomaliesById.TryGetValue(anomalyId, out var anomaly))
            {
                node.AnomalyLevel = Math.Max(node.AnomalyLevel, 1);
            }

            var anomalyState = GetOrCreateAnomalyState(state, node, anomalyId);
            if (anomalyState != null && string.IsNullOrEmpty(anomalyState.NodeId))
                anomalyState.NodeId = node.Id;
        }

        private static AnomalyState GetOrCreateAnomalyState(GameState state, CityState node, string anomalyId)
        {
            if (state == null || string.IsNullOrEmpty(anomalyId)) return null;
            state.Anomalies ??= new List<AnomalyState>();

            var existing = state.Anomalies.FirstOrDefault(a => a != null && string.Equals(a.AnomalyDefId, anomalyId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (node != null && string.IsNullOrEmpty(existing.NodeId))
                    existing.NodeId = node.Id;
                return existing;
            }

            var created = new AnomalyState
            {
                Id = "AN_STATE_" + Guid.NewGuid().ToString("N")[..8],
                AnomalyDefId = anomalyId,
                NodeId = node?.Id,
                SpawnDay = state.Day,
            };
            // S1: record spawn sequence for deterministic ordering of anomaly actions
            try
            {
                created.SpawnSeq = state.NextAnomalySpawnSeq++;
            }
            catch
            {
                // In case state or NextAnomalySpawnSeq is null/uninitialized, fall back to 0
                created.SpawnSeq = 0;
                if (state != null) state.NextAnomalySpawnSeq = (state.NextAnomalySpawnSeq >= 0) ? state.NextAnomalySpawnSeq : 0;
            }
            state.Anomalies.Add(created);
            return created;
        }

        private static string GetOrCreateAnomalyForNode(GameState state, CityState node, DataRegistry registry, Random rng)
        {
            if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Count > 0)
                return node.ActiveAnomalyIds[0];

            var anomalyId = PickRandomAnomalyId(registry, rng);
            if (!string.IsNullOrEmpty(anomalyId))
            {
                EnsureActiveAnomaly(state, node, anomalyId, registry);
                return anomalyId;
            }

            return null;
        }

        /// <summary>
        /// Picks a random anomaly ID from the registry that is not currently active or managed in the game state.
        /// </summary>
        private static string PickRandomAnomalyId(DataRegistry registry, Random rng)
        {
            var all = registry.AnomaliesById.Keys.ToList();
            if (all.Count == 0) return null;
            int idx = rng.Next(all.Count);
            return all[idx];
        }

        public static int GenerateScheduledAnomalies(GameState s, Random rng, DataRegistry registry, int day)
        {
            if (s == null || rng == null || registry == null) return 0;

            int genNum = registry.GetAnomaliesGenNumForDay(day);
            if (genNum <= 0) return 0;

            var nodes = s.Cities?.Where(n => n != null).ToList();
            if (nodes == null || nodes.Count == 0) return 0;
            nodes = nodes.Where(n => n != null && n.Type != 0 && n.Unlocked).ToList();
            if (nodes.Count == 0) return 0;

            int spawned = 0;
            int maxAttempts = Math.Max(10, genNum * 4);
            int attempts = 0;

            while (spawned < genNum && attempts < maxAttempts)
            {
                attempts++;
                var anomalyId = PickRandomAnomalyId(registry, rng);
                if (string.IsNullOrEmpty(anomalyId)) break;

                bool alreadySpawned = s.Cities.Any(n =>
                    n != null &&
                    ((n.ActiveAnomalyIds != null && n.ActiveAnomalyIds.Contains(anomalyId)) ||
                     (n.ManagedAnomalies != null && n.ManagedAnomalies.Any(m => m != null && m.AnomalyId == anomalyId)) ||
                     (n.KnownAnomalyDefIds != null && n.KnownAnomalyDefIds.Contains(anomalyId))));
                if (alreadySpawned)
                    continue;

                var node = nodes[rng.Next(nodes.Count)];
                if (node == null) continue;

                if (node.ActiveAnomalyIds != null && node.ActiveAnomalyIds.Contains(anomalyId))
                    continue;

                EnsureActiveAnomaly(s, node, anomalyId, registry);
                GetOrCreateAnomalyState(s, node, anomalyId);
                spawned++;

                // Emit fact for anomaly spawn
                var anomalyDef = registry.AnomaliesById.GetValueOrDefault(anomalyId);
            }

            if (spawned < genNum)
            {
                Debug.LogWarning($"[AnomalyGen] day={day} requested={genNum} spawned={spawned} attempts={attempts}");
            }
            else
            {
                Debug.Log($"[AnomalyGen] day={day} spawned={spawned}");
            }

            return spawned;
        }

        // =====================
        // Anomaly discovery helper
        // =====================

        private static bool AddKnown(CityState node, string anomalyDefId)
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
            if (state != null && state.UseSettlement_Pipeline)
            {
                var a = state.Agents?.FirstOrDefault(x => x != null && x.Id == agentId);
                if (a == null) return "";
                switch (a.LocationKind)
                {
                    case AgentLocationKind.TravellingToAnomaly: return "在途";
                    case AgentLocationKind.TravellingToBase: return "返程";
                    case AgentLocationKind.AtAnomaly: return "执行中";
                    default: return "";
                }
            }



            if (state?.Cities == null || string.IsNullOrEmpty(agentId))
                return string.Empty;

            var registry = DataRegistry.Instance;

            var agent = state.Agents?.FirstOrDefault(a => a != null && a.Id == agentId);
            if (agent != null)
            {
                if (agent.IsDead) return "死亡";
                if (agent.IsInsane) return "疯狂";
            }

            // Traverse all nodes and tasks to find where this agent is assigned
            foreach (var node in state.Cities)
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
                            busyText = $"在{node.Name}调查";
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
        // Fact System
        // =====================

        /// <summary>
        /// Calculate fact severity from anomaly threat level.
        /// Severity is clamped to 1-5 range.
        /// </summary>
        private static int CalculateSeverityFromThreatLevel(int threatLevel)
        {
            int severity = threatLevel / 2;
            return Math.Min(5, Math.Max(1, severity));
        }


      

        // =====================
        // Math helpers
        // =====================

        static float Clamp01(float v)
        {
            return Mathf.Clamp01(v);
        }

        // Pure legacy calculation: Investigate delta normalized to 0..1 per day (does NOT modify state)
        public static float CalcInvestigateDelta01_Legacy(GameState s, CityState node, NodeTask task, DataRegistry registry)
        {
            if (s == null || task == null) return 0f;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) return 0f;
            var squad = GetAssignedAgents(s, task.AssignedAgentIds);
            if (squad.Count == 0) return 0f;

            string anomalyId = GetTaskAnomalyId(node, task);
            AnomalyDef anomalyDef = null;
            if (!string.IsNullOrEmpty(anomalyId) && registry != null)
                registry.AnomaliesById.TryGetValue(anomalyId, out anomalyDef);

            var team = ComputeTeamAvgProps(squad);
            int[] req = NormalizeIntArray4(anomalyDef?.invReq);
            float sMatch = ComputeMatchS_NoWeight(team, req);
            float progressScale = MapSToMult(sMatch);
            float effDelta = progressScale;

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(anomalyDef));
            if (task.Type == TaskType.Investigate && task.InvestigateTargetLocked && string.IsNullOrEmpty(task.SourceAnomalyId) && task.InvestigateNoResultBaseDays > 0)
            {
                requiredDays = task.InvestigateNoResultBaseDays;
            }

            float delta01 = effDelta / Math.Max(1f, (float)requiredDays);
            return delta01;
        }

        // Pure legacy calculation: Contain delta normalized to 0..1 per day (does NOT modify state)
        public static float CalcContainDelta01_Legacy(GameState s, CityState node, NodeTask task, DataRegistry registry)
        {
            if (s == null || task == null) return 0f;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) return 0f;
            var squad = GetAssignedAgents(s, task.AssignedAgentIds);
            if (squad.Count == 0) return 0f;

            string anomalyId = GetTaskAnomalyId(node, task);
            AnomalyDef anomalyDef = null;
            if (!string.IsNullOrEmpty(anomalyId) && registry != null)
                registry.AnomaliesById.TryGetValue(anomalyId, out anomalyDef);

            var team = ComputeTeamAvgProps(squad);
            int[] req = NormalizeIntArray4(anomalyDef?.conReq);
            float sMatch = ComputeMatchS_NoWeight(team, req);
            float progressScale = MapSToMult(sMatch);
            float effDelta = progressScale;

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(anomalyDef));

            float delta01 = effDelta / Math.Max(1f, (float)requiredDays);
            return delta01;
        }

        // Pure legacy calculation: Manage daily NegEntropy yield (does NOT modify state)
        public static int CalcManageNegEntropyDelta_Legacy(GameState s, CityState node, NodeTask task, DataRegistry registry)
        {
            if (s == null || node == null || task == null) return 0;
            if (task.AssignedAgentIds == null || task.AssignedAgentIds.Count == 0) return 0;
            if (string.IsNullOrEmpty(task.TargetManagedAnomalyId)) return 0;

            var m = node.ManagedAnomalies?.FirstOrDefault(x => x != null && x.Id == task.TargetManagedAnomalyId);
            if (m == null) return 0;

            string defId = task.SourceAnomalyId;
            AnomalyDef manageDef = null;
            if (!string.IsNullOrEmpty(defId) && registry != null)
                registry.AnomaliesById.TryGetValue(defId, out manageDef);
            if (manageDef == null) return 0;

            int yield = CalcDailyNegEntropyYield(m, manageDef);
            return yield;
        }

        // =====================
        // Roster-based efficiency (DRY, no Task)
        // =====================


        private static float CalcEffDelta_FromRosterReq(List<AgentState> team, int[] req)
        {
            req = NormalizeIntArray4(req);
            float sMatch = ComputeMatchS_NoWeight(ComputeTeamAvgProps(team), req);
            float progressScale = MapSToMult(sMatch);
            return progressScale;
        }

        public static float CalcInvestigateDelta01_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0f;
            if (registry == null) return 0f;


            if (anom == null) return 0f; // 或 0
            if (string.IsNullOrEmpty(anom.AnomalyDefId))
            {
                Debug.LogWarning($"[SettleCalc] Missing anom.AnomalyDefId for anomStateId={anom?.Id ?? "null"}");
                return 0f; // NegEntropy 的函数返回 0
            }

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0f;

            int[] req = NormalizeIntArray4(def.invReq);
            float effDelta = CalcEffDelta_FromRosterReq(arrived, req);

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(def));
            float delta01 = effDelta / Mathf.Max(1f, (float)requiredDays);
            return Mathf.Clamp01(delta01);
        }

        public static float CalcContainDelta01_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0f;
            if (registry == null) return 0f;

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0f;

            int[] req = NormalizeIntArray4(def.conReq);
            float effDelta = CalcEffDelta_FromRosterReq(arrived, req);

            int requiredDays = Math.Max(1, GetTaskBaseDaysFromAnomaly(def));
            float delta01 = effDelta / Mathf.Max(1f, (float)requiredDays);
            return Mathf.Clamp01(delta01);
        }

        public static int CalcNegEntropyDelta_FromRoster(GameState state, AnomalyState anom, List<AgentState> arrived, DataRegistry registry)
        {
            if (arrived == null || arrived.Count == 0) return 0;
            if (registry == null) return 0;

            if (!registry.AnomaliesById.TryGetValue(anom.AnomalyDefId, out var def) || def == null)
                return 0;

            return CalcDailyNegEntropyYield(def);
        }
    }
}
