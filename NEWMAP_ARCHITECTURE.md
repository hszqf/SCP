# NewMap System Architecture

## Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      Main.unity Scene                        │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  Canvas                                                       │
│  ├── MapRoot (OLD - HIDDEN via UseNewMap)                   │
│  │   ├── Image (background)                                  │
│  │   └── NodeLayer                                           │
│  │       └── MapNodeSpawner (script)                         │
│  │           ├── UseNewMap = true ✓                          │
│  │           └── mapRoot → MapRoot reference                 │
│  │                                                            │
│  ├── MapBootstrap (NEW)                                      │
│  │   └── NewMapRuntime (script)                              │
│  │       └── Creates runtime UI ↓                            │
│  │                                                            │
│  └── NewMapRoot (CREATED AT RUNTIME)                         │
│      ├── Background (Image)                                  │
│      ├── NodesRoot                                           │
│      │   ├── NodeWidget_BASE                                 │
│      │   │   ├── Dot                                         │
│      │   │   ├── Name                                        │
│      │   │   ├── TaskBarRoot                                 │
│      │   │   ├── EventBadge                                  │
│      │   │   └── UnknownAnomIcon                             │
│      │   ├── NodeWidget_N1                                   │
│      │   ├── NodeWidget_N2                                   │
│      │   └── NodeWidget_N3                                   │
│      └── CityPanel (popup)                                   │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

## Execution Flow

```
1. Game Start
   ↓
2. MapNodeSpawner.Start()
   ↓
3. Check UseNewMap?
   ├─ true → Hide MapRoot, Skip Build()
   └─ false → Show MapRoot, Run Build()
   ↓
4. MapBootstrap.Start()
   ↓
5. NewMapRuntime.Start()
   ↓
6. Wait one frame (ensure GameState ready)
   ↓
7. NewMapRuntime.Initialize()
   ├─ Find Canvas
   ├─ Check old MapRoot status
   ├─ Create NewMapRoot structure
   ├─ Get node data (GameState or fallback)
   ├─ Create 4 NodeWidgets
   └─ Log verification
   ↓
8. Game Running
   ↓
9. User clicks node
   ↓
10. OnNodeClick(nodeId)
    ├─ Log click
    └─ Show CityPanel
```

## Node Layout

```
Screen Layout (relative positioning):

   +----------------------------------------+
   |                                        |
   |   BASE                     N1          |
   |    ●                        ●          |
   |   BASE                     N1          |
   |    ?                        ?          |
   |                                        |
   |                                        |
   |                                        |
   |   N2                       N3          |
   |    ●                        ●          |
   |   N2                       N3          |
   |    ?                        ?          |
   |                                        |
   +----------------------------------------+

Legend:
  ● = Node dot (blue circle/square)
  Text = Node name label
  ? = Unknown anomaly icon (yellow)
```

## Data Flow

```
GameState (Priority 1)
   ↓
GameController.I.State.Nodes
   ↓
NewMapRuntime.GetNodeData()
   ↓
   ├─ Has nodes? → Use first 4
   │     ↓
   │   Log: "source=GameState"
   │     ↓
   │   Return [node.Id, ...]
   │
   └─ No nodes? → Use hardcoded fallback
         ↓
       Log: "source=Hardcoded"
         ↓
       Return ["BASE", "N1", "N2", "N3"]
```

## State Management

```
┌──────────────┐
│ MapNodeSpawner│
│ (Controller) │
└───────┬──────┘
        │
        ├─ UseNewMap flag
        │  ├─ true → Disable old system
        │  └─ false → Enable old system
        │
        └─ mapRoot reference
           └─ SetActive(false) when UseNewMap=true
```

## Click Interaction

```
User Click
   ↓
NodeWidget Button.onClick
   ↓
NewMapRuntime.OnNodeClick(nodeId)
   ↓
   ├─ Log: "[MapUI] Click nodeId=..."
   │
   └─ CityPanel.SetActive(true)
      ├─ Update Title text
      └─ Show panel
```

## Logging Timeline

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

## Key Design Patterns

1. **Toggle Pattern**: UseNewMap flag for easy switching
2. **Runtime Generation**: All UI created dynamically in code
3. **Fallback Pattern**: GameState → Hardcoded data
4. **Singleton Access**: GameController.I, NewMapRuntime.Instance
5. **Coroutine Delay**: Wait one frame for initialization safety
6. **Anchor-based Layout**: Responsive positioning
7. **Component-based UI**: GameObject hierarchy

## Future Integration Points

```
NewMapRuntime
   ↓
RefreshNodes() ← (Future) Subscribe to GameController.OnStateChanged
   ↓
Update visuals based on:
   ├─ TaskBarRoot ← Active tasks from GameState
   ├─ EventBadge ← Pending events from GameState
   └─ UnknownAnomIcon ← Anomaly status from GameState
```

## File Dependencies

```
NewMapRuntime.cs
   ├─ using Core (GameController)
   ├─ using UnityEngine (GameObject, Transform, etc.)
   └─ using UnityEngine.UI (Image, Text, Button)

MapNodeSpawner.cs
   ├─ using Core (GameController)
   └─ using UnityEngine (MonoBehaviour)

Main.unity
   ├─ MapRoot (existing)
   ├─ MapBootstrap (new)
   └─ MapNodeSpawner configuration (modified)
```

## Testing Checklist

- [ ] MapRoot is hidden (inactive in hierarchy)
- [ ] NewMapRoot is created at runtime
- [ ] 4 nodes visible in corners
- [ ] Each node has: Dot, Name, TaskBarRoot, EventBadge, UnknownAnomIcon
- [ ] Click on node shows CityPanel
- [ ] CityPanel displays correct nodeId
- [ ] Close button works
- [ ] All logs appear in console
- [ ] No errors or warnings in console
- [ ] Works in WebGL build
