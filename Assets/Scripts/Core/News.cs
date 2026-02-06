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
        public int Day; // The day this news was created
        
        // Media profile for news separation (FORMAL/SENSATIONAL/INVESTIGATIVE)
        // Default to FORMAL for backward compatibility
        public string mediaProfileId = "FORMAL";
        
        // Generated content for fact-based news
        public string Title;
        public string Description;
    }

    public static class NewsInstanceFactory
    {
        public static NewsInstance Create(string newsDefId, string nodeId, string sourceAnomalyId, string causeType, int day = 1)
        {
            return new NewsInstance
            {
                Id = $"NEWS_{Guid.NewGuid():N}",
                NewsDefId = newsDefId,
                NodeId = nodeId,
                SourceAnomalyId = sourceAnomalyId,
                CauseType = causeType,
                AgeDays = 0,
                Day = day,
            };
        }
    }
}
