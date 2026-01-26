using System;
using System.Collections.Generic;
using Data;

namespace Core
{
    [Serializable]
    public class EventInstance
    {
        public string EventInstanceId;
        public string EventDefId;
        public string NodeId;
        public int CreatedDay;
        public int AgeDays;
        public string SourceTaskId;
        public string SourceAnomalyId;
        public string CauseType;

        // Runtime metadata for ignore-apply policies.
        public bool IgnoreAppliedOnce;
    }

    public static class EventInstanceFactory
    {
        public static EventInstance Create(string eventDefId, string nodeId, int day, string sourceTaskId = null, string sourceAnomalyId = null, string causeType = null)
        {
            return new EventInstance
            {
                EventInstanceId = $"EVI_{Guid.NewGuid():N}",
                EventDefId = eventDefId,
                NodeId = nodeId,
                CreatedDay = day,
                AgeDays = 0,
                SourceTaskId = sourceTaskId,
                SourceAnomalyId = sourceAnomalyId,
                CauseType = causeType,
                IgnoreAppliedOnce = false,
            };
        }
    }
}
