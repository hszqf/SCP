using System;
using System.Collections.Generic;

namespace Core
{
    [Serializable]
    public struct AgentAfter
    {
        public bool IsDead;
        public bool IsInsane;

        public int HP;
        public int SAN;
        public int Exp;
        public int Level;

        public AgentLocationKind LocationKind;
        public string LocationAnomalyInstanceId;
        public AssignmentSlot LocationSlot;
    }

    [Serializable]
    public struct AnomalyAfter
    {
        public AnomalyPhase Phase;
        public float InvestigateProgress;
        public float ContainProgress;

        public List<string> InvestigatorIds;
        public List<string> ContainmentIds;
        public List<string> OperateIds;
    }

    /// <summary>
    /// v0: After-value overwrite patch. Simple, debug-friendly.
    /// </summary>
    [Serializable]
    public sealed class DayCommitPatch
    {
        public Dictionary<string, int> CityPopulationAfter = new Dictionary<string, int>();
        public Dictionary<string, AgentAfter> AgentsAfter = new Dictionary<string, AgentAfter>();
        public Dictionary<string, AnomalyAfter> AnomaliesAfter = new Dictionary<string, AnomalyAfter>();

        public int MoneyAfter;
        public float WorldPanicAfter;
        public int NegEntropyAfter;

        public void ApplyTo(GameState s)
        {
            if (s == null) return;

            // Cities
            if (s.Cities != null)
            {
                for (int i = 0; i < s.Cities.Count; i++)
                {
                    var c = s.Cities[i];
                    if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                    if (CityPopulationAfter.TryGetValue(c.Id, out var pop))
                        c.Population = pop;
                }
            }

            // Agents
            if (s.Agents != null)
            {
                for (int i = 0; i < s.Agents.Count; i++)
                {
                    var a = s.Agents[i];
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;

                    if (!AgentsAfter.TryGetValue(a.Id, out var after))
                        continue;

                    a.IsDead = after.IsDead;
                    a.IsInsane = after.IsInsane;

                    a.HP = after.HP;
                    a.SAN = after.SAN;
                    a.Exp = after.Exp;
                    a.Level = after.Level;

                    a.LocationKind = after.LocationKind;
                    a.LocationAnomalyInstanceId = after.LocationAnomalyInstanceId;
                    a.LocationSlot = after.LocationSlot;
                }
            }

            // Anomalies
            if (s.Anomalies != null)
            {
                for (int i = 0; i < s.Anomalies.Count; i++)
                {
                    var a = s.Anomalies[i];
                    if (a == null || string.IsNullOrEmpty(a.Id)) continue;

                    if (!AnomaliesAfter.TryGetValue(a.Id, out var after))
                        continue;

                    a.Phase = after.Phase;
                    a.InvestigateProgress = after.InvestigateProgress;
                    a.ContainProgress = after.ContainProgress;

                    if (a.InvestigatorIds != null) { a.InvestigatorIds.Clear(); a.InvestigatorIds.AddRange(after.InvestigatorIds ?? new List<string>()); }
                    if (a.ContainmentIds != null) { a.ContainmentIds.Clear(); a.ContainmentIds.AddRange(after.ContainmentIds ?? new List<string>()); }
                    if (a.OperateIds != null) { a.OperateIds.Clear(); a.OperateIds.AddRange(after.OperateIds ?? new List<string>()); }
                }
            }

            s.Money = MoneyAfter;
            s.WorldPanic = WorldPanicAfter;
            s.NegEntropy = NegEntropyAfter;

            s.EnsureIndex();
        }
    }
}
