# Data Integration Report (DataRegistry Tables)

Scope: inventory the tables loaded by `DataRegistry`, map schema fields to runtime references, and highlight fields not wired into runtime logic yet.

Legend:
- **Runtime referenced**: the field is used by runtime logic (Sim/UI/GameController/Effect execution/validation) beyond being present in data.
- **Not wired yet**: no runtime references found beyond raw loading.

## Meta

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| schemaVersion | Yes | Assets/Scripts/Data/DataRegistry.cs:369-377 |
| dataVersion | Yes | Assets/Scripts/Data/DataRegistry.cs:369-377 |
| comment | No | Not wired yet |

## Balance

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| key | Yes (idField used to index rows) | Assets/Scripts/Data/TableRegistry.cs:135-165; Assets/Scripts/Data/DataRegistry.cs:676-681 |
| p1 | Yes | Assets/Scripts/Data/DataRegistry.cs:658-662 |
| p2 | Yes | Assets/Scripts/Data/DataRegistry.cs:664-668 |
| p3 | Yes | Assets/Scripts/Data/DataRegistry.cs:670-673 |

## Nodes

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| nodeId | Yes | Assets/Scripts/Data/DataRegistry.cs:184-191; Assets/Scripts/Runtime/GameController.cs:64-70 |
| name | Yes | Assets/Scripts/Data/DataRegistry.cs:191-192; Assets/Scripts/Core/Sim.cs:245-246 |
| tags | Yes | Assets/Scripts/Data/DataRegistry.cs:192; Assets/Scripts/Core/Sim.cs:481-482 |
| startLocalPanic | Yes | Assets/Scripts/Data/DataRegistry.cs:193; Assets/Scripts/Runtime/GameController.cs:73-74 |
| startPopulation | Yes | Assets/Scripts/Data/DataRegistry.cs:194; Assets/Scripts/Runtime/GameController.cs:73-75 |
| startAnomalyIds | Yes | Assets/Scripts/Data/DataRegistry.cs:195; Assets/Scripts/Runtime/GameController.cs:76-83 |

## Anomalies

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| anomalyId | Yes | Assets/Scripts/Data/DataRegistry.cs:202-206; Assets/Scripts/Core/Sim.cs:827-832 |
| name | Yes | Assets/Scripts/Data/DataRegistry.cs:207; Assets/Scripts/Core/Sim.cs:548-549 |
| class | Yes | Assets/Scripts/Data/DataRegistry.cs:208; Assets/Scripts/Core/Sim.cs:764-775 |
| tags | Yes | Assets/Scripts/Data/DataRegistry.cs:209; Assets/Scripts/Core/Sim.cs:513-515 |
| baseThreat | Yes | Assets/Scripts/Data/DataRegistry.cs:210; Assets/Scripts/Runtime/GameController.cs:83-85 |
| investigateDifficulty | No | Not wired yet |
| containDifficulty | No | Not wired yet |
| manageRisk | No | Not wired yet |
| worldPanicPerDayUncontained | Yes | Assets/Scripts/Core/Sim.cs:171 |
| maintenanceCostPerDay | Yes | Assets/Scripts/Core/Sim.cs:185 |

## TaskDefs

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| taskDefId | No | Not wired yet |
| taskType | Yes (used to index TaskDefs by enum) | Assets/Scripts/Data/DataRegistry.cs:218-226 |
| name | No | Not wired yet |
| baseDays | No | Not wired yet |
| progressPerDay | Yes | Assets/Scripts/Data/DataRegistry.cs:229; Assets/Scripts/Core/Sim.cs:660 |
| agentSlotsMin | No | Not wired yet |
| agentSlotsMax | No | Not wired yet |
| yieldKey | No | Not wired yet |
| yieldPerDay | No | Not wired yet |

## Events

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| eventDefId | Yes | Assets/Scripts/Data/DataRegistry.cs:241-246; Assets/Scripts/Core/Sim.cs:428-429 |
| source | Yes | Assets/Scripts/Data/DataRegistry.cs:246; Assets/Scripts/Core/Sim.cs:429-431 |
| causeType | Yes (validation only) | Assets/Scripts/Data/GameDataValidator.cs:50-56 |
| weight | Yes | Assets/Scripts/Data/DataRegistry.cs:248; Assets/Scripts/Core/Sim.cs:433-435 |
| title | Yes | Assets/Scripts/Data/DataRegistry.cs:249; Assets/Scripts/UI/EventPanel.cs:63-64 |
| desc | Yes | Assets/Scripts/Data/DataRegistry.cs:250; Assets/Scripts/UI/EventPanel.cs:63-64 |
| blockPolicy | Yes | Assets/Scripts/Data/DataRegistry.cs:251; Assets/Scripts/Core/Sim.cs:335-339 |
| defaultAffects | Yes | Assets/Scripts/Data/DataRegistry.cs:252; Assets/Scripts/Core/Sim.cs:365-368 |
| autoResolveAfterDays | No | Not wired yet |
| ignoreApplyMode | Yes | Assets/Scripts/Data/DataRegistry.cs:254; Assets/Scripts/Core/Sim.cs:283-284 |
| ignoreEffectId | Yes | Assets/Scripts/Data/DataRegistry.cs:255; Assets/Scripts/Core/Sim.cs:285-304 |

## EventOptions

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| rowId | Yes (idField used to index rows) | Assets/Scripts/Data/TableRegistry.cs:135-165 |
| eventDefId | Yes | Assets/Scripts/Data/DataRegistry.cs:263-269; Assets/Scripts/Data/GameDataValidator.cs:105-113 |
| optionId | Yes | Assets/Scripts/Data/DataRegistry.cs:264-270; Assets/Scripts/UI/EventPanel.cs:117-118 |
| text | Yes | Assets/Scripts/Data/DataRegistry.cs:270; Assets/Scripts/UI/EventPanel.cs:114-116 |
| resultText | Yes | Assets/Scripts/Data/DataRegistry.cs:271; Assets/Scripts/Core/Sim.cs:247-249 |
| affects | Yes | Assets/Scripts/Data/DataRegistry.cs:272; Assets/Scripts/Core/Sim.cs:352-356 |
| effectId | Yes | Assets/Scripts/Data/DataRegistry.cs:273; Assets/Scripts/Core/Sim.cs:239-247 |

## Effects

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| effectId | Yes (validated for foreign keys) | Assets/Scripts/Data/DataRegistry.cs:294-299; Assets/Scripts/Data/GameDataValidator.cs:96-107 |
| comment | No | Not wired yet |

## EffectOps

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| rowId | Yes (idField used to index rows) | Assets/Scripts/Data/TableRegistry.cs:135-165 |
| effectId | Yes | Assets/Scripts/Data/DataRegistry.cs:306-311; Assets/Scripts/Data/EffectOpExecutor.cs:20-27 |
| scope | Yes | Assets/Scripts/Data/DataRegistry.cs:311; Assets/Scripts/Data/EffectOpExecutor.cs:49-61 |
| statKey | Yes | Assets/Scripts/Data/DataRegistry.cs:312; Assets/Scripts/Data/EffectOpExecutor.cs:73-83 |
| op | Yes | Assets/Scripts/Data/DataRegistry.cs:313; Assets/Scripts/Data/EffectOpExecutor.cs:156-169 |
| value | Yes | Assets/Scripts/Data/DataRegistry.cs:314; Assets/Scripts/Data/EffectOpExecutor.cs:160-166 |
| min | Yes | Assets/Scripts/Data/DataRegistry.cs:315; Assets/Scripts/Data/EffectOpExecutor.cs:170-181 |
| max | Yes | Assets/Scripts/Data/DataRegistry.cs:316; Assets/Scripts/Data/EffectOpExecutor.cs:171-181 |
| comment | No | Not wired yet |

## EventTriggers

| Field | Runtime referenced? | References (file:line) |
| --- | --- | --- |
| rowId | Yes (idField used to index rows) | Assets/Scripts/Data/TableRegistry.cs:135-165 |
| eventDefId | Yes | Assets/Scripts/Data/DataRegistry.cs:332-336; Assets/Scripts/Core/Sim.cs:451-454 |
| taskType | Yes | Assets/Scripts/Data/DataRegistry.cs:344-345; Assets/Scripts/Core/Sim.cs:474-477 |
| onlyAffectOriginTask | Yes | Assets/Scripts/Data/DataRegistry.cs:345; Assets/Scripts/Core/Sim.cs:479 |
| minDay | Yes | Assets/Scripts/Data/DataRegistry.cs:337; Assets/Scripts/Core/Sim.cs:469-470 |
| maxDay | Yes | Assets/Scripts/Data/DataRegistry.cs:338; Assets/Scripts/Core/Sim.cs:469-470 |
| requiresNodeTagsAny | Yes | Assets/Scripts/Data/DataRegistry.cs:339; Assets/Scripts/Core/Sim.cs:481-482 |
| requiresNodeTagsAll | Yes | Assets/Scripts/Data/DataRegistry.cs:340; Assets/Scripts/Core/Sim.cs:481-482 |
| requiresAnomalyTagsAny | Yes | Assets/Scripts/Data/DataRegistry.cs:341; Assets/Scripts/Core/Sim.cs:484-487 |
| requiresSecured | Yes | Assets/Scripts/Data/DataRegistry.cs:342; Assets/Scripts/Core/Sim.cs:472 |
| minLocalPanic | Yes | Assets/Scripts/Data/DataRegistry.cs:343; Assets/Scripts/Core/Sim.cs:471 |

## Next-step integration order (recommended)

1. **Effects + EffectOps** — solidify effect execution coverage (stat keys, scopes, bounds). This unblocks richer EventOptions and triggers.
2. **EventOptions** — ensure options fully drive effect application and result text.
3. **EventTriggers** — expand trigger criteria coverage and testing.
4. **Events** — wire `autoResolveAfterDays` and `causeType` into runtime logic.
5. **TaskDefs** — integrate task configuration fields (baseDays, agentSlots, yields) into Sim and UI.
6. **Anomalies** — hook `investigateDifficulty`, `containDifficulty`, `manageRisk` into task tuning and UI.
7. **Meta/Balance** — keep as foundational configuration once above wiring stabilizes.
