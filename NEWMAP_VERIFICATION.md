# NewMap System Verification Guide

## Overview
This document describes the new dynamic map generation system and how to verify it works correctly.

## Components

### 1. MapNodeSpawner (Modified)
**File:** `Assets/Scripts/UI/MapNodeSpawner.cs`

**Changes:**
- Added `UseNewMap` boolean toggle (default: `true`)
- Added `mapRoot` GameObject reference field
- When `UseNewMap == true`:
  - Old MapRoot is hidden via `SetActive(false)`
  - Old node generation (`Build()`) is skipped
  - Logs: `[MapUI] Old MapRoot disabled (UseNewMap=true)`

**Key Code:**
```csharp
if (UseNewMap)
{
    if (mapRoot != null)
    {
        mapRoot.SetActive(false);
        Debug.Log("[MapUI] Old MapRoot disabled (UseNewMap=true)");
    }
    Debug.Log("[MapUI] Old map generation skipped (UseNewMap=true)");
    return;
}
```

### 2. NewMapRuntime (New)
**File:** `Assets/Scripts/UI/Map/NewMapRuntime.cs`

**Responsibilities:**
- Dynamically creates NewMapRoot UI container at runtime
- Generates 4 node widgets (BASE, N1, N2, N3)
- Provides click interaction for nodes
- Creates a simple popup panel for node info

**Structure:**
```
NewMapRoot (created at runtime)
├── Background (Image - solid color background)
├── NodesRoot (Container for all nodes)
│   ├── NodeWidget_BASE
│   │   ├── Dot (Image - circular marker)
│   │   ├── Name (Text - displays nodeId)
│   │   ├── TaskBarRoot (RectTransform - placeholder)
│   │   ├── EventBadge (Image - placeholder, hidden)
│   │   └── UnknownAnomIcon (Text "?" - placeholder)
│   ├── NodeWidget_N1
│   ├── NodeWidget_N2
│   └── NodeWidget_N3
└── CityPanel (Info panel, hidden by default)
    ├── Title (Text)
    └── CloseButton (Button)
```

**Node Positions:**
- BASE: Left-top area (-0.25, 0.25) → ~(-100, 75) pixels
- N1: Right-top area (0.25, 0.25) → ~(100, 75) pixels
- N2: Left-bottom area (-0.25, -0.25) → ~(-100, -75) pixels
- N3: Right-bottom area (0.25, -0.25) → ~(100, -75) pixels

**Data Source Priority:**
1. If `GameController.I.State.Nodes` exists and has data → use first 4 nodes
2. Otherwise → hardcoded fallback: `["BASE", "N1", "N2", "N3"]`

**Logging:**
- `[MapUI] NewMapRuntime initializing...`
- `[MapUI] Verify oldMap=FOUND(active=false)`
- `[MapUI] Nodes = BASE,N1,N2,N3 source=Hardcoded` (or `source=GameState`)
- `[MapUI] NewMapRoot structure created`
- `[MapUI] CityPanel created`
- `[MapUI] Created 4 node widgets`
- `[MapUI] Verify oldMap=FOUND(active=false) newMap=CREATED nodes=4`
- `[MapUI] Click nodeId=BASE` (when clicking nodes)

### 3. Scene Configuration (Main.unity)
**Changes:**
- Added `MapBootstrap` GameObject under Canvas
  - RectTransform: anchored to fill parent
  - Script: `NewMapRuntime` component attached
- Updated `MapNodeSpawner` component on NodeLayer:
  - `mapRoot`: Reference to MapRoot GameObject (fileID: 2000627560)
  - `UseNewMap`: Set to `true`

## Verification Steps

### Step 1: Check Console Logs
When the game starts, you should see these logs in order:
```
[MapUI] Old MapRoot disabled (UseNewMap=true)
[MapUI] Old map generation skipped (UseNewMap=true)
[MapUI] NewMapRuntime initializing...
[MapUI] Verify oldMap=FOUND(active=false)
[MapUI] Nodes = BASE,N1,N2,N3 source=Hardcoded
[MapUI] NewMapRoot structure created
[MapUI] CityPanel created
[MapUI] Created 4 node widgets
[MapUI] Verify oldMap=FOUND(active=false) newMap=CREATED nodes=4
```

### Step 2: Visual Verification
You should see:
- **Background**: Dark blue-gray solid color (RGB: 30, 30, 46)
- **No old map**: The old map background and nodes should NOT be visible
- **4 Node Widgets**: Positioned in corners
  - Each has a blue dot (circular)
  - Each has a text label showing the nodeId
  - Each has a yellow "?" icon above it
- **Layout**: Nodes should be roughly in 4 corners of the screen

### Step 3: Interaction Test
Click on any node widget:
- Console should log: `[MapUI] Click nodeId=<ID>`
- A popup panel should appear in the center showing "Node: <ID>"
- Click "Close" button to dismiss the panel

### Step 4: Old Map Verification
Verify old map is disabled:
1. Open Unity Hierarchy
2. Find `MapRoot` GameObject
3. Confirm it's disabled (inactive checkbox)
4. Confirm no node buttons are children of `NodeLayer`

## Expected Behavior

### ✅ Success Criteria
- Old MapRoot is hidden (not visible in game)
- NewMapRoot is created at runtime
- 4 nodes are visible with correct structure
- Clicking nodes shows popup panel
- All verification logs appear correctly

### ❌ Common Issues

**Issue: "NullReferenceException on GameController.I.State"**
- Cause: GameState not initialized before NewMapRuntime.Start()
- Solution: NewMapRuntime uses `StartCoroutine(InitializeNextFrame())` to wait one frame

**Issue: "Old map still visible"**
- Cause: `UseNewMap` is false or `mapRoot` reference not set
- Solution: Check MapNodeSpawner component in Unity Inspector

**Issue: "NewMapRoot not created"**
- Cause: Canvas not found or MapBootstrap not active
- Solution: Check MapBootstrap GameObject exists and is active in scene

**Issue: "Nodes not visible"**
- Cause: Positioning or color issues
- Solution: Check node positions and colors in NewMapRuntime settings

## Rollback Instructions

To revert to the old map system:
1. In Unity Inspector, find `MapNodeSpawner` component
2. Uncheck `UseNewMap` (set to false)
3. Save scene
4. OR: Delete/disable `MapBootstrap` GameObject

## Future Enhancements

Not implemented in this phase (as per requirements):
- [ ] Real pathfinding between nodes
- [ ] Excel/DataRegistry integration for node display names
- [ ] Actual task bar visualization
- [ ] Event badge logic
- [ ] Anomaly icon state management
- [ ] Line connections between nodes
- [ ] Node state synchronization with GameState
- [ ] Font/TMP resource improvements

## File Changes Summary

**Modified:**
- `Assets/Scripts/UI/MapNodeSpawner.cs` - Added UseNewMap toggle
- `Assets/Scenes/Main.unity` - Added MapBootstrap, configured MapNodeSpawner

**Created:**
- `Assets/Scripts/UI/Map/NewMapRuntime.cs` - New runtime map generator
- `Assets/Scripts/UI/Map/NewMapRuntime.cs.meta` - Unity metadata

**Not Modified:**
- No Excel/game_data.json changes
- No font/TMP resources added
- No news/fact system changes
- No other system refactoring
