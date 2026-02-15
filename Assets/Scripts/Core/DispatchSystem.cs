using System;
using System.Collections.Generic;

namespace Core
{
    public static class DispatchSystem
    {
        public static bool TrySetRoster(GameState s, string anomalyKey, AssignmentSlot slot, IList<string> roster, out string error)
        {
            error = null;

            if (s == null)
            {
                error = "GameState is null";
                return false;
            }

            if (string.IsNullOrEmpty(anomalyKey))
            {
                error = "anomalyKey is null/empty";
                return false;
            }

            var anomaly = FindAnomaly(s, anomalyKey);
            if (anomaly == null)
            {
                error = $"Anomaly not found: {anomalyKey}";
                return false;
            }

            // canonical key (use AnomalyState.Id as single source-of-truth)
            var canonicalKey = anomaly.Id;

            var list = anomaly.GetRoster(slot);
            if (list == null)
            {
                error = $"Roster list is null for slot={slot}";
                return false;
            }

            // Save old roster ids before clearing (for AgentState sync)
            var oldIds = new List<string>(list);

            // normalize: remove null/empty + de-dup (preserve order)
            list.Clear();
            var seen = new HashSet<string>();
            if (roster != null)
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    var id = roster[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (seen.Add(id)) list.Add(id);
                }
            }

            // After writing new roster, compute diff sets
            var oldSet = new HashSet<string>(oldIds);
            var newSet = new HashSet<string>(list);

            // Ensure an agent is not listed in multiple rosters for the same anomaly.
            // Remove newSet ids from the other two rosters on this anomaly.
            if (slot == AssignmentSlot.Operate)
            {
                anomaly.InvestigatorIds?.RemoveAll(id => newSet.Contains(id));
                anomaly.ContainmentIds?.RemoveAll(id => newSet.Contains(id));
            }
            else if (slot == AssignmentSlot.Investigate)
            {
                anomaly.OperateIds?.RemoveAll(id => newSet.Contains(id));
                anomaly.ContainmentIds?.RemoveAll(id => newSet.Contains(id));
            }
            else if (slot == AssignmentSlot.Contain)
            {
                anomaly.OperateIds?.RemoveAll(id => newSet.Contains(id));
                anomaly.InvestigatorIds?.RemoveAll(id => newSet.Contains(id));
            }

            // Diff lists for movement tokens
            var added = new List<string>();
            var removed = new List<string>();

            foreach (var id in newSet)
                if (!oldSet.Contains(id)) added.Add(id);

            foreach (var id in oldSet)
                if (!newSet.Contains(id)) removed.Add(id);

            // Ensure token list exists
            if (s.MovementTokens == null) s.MovementTokens = new List<MovementToken>();

            // Build agent lookup by id for conservative enqueue checks
            var agentById = new Dictionary<string, AgentState>();
            if (s.Agents != null)
            {
                for (int i = 0; i < s.Agents.Count; i++)
                {
                    var ag = s.Agents[i];
                    if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;
                    agentById[ag.Id] = ag;
                }
            }

            // Enqueue movement tokens (state Pending)
            // added -> Dispatch tokens (only if agent not already at this anomaly)
            for (int i = 0; i < added.Count; i++)
            {
                var id = added[i];
                if (agentById.TryGetValue(id, out var existing))
                {
                    // If already at this anomaly, skip enqueue
                    if (existing.LocationKind == AgentLocationKind.AtAnomaly &&
                        !string.IsNullOrEmpty(existing.LocationAnomalyKey) &&
                        existing.LocationAnomalyKey == canonicalKey)
                    {
                        continue;
                    }
                }

                // Conservative: enqueue if agent not found or not already at anomaly
                s.MovementTokens.Add(new MovementToken
                {
                    TokenId = Guid.NewGuid().ToString("N"),
                    AgentId = id,
                    AnomalyKey = canonicalKey,
                    Slot = slot,
                    Type = MovementTokenType.Dispatch,
                    State = MovementTokenState.Pending,
                    CreatedDay = s.Day
                });
                s.MovementLockCount += 1;
            }

            // removed -> Recall tokens (only if agent currently at this anomaly+slot)
            for (int i = 0; i < removed.Count; i++)
            {
                var id = removed[i];
                if (agentById.TryGetValue(id, out var existing))
                {
                    if (!((existing.LocationKind == AgentLocationKind.AtAnomaly ||
                           existing.LocationKind == AgentLocationKind.TravellingToAnomaly) &&
                          !string.IsNullOrEmpty(existing.LocationAnomalyKey) &&
                          existing.LocationAnomalyKey == canonicalKey &&
                          existing.LocationSlot == slot))
                    {
                        // Not at/travelling to this anomaly+slot -> skip enqueue
                        continue;
                    }
                }

                // Conservative: enqueue if agent not found or confirmed at anomaly+slot
                s.MovementTokens.Add(new MovementToken
                {
                    TokenId = Guid.NewGuid().ToString("N"),
                    AgentId = id,
                    AnomalyKey = canonicalKey,
                    Slot = slot,
                    Type = MovementTokenType.Recall,
                    State = MovementTokenState.Pending,
                    CreatedDay = s.Day
                });
                s.MovementLockCount += 1;
            }

            // Synchronize AgentState location info when GameState agents exist.
            if (s?.Agents != null)
            {
                for (int i = 0; i < s.Agents.Count; i++)
                {
                    var ag = s.Agents[i];
                    if (ag == null || string.IsNullOrEmpty(ag.Id)) continue;

                    if (newSet.Contains(ag.Id))
                    {
                        // If already at this anomaly, no travel needed (slot change / re-confirm)
                        if (ag.LocationKind == AgentLocationKind.AtAnomaly &&
                            !string.IsNullOrEmpty(ag.LocationAnomalyKey) &&
                            ag.LocationAnomalyKey == canonicalKey)
                        {
                            ag.LocationKind = AgentLocationKind.AtAnomaly;
                        }
                        else
                        {
                            ag.LocationKind = AgentLocationKind.TravellingToAnomaly;
                        }

                        ag.LocationAnomalyKey = canonicalKey;
                        ag.LocationSlot = slot;
                    }
                    else if (oldSet.Contains(ag.Id))
                    {
                        if ((ag.LocationKind == AgentLocationKind.AtAnomaly ||
                             ag.LocationKind == AgentLocationKind.TravellingToAnomaly) &&
                            !string.IsNullOrEmpty(ag.LocationAnomalyKey) &&
                            ag.LocationAnomalyKey == canonicalKey &&
                            ag.LocationSlot == slot)
                        {
                            ag.LocationKind = AgentLocationKind.TravellingToBase;
                            ag.LocationAnomalyKey = canonicalKey; // keep until animation completes
                            ag.LocationSlot = slot;
                        }
                    }
                }
            }

            // TODO: enqueue dispatch/recall animation tokens + handle Travelling states
            // TODO (later): enqueue dispatch/recall animation tokens

            return true;
        }

        public static AnomalyState FindAnomaly(GameState s, string key)
        {
            if (s.Anomalies == null) return null;

            for (int i = 0; i < s.Anomalies.Count; i++)
            {
                var a = s.Anomalies[i];
                if (a == null) continue;

                // Support both legacy Id and new-arch AnomalyId
                if (a.Id == key) return a;
                if (!string.IsNullOrEmpty(a.AnomalyId) && a.AnomalyId == key) return a;
                if (!string.IsNullOrEmpty(a.AnomalyDefId) && a.AnomalyDefId == key) return a; // added support
                if (a.ManagedState != null && !string.IsNullOrEmpty(a.ManagedState.Id) && a.ManagedState.Id == key)
                    return a;
            }

            // Fallback: allow lookup by ManagedAnomalyState.Id across cities -> find or create AnomalyState
            if (s.Cities != null)
            {
                for (int ci = 0; ci < s.Cities.Count; ci++)
                {
                    var city = s.Cities[ci];
                    if (city == null || city.ManagedAnomalies == null) continue;

                    for (int mi = 0; mi < city.ManagedAnomalies.Count; mi++)
                    {
                        var m = city.ManagedAnomalies[mi];
                        if (m == null || string.IsNullOrEmpty(m.Id)) continue;
                        if (m.Id != key) continue;

                        // matched a managed anomaly id; try to find a corresponding AnomalyState by node and def id
                        var nodeId = city.Id;
                        var defId = m.AnomalyId;

                        if (s.Anomalies != null)
                        {
                            for (int ai = 0; ai < s.Anomalies.Count; ai++)
                            {
                                var a2 = s.Anomalies[ai];
                                if (a2 == null) continue;
                                if (!string.IsNullOrEmpty(a2.NodeId) && a2.NodeId == nodeId &&
                                    ((!string.IsNullOrEmpty(a2.AnomalyDefId) && a2.AnomalyDefId == defId) ||
                                     (!string.IsNullOrEmpty(a2.AnomalyId) && a2.AnomalyId == defId)))
                                {
                                    return a2;
                                }
                            }
                        }

                        // Not found: create a new AnomalyState that uses managed Id as canonical key
                        var created = new AnomalyState();
                        created.Id = m.Id;
                        created.NodeId = nodeId;
                        created.AnomalyDefId = m.AnomalyId;
                        created.ManagedState = m;
                        created.Phase = AnomalyPhase.Contained;

                        if (s.Anomalies == null) s.Anomalies = new List<AnomalyState>();
                        s.Anomalies.Add(created);
                        return created;
                    }
                }
            }

            return null;
        }
    }
}
