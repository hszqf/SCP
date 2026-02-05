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
        public List<NewsDef> newsDefs = new();
        public List<MediaProfileDef> mediaProfiles = new();
        public List<FactTemplateDef> factTemplates = new();
        public List<EffectDef> effects = new();
        public List<EffectOpRow> effectOps = new();
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
        public int startPopulation;
        public int unlocked = 1;
    }

    [Serializable]
    public class AnomalyDef
    {
        public string anomalyId;
        public string name;
        public string @class;
        public int baseThreat;
        public int baseDays;
        public int actPeopleKill;
        public int invExp;
        public int conExp;
        public int manExpPerDay;
        public int manNegentropyPerDay;
        public int invHp;
        public int invSan;
        public int conHp;
        public int conSan;
        public int manHp;
        public int manSan;
        public int invhpDmg;
        public int invsanDmg;
        public int conhpDmg;
        public int consanDmg;
        public int manhpDmg;
        public int mansanDmg;
        public int[] invReq = new int[4];
        public int[] conReq = new int[4];
        public int[] manReq = new int[4];
    }

    [Serializable]
    public class TaskDef
    {
        public string taskDefId;
        public string taskType;
        public string name;
        public int agentSlotsMin;
        public int agentSlotsMax;
    }

    [Serializable]
    public class AnomaliesGenDef
    {
        public int day;
        public int AnomaliesGenNum;
    }

    [Serializable]
    public class EventDef
    {
        public string eventDefId;
        public string source;
        public int weight;
        public string title;
        public string desc;
        public string blockPolicy;
        public List<string> defaultAffects = new();
        public int autoResolveAfterDays;
        public string ignoreApplyMode;
        public string ignoreEffectId;
        public string requiresNodeId;
        public string requiresAnomalyId;
        public string requiresTaskType;
        public float p;
        public int minDay;
        public int maxDay;
        public int cd;
        public int limitNum;
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
    public class NewsDef
    {
        public string newsDefId;
        public string source;
        public int weight;
        public float p;
        public int minDay;
        public int maxDay;
        public int cd;
        public int limitNum;
        public string requiresNodeId;
        public string requiresAnomalyId;
        public string title;
        public string desc;
    }

    // MediaProfile: Defines different media styles for news reporting
    [Serializable]
    public class MediaProfileDef
    {
        public string profileId;
        public string name; // e.g., "Formal", "Sensational", "Investigative"
        public string tone; // e.g., "neutral", "alarmist", "optimistic"
        public List<string> titleTemplates = new(); // Template strings with {placeholders}
        public List<string> descTemplates = new();
        public int weight = 1; // For random selection
    }

    // FactTemplateDef: Maps fact types to news templates
    [Serializable]
    public class FactTemplateDef
    {
        public string factType; // e.g., "AnomalySpawned", "TaskCompleted"
        public string mediaProfileId; // Which media profile to use
        public string titleTemplate; // e.g., "Breaking: Anomaly detected at {nodeName}"
        public string descTemplate; // e.g., "Authorities report strange activity..."
        public int severityMin = 1; // Min severity level to use this template
        public int severityMax = 5; // Max severity level to use this template
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
