# 改动点总结 (Change Points Summary)

## 1. 干员字段（当前默认值）

### GameState.cs - AgentState
新增/调整的生命与理智字段：

- `HP / MaxHP` 默认 `20`
- `SAN / MaxSAN` 默认 `20`
- 仍保留 `IsDead / IsInsane` 作为不可用状态

## 2. 统一影响函数 (ApplyAgentImpact)

### Sim.cs - ApplyAgentImpact

功能：

- 修改指定干员的 HP/SAN，自动 clamp 到 `[0, Max]`
- 输出日志：`[AgentImpact] day=X agent=Y hp=±Z (before->after) san=±W (before->after) reason=...`
- 当 `HP <= 0` 标记为死亡；当 `SAN <= 0` 标记为疯狂
- 触发 `HandleAgentUnusable`：自动从任务中移除干员，若任务无可用干员则取消任务

## 3. 影响计算改为数据驱动

### Anomalies 表字段驱动

影响来源已迁移到异常配置：

- 基础损伤：`invhpDmg / invsanDmg / conhpDmg / consanDmg / manhpDmg / mansanDmg`
- 能力需求：`invReq / conReq / manReq`（4 维能力数组）

### ComputeImpact（能力修正）

- 依据团队能力与需求差计算 `hpMul / sanMul`
- 当基础损伤 > 0 时，最终伤害至少为 1

## 4. 结算时机调整

### A. Investigate / Contain
- **不再在任务完成时结算**
- 改为 **每日推进时结算**（`ApplyDailyTaskImpact`）

### B. Manage
- **每日结算**（StepDay 中）
- 当前实现 **仅对 SAN 施加影响**（`hpDelta` 不应用）

## 5. 事件结算仍保留示例硬编码

ResolveEvent 中仍保留 `EV_001/EV_002/EV_003` 的示例影响（用于演示）。

## 6. 额外机制

- **闲置恢复**：未分配任务的干员每日恢复 `MaxHP/MaxSAN` 的 10%
- **日志增强**：新增 `ImpactCalc` / `TaskFailed` / `TaskAgentRemoved` 等日志，便于追踪

## 7. 数据侧补充

异常表已包含经验与产出字段：

- `invExp / conExp / manExpPerDay`
- `manNegentropyPerDay`

用于 Investigate/Contain 完成与 Manage 每日的经验与负熵结算
