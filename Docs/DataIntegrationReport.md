# Data Integration Report (DataRegistry Tables)

Scope: inventory the tables loaded by `DataRegistry`, map schema fields to runtime references, and highlight fields not wired into runtime logic yet.

Legend:
- **Runtime referenced**: the field is used by runtime logic (Sim/UI/GameController/Effect execution/validation) beyond being present in data.
- **Not wired yet**: no runtime references found beyond raw loading.

## Meta

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| schemaVersion | Yes | Used in startup logging/summary |
| dataVersion | Yes | Used in startup logging/summary |
| comment | No | Not wired yet |

## Balance

Balance table uses columns `key / p1 / p2 / p3` (legacy numeric slots). The values are consumed via `GetBalance*` helpers.

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| key | Yes | Id field for rows |
| p1 | Yes | Numeric payload used by balance lookups |
| p2 | Yes | Numeric payload used by balance lookups |
| p3 | Yes | Numeric payload used by balance lookups |

## Nodes

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| nodeId | Yes | Node lookup keys |
| name | Yes | UI display and news strings |
| startPopulation | Yes | Initial node population |
| unlocked | Yes | Map/availability gates |

## Anomalies

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| anomalyId | Yes | Lookup key |
| name | Yes | UI/news |
| class | Yes | Stored into managed anomaly records |
| baseThreat | Yes | Rewards/levels |
| baseDays | Yes | Task base duration |
| invExp | Yes | Investigate EXP reward |
| conExp | Yes | Contain EXP reward |
| manExpPerDay | Yes | Manage EXP per day |
| manNegentropyPerDay | Yes | Manage negentropy per day |
| invhpDmg / invsanDmg | Yes | Daily Investigate impact |
| conhpDmg / consanDmg | Yes | Daily Contain impact |
| manhpDmg / mansanDmg | Yes | Daily Manage impact (SAN applied) |
| invReq / conReq / manReq | Yes | Ability requirements used in progress & impact |
| worldPanicPerDayUncontained | Yes | World panic tick |
| maintenanceCostPerDay | Yes | Contained maintenance cost |
| invHp / invSan / conHp / conSan / manHp / manSan | No | Loaded but not used in runtime |

## AnomaliesGen

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| day | Yes | Spawn schedule |
| AnomaliesGenNum | Yes | Spawn count |

## TaskDefs

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| taskDefId | Yes | Stored on `NodeTask.TaskDefId` |
| taskType | Yes | Indexed by enum |
| name | Yes | UI display |
| agentSlotsMin | Yes | Assignment UI limits |
| agentSlotsMax | Yes | Assignment UI limits |

## Events

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| eventDefId | Yes | Lookup key |
| source | Yes | RandomDaily routing |
| weight | Yes | Weighted pick |
| title / desc | Yes | UI display |
| blockPolicy | Yes | Task blocking logic |
| defaultAffects | Yes | Effect scope resolution |
| autoResolveAfterDays | Yes | Auto-resolve tick |
| ignoreApplyMode | Yes | Ignore penalty behavior |
| ignoreEffectId | Yes | Ignore penalty effect |
| requiresNodeId | Yes | Event filters |
| requiresAnomalyId | Yes | Event filters |
| requiresTaskType | Yes | Event filters |
| p | Yes | Context probability cap |
| minDay / maxDay | Yes | Day window |
| CD | Yes | Cooldown |
| limitNum | Yes | Max fire count |

## EventOptions

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| rowId | Yes | Id field when configured |
| eventDefId | Yes | Group key |
| optionId | Yes | Option lookup |
| text | Yes | UI text |
| resultText | Yes | Result summary |
| affects | Yes | Effect scope override |
| effectId | Yes | Effect execution |

## NewsDefs

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| newsDefId | Yes | Lookup key |
| source | Yes | RandomDaily routing |
| weight | Yes | Weighted pick |
| title / desc | Yes | UI display |
| requiresNodeId | Yes | News filters |
| requiresAnomalyId | Yes | News filters |
| p | Yes | Emit probability |
| minDay / maxDay | Yes | Day window |
| CD | Yes | Cooldown |
| limitNum | Yes | Max fire count |

## Effects

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| effectId | Yes | Foreign key for ops |
| comment | No | Not wired yet |

## EffectOps

| Field | Runtime referenced? | Notes |
| --- | --- | --- |
| rowId | Yes | Id field when configured |
| effectId | Yes | Group key |
| scope | Yes | Target selection |
| statKey | Yes | Stat to modify |
| op | Yes | Operation kind |
| value | Yes | Operation value |
| min | Yes | Clamp min |
| max | Yes | Clamp max |
| comment | No | Not wired yet |

## Next-step integration order (recommended)

1. **Events + Effects** — continue moving hardcoded event impacts into EffectOps
2. **Anomalies** — decide whether to use legacy `invHp/conHp/manHp` fields or remove
3. **Balance** — standardize key naming once gameplay tuning stabilizes
