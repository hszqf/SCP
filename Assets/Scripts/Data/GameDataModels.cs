using System;
using System.Collections.Generic;

namespace Data
{
    [Serializable]
    public class GameDataRoot
    {
        public MetaRow meta = new();
        public Dictionary<string, BalanceValue> balance = new();
        public List<NodeDef> nodes = new();
        public List<AnomalyDef> anomalies = new();
        public List<TaskDef> taskDefs = new();
        public List<EventDef> events = new();
        public List<EventOptionDef> eventOptions = new();
        public List<EffectDef> effects = new();
        public List<EffectOpRow> effectOps = new();
        public List<EventTriggerRow> eventTriggers = new();
        public Dictionary<string, GameDataTable> tables = new();
    }

    [Serializable]
    public class MetaRow
    {
        public string schemaVersion;
        public string dataVersion;
        public string comment;
    }

    [Serializable]
    public class BalanceValue
    {
        public string value;
        public string type;
        public string comment;
    }

    [Serializable]
    public class NodeDef
    {
        public string nodeId;
        public string name;
        public List<string> tags = new();
        public int startLocalPanic;
        public int startPopulation;
        public List<string> startAnomalyIds = new();
    }

    [Serializable]
    public class AnomalyDef
    {
        public string anomalyId;
        public string name;
        public string @class;
        public List<string> tags = new();
        public int baseThreat;
        public int investigateDifficulty;
        public int containDifficulty;
        public int manageRisk;
    }

    [Serializable]
    public class TaskDef
    {
        public string taskDefId;
        public string taskType;
        public string name;
        public int baseDays;
        public float progressPerDay;
        public int agentSlotsMin;
        public int agentSlotsMax;
        public string yieldKey;
        public float yieldPerDay;
        public bool hasYieldKey;
        public bool hasYieldPerDay;
    }

    [Serializable]
    public class EventDef
    {
        public string eventDefId;
        public string source;
        public string causeType;
        public int weight;
        public string title;
        public string desc;
        public string blockPolicy;
        public List<string> defaultAffects = new();
        public int autoResolveAfterDays;
        public string ignoreApplyMode;
        public string ignoreEffectId;
    }

    [Serializable]
    public class EventOptionDef
    {
        public string eventDefId;
        public string optionId;
        public string text;
        public string resultText;
        public List<string> affects = new();
        public string effectId;
    }

    [Serializable]
    public class EffectDef
    {
        public string effectId;
        public string comment;
    }

    [Serializable]
    public class EffectOpRow
    {
        public string effectId;
        public string scope;
        public string statKey;
        public string op;
        public float value;
        public float? min;
        public float? max;
        public string comment;
    }

    [Serializable]
    public class EventTriggerRow
    {
        public string rowId;
        public string eventDefId;
        public int? minDay;
        public int? maxDay;
        public List<string> requiresNodeTagsAny = new();
        public List<string> requiresNodeTagsAll = new();
        public List<string> requiresAnomalyTagsAny = new();
        public bool? requiresSecured;
        public int? minLocalPanic;
        public string taskType;
        public bool? onlyAffectOriginTask;
    }

    [Serializable]
    public class GameDataTable
    {
        public string mode;
        public string idField;
        public List<GameDataColumn> columns = new();
        public List<Dictionary<string, object>> rows = new();
    }

    [Serializable]
    public class GameDataColumn
    {
        public string name;
        public string type;
    }
}
