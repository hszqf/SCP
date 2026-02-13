# SCP Codebase Guide for AI Agents

## Project Overview

**SCP** is a tactical resource management game built in **Unity** with **C#**. The architecture follows a **data-driven simulation** pattern where game mechanics are defined in JSON config files and applied dynamically at runtime.

### Core Technology Stack
- **Engine**: Unity (C#)
- **Data Format**: JSON (`GameData/game_data.json`)
- **Serialization**: Newtonsoft.Json (Newtonsoft.Json NuGet)
- **Architecture Pattern**: Data-Registry + Event-Driven Simulation + Task-Based Management

---

## Essential Architecture

### The Three Pillars

1. **Data Layer** (`Assets/Scripts/Data/`)
   - `DataRegistry`: Singleton that loads and manages all game data
   - `GameDataModels`: JSON-serializable definitions (NodeDef, AnomalyDef, TaskDef, EventDef, EffectDef, NewsDef, FactTypeDef)
   - `EffectOpExecutor`: Applies stat changes to GameState based on effect operations
   - All game rules come from JSON, not hardcoded logic

2. **Simulation Engine** (`Assets/Scripts/Core/`)
   - `Sim.StepDay()`: Main game loop that progresses tasks, spawns events, manages anomalies, generates news
   - `GameState`: Complete runtime state (agents, nodes, tasks, events, resources, facts)
   - `NodeTask`: Task model supporting unlimited per-node tasks (Investigate/Contain/Manage)
   - Event-driven architecture via `EventInstance` + effect system
   - Fact-based news generation via `FactSystem` + `FactNewsGenerator`

3. **UI Layer** (`Assets/Scripts/UI/`)
   - `UIPanelRoot`: Central hub managing all UI panels
   - `GameController`: Bridge between UI and Sim (state mutations + notifications)
   - Panel-based architecture: NodePanelView, EventPanel, NewsPanel, AgentPickerView, AnomalyManagePanel, etc.
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

The `StepDay()` method is the heartbeat:
1. Prune old facts (60-day retention)
2. Generate scheduled anomalies (AnomaliesGen table)
3. Advance active tasks with progress calculations & HP/SAN impacts
4. Step management tasks (NegEntropy production)
5. Apply idle agent recovery (10% HP/SAN per day)
6. Apply ignore penalties for unresolved events
7. Auto-resolve aged events (per autoResolveAfterDays)
8. Generate RandomDaily events (task-context + node-context)
9. Generate news: Fact-based (priority) + RandomDaily (backup)
10. Update economy & world panic
11. Check game-over conditions (world panic threshold)

### Task Assignment Flow (N-Task System)
- UI creates `NodeTask` → calls `GameController.CreateInvestigateTask/CreateContainTask/CreateManageTask`
- Multiple tasks per node are allowed; each task is independent
- Task lifecycle: Active → Completed/Cancelled
- **Investigate tasks**: Can target specific news or be generic; auto-locks anomaly based on team Perception
- **Contain tasks**: Requires known anomaly; produces ManagedAnomalyState on completion
- **Manage tasks**: Long-running (progress capped at 0.99); daily NegEntropy + EXP yield
- Progress formula: `baseDelta = squad match score (S) * progressScale`
- Daily HP/SAN impacts calculated via `ComputeImpact()` using team vs. requirement match

### Event Handling
```csharp
// EventInstance + EventDef + EffectDef form a pipeline:
1. Event triggered (EventInstanceFactory.Create)
2. Added to node.PendingEvents (blocks tasks per blockPolicy)
3. Options presented to player (EventPanel UI)
4. Selected option → EffectDef applied via EffectOpExecutor
5. Event removed from PendingEvents
```

### Fact-Based News Generation
```csharp
// Facts are emitted during simulation:
1. Sim.EmitFact() called when significant events occur (spawn, investigate, contain)
2. FactInstance stored in GameState.FactSystem.Facts
3. FactNewsGenerator.GenerateNewsFromFacts() converts facts to news
4. NewsInstance created with title/description from FactNewsDef templates
5. Facts marked as Reported to prevent duplicate news
6. Old facts pruned after 60 days
```

---

## Project-Specific Conventions

### Naming & Location Patterns
- **C# Classes**: PascalCase, paired with `.meta` files
- **IDs**: String identifiers with prefixes: `N1` (nodes), `A1` (agents), `E1` (events), `EVI_` (event instances), `NEWS_` (news), `FACT_` (facts), `T_` (tasks), `MANAGED_` (managed anomalies)
- **"Canvas-maintained" files**: Header comments indicate files are actively refactored; preserve those comments
- **Namespace organization**: `Core`, `Data`, `UI` (no `Runtime` namespace in scripts, but `Runtime/` folder contains orchestration)

### Data-Driven Patterns
- **Never hardcode game rules**: If values might change, they belong in `balance` table or definitions
- **Validation**: `GameDataValidator` warns about missing or invalid definitions at load time
- **Warning systems**: Registry uses `-WithWarn()` methods to log issues without crashing
- **Task configuration**: TaskDef.agentSlotsMin/Max controls squad size requirements

### Task Progress Calculation
```csharp
// Formula: progressScale = MapSToMult(ComputeMatchS_NoWeight(team, req))
// team = [totalPerception, totalResistance, totalOperation, totalPower]
// req = anomaly[invReq/conReq/manReq] (4-element int array)
// S = 0.5 * minRatio + 0.5 * avgRatio (ratio per stat: team/req clamped 0-2)
// progressScale: <0.7→0.3, 0.7-1.0→0.3-1.0 lerp, 1.0-1.3→1.0-1.6 lerp, >1.3→1.6
// Manage tasks: progress capped at 0.99, never auto-complete
// Other tasks: progress 0..baseDays
```

### Agent HP/SAN Impact System
```csharp
// HP/SAN damage calculated daily for active tasks (Investigate/Contain/Manage)
// Formula: ComputeImpact(type, anomalyDef, agentIds)
// - Base damage from anomaly[invhpDmg/consanDmg/manhpDmg/mansanDmg]
// - Multiplier (hpMul/sanMul) from ability deficit (team vs. req)
// - Applied via Sim.ApplyAgentImpact(agentId, hpDelta, sanDelta, reason)
// - Clamps to [0, max], checks for death/insanity
// - Dead/insane agents auto-removed from tasks
// - Idle agents recover 10% max HP/SAN per day
```

### Effect Scope System
- **Node**: Affects panic/population of a specific node
- **OriginTask**: Affects the task that triggered the event (progress, etc.)
- **Global**: Affects worldPanic/money/negEntropy
- **TaskType**: Affects all active tasks of a type (Investigate/Contain/Manage)

### Event Blocking Policies
- **BlockOriginTask**: Only blocks the task that triggered the event
- **BlockAllTasksOnNode**: Blocks all active tasks on the node
- Tasks check `IsTaskBlockedByEvents()` before progressing each day

---

## Key Files & Their Responsibilities

| File | Purpose | Key Methods |
|------|---------|-------------|
| `Sim.cs` | Game simulation engine | `StepDay()`, `ResolveEvent()`, `EmitFact()`, `ApplyAgentImpact()`, `ComputeImpact()` |
| `GameState.cs` | Data structures | NodeState, NodeTask, GameState, AgentState, FactInstance, NewsInstance, etc. |
| `DataRegistry.cs` | Config loader & accessor | `GetTaskBaseDaysWithWarn()`, `GetBalance*()`, `TryGetEvent()`, `GetAnomalyIntWithWarn()` |
| `GameController.cs` | State manager & mediator | `EndDay()`, `ResolveEvent()`, `CreateInvestigateTask()`, `CreateContainTask()`, `CreateManageTask()`, `AssignTask()`, `CancelOrRetreatTask()` |
| `UIPanelRoot.cs` | UI orchestrator | Panel instantiation & navigation, modal stack management |
| `EffectOpExecutor.cs` | Effect application | `ApplyEffect()` (stat modifications per scope) |
| `NodeEvents.cs` | Event factory | `EventInstance` creation via `EventInstanceFactory` |
| `AnomalyManagePanel.cs` | Management UI | Agent assignment for Investigate/Contain/Manage, reusable picker panel |
| `FactNewsGenerator.cs` | Fact-to-news converter | `GenerateNewsFromFacts()`, template-based news generation |
| `NewsGenerator.cs` | Bootstrap news utility | `EnsureBootstrapNews()` for initial game state |

---

## Common Development Tasks

### Adding a New Stat/Balance Value
1. Define in `GameData/game_data.json` → `balance` dict
2. Access via: `DataRegistry.Instance.GetBalanceIntWithWarn("StatName", defaultValue)`
3. Apply in effects via `EffectDef` with proper `EffectOpRow` entries

### Modifying Task Logic
- Core mechanics: `Sim.StepDay()` → task progress section
- Progress calculation: `Sim.ComputeMatchS_NoWeight()` + `Sim.MapSToMult()`
- Impact calculation: `Sim.ComputeImpact()` (team vs. req matching)
- HP/SAN application: `Sim.ApplyAgentImpact()` (unified agent damage handling)

### Adding UI Panels
1. Create script inheriting `MonoBehaviour`
2. Prefab in `Assets/Prefabs/UI/`
3. Reference & instantiate in `UIPanelRoot`
4. Subscribe to `GameController.OnStateChanged` for updates
5. Implement `IModalClosable` interface for proper modal stack integration

### Event Triggering
- Use `EventInstanceFactory.Create()` to instantiate
- Add to `node.PendingEvents`
- `Sim.ResolveEvent()` applies selected option's effect
- Events can block tasks via `blockPolicy` field

### Adding Anomalies
- Define in `GameData/game_data.json` → `Anomalies` table
- Specify requirements: `invReq`, `conReq`, `manReq` (4-element int arrays: Perception, Resistance, Operation, Power)
- Specify impacts: `invhpDmg/invsanDmg`, `conhpDmg/consanDmg`, `manhpDmg/mansanDmg`
- Specify rewards: `invExp`, `conExp`, `manExpPerDay`, `manNegentropyPerDay`
- Schedule spawns in `AnomaliesGen` table

### Adding News
- **RandomDaily News**: Define in `NewsDefs` with `source="RandomDaily"`, requirements, weight, probability
- **Fact-Based News**: Define `FactNewsDef` with templates for title/description, linked to fact types
- News generation prioritizes fact-based over RandomDaily
- Multiple media profiles supported (FORMAL/SENSATIONAL/INVESTIGATIVE)

---

## Testing & Debugging

- **Debug seed**: `GameController` has configurable `seed` field (same seed = reproducible run)
- **Logging**: All major decisions logged with `[Tag] day=X context=Y` format
- **State inspection**: `GameController.State` is public; breakpoint-friendly structure
- **Busy agent tracking**: `GameControllerTaskExt.LogBusySnapshot()` logs current agent occupancy
- **Fact system test**: `FactSystemTest.RunTests()` validates fact creation, emission, pruning

---

## Integration Points

- **JSON Loading**: `DataRegistry.LoadFromStreamingAssets()` reads from `GameData/game_data.json`
- **WebGL Loading**: `DataRegistry.LoadJsonTextCoroutine()` handles remote URLs with fallback
- **UI Notifications**: `GameController.OnStateChanged` fires after every mutation
- **Random Generation**: Centralized `System.Random` in GameController; passed to Sim
- **Effect System**: Open-ended `EffectDef` allows new stat modifications without code changes
- **Fact System**: Open-ended fact emission via `Sim.EmitFact()` for custom game events

---

## Red Flags & Gotchas

1. **Null Node/Task Checks**: Always null-check `node.Tasks` before iterating
2. **Manage Tasks**: Have special progress logic (caps at 0.99, never complete, daily yields)
3. **Event Blocking**: Tasks can be blocked by events; check `IsTaskBlockedByEvents()` before progress
4. **Agent Busy State**: Uses global scan across all nodes; `GameControllerTaskExt.AreAgentsBusy()`
5. **Legacy Compatibility**: Single-task fields remain for UI compatibility; prefer `NodeTask` collections
6. **HP/SAN Clamps**: Always clamped to [0, max]; death/insanity auto-removes from tasks
7. **Task Progress Units**: Investigate/Contain use baseDays units (e.g., 0-5); Manage uses 0-1 float
8. **Anomaly IDs**: Use anomalyDefId (e.g., "AN_002") for discovery/containment, not instance IDs
9. **News Media Profiles**: Each news has a `mediaProfileId` for separation (FORMAL/SENSATIONAL/INVESTIGATIVE)
10. **Fact Retention**: Facts are pruned after 60 days; ensure important facts generate news before expiry

---

## N-Task System Architecture

### Key Differences from Legacy System
- **Before**: Single Investigate + Single Contain per node (stored in node fields)
- **Now**: Unlimited tasks per node (stored in `NodeState.Tasks` list)
- **Task Identity**: Each task has unique ID, type, state, progress, assigned agents
- **Task Lifecycle**: Active → Completed/Cancelled (no pause/resume)
- **Assignment Model**: Tasks created first, then agents assigned via `AssignTask(taskId, agentIds)`

### Task Types
1. **Investigate**: 
   - Can target specific news (`TargetNewsId`) or be generic
   - Auto-locks anomaly based on team Perception vs. requirements
   - No-result investigations have random baseDays (2-5)
   - Produces known anomaly on completion
   
2. **Contain**: 
   - Requires known anomaly (`KnownAnomalyDefIds`)
   - Target specified via `SourceAnomalyId`
   - Produces `ManagedAnomalyState` on completion
   - Removes anomaly from `ActiveAnomalyIds`
   
3. **Manage**: 
   - Long-running (never auto-completes)
   - Target specified via `TargetManagedAnomalyId`
   - Daily yields: NegEntropy + EXP
   - Progress capped at 0.99

### Agent Occupancy Rules
- **Busy Check**: Global scan across all active tasks on all nodes
- **Dead/Insane Agents**: Auto-removed from tasks, task cancelled if no agents remain
- **Idle Recovery**: Agents not in any active task recover 10% HP/SAN per day
- **Status Display**: `Sim.BuildAgentBusyText()` generates human-readable busy status

---

## Fact System

### Purpose
- Decouple game events from news generation
- Store structured data about significant events (spawn, investigate, contain, etc.)
- Enable flexible news generation from multiple fact sources
- Support retention management (60-day default)

### Core Components
- **FactInstance**: Runtime structure (FactId, Type, Day, NodeId, AnomalyId, Severity, Tags, Payload, Reported)
- **FactState**: Container (Facts list, RetentionDays)
- **FactTypeDef**: Defines valid fact types in GameData (for validation warnings)
- **FactNewsDef**: Templates for converting facts to news (title/desc patterns)

### Fact Emission Points
- Anomaly spawned (`AnomalySpawned`)
- Investigation completed (`InvestigateCompleted` or `InvestigateNoResult`)
- Containment completed (`ContainCompleted`)
- Custom events via `Sim.EmitFact()`

### Fact-to-News Pipeline
1. Facts emitted during simulation (with severity 1-5)
2. `FactNewsGenerator.GenerateNewsFromFacts()` called each day
3. Facts matched against `FactNewsDef` templates
4. Template placeholders replaced with fact payload data
5. NewsInstance created with generated title/description
6. Fact marked as `Reported = true`
7. Old facts pruned after retention period

---

## Quick Navigation

- **Game Loop**: `GameController.EndDay()` → `Sim.StepDay()`
- **Event Flow**: `Sim.GetPendingEvents()` → `Sim.ResolveEvent()` → `EffectOpExecutor.ApplyEffect()`
- **Task Flow**: `GameController.CreateXTask()` → `GameController.AssignTask()` → `Sim.StepDay()` (progress) → `Sim.CompleteTask()`
- **UI Updates**: `OnStateChanged` → Panel's `OnGameStateChanged()` handlers
- **Data Access**: Always via `DataRegistry.Instance` singleton
- **News Flow**: `Sim.EmitFact()` → `FactNewsGenerator.GenerateNewsFromFacts()` → `NewsInstance` → UI display
- **Agent Impact Flow**: Task progress → `Sim.ApplyDailyTaskImpact()` → `Sim.ComputeImpact()` → `Sim.ApplyAgentImpact()`

---

## Best Practices

### When Adding New Features
1. Define data in JSON first (game_data.json)
2. Add validation to `GameDataValidator` if needed
3. Implement simulation logic in `Sim.cs`
4. Update `GameController` for UI interaction points
5. Create/update UI panels with proper modal stack integration
6. Test with debug seed for reproducibility
7. Log key decisions with structured format `[Tag] day=X context=Y`

### When Modifying Tasks
1. Check `IsTaskBlockedByEvents()` before progress
2. Handle HP/SAN impacts via `ComputeImpact()` + `ApplyAgentImpact()`
3. Log progress with team/req/S values for debugging
4. Update task state properly (Active/Completed/Cancelled)
5. Clean up agent assignments on completion/cancellation

### When Adding Events
1. Define `EventDef` with requirements, options, effects
2. Specify `blockPolicy` (BlockOriginTask/BlockAllTasksOnNode)
3. Set `autoResolveAfterDays` for auto-cleanup
4. Define `ignoreEffectId` + `ignoreApplyMode` for unresolved handling
5. Use `EffectOpExecutor` for stat changes (don't hardcode)

### When Working with Facts/News
1. Emit facts at significant game events via `Sim.EmitFact()`
2. Define `FactTypeDef` for validation
3. Create `FactNewsDef` templates for automatic news generation
4. Use descriptive tags for fact filtering
5. Store relevant context in fact payload
6. Test with multiple fact sources to ensure variety
