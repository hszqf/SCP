// TaskDispatchWatcher - Monitors task state changes and triggers return animations
// Author: Canvas
// Version: 1.0

using System.Collections.Generic;
using Core;
using UnityEngine;

namespace UI.Map
{
    /// <summary>
    /// Watches for task completions and triggers return animations.
    /// Attach to MapBootstrap or a persistent object.
    /// </summary>
    public class TaskDispatchWatcher : MonoBehaviour
    {
        private Dictionary<string, TaskState> _taskStates = new Dictionary<string, TaskState>();

        private void OnEnable()
        {
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged += OnGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (GameController.I != null)
            {
                GameController.I.OnStateChanged -= OnGameStateChanged;
            }
        }

        private void OnGameStateChanged()
        {
            if (GameController.I == null || GameController.I.State?.Nodes == null)
                return;

            // Scan all nodes and tasks for state changes
            foreach (var node in GameController.I.State.Nodes)
            {
                if (node?.Tasks == null)
                    continue;

                foreach (var task in node.Tasks)
                {
                    if (task == null)
                        continue;

                    string taskKey = task.Id;
                    TaskState currentState = task.State;

                    // Check if we've seen this task before
                    if (_taskStates.TryGetValue(taskKey, out TaskState previousState))
                    {
                        // Check for completion: Active → Completed or Cancelled
                        if (previousState == TaskState.Active && 
                            (currentState == TaskState.Completed || currentState == TaskState.Cancelled))
                        {
                            Debug.Log($"[TaskWatcher] Task completed: {taskKey} state={currentState} node={node.Id}");
                            TriggerReturnAnimation(node.Id, task.AssignedAgentIds, task.Type);
                        }

                        // Update state
                        _taskStates[taskKey] = currentState;
                    }
                    else
                    {
                        // First time seeing this task, just record its state
                        _taskStates[taskKey] = currentState;
                    }
                }
            }

            // Clean up old task states (optional, prevents memory leaks)
            CleanupOldTaskStates();
        }

        private void TriggerReturnAnimation(string fromNodeId, List<string> agentIds, TaskType taskType)
        {
            if (string.IsNullOrEmpty(fromNodeId))
                return;

            var dispatchFX = DispatchLineFX.Instance;
            if (dispatchFX != null)
            {
                // Play return animation from node back to BASE/HQ
                dispatchFX.PlayDispatchAnimation(fromNodeId, "BASE", taskType);
                Debug.Log($"[TaskWatcher] Return animation triggered: from={fromNodeId} to=BASE type={taskType} agents={string.Join(",", agentIds ?? new List<string>())}");
            }
            else
            {
                Debug.LogWarning("[TaskWatcher] DispatchLineFX.Instance is null, return animation not triggered");
            }
        }

        private void CleanupOldTaskStates()
        {
            // Keep only task states that still exist in the game state
            var currentTaskIds = new HashSet<string>();

            if (GameController.I?.State?.Nodes != null)
            {
                foreach (var node in GameController.I.State.Nodes)
                {
                    if (node?.Tasks != null)
                    {
                        foreach (var task in node.Tasks)
                        {
                            if (task != null)
                            {
                                currentTaskIds.Add(task.Id);
                            }
                        }
                    }
                }
            }

            // Remove task states that no longer exist
            var keysToRemove = new List<string>();
            foreach (var key in _taskStates.Keys)
            {
                if (!currentTaskIds.Contains(key))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _taskStates.Remove(key);
            }
        }
    }
}
