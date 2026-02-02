V0 数据结构定稿（Schema v0.1，Excel→JSON）

本节用于“冻结口径”，让后续新增事件/异常/数值时尽量不改 C#。内容已对齐当前 `DataRegistry` 加载字段与运行时逻辑。

A. 约定与规范

A0. GameData 表格协议（导表/运行时一致）

每个 sheet 前 3 行固定：

1) 第 1 行：备注，可空，导出时忽略。

2) 第 2 行：字段名（# 开头列不导出）。

3) 第 3 行：类型（int/float/string/bool 及数组，数组用 string[]/int[]/float[]）。

第 4 行起为数据。导出时按 sheet 分割到 JSON 的 tables 字段中。运行时每张表基于 `idField` 建索引（第一列应唯一）。

一对多表（EventOptions/EffectOps）推荐首列为 `rowId`，避免重复键覆盖。

A1. ID 规范（强制）

nodeId：N1 / N2 / ...

anomalyId：AN_001 / AN_002 / ...

taskDefId：TASK_INVESTIGATE / TASK_CONTAIN / TASK_MANAGE

eventDefId：EVT_...（稳定，不要运行时随机）

optionId：O1/O2/...（在同一 eventDefId 下唯一）

newsDefId：NEWS_...

effectId：EFF_...

A2. 字段类型约定

int：整数

float：小数

bool：0/1（Excel 里用 0/1 更稳）

enum：固定枚举字符串（见后文）

list：逗号/分号分隔（如 tagA;tagB）

A3. 枚举（V0 统一）

TaskType：Investigate | Contain | Manage

BlockPolicy：None | BlockOriginTask | BlockAllTasksOnNode

IgnoreApplyMode：ApplyOnceThenRemove | ApplyDailyKeep | NeverAuto

AffectScope：OriginTask | Node | Global | TaskType:Investigate | TaskType:Contain | TaskType:Manage

EffectOp：Add | Mul | Set | ClampAdd

B. Excel 工作簿结构（V0 最小可用集）

1) Sheet: Meta（单行）

schemaVersion（如 0.1）

dataVersion（如 2026.02.02a）

comment

2) Sheet: Balance（全局常量）

key (string, unique)

p1 / p2 / p3 (float/int)

说明：当前运行时通过 `GetBalance*` 读取数值，通常使用 `p1` 作为主值。

3) Sheet: Nodes（节点定义）

nodeId (string, unique)

name (string)

startPopulation (int)

unlocked (int, 0/1)

4) Sheet: Anomalies（异常定义）

anomalyId (string, unique)

name (string)

class (enum)

baseThreat (int)

baseDays (int)

invExp / conExp / manExpPerDay (int)

manNegentropyPerDay (int)

invhpDmg / invsanDmg / conhpDmg / consanDmg / manhpDmg / mansanDmg (int)

invReq / conReq / manReq (int[4])

worldPanicPerDayUncontained (float)

maintenanceCostPerDay (int)

（可选但未使用）invHp / invSan / conHp / conSan / manHp / manSan

5) Sheet: AnomaliesGen（按天生成）

day (int)

AnomaliesGenNum (int)

6) Sheet: TaskDefs（任务模板定义）

taskDefId (string, unique)

taskType (enum TaskType)

name (string)

agentSlotsMin (int)

agentSlotsMax (int)

7) Sheet: Events（事件定义）

eventDefId (string, unique)

source (string, 主要使用 RandomDaily)

weight (int)

title (string)

desc (string)

blockPolicy (enum BlockPolicy)

defaultAffects (enum/list AffectScope)

autoResolveAfterDays (int)

ignoreApplyMode (enum IgnoreApplyMode)

ignoreEffectId (effectId, 可空)

requiresNodeId / requiresAnomalyId / requiresTaskType (string, 支持 ANY)

p (float, 0..1)

minDay / maxDay (int)

CD (int)

limitNum (int)

8) Sheet: EventOptions（事件选项）

rowId (string, unique)

eventDefId (fk Events.eventDefId)

optionId (string)

text (string)

resultText (string)

affects (enum/list AffectScope；为空则用 Events.defaultAffects)

effectId (effectId)

唯一性约束：(eventDefId, optionId) 唯一。

9) Sheet: NewsDefs（新闻定义）

newsDefId (string, unique)

source (string, 主要使用 RandomDaily)

weight (int)

title (string)

desc (string)

requiresNodeId / requiresAnomalyId (string, 支持 ANY)

p (float, 0..1)

minDay / maxDay (int)

CD (int)

limitNum (int)

10) Sheet: Effects（效果定义）

effectId (string, unique)

comment

11) Sheet: EffectOps（效果操作明细）

rowId (string, unique)

effectId (fk Effects.effectId)

scope (enum/list AffectScope)

statKey (string)

op (enum EffectOp)

value (float)

min (float, optional)

max (float, optional)

comment

C. SaveState（C#）字段冻结建议（避免频繁破档）

建议保持：

- GameState.Day / Nodes / Agents / News / NewsLog
- NodeState.Tasks（多任务）/ PendingEvents / ManagedAnomalies
- EventInstance 中仅存引用字段（eventDefId/optionId/nodeId/sourceTaskId/sourceAnomalyId）

D. JSON 输出结构（建议单文件）

输出 game_data.json：

meta

balance (map)

tables (包含 Nodes/Anomalies/TaskDefs/Events/EventOptions/NewsDefs/Effects/EffectOps/AnomaliesGen)

E. V0 校验规则（启动时一次性报错，别静默失败）

- 外键校验：EventOptions.eventDefId 与 Effects/effectOps.effectId 必须存在
- 枚举校验：blockPolicy/affects/scope/op 必须在允许集合
- 逻辑校验：BlockOriginTask 的事件需存在对 OriginTask 的进度修正
