# NodeMarkerView Implementation Guide

## Overview
This document describes the implementation of the NodeMarkerView prefab-based system for displaying nodes on the game map.

## Components

### 1. NodeMarkerView.cs
**Location:** `Assets/Scripts/UI/Map/NodeMarkerView.cs`

**Purpose:** Simplified node display component that only handles display and click events.

**Key Methods:**
- `Bind(string nodeId, System.Action<string> onClick)` - Binds view to a node and sets up click handler
- `Refresh(NodeState node)` - Refreshes display based on current node state
- `SetSelected(bool selected)` - Optional method for visual feedback (stub)

**Display Logic:**
- **Node Name:** Shows `node.Name`, falls back to `node.Id` if name is empty or starts with "??"
- **Event Badge:** Shows count of pending events, hidden if count is 0
- **Unknown Icon:** Shows "?" if there are active anomalies not in the known list
- **Task Bar:** Shows representative task with highest priority (Contain > Investigate > Manage)
  - Displays agent avatars (up to 4)
  - Shows HP/SAN placeholders (currently "HP 100 | SAN 100")
  - Shows progress bar (0.0 to 1.0 based on task.Progress / baseDays)

### 2. NodeMarkerView.prefab
**Location:** `Assets/Prefabs/UI/Map/NodeMarkerView.prefab`

**Structure:**
```
NodeMarkerView (RectTransform + Button + Image + NodeMarkerView script)
├── Dot (Image, 40x40, blue circle placeholder)
├── Name (Text, node name display)
├── TaskBar (RectTransform, 140x60)
│   ├── Avatars (HorizontalLayoutGroup)
│   │   └── AvatarTemplate (Image, 20x20, inactive)
│   ├── Stats (Text, "HP - | SAN -")
│   └── ProgressBg (Image, 8px height)
│       └── ProgressFill (Image, filled type)
├── EventBadge (Image + Text, hidden by default)
└── UnknownIcon (Text "?", hidden by default)
```

**Raycast Settings:**
- Root Button Image: `raycastTarget = true` (only clickable element)
- All child elements: `raycastTarget = false` (don't block clicks)

### 3. NodeMarkerPrefabGenerator.cs
**Location:** `Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs`

**Purpose:** Editor script to generate the NodeMarkerView.prefab programmatically.

**Usage:**
1. Open Unity Editor
2. Go to menu: `Tools > SCP > Generate NodeMarkerView Prefab`
3. Prefab will be created at `Assets/Prefabs/UI/Map/NodeMarkerView.prefab`

### 4. NewMapRuntime.cs (Updated)
**Location:** `Assets/Scripts/UI/Map/NewMapRuntime.cs`

**Changes:**
- Added `[SerializeField] private NodeMarkerView nodeMarkerPrefab` field
- Replaced manual widget creation with prefab instantiation
- Added `OnGameStateChanged()` method that refreshes all node views
- Subscribed to `GameController.I.OnStateChanged` event
- Properly unsubscribes on destroy

**Usage Flow:**
1. `Initialize()` validates prefab reference
2. For each node, calls `CreateNodeWidget(nodeId)`
3. `CreateNodeWidget()` instantiates prefab, positions it, binds it, and refreshes it
4. When game state changes, `OnGameStateChanged()` refreshes all views

## Setup Instructions

### Step 1: Generate Prefab
```
1. Open Unity Editor
2. Select Tools > SCP > Generate NodeMarkerView Prefab
3. Verify prefab created at Assets/Prefabs/UI/Map/NodeMarkerView.prefab
```

### Step 2: Assign Prefab Reference
```
1. In Unity Hierarchy, find MapBootstrap GameObject
2. Select it and look at Inspector
3. Find NewMapRuntime component
4. Drag NodeMarkerView.prefab from Project to "Node Marker Prefab" field
```

### Step 3: Test
```
1. Play the scene in Editor
2. Verify 4 nodes appear on map
3. Click each node to verify NodePanel opens
4. Check Console for [MapUI] logs
```

## Testing Checklist

- [ ] Prefab generated successfully
- [ ] Prefab assigned to NewMapRuntime
- [ ] 4 nodes visible on map
- [ ] Each node shows:
  - [ ] Dot (blue circle)
  - [ ] Node name
  - [ ] Task bar (when tasks exist)
  - [ ] Event badge (when events exist)
  - [ ] Unknown icon (when unknown anomalies exist)
- [ ] Node clicks open NodePanel
- [ ] State changes refresh all nodes automatically
- [ ] No raycast blocking issues
- [ ] Console shows [MapUI] logs with no errors

## Troubleshooting

### Issue: "NodeMarkerPrefab is not assigned"
**Solution:** Assign the prefab in Inspector as described in Step 2 above.

### Issue: Nodes not appearing
**Checks:**
1. Verify MapBootstrap GameObject exists in scene
2. Check NewMapRuntime component is attached
3. Verify prefab is assigned
4. Check Console for error messages

### Issue: Clicks not working
**Checks:**
1. Verify EventSystem exists in scene
2. Check Canvas has GraphicRaycaster component
3. Verify root button Image has raycastTarget = true
4. Verify child elements have raycastTarget = false

### Issue: Task bars not showing
**Debug:**
1. Check if nodes have active tasks in GameState
2. Verify task.State == TaskState.Active
3. Check Console for [MapUI] logs showing task selection

## Future Enhancements

### Planned Features (in TODO comments):
1. **Real HP/SAN values:** Get actual agent HP/SAN from AgentState
2. **Visual selection feedback:** Implement SetSelected() method with visual cues
3. **More task types:** Support additional task types beyond Investigate/Contain/Manage

### Extension Points:
- Add custom task bar colors per task type
- Add animation for progress changes
- Add tooltips on hover
- Add more detailed agent information

## Logging Convention

All logs use the `[MapUI]` prefix for consistency:
- `[MapUI] NodeMarkerView.Bind nodeId=...` - View binding
- `[MapUI] NodeMarkerView.OnButtonClick nodeId=...` - Click event
- `[MapUI] NodeWidget created for nodeId=...` - Widget creation
- `[MapUI] RefreshNodes called` - Manual refresh
- `[MapUI] Subscribed to GameController.OnStateChanged` - Event subscription

## Code Quality Notes

### Design Patterns Used:
- **Component-based architecture:** NodeMarkerView as reusable component
- **Prefab instantiation:** Consistent UI creation
- **Event-driven updates:** Automatic refresh on state changes
- **Separation of concerns:** View only handles display, controller handles data

### Best Practices:
- All serialized fields are properly named and organized
- Null checks before accessing components
- Proper event subscription/unsubscription
- Consistent logging with prefix
- Raycast target management for click handling
- Clean separation between data and presentation

## Related Files

- `Assets/Scripts/Core/GameState.cs` - Node state data structures
- `Assets/Scripts/Data/DataRegistry.cs` - Anomaly base days lookup
- `Assets/Scripts/Runtime/GameController.cs` - Game state management
- `Assets/Scripts/UI/UIPanelRoot.cs` - Panel navigation
