using System;
using Core;

namespace Core
{
    public enum MovementTokenType
    {
        Dispatch, // Base -> Anomaly
        Recall    // Anomaly -> Base
    }

    public enum MovementTokenState
    {
        Pending,
        Playing,
        Completed
    }

    [Serializable]
    public class MovementToken
    {
        public string TokenId;          // unique
        public string AgentId;
        public string AnomalyInstanceId;       // == AnomalyState.Id
        public AssignmentSlot Slot;     // Investigate/Contain/Operate
        public MovementTokenType Type;  // Dispatch/Recall
        public MovementTokenState State = MovementTokenState.Pending;

        // Optional metadata (can be unused for now)
        public int CreatedDay = 0;
    }
}
