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
        public int AgeDays;
        public string SourceTaskId;
        public string SourceAnomalyId;

        // Runtime metadata for ignore-apply policies.
        public bool IgnoreAppliedOnce;
    }

    public static class EventInstanceFactory
    {
        public static EventInstance Create(string eventDefId, string nodeId, int day, string sourceTaskId = null, string sourceAnomalyId = null)
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
                IgnoreAppliedOnce = false,
            };
        }
    }
}
