using System;

namespace Core
{
    [Serializable]
    public class NewsInstance
    {
        public string Id;
        public string NewsDefId;
        public string NodeId;
        public string SourceAnomalyId;
        // NOTE: SourceAnomalyId is anomalyDefId (e.g., AN_002)
        public string SourceAnomalyDefId
        {
            get => SourceAnomalyId;
            set => SourceAnomalyId = value;
        }
        public string CauseType;
        public int AgeDays;
        public bool IsResolved;
        public int ResolvedDay;
    }

    public static class NewsInstanceFactory
    {
        public static NewsInstance Create(string newsDefId, string nodeId, string sourceAnomalyId, string causeType)
        {
            return new NewsInstance
            {
                Id = $"NEWS_{Guid.NewGuid():N}",
                NewsDefId = newsDefId,
                NodeId = nodeId,
                SourceAnomalyId = sourceAnomalyId,
                CauseType = causeType,
                AgeDays = 0,
                IsResolved = false,
                ResolvedDay = 0,
            };
        }
    }
}
