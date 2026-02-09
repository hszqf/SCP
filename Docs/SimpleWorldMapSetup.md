# Simplified World Map UI - Unity Editor Setup Guide

## Overview
This guide explains how to set up the new simplified world map UI system in the Unity Editor.

## Prerequisites
- Unity Editor (2021.3 or later recommended)
- SCP project opened in Unity

## Step 1: Create Required Prefabs

### 1.1 Create SimpleWorldMapPanel Prefab

1. In Unity Hierarchy, create: `GameObject > UI > Panel`
2. Rename it to "SimpleWorldMapPanel"
3. Add the `SimpleWorldMapPanel` component
4. Configure the panel:
   - **RectTransform**: Stretch to fill (Anchor: stretch, Offset: 0,0,0,0)
   - **Background Image**: Set color to dark blue/gray (R:0.1, G:0.1, B:0.15, A:1.0)
5. Create child object: `GameObject > Create Empty`
   - Rename to "MapContainer"
   - **RectTransform**: Center (Anchor: center, Width: 1200, Height: 800)
6. In SimpleWorldMapPanel component, assign:
   - **Map Container**: Drag MapContainer object
   - **Background Image**: Drag Panel's Image component
7. Save as prefab: Drag to `Assets/Prefabs/UI/Map/SimpleWorldMapPanel.prefab`

### 1.2 Create NodeMarker Prefab

1. In Hierarchy, create: `GameObject > Create Empty`
2. Rename to "NodeMarker"
3. Add `NodeMarkerView` component
4. Add `Button` component (for click handling)
5. Create structure:
   ```
   NodeMarker (NodeMarkerView + Button)
   ├─ Circle (Image) - white circle, size 60x60
   ├─ NameText (TextMeshPro) - node name, below circle
   ├─ TaskBarContainer (GameObject)
   │  └─ (TaskBars will be instantiated here)
   ├─ AttentionBadge (Image) - red circle, size 20x20, top-right
   └─ AnomalyPinsContainer (GameObject)
      └─ (Pins will be instantiated here)
   ```
6. Configure components:
   - **NodeMarkerView**: Assign all child references
   - **Button**: Set target graphic to Circle Image
7. Save as prefab: `Assets/Prefabs/UI/Map/NodeMarker.prefab`

### 1.3 Create HQMarker Prefab

1. Similar to NodeMarker but simpler
2. Just a circle with "HQ" text
3. No task bars or pins needed
4. Save as: `Assets/Prefabs/UI/Map/HQMarker.prefab`

### 1.4 Create TaskBar Prefab

1. Create: `GameObject > UI > Panel`
2. Rename to "TaskBar"
3. Add `TaskBarView` component
4. Create structure:
   ```
   TaskBar (TaskBarView)
   ├─ AgentAvatarsContainer (Horizontal Layout Group)
   │  └─ (Agent avatars instantiated here)
   ├─ ProgressBar (Slider)
   └─ StatusText (TextMeshPro)
   ```
5. Configure:
   - **Panel**: Background color, size (e.g., 200x40)
   - **Horizontal Layout Group**: Spacing 5, padding 5
   - **ProgressBar**: Min 0, Max 1, size (150x10)
6. Save as: `Assets/Prefabs/UI/Map/TaskBar.prefab`

### 1.5 Create AgentAvatar Prefab

1. Create: `GameObject > Create Empty`
2. Rename to "AgentAvatar"
3. Create structure:
   ```
   AgentAvatar
   ├─ Avatar (Image) - circle, size 30x30
   ├─ HP (TextMeshPro) - small text, below avatar
   └─ SAN (TextMeshPro) - small text, below HP
   ```
4. Save as: `Assets/Prefabs/UI/Map/AgentAvatar.prefab`

### 1.6 Create AnomalyPin Prefab

1. Create: `GameObject > Create Empty`
2. Rename to "AnomalyPin"
3. Add `AnomalyPinView` component
4. Add `Button` component
5. Create structure:
   ```
   AnomalyPin (AnomalyPinView + Button)
   ├─ IconText (TextMeshPro) - large text for emoji/symbol
   └─ IconImage (Image) - background circle, size 40x40
   ```
6. Save as: `Assets/Prefabs/UI/Map/AnomalyPin.prefab`

## Step 2: Update Main Scene

### 2.1 Add SimpleWorldMapPanel to Scene

1. Open `Assets/Scenes/Main.unity`
2. Find the Canvas in Hierarchy
3. Drag `SimpleWorldMapPanel.prefab` into Canvas as a child
4. Position it to fill the screen (or as desired)
5. In SimpleWorldMapPanel component:
   - Assign **Node Marker Prefab**: NodeMarker.prefab
   - Assign **HQ Marker Prefab**: HQMarker.prefab

### 2.2 Configure NodeMarker Prefab References

1. Open NodeMarker prefab
2. In NodeMarkerView component:
   - Assign **Task Bar Prefab**: TaskBar.prefab
   - Assign **Anomaly Pin Prefab**: AnomalyPin.prefab
3. Apply prefab changes

### 2.3 Configure TaskBar Prefab References

1. Open TaskBar prefab
2. In TaskBarView component:
   - Assign **Agent Avatar Prefab**: AgentAvatar.prefab
3. Apply prefab changes

### 2.4 Add DispatchLineFX

1. In Canvas, create empty GameObject "DispatchLineFX"
2. Add `DispatchLineFX` component
3. Position as sibling to SimpleWorldMapPanel (same level)
4. Configure optional prefabs if you want custom visuals:
   - **Line Prefab**: (optional) custom line visual
   - **Dispatch Icon Prefab**: (optional) custom moving icon
   - **Completion Icon Prefab**: (optional) custom completion effect

### 2.5 Hide Old Map System

**Option A: Using MapSystemManager (Recommended)**
1. In Canvas, create empty GameObject "MapSystemManager"
2. Add `MapSystemManager` component
3. In component:
   - Assign **Old Map System**: Drag the old map GameObject (likely MapNodeSpawner's parent)
   - Assign **Simple World Map Panel**: Drag SimpleWorldMapPanel
   - Check **Use Simple Map**: true
4. The manager will auto-disable the old map on Start

**Option B: Manual**
1. Find the old map GameObject in Hierarchy (has MapNodeSpawner)
2. Either:
   - Disable it (uncheck in Inspector)
   - Delete it if no longer needed

## Step 3: Test in Play Mode

1. Press Play in Unity Editor
2. Verify:
   - ✓ Background is solid color (dark blue/gray)
   - ✓ HQ marker visible at bottom
   - ✓ N1, N2, N3 markers visible at their positions
   - ✓ Node names displayed (or fallback to IDs)
   - ✓ Clicking nodes opens panels
3. Test task assignment:
   - Open a node, assign investigate task
   - Should see dispatch line animation from HQ to node
   - Task bar should appear on node marker
4. Test anomaly interactions:
   - When anomalies spawn, pins should appear
   - Clicking pins should open appropriate panels

## Step 4: WebGL Build

1. File > Build Settings
2. Select WebGL platform
3. Click "Build and Run" or "Build"
4. Test in browser:
   - All visuals should work
   - No excessive logging (only [MapUI] on state changes)
   - Animations should be smooth

## Troubleshooting

### Issue: Prefabs not showing

- Check all references are assigned in components
- Verify prefabs are in correct folders
- Check Console for errors

### Issue: Old map still visible

- Ensure MapSystemManager is properly configured
- Or manually disable old map GameObject

### Issue: Dispatch animations not playing

- Check DispatchLineFX is in scene and enabled
- Verify it's subscribed to GameController.OnStateChanged
- Check Console for [MapUI] task state change logs

### Issue: Task bars not showing agents

- Verify AgentAvatar prefab is assigned in TaskBar
- Check task has AssignedAgentIds populated
- Look for errors in Console

## Additional Customization

### Colors

Adjust colors in prefab components:
- **NodeMarkerView**: normalColor, anomalyColor
- **AnomalyPinView**: unknownColor, discoveredColor, etc.
- **SimpleWorldMapPanel**: backgroundColor

### Positions

Edit node positions in `SimpleWorldMapPanel.cs`:
```csharp
private readonly Dictionary<string, Vector2> _nodePositions = new Dictionary<string, Vector2>
{
    ["HQ"] = new Vector2(0, -200),
    ["N1"] = new Vector2(-300, 100),
    ["N2"] = new Vector2(300, 100),
    ["N3"] = new Vector2(0, 250)
};
```

### Animation Timing

Adjust in `DispatchLineFX.cs`:
- `lineAnimDuration`: How long dispatch line animation takes
- `completionDisplayDuration`: How long completion icon shows
- `iconMoveSpeed`: Speed of moving icon

## Notes

- All scripts use `[MapUI]` log prefix for easy filtering
- Logging only happens on state changes, not every frame
- System is WebGL-compatible and optimized
- Prefabs are modular and can be customized independently
