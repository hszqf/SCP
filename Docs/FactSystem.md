# Fact System Documentation

## Overview

The Fact System is a mechanistic approach to news generation in the SCP game. Instead of relying solely on random news generation, the system captures significant game events as "Facts" and converts them into news articles using customizable media profiles and templates.

## Architecture

### Core Components

1. **FactInstance** (`GameState.cs`)
   - Runtime structure representing a game event/fact
   - Fields:
     - `FactId`: Unique identifier (e.g., "FACT_abc123...")
     - `Day`: Day when the fact was created
     - `Type`: Event type (e.g., "AnomalySpawned", "InvestigateCompleted")
     - `NodeId`: Related node (optional)
     - `AnomalyId`: Related anomaly (optional)
     - `Severity`: Importance level (1-5)
     - `Tags`: Categorization tags (list of strings)
     - `Payload`: Flexible data storage (dictionary)
     - `Source`: Origin description (optional)
     - `Reported`: Whether converted to news (boolean)

2. **FactState** (`GameState.cs`)
   - Container for all facts
   - Fields:
     - `Facts`: List of FactInstance
     - `RetentionDays`: How long to keep facts (default: 60 days)

3. **MediaProfileDef** (`GameDataModels.cs`)
   - Defines different media reporting styles
   - Fields:
     - `profileId`: Unique identifier
     - `name`: Display name
     - `tone`: Editorial tone (e.g., "neutral", "alarmist")
     - `titleTemplates`: Template strings for titles
     - `descTemplates`: Template strings for descriptions
     - `weight`: Selection probability

4. **FactTemplateDef** (`GameDataModels.cs`)
   - Maps fact types to news templates
   - Fields:
     - `factType`: Type of fact to match
     - `mediaProfileId`: Which profile to use
     - `titleTemplate`: Title template with placeholders
     - `descTemplate`: Description template with placeholders
     - `severityMin`: Minimum severity to use template
     - `severityMax`: Maximum severity to use template

## API Reference

### Sim.EmitFact()

Emit a new fact into the game state.

```csharp
public static void EmitFact(
    GameState state,
    string type,
    string nodeId = null,
    string anomalyId = null,
    int severity = 1,
    List<string> tags = null,
    Dictionary<string, object> payload = null,
    string source = null)
```

**Example:**
```csharp
Sim.EmitFact(
    state,
    type: "AnomalySpawned",
    nodeId: "N1",
    anomalyId: "AN_001",
    severity: 4,
    tags: new List<string> { "anomaly", "spawn" },
    payload: new Dictionary<string, object>
    {
        { "nodeName", "Beijing" },
        { "anomalyClass", "Keter" }
    },
    source: "GenerateScheduledAnomalies"
);
```

**Logging Output:**
```
[Fact] EMIT day=5 factId=FACT_abc123... type=AnomalySpawned nodeId=N1 anomalyId=AN_001 severity=4 tags=[anomaly,spawn] payload=[nodeName=Beijing,anomalyClass=Keter] source=GenerateScheduledAnomalies
```

### Sim.PruneFacts()

Remove facts older than the retention period.

```csharp
public static void PruneFacts(GameState state)
```

Called automatically at the start of each day in `StepDay()`.

**Logging Output:**
```
[Fact] PRUNE day=65 cutoffDay=5 removed=12 remaining=48
```

### FactNewsGenerator.GenerateNewsFromFacts()

Convert unreported facts to news articles.

```csharp
public static int GenerateNewsFromFacts(GameState state, DataRegistry registry, int maxCount = 5)
```

**Returns:** Number of news articles generated

**Logging Output:**
```
[FactNews] day=10 factId=FACT_abc123... type=AnomalySpawned newsId=NEWS_def456... severity=4
```

## Fact Types

### Currently Implemented

1. **AnomalySpawned**
   - Emitted when: Anomaly is generated in a node
   - Severity: Based on anomaly threat level (baseThreat / 2)
   - Payload: `nodeName`, `anomalyClass`

2. **InvestigateCompleted**
   - Emitted when: Investigation task succeeds with a result
   - Severity: Based on anomaly threat level
   - Payload: `nodeName`, `anomalyClass`, `taskId`

3. **InvestigateNoResult**
   - Emitted when: Investigation task completes without finding anomaly
   - Severity: 1 (low)
   - Payload: `nodeName`, `taskId`

4. **ContainCompleted**
   - Emitted when: Containment task succeeds
   - Severity: Based on anomaly threat level (minimum 2)
   - Payload: `nodeName`, `anomalyClass`, `reward`, `panicRelief`, `taskId`

## Media Profiles

### Default Profiles (Hardcoded)

1. **FORMAL** (正式报道)
   - Tone: Neutral, official
   - Usage: Low severity events, official announcements
   - Example: "【快讯】北京发现异常现象"

2. **SENSATIONAL** (耸人听闻)
   - Tone: Alarmist, exciting
   - Usage: High severity events (severity >= 4)
   - Example: "【紧急】北京出现神秘事件！"

3. **INVESTIGATIVE** (调查报道)
   - Tone: Analytical, in-depth
   - Usage: Medium severity events
   - Example: "【调查】北京地区异常活动报告"

## News Generation Priority

The system follows this priority order:

1. **Fact-based News** (Priority 1)
   - Generate up to 5 news articles from unreported facts
   - Order by: Severity (high to low), then Day (recent first)
   - Mark facts as reported after conversion

2. **RandomDaily News** (Priority 2)
   - Only if fact-based news count < `MinNewsPerDay` balance value
   - Uses existing RandomDaily system

**Configuration:**
- Set `MinNewsPerDay` in Balance table (default: 1)

## Data Flow

```
Game Event (e.g., Anomaly Spawned)
    ↓
Sim.EmitFact()
    ↓
FactInstance created and stored in GameState.FactSystem
    ↓
(Daily) Sim.StepDay() → FactNewsGenerator.GenerateNewsFromFacts()
    ↓
Select unreported facts (by severity/recency)
    ↓
For each fact:
    - Select MediaProfile based on severity
    - Generate title from template
    - Generate description from template
    - Create NewsInstance
    - Mark fact as reported
    ↓
NewsInstance added to GameState.NewsLog
    ↓
News displayed in UI
```

## Storage and Retention

- **Storage**: Facts stored in `GameState.FactSystem.Facts`
- **Retention**: Configurable via `FactState.RetentionDays` (default: 60 days)
- **Pruning**: Automatic at start of each day
- **Serialization**: Facts are serialized with game state (saved/loaded)

## Integration Points

### Where Facts are Emitted

1. `GenerateScheduledAnomalies()` - Line ~1815
   - Emits: AnomalySpawned

2. `CompleteTask()` - Lines ~1060, ~1081, ~1141
   - Emits: InvestigateCompleted, InvestigateNoResult, ContainCompleted

### Where News is Generated

`Sim.StepDay()` - Lines ~215-228
- Calls `FactNewsGenerator.GenerateNewsFromFacts()`
- Falls back to `GenerateRandomDailyNews()` if needed

## Extending the System

### Adding New Fact Types

1. Choose a descriptive type name (e.g., "AgentDied", "EventResolved")
2. Add `EmitFact()` call at the appropriate location
3. Add template generation logic in `FactNewsGenerator.GenerateTitle/Description()`

Example:
```csharp
// In Sim.cs, when agent dies
Sim.EmitFact(
    state,
    type: "AgentDied",
    nodeId: node.Id,
    severity: 3,
    tags: new List<string> { "agent", "casualty" },
    payload: new Dictionary<string, object>
    {
        { "agentName", agent.Name },
        { "cause", "Anomaly" }
    },
    source: "TaskImpact"
);

// In FactNewsGenerator.cs
case "AgentDied":
    return profile?.profileId switch
    {
        "FORMAL" => $"【讣告】特工{agentName}殉职",
        "SENSATIONAL" => $"【悲剧】{agentName}牺牲在异常现场！",
        "INVESTIGATIVE" => $"【追踪】特工伤亡事件调查",
        _ => $"特工{agentName}殉职"
    };
```

### Adding New Media Profiles

Currently hardcoded in `FactNewsGenerator.GetDefaultMediaProfiles()`.

To add a new profile:
1. Add to the list in `GetDefaultMediaProfiles()`
2. Add template cases in `GenerateTitle()` and `GenerateDescription()`

Future: Move to JSON configuration in `game_data.json`.

### Moving to JSON Configuration

To make media profiles configurable:

1. Add to `game_data.json`:
```json
{
  "mediaProfiles": [
    {
      "profileId": "FORMAL",
      "name": "正式报道",
      "tone": "neutral",
      "weight": 1
    }
  ],
  "factTemplates": [
    {
      "factType": "AnomalySpawned",
      "mediaProfileId": "FORMAL",
      "titleTemplate": "【快讯】{nodeName}发现异常现象",
      "descTemplate": "根据官方通报...",
      "severityMin": 1,
      "severityMax": 5
    }
  ]
}
```

2. Load in `DataRegistry.cs`
3. Update `FactNewsGenerator` to use loaded data

## Testing

Run the test suite:

```csharp
// In Unity console or test runner
Tests.FactSystemTest.RunTests();
```

Tests cover:
- Fact creation
- Fact emission
- Fact pruning (60-day retention)
- Fact reporting status

## Performance Considerations

- Facts are pruned daily (O(n) where n = fact count)
- News generation processes at most `maxCount` facts per day (default: 5)
- Recommended max facts per day: ~10-20 to avoid memory bloat

## Debugging

Enable detailed logging by checking Unity console for:
- `[Fact] EMIT` - When facts are created
- `[Fact] PRUNE` - When old facts are removed
- `[FactNews]` - When facts are converted to news
- `[NewsGen]` - News generation summary

## Future Enhancements

1. **JSON Configuration**
   - Move media profiles to game_data.json
   - Move templates to game_data.json
   - Allow runtime customization

2. **Advanced Templates**
   - Support complex placeholder syntax
   - Conditional template sections
   - Template inheritance

3. **Fact Chaining**
   - Link related facts (cause-effect)
   - Generate "story arc" news

4. **Player Choice**
   - Allow players to suppress/promote certain facts
   - Media influence mechanics

5. **Statistics**
   - Track fact type frequency
   - Analyze news coverage patterns
   - Player sentiment simulation
