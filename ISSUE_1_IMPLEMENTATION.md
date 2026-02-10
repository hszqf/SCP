# Issue #1 Implementation: SimpleWorldMapPanel Node Rendering

## Overview
This document describes the implementation of changes to ensure SimpleWorldMapPanel properly renders all nodes (HQ + N1 at minimum) and supports clicking to open NodePanel.

## Problem Statement
The original issue was that the WebGL logs showed only "Spawned HQ marker" and even though the Fact system generated anomalies in N1, the UI did not display the N1 node. This was due to a race condition where the map initialization happened before GameController finished initializing its State.Nodes.

## Root Cause
`SimpleWorldMapBootstrap.Initialize()` was called via `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)`, which runs immediately after scene load but **before** `GameController.Start()` coroutine completes. Since GameController loads data and initializes nodes in its Start() coroutine (especially for WebGL which loads from remote URLs), the map tried to spawn nodes before they existed.

## Solution
Changed SimpleWorldMapBootstrap to use a coroutine-based initialization pattern that waits for GameController to be fully ready:

### Key Changes

#### 1. SimpleWorldMapBootstrap.cs
**Before**: Direct initialization in static method
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void Initialize()
{
    // Immediately tried to create map
    var canvas = Object.FindAnyObjectByType<Canvas>();
    CreateSimpleWorldMapPanel(canvas.transform);
}
```

**After**: Coroutine-based initialization with WaitUntil
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void Initialize()
{
    // Create a temporary MonoBehaviour to run coroutine
    var bootstrapObj = new GameObject("SimpleWorldMapBootstrap_Runner");
    var runner = bootstrapObj.AddComponent<BootstrapRunner>();
    Object.DontDestroyOnLoad(bootstrapObj);
}

private class BootstrapRunner : MonoBehaviour
{
    private System.Collections.IEnumerator InitializeWhenReady()
    {
        // Wait for GameController to be fully initialized
        yield return new WaitUntil(() => 
            GameController.I != null && 
            GameController.I.State != null && 
            GameController.I.State.Nodes != null && 
            GameController.I.State.Nodes.Count > 0
        );
        
        // Now create the map
        CreateSimpleWorldMapPanel(canvas.transform);
        
        // Clean up the temporary runner
        Destroy(gameObject);
    }
}
```

#### 2. Enhanced Logging
Added comprehensive [MapUI] prefixed logging to track initialization:

- `[MapUI] MapBootstrap waiting for GameController initialization...` - Start of wait
- `[MapUI] MapBootstrap nodesReady count=X` - When nodes are ready
- `[MapUI] MapBootstrap creating SimpleWorldMapPanel programmatically...` - Start creation
- `[MapUI] MapBootstrap ✅ SimpleWorldMapPanel created successfully` - Success
- `[MapUI] MapBootstrap will spawn markers for X nodes` - About to spawn
- `[MapUI] Spawned HQ marker` - HQ spawned
- `[MapUI] Spawned marker for node X` - Each city node spawned
- `[MapUI] SpawnMarkers complete: HQ=true, CityNodes=X [N1, N2, N3]` - Summary

#### 3. SimpleWorldMapPanel.cs Enhancements
Added tracking and summary logging in SpawnMarkers():
- Track `hqSpawned` boolean
- Count `spawnedCount` for city nodes
- Log final summary with node list

## Verification Criteria

### Expected Logs
When running in Unity/WebGL, you should see this log sequence:

```
[Boot] Platform=WebGLPlayer ...
[Boot] Try remote URL #1: ...
[Boot] Remote URL OK #1: length=...
[Boot] InitFromJson succeeded
[Boot] Calling InitGame
[DataRegistry] Nodes: 3 loaded
[Boot] InitGame completed
[MapUI] MapBootstrap waiting for GameController initialization...
[MapUI] MapBootstrap nodesReady count=3
[MapUI] MapBootstrap creating SimpleWorldMapPanel programmatically...
[MapUI] MapBootstrap ✅ SimpleWorldMapPanel created successfully
[MapUI] MapBootstrap will spawn markers for 3 nodes
[MapUI] Spawned HQ marker
[MapUI] Spawned marker for node N1
[MapUI] Spawned marker for node N2
[MapUI] Spawned marker for node N3
[MapUI] SpawnMarkers complete: HQ=true, CityNodes=3 [N1, N2, N3]
```

### Functional Verification
1. **HQ marker appears** - Blue circle with "HQ" text at bottom center
2. **At least N1 node appears** - White dot with "Node Name" text
3. **Clicking any node** - Should call `UIPanelRoot.I.OpenNode(nodeId)` and display NodePanel
4. **Event badges** - Should appear on nodes with pending events
5. **Task bars** - Should appear below nodes with active tasks

## Node Click Flow
```
User clicks node marker
  ↓
NodeMarkerView.Button.onClick fires
  ↓
NodeMarkerView calls Bind callback with nodeId
  ↓
SimpleWorldMapPanel.OnNodeClick(nodeId)
  ↓
Log: [MapUI] Node clicked: {nodeId}
  ↓
UIPanelRoot.I.OpenNode(nodeId)
  ↓
NodePanel appears showing node details
```

## Files Modified
1. `Assets/Scripts/Runtime/SimpleWorldMapBootstrap.cs` - Async initialization
2. `Assets/Scripts/UI/Map/SimpleWorldMapPanel.cs` - Enhanced logging

## Technical Notes

### Why BootstrapRunner?
- Static methods cannot use coroutines (Unity limitation)
- Need MonoBehaviour to run `StartCoroutine()`
- Create temporary GameObject with MonoBehaviour
- Self-destructs after initialization completes

### WaitUntil Conditions
All conditions must be true before proceeding:
1. `GameController.I != null` - Singleton instantiated
2. `GameController.I.State != null` - State object created
3. `GameController.I.State.Nodes != null` - Nodes list exists
4. `GameController.I.State.Nodes.Count > 0` - At least one node loaded

### Node Position Mapping
SimpleWorldMapPanel has hardcoded positions for up to 4 nodes:
- `HQ` - (0, -200) - Bottom center
- `N1` - (-300, 100) - Left
- `N2` - (300, 100) - Right
- `N3` - (0, 250) - Top

Only nodes present in both `GameController.I.State.Nodes` and `_nodePositions` dictionary will be spawned.

## Future Improvements
1. Dynamic positioning based on node coordinates from data
2. Support for more than 3 city nodes
3. Timeout mechanism if GameController never initializes
4. Better error messages if specific nodes fail to spawn

## Testing in Unity Editor
1. Open Main.unity scene
2. Press Play
3. Check Console for [MapUI] logs
4. Verify nodes appear on map
5. Click each node to verify NodePanel opens
6. Check that event badges appear for nodes with events
7. Verify task bars show for nodes with active tasks

## Testing in WebGL Build
1. Build for WebGL platform
2. Run in browser
3. Open browser console (F12)
4. Look for [MapUI] logs
5. Verify nodes appear after data loads from remote URL
6. Test node clicking functionality
