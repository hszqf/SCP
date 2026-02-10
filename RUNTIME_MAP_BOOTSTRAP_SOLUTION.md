# SimpleWorldMapPanel Runtime Initialization Fix

## Problem Summary

The game was showing errors in the logs indicating that:
1. `SimpleWorldMapPanel NOT in scene` - The new map UI component was missing
2. `NodeMarkerPrefab is not assigned` - The NewMapRuntime component couldn't find its prefab reference

These errors occurred because the game required manual Unity Editor setup via `Tools > SCP > Setup Simple Map (Full)`, which doesn't work for WebGL builds or runtime-only environments.

## Root Cause

The game has **three different map systems**:
1. **Old map system** (MapNodeSpawner) - The original UI, can be disabled via `UseNewMap=true`
2. **NewMapRuntime** - An intermediate dynamic map system attached to MapBootstrap GameObject
3. **SimpleWorldMapPanel** - The newest prefab-based system (preferred)

The issue was that:
- Old map was properly disabled (✓)
- NewMapRuntime was still active and trying to initialize but missing its prefab reference (✗)
- SimpleWorldMapPanel was not instantiated in the scene (✗)

## Solution

Created `SimpleWorldMapBootstrap.cs` - a runtime initialization script that:

1. **Runs automatically at startup** using `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`
2. **Checks if SimpleWorldMapPanel exists** - skips creation if already present (for manual setups)
3. **Disables old NewMapRuntime** - deactivates MapBootstrap GameObject to prevent conflicts
4. **Creates SimpleWorldMapPanel programmatically** with full UI hierarchy:
   - Background image
   - Map container
   - Node marker prefabs (created dynamically)
   - HQ marker prefabs (created dynamically)
5. **Uses reflection** to set private SerializeField values on components

## Technical Details

### Bootstrap Initialization Flow

```
RuntimeInitializeOnLoadMethod(AfterSceneLoad)
  ↓
Check if SimpleWorldMapPanel exists
  ├─ Yes → Skip creation (already set up manually)
  └─ No → Continue
     ↓
Find Canvas in scene
  ↓
Disable MapBootstrap GameObject (old NewMapRuntime)
  ↓
Create SimpleWorldMapPanel programmatically
  ├─ Create panel GameObject
  ├─ Add RectTransform (fullscreen)
  ├─ Add Image component (background)
  ├─ Create MapContainer child
  ├─ Add SimpleWorldMapPanel component
  ├─ Create NodeMarker prefab
  │  ├─ NodeMarkerView component
  │  ├─ Button component
  │  ├─ Visual elements (dot, text, task bar, badges)
  │  └─ Wire up all references via reflection
  ├─ Create HQMarker prefab
  │  └─ Visual elements (circle, text)
  └─ Assign prefabs to SimpleWorldMapPanel
```

### NodeMarker Prefab Structure

The dynamically created NodeMarker prefab includes:
- **Dot** (Image) - The node circle visual
- **NameText** (Text) - Node name display
- **TaskBar** (RectTransform container) - Task information display
  - AvatarsContainer (HorizontalLayoutGroup)
  - AvatarTemplate (Image, inactive)
  - StatsText (Text) - HP/SAN display
  - ProgressBg (Image) - Progress bar background
  - ProgressFill (Image) - Progress bar fill
- **EventBadge** (GameObject) - Event notification badge
  - EventBadgeText (Text) - Event count
- **UnknownIcon** (GameObject) - Unknown anomaly indicator

### HQMarker Prefab Structure

The dynamically created HQMarker prefab includes:
- **Circle** (Image) - HQ circle visual
- **Text** (Text) - "HQ" label

### Reflection Usage

Since SimpleWorldMapPanel and NodeMarkerView use private `[SerializeField]` fields, the bootstrap uses reflection to set these values:

```csharp
var type = typeof(SimpleWorldMapPanel);
var field = type.GetField("mapContainer", BindingFlags.NonPublic | BindingFlags.Instance);
field.SetValue(mapPanel, containerRT);
```

This approach allows the runtime bootstrap to configure components without modifying the original component classes.

## Benefits

1. **WebGL Compatibility** - Works in WebGL builds without Editor setup
2. **Zero Manual Setup** - No need to run Unity Editor menu commands
3. **Backward Compatible** - Skips creation if SimpleWorldMapPanel already exists
4. **Automatic Conflict Resolution** - Disables old NewMapRuntime to prevent errors
5. **Complete UI Generation** - Creates all necessary prefabs and components at runtime

## Testing

### Expected Logs (Success)

```
[MapBootstrap] SimpleWorldMapBootstrap starting...
[MapBootstrap] Creating SimpleWorldMapPanel programmatically...
[MapBootstrap] Disabling old NewMapRuntime system
[MapBootstrap] SimpleWorldMapPanel component configured
[MapBootstrap] NodeMarker prefab created
[MapBootstrap] HQMarker prefab created
[MapBootstrap] ✅ SimpleWorldMapPanel created successfully
[MapUI] Initializing simple world map
[MapUI] Spawned HQ marker
[MapUI] Spawned marker for node N1
[MapUI] Spawned marker for node N2
[MapUI] Spawned marker for node N3
```

### Expected Logs (Already Setup)

```
[MapBootstrap] SimpleWorldMapBootstrap starting...
[MapBootstrap] SimpleWorldMapPanel already exists, skipping creation
```

### Error Cases

**Error: Canvas not found**
```
[MapBootstrap] Cannot find Canvas to attach SimpleWorldMapPanel
```
- **Cause**: No Canvas component in scene
- **Fix**: Ensure Main.unity has a Canvas GameObject

**Error: Failed to create SimpleWorldMapPanel**
```
[MapBootstrap] ❌ Failed to create SimpleWorldMapPanel
```
- **Cause**: Exception during creation (check stack trace)
- **Fix**: Review Console for detailed error message

## Files Added

- `Assets/Scripts/Runtime/SimpleWorldMapBootstrap.cs` - Main bootstrap script (340 lines)
- `Assets/Scripts/Runtime/SimpleWorldMapBootstrap.cs.meta` - Unity meta file

## Code Quality

- ✅ Code review passed (no issues)
- ✅ CodeQL security scan passed (no vulnerabilities)
- ✅ Follows Unity best practices
- ✅ Uses [MapBootstrap] log prefix for debugging
- ✅ Comprehensive error handling
- ✅ Minimal code changes (surgical fix)

## Integration with Existing Systems

### SimpleWorldMapPanel Component

The bootstrap creates a fully functional SimpleWorldMapPanel that:
- Subscribes to `GameController.OnStateChanged` events
- Spawns markers for all nodes in `GameState.Nodes`
- Handles node click events via `UIPanelRoot.OpenNode(nodeId)`
- Refreshes map display on state changes

### Diagnostic System

The existing `MapSystemDiagnostic.cs` will now report:
```
✓ SimpleWorldMapPanel found: SimpleWorldMapPanel
✓ SimpleWorldMapPanel active: True
```

Instead of the previous error:
```
❌ ISSUE FOUND: SimpleWorldMapPanel NOT in scene!
```

### Map System Manager

If `MapSystemManager` exists in the scene, it will manage the SimpleWorldMapPanel visibility. The bootstrap creates the panel but doesn't interfere with MapSystemManager's logic.

## Alternative Approaches Considered

1. **Resources Folder Loading** - Rejected because prefabs don't exist in Resources folder
2. **AssetBundle Loading** - Rejected due to complexity and build requirements
3. **Editor-Only Setup** - Current approach, but doesn't work for WebGL
4. **Fix NewMapRuntime** - Rejected because SimpleWorldMapPanel is the newer, preferred system

## Future Enhancements

Possible improvements for future versions:
1. **Configurable Styling** - Allow customization of colors, sizes via scriptable objects
2. **Prefab Caching** - Cache created prefabs to avoid recreation on scene reload
3. **Editor Integration** - Add context menu to manually trigger bootstrap in Editor
4. **Visual Validation** - Add visual indicators for successfully created components

## Migration Path

For projects with manual setup:
1. The bootstrap will detect existing SimpleWorldMapPanel and skip creation
2. No changes needed to existing manually configured scenes
3. Can safely remove MapBootstrap GameObject if no longer needed

For new deployments:
1. Bootstrap runs automatically
2. No Unity Editor access required
3. Works out of the box in WebGL builds

## References

- Issue #1 - Original bug report with logs
- `FIX_OLD_MAP_DISPLAY_ISSUE.md` - Manual setup documentation
- `README_SimpleWorldMap.md` - SimpleWorldMap feature documentation
- `MapSystemDiagnostic.cs` - Diagnostic script that detects the issue
- `SimpleWorldMapPanel.cs` - Main map component
- `NodeMarkerView.cs` - Node display component

## Version History

- **v1.0** (2026-02-10) - Initial implementation
  - Added SimpleWorldMapBootstrap.cs
  - Runtime prefab generation
  - Automatic conflict resolution with NewMapRuntime
  - Reflection-based component configuration

---

**Status**: ✅ Complete - Ready for testing in WebGL build
**Impact**: High - Fixes critical map display issue in production
**Risk**: Low - Backward compatible, only runs when needed
