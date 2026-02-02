# HP/SAN Impact System Implementation

## Overview
This document describes the current HP/SAN (Health/Sanity) impact system for agents in the SCP game.

## Components

### 1. AgentState Fields (GameState.cs)
Defaults in current build:

- `HP / MaxHP`: 20
- `SAN / MaxSAN`: 20
- `IsDead / IsInsane` are used to mark unusable agents

### 2. ApplyAgentImpact Function (Sim.cs)
Unified entry point for all HP/SAN modifications:

```csharp
public static void ApplyAgentImpact(GameState s, string agentId, int hpDelta, int sanDelta, string reason)
```

**Features:**

- Clamps HP/SAN to `[0, Max]`
- Logs all impacts with format: `[AgentImpact] day=X agent=Y hp=+/-Z san=+/-W reason=...`
- If `HP <= 0` → `IsDead`, if `SAN <= 0` → `IsInsane`
- Removes unusable agents from tasks; cancels tasks with no usable agents

## Impact Calculation (Data-Driven)

### Source fields (Anomalies table)

- Base damage per task type:
  - `invhpDmg / invsanDmg`
  - `conhpDmg / consanDmg`
  - `manhpDmg / mansanDmg`
- Ability requirements per task type:
  - `invReq / conReq / manReq`

### ComputeImpact

- Computes `hpMul / sanMul` from team vs requirement deficit
- If base damage is positive, final damage is at least 1

## Integration Points

### A. Daily Task Impacts

- **Investigate / Contain:** applied daily during progress (`ApplyDailyTaskImpact`)
- **Manage:** applied daily during `StepDay`
  - Current implementation applies **SAN only** (HP delta is computed but not applied)

### B. Event Resolution (ResolveEvent)

Hardcoded example impacts for 3 event types (demo usage):

- **EV_001:** HP -1..-3, SAN -2..-4
- **EV_002:** HP -2..-5, SAN -1..-2
- **EV_003:** HP 0, SAN -3..-5

**Log format:** `EventResolve:event=EV_001,option=OPT1,node=N1`

## Recovery

- **Idle recovery:** agents not assigned to any active task recover 10% of MaxHP/MaxSAN per day

## Notes

1. **Code-only change:** no prefab updates are required
2. **Backward compatible:** new fields have safe defaults (20/20)
3. **Transparent:** all impacts are logged for debugging

## Future Enhancements

Possible improvements (not implemented yet):

- Move event impact examples into JSON (EffectOps)
- Apply HP damage for Manage tasks if desired
- Add recovery mechanics (rest/medical)
- Add stronger consequences for 0 SAN (panic/AI behavior)
