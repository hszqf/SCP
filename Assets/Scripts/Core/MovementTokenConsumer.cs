using System;
using System.Collections.Generic;

namespace Core
{
    public static class MovementTokenConsumer
    {
        // Consume at most one token per call (keeps behavior deterministic).
        public static bool TryConsumeOne(GameState s, out string consumedTokenId)
        {
            consumedTokenId = null;
            if (s == null || s.MovementTokens == null || s.MovementTokens.Count == 0) return false;

            // Find first Pending token
            MovementToken token = null;
            int tokenIndex = -1;
            for (int i = 0; i < s.MovementTokens.Count; i++)
            {
                var t = s.MovementTokens[i];
                if (t != null && t.State == MovementTokenState.Pending)
                {
                    token = t;
                    tokenIndex = i;
                    break;
                }
            }
            if (token == null) return false;

            token.State = MovementTokenState.Playing;

            // Find agent (optional)
            AgentState ag = null;
            if (s.Agents != null)
            {
                for (int i = 0; i < s.Agents.Count; i++)
                {
                    var a = s.Agents[i];
                    if (a != null && a.Id == token.AgentId) { ag = a; break; }
                }
            }

            // Apply immediately (no animation)
            if (ag != null)
            {
                if (token.Type == MovementTokenType.Dispatch)
                {
                    // TravellingToAnomaly -> AtAnomaly
                    if (ag.LocationKind == AgentLocationKind.TravellingToAnomaly &&
                        ag.LocationAnomalyInstanceId == token.AnomalyInstanceId)
                    {
                        ag.LocationKind = AgentLocationKind.AtAnomaly;
                    }
                    else
                    {
                        // Be tolerant: if state drifted, still land at anomaly
                        ag.LocationKind = AgentLocationKind.AtAnomaly;
                        ag.LocationAnomalyInstanceId = token.AnomalyInstanceId;
                    }
                    ag.LocationSlot = token.Slot;
                }
                else // Recall
                {
                    // TravellingToBase -> Base
                    ag.LocationKind = AgentLocationKind.Base;
                    ag.LocationAnomalyInstanceId = null;
                    ag.LocationSlot = token.Slot;
                }
            }

            token.State = MovementTokenState.Completed;
            consumedTokenId = token.TokenId;

            // Unlock (never negative)
            if (s.MovementLockCount > 0) s.MovementLockCount -= 1;

            return true;
        }
    }
}
