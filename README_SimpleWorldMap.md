# Simple World Map UI Implementation

## Overview

This implementation adds a simplified world map UI to the SCP game, featuring:

- **Visual Map**: HQ + 3 cities (N1, N2, N3) on a solid color background
- **Node Markers**: Display city names, task bars with agent info, attention badges, and anomaly pins
- **Task Bars**: Show active tasks with agent avatars, HP/SAN stats, and progress bars
- **Anomaly Pins**: Visual indicators for different anomaly states (Unknown/Discovered/Contained/Managed)
- **Dispatch Animations**: Animated flying lines and icons when tasks start/complete
- **Panel Integration**: Click interactions open appropriate panels (investigate/contain/manage)

## Quick Start

### For Unity Editor Users

1. **Generate Prefabs** (One-time setup):
   - Open Unity Editor
   - Go to `Tools > SCP > Generate Simple Map Prefabs`
   - Click "Generate All Prefabs"
   - Prefabs will be created in `Assets/Prefabs/UI/Map/`

2. **Setup Scene**:
   - Open `Assets/Scenes/Main.unity`
   - Drag `SimpleWorldMapPanel.prefab` into the Canvas
   - Assign prefab references:
     - In SimpleWorldMapPanel: assign NodeMarker and HQMarker prefabs
     - In NodeMarker: assign TaskBar and AnomalyPin prefabs
     - In TaskBar: assign AgentAvatar prefab
   - Add `DispatchLineFX` component to Canvas
   - Optional: Add `MapSystemManager` to manage old/new map switching

3. **Play**:
   - Press Play in Unity Editor
   - Map should display with HQ and 3 cities
   - Assign tasks to see animations

### For WebGL Build

The system is WebGL-compatible. Build normally:
```
File > Build Settings > WebGL > Build
```

## Architecture

### Core Components

1. **SimpleWorldMapPanel.cs**
   - Main map controller
   - Spawns and manages node markers
   - Provides position lookup for animations

2. **NodeMarkerView.cs**
   - Individual city marker
   - Displays node info, task bars, pins, badges
   - Handles click to open panels

3. **AnomalyPinView.cs**
   - Visual indicator for anomaly states
   - Handles click to open appropriate panels
   - States: Unknown(?), Discovered(⚠), Contained(🔒), Managed(⚡)

4. **TaskBarView.cs**
   - Displays active tasks on node markers
   - Shows agent avatars with HP/SAN
   - Progress bar and status text
   - Robust: handles missing/placeholder agents

5. **DispatchLineFX.cs**
   - Animated dispatch lines from HQ to nodes
   - Moving icons during task start
   - Completion animations (✓/✗)
   - Monitors task state changes automatically

6. **MapSystemManager.cs**
   - Manages visibility of old vs new map
   - Toggle between systems
   - Useful during transition period

### Data Flow

```
GameController.OnStateChanged
  ↓
SimpleWorldMapPanel.RefreshMap()
  ↓
NodeMarkerView.Refresh()
  ├─ RefreshNodeCircle (color)
  ├─ RefreshTaskBars (active tasks)
  ├─ RefreshAttentionBadge (pending actions)
  └─ RefreshAnomalyPins (anomaly states)

Task State Change
  ↓
DispatchLineFX.OnGameStateChanged()
  ↓
CheckForTaskStateChanges()
  ├─ Task Started → PlayDispatchAnimation()
  └─ Task Completed → PlayCompletionAnimation()
```

### Integration with Existing Systems

The new map integrates with existing panels:
- **NodePanelView**: Opens on city marker click (investigate/contain)
- **AnomalyManagePanel**: Opens on managed anomaly pin click
- **EventPanel**: Opens automatically for pending events
- **UIPanelRoot**: Central coordinator for all panels

No changes to Excel/game_data structure required.

## Customization

### Node Positions

Edit `SimpleWorldMapPanel.cs`:
```csharp
private readonly Dictionary<string, Vector2> _nodePositions = new Dictionary<string, Vector2>
{
    ["HQ"] = new Vector2(0, -200),
    ["N1"] = new Vector2(-300, 100),
    ["N2"] = new Vector2(300, 100),
    ["N3"] = new Vector2(0, 250)
};
```

### Colors

**Background**: Edit `SimpleWorldMapPanel` component or:
```csharp
[SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
```

**Node Colors**: Edit `NodeMarkerView`:
```csharp
[SerializeField] private Color normalColor = Color.white;
[SerializeField] private Color anomalyColor = new Color(1f, 0.3f, 0.3f);
```

**Pin Colors**: Edit `AnomalyPinView`:
```csharp
[SerializeField] private Color unknownColor = new Color(1f, 1f, 0f, 1f);      // Yellow
[SerializeField] private Color discoveredColor = new Color(1f, 0.3f, 0.3f, 1f); // Red
// ... etc
```

### Animation Timing

Edit `DispatchLineFX.cs`:
```csharp
[SerializeField] private float lineAnimDuration = 2.5f;
[SerializeField] private float completionDisplayDuration = 1f;
```

## Logging

All map-related logs use the `[MapUI]` prefix for easy filtering:
```
[MapUI] Initializing simple world map
[MapUI] Spawned HQ marker
[MapUI] Spawned marker for node N1
[MapUI] Task started: TASK_001 type=Investigate node=N1
[MapUI] Task completed: TASK_001 type=Investigate node=N1
```

Logs only appear on state changes, not every frame (WebGL-friendly).

## Testing Checklist

### Visual Tests
- [ ] Background is solid color (not texture)
- [ ] HQ marker visible at bottom center
- [ ] N1, N2, N3 markers visible at their positions
- [ ] Node names displayed (or IDs as fallback)
- [ ] Node circles change color when anomaly present

### Interaction Tests
- [ ] Click city marker opens NodePanel
- [ ] Click unknown pin (?) opens investigate panel
- [ ] Click discovered pin (⚠) opens contain panel
- [ ] Click managed pin (⚡) opens manage panel

### Task Bar Tests
- [ ] Assign investigate task → task bar appears on node
- [ ] Task bar shows agent avatars (or placeholders)
- [ ] HP/SAN values displayed correctly
- [ ] Progress bar updates as task progresses
- [ ] Task bar hides when task completes

### Animation Tests
- [ ] Start task → dispatch line animates HQ to node
- [ ] Moving icon travels along line
- [ ] Complete task → completion icon appears (✓)
- [ ] Line fades out after animation

### Badge Tests
- [ ] Unknown anomaly → attention badge shows
- [ ] Containable anomaly → attention badge shows
- [ ] Pending event → attention badge shows
- [ ] No pending actions → badge hidden

### Pin Tests
- [ ] Unknown anomaly → yellow "?" pin
- [ ] Discovered anomaly → red "⚠" pin
- [ ] Contained anomaly → green "🔒" pin
- [ ] Managed anomaly → blue "⚡" pin
- [ ] Pins positioned around node marker

### WebGL Tests
- [ ] Builds without errors
- [ ] Runs in browser
- [ ] Animations smooth (not laggy)
- [ ] No console spam
- [ ] Click interactions work

## Troubleshooting

### Problem: Prefabs not assigned

**Symptom**: Map shows but no markers appear

**Solution**:
1. Check SimpleWorldMapPanel component
2. Verify nodeMarkerPrefab and hqMarkerPrefab are assigned
3. Check Console for "[MapUI] MapContainer not assigned" errors

### Problem: Task bars not showing agents

**Symptom**: Task bar appears but no agent avatars

**Solution**:
1. Check TaskBarView component has agentAvatarPrefab assigned
2. Verify AgentAvatar prefab exists
3. Check task.AssignedAgentIds is populated

### Problem: Animations not playing

**Symptom**: No dispatch lines when starting tasks

**Solution**:
1. Verify DispatchLineFX component is in scene
2. Check DispatchLineFX.Instance is not null
3. Verify GameController.OnStateChanged is firing
4. Look for "[MapUI] Task started:" logs

### Problem: Old map still visible

**Symptom**: Both old and new maps showing

**Solution**:
1. Add MapSystemManager component to Canvas
2. Assign oldMapSystem and simpleWorldMapPanel references
3. Set useSimpleMap = true
4. Or manually disable old map GameObject

### Problem: Pins not clickable

**Symptom**: Can't click anomaly pins

**Solution**:
1. Verify AnomalyPinView has Button component
2. Check Button is enabled
3. Verify targetGraphic is assigned
4. Check pin is not behind other UI elements

## Performance Notes

- **Object Pooling**: Not implemented in v1.0; creates/destroys prefabs on demand
- **Update Loop**: Only runs on state changes, not every frame
- **WebGL**: Optimized for WebGL with minimal logging and efficient state tracking
- **Scalability**: Supports 4 markers (HQ + 3 cities) without performance issues

## Future Enhancements

Potential improvements (not in scope for v1.0):
- Object pooling for task bars and pins
- Zoom/pan map controls
- More cities/nodes
- Custom anomaly icons (not just emojis)
- Path-finding for dispatch lines (currently straight lines)
- Particle effects for animations
- Sound effects

## Files Overview

### Scripts
- `Assets/Scripts/UI/Map/SimpleWorldMapPanel.cs` - Main map controller
- `Assets/Scripts/UI/Map/NodeMarkerView.cs` - City marker view
- `Assets/Scripts/UI/Map/AnomalyPinView.cs` - Anomaly pin view
- `Assets/Scripts/UI/Map/TaskBarView.cs` - Task progress bar
- `Assets/Scripts/UI/Map/DispatchLineFX.cs` - Animation controller
- `Assets/Scripts/UI/Map/MapSystemManager.cs` - Old/new map manager
- `Assets/Scripts/Editor/SimpleMapPrefabGenerator.cs` - Prefab generator tool

### Prefabs (Generated)
- `Assets/Prefabs/UI/Map/SimpleWorldMapPanel.prefab`
- `Assets/Prefabs/UI/Map/NodeMarker.prefab`
- `Assets/Prefabs/UI/Map/HQMarker.prefab`
- `Assets/Prefabs/UI/Map/TaskBar.prefab`
- `Assets/Prefabs/UI/Map/AgentAvatar.prefab`
- `Assets/Prefabs/UI/Map/AnomalyPin.prefab`

### Documentation
- `Docs/SimpleWorldMapSetup.md` - Detailed Unity Editor setup guide
- `README_SimpleWorldMap.md` - This file

## Credits

- Implementation: Canvas (AI Agent)
- Based on: SCP tactical resource management game
- Unity Version: 2021.3+

## Support

For issues or questions:
1. Check this README
2. Review Docs/SimpleWorldMapSetup.md
3. Check Unity Console for [MapUI] logs
4. Verify prefab references are assigned
