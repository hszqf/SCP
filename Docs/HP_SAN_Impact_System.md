# HP/SAN Impact System Implementation

## Overview
This document describes the unified HP/SAN (Health/Sanity) impact system implemented for agents in the SCP game.

## Components

### 1. AgentState Fields (GameState.cs)
New fields added to track agent health and sanity:
```csharp
public int HP = 100;
public int MaxHP = 100;
public int SAN = 100;
public int MaxSAN = 100;
```

### 2. ApplyAgentImpact Function (Sim.cs)
Unified entry point for all HP/SAN modifications:
```csharp
public static void ApplyAgentImpact(GameState s, string agentId, int hpDelta, int sanDelta, string reason)
```

**Features:**
- Clamps HP to [0, MaxHP]
- Clamps SAN to [0, MaxSAN]
- Logs all impacts with format: `[AgentImpact] day=X agent=Y hp=+/-Z san=+/-W reason=...`

## Integration Points

### A. Task Completion (CompleteTask)

#### 1. Investigate Tasks
- **Base SAN cost:** -1 to -3 (random)
- **Anomaly multipliers:**
  - AN_001: 1.5x SAN cost (more stressful)
  - AN_002: 2.0x SAN cost (very stressful)
  - AN_003: 1.2x SAN cost (slightly more stressful)
- **Log format:** `InvestigateComplete:node=N1,anomaly=AN_001`

#### 2. Contain Tasks
- **Base HP cost:** -0 to -5 (random)
- **Base SAN cost:** -1 to -4 (random)
- **Anomaly multipliers:**
  - AN_001: 1.3x HP, 1.2x SAN
  - AN_002: 1.8x HP, 1.5x SAN (very dangerous)
  - AN_003: 1.1x HP, 1.3x SAN
- **Log format:** `ContainComplete:node=N1,anomaly=AN_002`

#### 3. Manage Tasks (Daily)
- **Base SAN cost:** -1 per day
- **Anomaly multipliers:**
  - AN_001: 1.2x SAN cost
  - AN_002: 1.5x SAN cost (very stressful to manage)
  - AN_003: 1.1x SAN cost
- **Log format:** `ManageDaily:node=N1,anomaly=AN_001,managed=M1`

### B. Event Resolution (ResolveEvent)

Hardcoded examples for 3 event types:

#### EV_001
- HP: -1 to -3
- SAN: -2 to -4

#### EV_002
- HP: -2 to -5
- SAN: -1 to -2

#### EV_003
- HP: 0 (no physical damage)
- SAN: -3 to -5 (pure psychological impact)

**Log format:** `EventResolve:event=EV_001,option=OPT1,node=N1`

## Example Logs

When an agent completes an Investigate task on node N1 with anomaly AN_002:
```
[AgentImpact] day=5 agent=A1 hp=+0 (100->100) san=-4 (100->96) reason=InvestigateComplete:node=N1,anomaly=AN_002
```

When an agent completes a Contain task on node N2 with anomaly AN_001:
```
[AgentImpact] day=7 agent=A2 hp=-4 (100->96) san=-3 (96->93) reason=ContainComplete:node=N2,anomaly=AN_001
```

When an agent manages anomaly AN_002 daily:
```
[AgentImpact] day=10 agent=A3 hp=+0 (96->96) san=-1 (93->92) reason=ManageDaily:node=N3,anomaly=AN_002,managed=M1
```

When an event EV_001 is resolved:
```
[AgentImpact] day=12 agent=A1 hp=-2 (96->94) san=-3 (92->89) reason=EventResolve:event=EV_001,option=OPT1,node=N1
```

## Notes

1. **No Prefabs Modified:** All changes are code-only
2. **Backward Compatible:** New fields have default values (100/100)
3. **Extensible:** Easy to add more anomaly/event modifiers
4. **Transparent:** All impacts are logged for debugging

## Future Enhancements

Possible improvements (not implemented yet):
- Move hardcoded multipliers to JSON config (AnomalyDef or Balance table)
- Add agent stat-based resistance (Resistance stat affects SAN decay)
- Add recovery mechanics (rest, medical treatment)
- Add consequences for 0 HP/SAN (death, insanity)
- Add UI display for HP/SAN values
