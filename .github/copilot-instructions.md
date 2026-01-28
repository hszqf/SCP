# SCP Codebase Guide for AI Agents

## Project Overview

**SCP** is a tactical resource management game built in **Unity** with **C#**. The architecture follows a **data-driven simulation** pattern where game mechanics are defined in JSON config files and applied dynamically at runtime.

### Core Technology Stack
- **Engine**: Unity (C#)
- **Data Format**: JSON (`GameData/game_data.json`)
- **Serialization**: Newtonsoft.Json (Newtonsoft.Json NuGet)
- **Architecture Pattern**: Data-Registry + Event-Driven Simulation

---

## Essential Architecture

### The Three Pillars

1. **Data Layer** (`Assets/Scripts/Data/`)
   - `DataRegistry`: Singleton that loads and manages all game data
   - `GameDataModels`: JSON-serializable definitions (NodeDef, AnomalyDef, TaskDef, EventDef, EffectDef)
   - `EffectOpExecutor`: Applies stat changes to GameState based on effect operations
   - All game rules come from JSON, not hardcoded logic

2. **Simulation Engine** (`Assets/Scripts/Core/`)
   - `Sim.StepDay()`: Main game loop that progresses tasks, spawns events, manages anomalies
   - `GameState`: Complete runtime state (agents, nodes, tasks, events, resources)
   - `NodeTask`: Task model supporting unlimited per-node tasks (Investigate/Contain/Manage)
   - Event-driven architecture via `EventInstance` + effect system

3. **UI Layer** (`Assets/Scripts/UI/`)
   - `UIPanelRoot`: Central hub managing all UI panels
   - `GameController`: Bridge between UI and Sim (state mutations + notifications)
   - Panel-based architecture: NodePanelView, EventPanel, NewsPanel, AgentPickerView, etc.
   - Single-responsibility: each panel handles one domain

---

## Critical Workflows

### Daily Simulation Cycle
```csharp
// In GameController.EndDay():
Sim.StepDay(State, _rng);  // Executes entire day logic
Notify();                    // Notifies UI of state changes
RefreshMapNodes();          // Updates visual representation
```

The `StepDay()` method (1350+ lines) is the heartbeat:
1. Spawn anomalies (15% chance per calm node)
2. Advance active tasks with progress calculations
3. Trigger events based on probabilities
4. Apply event effects (stat modifications)
5. Generate news items for UI

### Task Assignment Flow
- UI creates `NodeTask` → calls `GameController.TryAssignAgent()` → updates `AssignedAgentIds` list
- **Key**: Multiple tasks per node are allowed; each task is independent
- **Manage tasks** have special logic: progress caps at 0.99 and never auto-completes

### Event Handling
```csharp
// EventInstance + EventDef + EffectDef form a pipeline:
1. Event triggered (NodeEvents.cs factory)
2. Options presented to player
3. Selected option → EffectDef applied
4. EffectOpExecutor modifies state (panic, money, progress, etc.)
```

---

## Project-Specific Conventions

### Naming & Location Patterns
- **C# Classes**: PascalCase, paired with `.meta` files
- **IDs**: String identifiers with prefixes: `N1` (nodes), `A1` (agents), `E1` (events), `EVI_` (event instances)
- **"Canvas-maintained" files**: Header comments indicate files are actively refactored; preserve those comments
- **Namespace organization**: `Core`, `Data`, `UI` (no `Runtime` namespace in scripts, but `Runtime/` folder contains orchestration)

### Data-Driven Patterns
- **Never hardcode game rules**: If values might change, they belong in `balance` table or definitions
- **Validation**: `GameDataValidator` warns about missing or invalid definitions at load time
- **Warning systems**: Registry uses `-WithWarn()` methods to log issues without crashing

### Task Progress Calculation
```csharp
// Formula: baseDelta / difficulty = effective progress per day
// baseDelta = (squad.Count * agent.Perception) / baseDays
// Manage tasks: capped at 0.99, never complete
// Other tasks: progress from 0 to baseDays
```

### Effect Scope System
- **Node**: Affects panic/population of a specific node
- **OriginTask**: Affects the task that triggered the event
- **Global**: Affects worldPanic/money/negEntropy
- **TaskType**: Affects all active tasks of a type (Investigate/Contain/Manage)

---

## Key Files & Their Responsibilities

| File | Purpose | Key Methods |
|------|---------|-------------|
| `Sim.cs` | Game simulation engine | `StepDay()`, `ResolveEvent()`, task/event logic |
| `GameState.cs` | Data structures | NodeState, NodeTask, GameState, AgentState, etc. |
| `DataRegistry.cs` | Config loader & accessor | `GetTaskBaseDaysWithWarn()`, `GetBalance*()` |
| `GameController.cs` | State manager & mediator | `EndDay()`, `ResolveEvent()`, `TryAssign*()` |
| `UIPanelRoot.cs` | UI orchestrator | Panel instantiation & navigation |
| `EffectOpExecutor.cs` | Effect application | `ApplyEffect()` (stat modifications) |
| `NodeEvents.cs` | Event factory | `EventInstance` creation and lifecycle |

---

## Common Development Tasks

### Adding a New Stat/Balance Value
1. Define in `GameData/game_data.json` → `balance` dict
2. Access via: `DataRegistry.Instance.GetBalanceIntWithWarn("StatName", defaultValue)`
3. Apply in effects via `EffectDef` with proper `EffectOpRow` entries

### Modifying Task Logic
- Core mechanics: `Sim.StepDay()` → task progress section
- Difficulty calculation: `Sim.GetTaskDifficulty()` (anomaly-based)
- Progress per day: `Sim.CalcDailyProgressDelta()` (agent-based)

### Adding UI Panels
1. Create script inheriting `MonoBehaviour`
2. Prefab in `Assets/Prefabs/UI/`
3. Reference & instantiate in `UIPanelRoot`
4. Subscribe to `GameController.OnStateChanged` for updates

### Event Triggering
- Use `EventInstanceFactory.Create()` to instantiate
- Add to `node.PendingEvents`
- `Sim.ResolveEvent()` applies selected option's effect

---

## Testing & Debugging

- **Debug seed**: `GameController` has configurable `seed` field (same seed = reproducible run)
- **Logging**: All major decisions logged with `[Tag] day=X context=Y` format
- **State inspection**: `GameController.State` is public; breakpoint-friendly structure

---

## Integration Points

- **JSON Loading**: `DataRegistry.LoadFromStreamingAssets()` reads from `GameData/game_data.json`
- **UI Notifications**: `GameController.OnStateChanged` fires after every mutation
- **Random Generation**: Centralized `System.Random` in GameController; passed to Sim
- **Effect System**: Open-ended `EffectDef` allows new stat modifications without code changes

---

## Red Flags & Gotchas

1. **Null Node/Task Checks**: Always null-check `node.Tasks` before iterating
2. **Manage Tasks**: Have special progress logic (caps at 0.99, events on progress change, not completion)
3. **Event Blocking**: Tasks can be blocked by events; check `IsTaskBlockedByEvents()` before progress
4. **Agent Busy State**: Uses global scan across all nodes; `GameControllerTaskExt.AreAgentsBusy()`
5. **Legacy Compatibility**: Single-task fields remain for UI compatibility; prefer `NodeTask` collections

---

## Quick Navigation

- **Game Loop**: `GameController.EndDay()` → `Sim.StepDay()`
- **Event Flow**: `Sim.GetPendingEvents()` → `Sim.ResolveEvent()` → `EffectOpExecutor.ApplyEffect()`
- **UI Updates**: `OnStateChanged` → Panel's `OnGameStateChanged()` handlers
- **Data Access**: Always via `DataRegistry.Instance` singleton
