# Step 3 Implementation Summary

## Overview
This document describes the implementation of Step 3 requirements: Individual HP/SAN display per avatar, progress bar fixes, dispatch animations, and button disabling.

## Changes Made

### A. Individual HP/SAN per Avatar ✅

**File Modified:** `Assets/Scripts/UI/Map/NodeMarkerView.cs`

**Implementation:**
1. Modified `RefreshAvatars()` to fetch actual agent data from GameController
2. Created `CreateOrUpdateAvatarStats()` method that dynamically creates HP/SAN text components for each avatar:
   - Creates a "Stats" container under each avatar GameObject
   - Adds "HPText" component (green, displays "HP {value}")
   - Adds "SANText" component (cyan, displays "SAN {value}")
   - Falls back to "HP 100" / "SAN 100" if agent data is unavailable

3. Modified `RefreshStats()` to display task type label instead of unified stats:
   - Shows "Investigating", "Containing", or "Managing" based on task type
   - Removed the old "HP 100 | SAN 100" placeholder

**Key Code:**
```csharp
// Get agent data for each assigned agent
string agentId = agentIds[i];
var agentState = GameController.I?.GetAgent(agentId);
CreateOrUpdateAvatarStats(avatarObj, agentState);
```

### B. Progress Bar Functionality ✅

**Status:** Already working correctly

The progress bar was already implemented correctly in the existing code:
- Uses `GetTaskProgress01()` to calculate progress as `Clamp01(task.Progress / baseDays)`
- Gets baseDays from `DataRegistry` based on anomaly or task type
- Updates `progressFill.fillAmount` on every refresh
- Supports both Image.Type.Filled and sizeDelta-based progress bars

No changes needed.

### C. Dispatch Animation on Task Assignment ✅

**Files Modified:** `Assets/Scripts/UI/UIPanelRoot.cs`

**Implementation:**
The dispatch animation is automatically handled by the existing `DispatchLineFX` component, which monitors task state changes. We made the following changes:

1. Close node panel after successful task assignment (in both Investigate and Contain flows)
2. This hides all node operation buttons during dispatch
3. The existing `DispatchLineFX.CheckForTaskStateChanges()` automatically detects when a task is assigned and triggers the dispatch animation from "HQ" to the target node

**Key Changes:**
```csharp
gc.AssignTask(task.Id, agentIds);

// Close panels after successful dispatch
if (_managePanel)
    CloseModal(_managePanel, "assign_confirm");
else
    _managePanelView.Hide();

// Close node panel to hide buttons during dispatch
CloseNode();

// Note: Dispatch animation is automatically triggered by DispatchLineFX
```

### D. Return Animation on Task Completion ✅

**Files Created:** 
- `Assets/Scripts/UI/Map/TaskDispatchWatcher.cs`
- `Assets/Scripts/UI/Map/TaskDispatchWatcher.cs.meta`

**Files Modified:** 
- `Assets/Scripts/Runtime/SimpleWorldMapBootstrap.cs` (to add TaskDispatchWatcher component)

**Implementation:**
Created a new `TaskDispatchWatcher` component that:

1. Subscribes to `GameController.OnStateChanged` event
2. Maintains a dictionary of task states to detect transitions
3. When a task transitions from Active → Completed or Cancelled:
   - Triggers a return animation from the node back to BASE
   - Uses `DispatchLineFX.PlayDispatchAnimation(nodeId, "BASE", taskType)`
4. Automatically cleans up old task states to prevent memory leaks

The watcher is automatically added to the SimpleWorldMapPanel during bootstrap initialization.

**Key Code:**
```csharp
// Detect completion
if (previousState == TaskState.Active && 
    (currentState == TaskState.Completed || currentState == TaskState.Cancelled))
{
    Debug.Log($"[TaskWatcher] Task completed: {taskKey}");
    TriggerReturnAnimation(node.Id, task.AssignedAgentIds);
}
```

## Architecture Notes

### Animation System
The game now has a unified animation system:

1. **DispatchLineFX** (existing):
   - Monitors all task state changes via GameController.OnStateChanged
   - Automatically plays dispatch animations when tasks start
   - Shows completion icons (✓/✗) when tasks complete

2. **TaskDispatchWatcher** (new):
   - Also monitors task state changes
   - Specifically handles "return to base" animations when tasks complete
   - Works alongside DispatchLineFX without conflicts

Both components are attached to the SimpleWorldMapPanel and work independently.

### Component Hierarchy
```
SimpleWorldMapPanel (GameObject)
├── SimpleWorldMapPanel (Component)
├── TaskDispatchWatcher (Component) [NEW]
└── DispatchLineFX (Component)
```

## Testing Checklist

- [ ] Deploy and start a new game
- [ ] Assign 3 agents to an investigate task
- [ ] Verify each avatar shows individual HP/SAN values
- [ ] Verify task type label shows "Investigating"
- [ ] Verify node panel closes after assignment
- [ ] Verify dispatch animation plays from HQ to target node
- [ ] Click "Next Day" several times
- [ ] Verify progress bar fills up over time
- [ ] Wait for task to complete
- [ ] Verify return animation plays from node to HQ
- [ ] Verify completion icon appears at node

## Potential Issues & Future Improvements

1. **Avatar Layout**: The dynamically created HP/SAN text might need position adjustments in the prefab template for optimal display
2. **Animation Overlap**: If multiple tasks complete on the same day, multiple return animations will play simultaneously (this is expected behavior)
3. **HQ NodeId**: Currently hardcoded as "BASE" and "HQ" - consider unifying to a constant
4. **Font**: Using LegacyRuntime.ttf fallback font - consider using a better font resource if available

## Files Changed Summary

### Modified Files (3):
1. `Assets/Scripts/UI/Map/NodeMarkerView.cs` - Individual HP/SAN per avatar
2. `Assets/Scripts/UI/UIPanelRoot.cs` - Close panels after dispatch
3. `Assets/Scripts/Runtime/SimpleWorldMapBootstrap.cs` - Add TaskDispatchWatcher component

### New Files (2):
1. `Assets/Scripts/UI/Map/TaskDispatchWatcher.cs` - Return animation watcher
2. `Assets/Scripts/UI/Map/TaskDispatchWatcher.cs.meta` - Unity meta file
