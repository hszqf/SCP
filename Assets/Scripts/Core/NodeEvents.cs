using System;
using System.Collections.Generic;
using Data;

namespace Core
{
    public enum EventSource
    {
        Investigate,
        Contain,
        Manage,
        LocalPanicHigh,
        Fixed,
        SecuredManage,
        Random,
    }

    [Serializable]
    public class EventInstance
    {
        public string EventInstanceId;
        public string EventDefId;
        public string NodeId;
        public int CreatedDay;
        public CauseType CauseType;
        public string SourceTaskId;
        public string SourceAnomalyId;

        // Runtime metadata for ignore-apply policies.
        public bool IgnoreAppliedOnce;
    }

    public static class EventInstanceFactory
    {
        public static EventInstance Create(string eventDefId, string nodeId, int day, CauseType causeType, string sourceTaskId = null, string sourceAnomalyId = null)
        {
            return new EventInstance
            {
                EventInstanceId = $"EVI_{Guid.NewGuid():N}",
                EventDefId = eventDefId,
                NodeId = nodeId,
                CreatedDay = day,
                CauseType = causeType,
                SourceTaskId = sourceTaskId,
                SourceAnomalyId = sourceAnomalyId,
                IgnoreAppliedOnce = false,
            };
        }
    }
}
