V0 数据结构定稿（Schema v0.1，Excel→JSON）

本节用于“冻结口径”，让后续新增事件/异常/数值时尽量不改 C#。先覆盖你当前已经跑通的：节点/任务/事件（成因、作用域、阻塞、忽略次日结算）。

A. 约定与规范

A0. GameData 表格协议（导表/运行时一致）

每个 sheet 前 3 行固定：

1) 第 1 行：备注，可空，导出时忽略。

2) 第 2 行：字段名（# 开头列不导出）。

3) 第 3 行：类型（int/float/string/bool 及数组，数组用 string[]/int[]/float[]）。

第 4 行起为数据。导出时按 sheet 分割到 JSON 的 tables 字段中。运行时每张表都基于第一列（id 列）构建 ById 索引（第一列必须唯一）。

一对多表（EventOptions/EffectOps/EventTriggers）第一列必须是 rowId（唯一），再按外键列做 GroupBy 索引；禁止用外键当第一列，避免覆盖。

A1. ID 规范（强制）

nodeId：N1 / N2 / ...

anomalyId：AN_001 / AN_002 / ...

taskDefId：TASK_INVESTIGATE / TASK_CONTAIN / TASK_MANAGE

eventDefId：EVT_...（稳定，不要运行时随机）

optionId：O1/O2/...（在同一 eventDefId 下唯一）

effectId：EFF_...

A2. 字段类型约定

int：整数

float：小数

bool：0/1（Excel 里用 0/1 更稳）

enum：固定枚举字符串（见后文）

list：分号分隔（如 tagA;tagB）

json：允许放 JSON 字符串（V0 尽量少用，后续扩展用）

A3. 枚举（V0 统一）

TaskType：Investigate | Contain | Manage

EventSource：Investigate | Contain | Manage | LocalPanicHigh | Fixed | SecuredManage | Random

CauseType：TaskInvestigate | TaskContain | TaskManage | Anomaly | LocalPanic | Fixed | Random

BlockPolicy：None | BlockOriginTask | BlockAllTasksOnNode

IgnoreApplyMode：ApplyOnceThenRemove | ApplyDailyKeep | NeverAuto

AffectScope：OriginTask | Node | Global | TaskType:Investigate | TaskType:Contain | TaskType:Manage

AnomalyClass：Safe | Euclid | Keter（以后可扩）

B. Excel 工作簿结构（V0 最小可用集）

1) Sheet: Meta（单行）

用于校验与版本治理。

schemaVersion（如 0.1）

dataVersion（如 2026.01.24a）

comment

2) Sheet: Balance（全局常量）

key (string, unique)

value (string)

type (int/float/bool/string)

comment

建议至少包含：

LocalPanicHighThreshold (int)

RandomEventBaseProb (float)

DefaultAutoResolveAfterDays (int)

DefaultIgnoreApplyMode (enum IgnoreApplyMode)

3) Sheet: Nodes（节点定义）

nodeId (string, unique)

name (string)

tags (list)

startLocalPanic (int)

startPopulation (int)

startAnomalyIds (list, anomalyId)

4) Sheet: Anomalies（异常定义）

anomalyId (string, unique)

name (string)

class (enum AnomalyClass)

tags (list)

baseThreat (int)

investigateDifficulty (int)

containDifficulty (int)

manageRisk (int)

V0 只需要给事件触发/权重提供输入；异常的具体玩法数值以后再扩。

5) Sheet: TaskDefs（任务模板定义）

你当前 Task 逻辑在代码里，但数值可先抽到这里。

taskDefId (string, unique)（建议用 TASK_INVESTIGATE 等）

taskType (enum TaskType)

name (string)

baseDays (int)（默认完成所需天数/或用于归一化）

progressPerDay (float)

agentSlotsMin (int)

agentSlotsMax (int)

yieldKey (string, optional)（如 Money/Intel/…，V0 可不接）

yieldPerDay (float, optional)

6) Sheet: Events（事件定义）

eventDefId (string, unique)

source (enum EventSource)

causeType (enum CauseType)

weight (int)（权重抽取）

title (string)

desc (string)

blockPolicy (enum BlockPolicy)

defaultAffects (enum/list AffectScope)（如 OriginTask）

autoResolveAfterDays (int, default from Balance)

ignoreApplyMode (enum IgnoreApplyMode, default from Balance)

ignoreEffectId (effectId, 可空)

7) Sheet: EventOptions（事件选项）

eventDefId (fk Events.eventDefId)

optionId (string)

text (string)

resultText (string)

affects (enum/list AffectScope；为空则用 Events.defaultAffects)

effectId (effectId)

唯一性约束：(eventDefId, optionId) 唯一。

8) Sheet: Effects（效果定义）

把“影响谁/影响什么/如何运算”抽象出来，避免未来无限加列。

effectId (string, unique)

comment

9) Sheet: EffectOps（效果操作明细）

一条 effectId 可以对应多行 op。

effectId (fk Effects.effectId)

scope (enum/list AffectScope)（Node/OriginTask/Global/TaskType:...）

statKey (string)（如 LocalPanic / Population / TaskProgressDelta / WorldPanic / Money 等）

op (enum) Add | Mul | Set | ClampAdd（V0 建议只用 Add/Set）

value (float)

min (float, optional)

max (float, optional)

comment

V0 你至少要支持：

Node.LocalPanic += x

Node.Population += x

OriginTask.Progress += x（允许负数，表达“扣回进度”）

10) Sheet: EventTriggers（触发条件与筛选）

一个 eventDefId 可以有 0~N 行触发规则（多行表示“任一满足即可入池”）。

eventDefId (fk Events.eventDefId)

minDay (int, optional)

maxDay (int, optional)

requiresNodeTagsAny (list, optional)

requiresNodeTagsAll (list, optional)

requiresAnomalyTagsAny (list, optional)

requiresSecured (bool, optional)

minLocalPanic (int, optional)

taskType (enum TaskType, optional)

onlyAffectOriginTask (bool, optional)（用于你说的“主要影响自己的任务”）

C. SaveState（C#）字段冻结建议（避免频繁破档）

C1. GameState（建议固定）

Day

Nodes: Dictionary<string, NodeState>

Tasks: Dictionary<string, TaskInstance>（实例 ID→实例）

Agents: ...

News: List<NewsItem>

C2. NodeState（建议固定）

LocalPanic: int

Population: int

PendingEvents: List<EventInstance>（事件实例里只存引用信息 + 少量 runtime 字段）

C3. EventInstance（SaveState 中）建议仅存：

eventInstanceId（运行时唯一，可用 GUID）

eventDefId（指向配置）

nodeId

createdDay

causeType（可冗余存，便于调试；也可从 eventDef 读）

sourceTaskId（可空）

sourceAnomalyId（可空）

说明：title/desc/options/resultText/effects 一律从 Config 查 eventDefId 与 optionId，不要存进存档。

D. JSON 输出结构（建议单文件）

输出 game_data.json：

meta

balance (map)

nodes[]

anomalies[]

taskDefs[]

events[]

eventOptions[]

effects[]

effectOps[]

eventTriggers[]

Unity 启动加载进 DataRegistry：

EventsById、OptionsByEvent、EffectOpsByEffectId、TriggersByEventId 等字典索引。

E. V0 校验规则（启动时一次性报错，别静默失败）

外键校验：EventOptions.eventDefId 必须存在；Effects/effectOps/effectId 必须存在。

枚举校验：source/causeType/blockPolicy/affects/scope/op 必须在允许集合。

触发校验：minDay<=maxDay；概率/阈值合法。

逻辑校验（V0 最重要）：

任何会 BlockOriginTask 的事件，必须允许 effectOps 里对 OriginTask.Progress 做 Add（否则永远阻塞没有意义）。
