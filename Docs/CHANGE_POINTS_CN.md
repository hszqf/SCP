# 改动点总结 (Change Points Summary)

## 1. 新增字段 (New Fields)

### GameState.cs - AgentState 类
**位置:** Line 24-39

添加了 4 个新字段：
```csharp
public int HP = 100;
public int MaxHP = 100;
public int SAN = 100;
public int MaxSAN = 100;
```

## 2. 统一影响函数 (Unified Impact Function)

### Sim.cs - ApplyAgentImpact
**位置:** Line 1131-1148 (approx)

新增公共静态函数：
```csharp
public static void ApplyAgentImpact(GameState s, string agentId, int hpDelta, int sanDelta, string reason)
```

**功能:**
- 修改指定干员的 HP 和 SAN
- 自动 clamp 到 [0, max] 范围
- 输出日志：`[AgentImpact] day=X agent=Y hp=±Z (before->after) san=±W (before->after) reason=...`

## 3. 任务完成结算插入位置 (Task Completion Integration Points)

### A. Investigate 任务完成
**文件:** Sim.cs - CompleteTask 函数  
**插入位置:** 在 Anomaly Discovery Logic 之后 (Line 932-966)

```csharp
// ===== HP/SAN Impact for Investigate =====
// Base SAN cost: -1 to -3
int baseSanCost = -(1 + rng.Next(3));

// Anomaly-specific modifier (hardcoded examples)
float sanMultiplier = 1.0f;
if (!string.IsNullOrEmpty(anomalyId))
{
    if (anomalyId == "AN_001") sanMultiplier = 1.5f;
    else if (anomalyId == "AN_002") sanMultiplier = 2.0f;
    else if (anomalyId == "AN_003") sanMultiplier = 1.2f;
}

int finalSanCost = (int)(baseSanCost * sanMultiplier);

foreach (var agentId in assignedAgents)
{
    ApplyAgentImpact(s, agentId, 0, finalSanCost, 
        $"InvestigateComplete:node={node.Id},anomaly={anomalyId ?? "unknown"}");
}
```

**结算时机:** 调查进度达到 baseDays 时（任务完成时）

### B. Contain 任务完成
**文件:** Sim.cs - CompleteTask 函数  
**插入位置:** 在收容成功逻辑之后 (Line 1003-1041)

```csharp
// ===== HP/SAN Impact for Contain =====
// Base HP cost: -0 to -5, SAN cost: -1 to -4
int baseHpCost = -(rng.Next(6));
int baseSanCost = -(1 + rng.Next(4));

// Anomaly-specific modifier (hardcoded examples)
float hpMultiplier = 1.0f;
float sanMultiplier = 1.0f;
if (!string.IsNullOrEmpty(anomalyId))
{
    if (anomalyId == "AN_001") { hpMultiplier = 1.3f; sanMultiplier = 1.2f; }
    else if (anomalyId == "AN_002") { hpMultiplier = 1.8f; sanMultiplier = 1.5f; }
    else if (anomalyId == "AN_003") { hpMultiplier = 1.1f; sanMultiplier = 1.3f; }
}

int finalHpCost = (int)(baseHpCost * hpMultiplier);
int finalSanCost = (int)(baseSanCost * sanMultiplier);

foreach (var agentId in assignedAgents)
{
    ApplyAgentImpact(s, agentId, finalHpCost, finalSanCost,
        $"ContainComplete:node={node.Id},anomaly={anomalyId ?? "unknown"}");
}
```

**结算时机:** 收容进度达到 baseDays 时（任务完成时）

### C. Manage 任务日结算
**文件:** Sim.cs - StepManageTasks 函数  
**插入位置:** 在负熵产出计算之后 (Line 1178-1212)

```csharp
// ===== HP/SAN Impact for Manage (daily) =====
// Base SAN cost per day: -1
int baseSanCost = -1;

// Anomaly-specific modifier (hardcoded examples)
float sanMultiplier = 1.0f;
string anomalyId = m.AnomalyId;
if (!string.IsNullOrEmpty(anomalyId))
{
    if (anomalyId == "AN_001") sanMultiplier = 1.2f;
    else if (anomalyId == "AN_002") sanMultiplier = 1.5f;
    else if (anomalyId == "AN_003") sanMultiplier = 1.1f;
}

int finalSanCost = (int)(baseSanCost * sanMultiplier);

foreach (var agentId in t.AssignedAgentIds)
{
    ApplyAgentImpact(s, agentId, 0, finalSanCost,
        $"ManageDaily:node={node.Id},anomaly={anomalyId ?? "unknown"},managed={m.Id}");
}
```

**结算时机:** 每日 StepDay() 时，对所有正在进行的 Manage 任务

## 4. 事件结算插入位置 (Event Resolution Integration Point)

### ResolveEvent 函数
**文件:** Sim.cs - ResolveEvent 函数  
**插入位置:** 在 EffectOpExecutor.ApplyEffect 之后，News 添加之前 (Line 233-265)

```csharp
// ===== HP/SAN Impact for Events (hardcoded examples) =====
// Apply impacts to agents assigned to the origin task, if any
if (originTask != null && originTask.AssignedAgentIds != null && originTask.AssignedAgentIds.Count > 0)
{
    int hpDelta = 0;
    int sanDelta = 0;
    
    // Hardcoded examples for specific events
    if (ev.EventDefId == "EV_001")      // Example event 1
    {
        hpDelta = -(1 + rng.Next(3));   // -1 to -3
        sanDelta = -(2 + rng.Next(3));  // -2 to -4
    }
    else if (ev.EventDefId == "EV_002") // Example event 2
    {
        hpDelta = -(2 + rng.Next(4));   // -2 to -5
        sanDelta = -(1 + rng.Next(2));  // -1 to -2
    }
    else if (ev.EventDefId == "EV_003") // Example event 3
    {
        hpDelta = 0;
        sanDelta = -(3 + rng.Next(3));  // -3 to -5
    }
    
    if (hpDelta != 0 || sanDelta != 0)
    {
        foreach (var agentId in originTask.AssignedAgentIds)
        {
            ApplyAgentImpact(s, agentId, hpDelta, sanDelta,
                $"EventResolve:event={ev.EventDefId},option={optionId},node={node.Id}");
        }
    }
}
```

**结算时机:** 玩家选择事件选项时（事件即时结算）

## 5. 示例日志 (Example Logs)

### Investigate 完成
```
[AgentImpact] day=5 agent=A1 hp=+0 (100->100) san=-3 (100->97) reason=InvestigateComplete:node=N1,anomaly=AN_002
```

### Contain 完成
```
[AgentImpact] day=7 agent=A2 hp=-4 (100->96) san=-3 (97->94) reason=ContainComplete:node=N2,anomaly=AN_001
```

### Manage 每日
```
[AgentImpact] day=10 agent=A3 hp=+0 (96->96) san=-1 (94->93) reason=ManageDaily:node=N3,anomaly=AN_002,managed=M1
```

### Event 结算
```
[AgentImpact] day=12 agent=A1 hp=-2 (96->94) san=-3 (93->90) reason=EventResolve:event=EV_001,option=OPT1,node=N1
```

## 6. 硬编码异常/事件分支 (Hardcoded Examples)

### 异常 (Anomalies)
- **AN_001**: 中等压力（Investigate: 1.5x SAN, Contain: 1.3x HP + 1.2x SAN, Manage: 1.2x SAN）
- **AN_002**: 高压力（Investigate: 2.0x SAN, Contain: 1.8x HP + 1.5x SAN, Manage: 1.5x SAN）
- **AN_003**: 轻微压力（Investigate: 1.2x SAN, Contain: 1.1x HP + 1.3x SAN, Manage: 1.1x SAN）

### 事件 (Events)
- **EV_001**: 均衡伤害（HP: -1~-3, SAN: -2~-4）
- **EV_002**: 物理伤害为主（HP: -2~-5, SAN: -1~-2）
- **EV_003**: 纯心理伤害（HP: 0, SAN: -3~-5）

## 7. 技术要点 (Technical Notes)

1. **不改 Prefab**: 无 UI 或场景文件修改
2. **不改存档字段名**: HP/SAN 为新增字段，有默认值
3. **向后兼容**: 旧存档加载时自动初始化为 100/100
4. **可扩展**: 后续可将硬编码值移到 game_data.json
5. **日志完整**: 所有影响都有清晰的原因字符串
