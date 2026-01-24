using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using UnityEngine;

namespace Data
{
    public static class GameDataValidator
    {
        public static void ValidateOrThrow(DataRegistry registry)
        {
            var errors = new List<string>();
            if (registry == null)
            {
                throw new InvalidOperationException("[GameDataValidator] Registry is null.");
            }

            ValidateEnums(registry, errors);
            ValidateForeignKeys(registry, errors);
            ValidateTriggers(registry, errors);
            ValidateBlockOriginTaskLogic(registry, errors);

            if (errors.Count > 0)
            {
                var message = "[GameDataValidator] Validation failed:\n - " + string.Join("\n - ", errors);
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            Debug.Log($"[GameDataValidator] Validation passed. events={registry.EventsById.Count} effects={registry.EffectsById.Count}");
        }

        private static void ValidateEnums(DataRegistry registry, List<string> errors)
        {
            foreach (var ev in registry.Root.events ?? new List<EventDef>())
            {
                if (ev == null) continue;
                if (!DataRegistry.TryParseEventSource(ev.source, out _, out var sourceError))
                    errors.Add($"Events[{ev.eventDefId}].source: {sourceError}");
                if (!DataRegistry.TryParseCauseType(ev.causeType, out _, out var causeError))
                    errors.Add($"Events[{ev.eventDefId}].causeType: {causeError}");
                if (!DataRegistry.TryParseBlockPolicy(ev.blockPolicy, out _, out var blockError))
                    errors.Add($"Events[{ev.eventDefId}].blockPolicy: {blockError}");

                var affectsRaw = ev.defaultAffects ?? new List<string>();
                if (!DataRegistry.TryParseAffectScopes(affectsRaw, out _, out var affectsError))
                    errors.Add($"Events[{ev.eventDefId}].defaultAffects: {affectsError}");

                if (!string.IsNullOrEmpty(ev.ignoreApplyMode) && !DataRegistry.TryParseIgnoreApplyMode(ev.ignoreApplyMode, out _, out var modeError))
                    errors.Add($"Events[{ev.eventDefId}].ignoreApplyMode: {modeError}");
            }

            foreach (var opt in registry.Root.eventOptions ?? new List<EventOptionDef>())
            {
                if (opt == null) continue;
                if (opt.affects != null && opt.affects.Count > 0)
                {
                    if (!DataRegistry.TryParseAffectScopes(opt.affects, out _, out var affectsError))
                        errors.Add($"EventOptions[{opt.eventDefId}/{opt.optionId}].affects: {affectsError}");
                }
            }

            foreach (var row in registry.Root.effectOps ?? new List<EffectOpRow>())
            {
                if (row == null) continue;
                if (!DataRegistry.TryParseAffectScopes(row.scope, out _, out var scopeError))
                    errors.Add($"EffectOps[{row.effectId}].scope: {scopeError}");
                if (!DataRegistry.TryParseEffectOpType(row.op, out _, out var opError))
                    errors.Add($"EffectOps[{row.effectId}].op: {opError}");
            }

            foreach (var trigger in registry.Root.eventTriggers ?? new List<EventTriggerRow>())
            {
                if (trigger == null || string.IsNullOrEmpty(trigger.taskType)) continue;
                if (!DataRegistry.TryParseTaskType(trigger.taskType, out _, out var taskTypeError))
                    errors.Add($"EventTriggers[{trigger.eventDefId}].taskType: {taskTypeError}");
            }
        }

        private static void ValidateForeignKeys(DataRegistry registry, List<string> errors)
        {
            foreach (var opt in registry.Root.eventOptions ?? new List<EventOptionDef>())
            {
                if (opt == null) continue;
                if (string.IsNullOrEmpty(opt.eventDefId) || !registry.EventsById.ContainsKey(opt.eventDefId))
                    errors.Add($"EventOptions[{opt.optionId}] references missing eventDefId={opt.eventDefId}");
                if (!string.IsNullOrEmpty(opt.effectId) && !registry.EffectsById.ContainsKey(opt.effectId))
                    errors.Add($"EventOptions[{opt.eventDefId}/{opt.optionId}] references missing effectId={opt.effectId}");
            }

            foreach (var ev in registry.Root.events ?? new List<EventDef>())
            {
                if (ev == null) continue;
                if (!string.IsNullOrEmpty(ev.ignoreEffectId) && !registry.EffectsById.ContainsKey(ev.ignoreEffectId))
                    errors.Add($"Events[{ev.eventDefId}] references missing ignoreEffectId={ev.ignoreEffectId}");
            }

            foreach (var row in registry.Root.effectOps ?? new List<EffectOpRow>())
            {
                if (row == null) continue;
                if (string.IsNullOrEmpty(row.effectId) || !registry.EffectsById.ContainsKey(row.effectId))
                    errors.Add($"EffectOps references missing effectId={row.effectId}");
            }

            foreach (var trigger in registry.Root.eventTriggers ?? new List<EventTriggerRow>())
            {
                if (trigger == null) continue;
                if (string.IsNullOrEmpty(trigger.eventDefId) || !registry.EventsById.ContainsKey(trigger.eventDefId))
                    errors.Add($"EventTriggers references missing eventDefId={trigger.eventDefId}");
            }

            var duplicates = registry.Root.eventOptions
                ?.Where(o => o != null)
                .GroupBy(o => (o.eventDefId, o.optionId))
                .Where(g => g.Count() > 1)
                .Select(g => $"EventOptions duplicate key ({g.Key.eventDefId},{g.Key.optionId})")
                .ToList();
            if (duplicates != null && duplicates.Count > 0) errors.AddRange(duplicates);
        }

        private static void ValidateTriggers(DataRegistry registry, List<string> errors)
        {
            foreach (var trigger in registry.Root.eventTriggers ?? new List<EventTriggerRow>())
            {
                if (trigger == null) continue;
                if (trigger.minDay.HasValue && trigger.maxDay.HasValue && trigger.minDay.Value > trigger.maxDay.Value)
                {
                    errors.Add($"EventTriggers[{trigger.eventDefId}] invalid day range: {trigger.minDay}>{trigger.maxDay}");
                }

                if (trigger.minLocalPanic.HasValue && trigger.minLocalPanic.Value < 0)
                {
                    errors.Add($"EventTriggers[{trigger.eventDefId}] minLocalPanic < 0");
                }
            }
        }

        private static void ValidateBlockOriginTaskLogic(DataRegistry registry, List<string> errors)
        {
            foreach (var ev in registry.Root.events ?? new List<EventDef>())
            {
                if (ev == null || string.IsNullOrEmpty(ev.eventDefId)) continue;
                if (!DataRegistry.TryParseBlockPolicy(ev.blockPolicy, out var policy, out _)) continue;
                if (policy != BlockPolicy.BlockOriginTask) continue;

                var relatedEffectIds = new List<string>();
                if (!string.IsNullOrEmpty(ev.ignoreEffectId)) relatedEffectIds.Add(ev.ignoreEffectId);
                if (registry.OptionsByEvent.TryGetValue(ev.eventDefId, out var options))
                {
                    relatedEffectIds.AddRange(options.Where(o => !string.IsNullOrEmpty(o.effectId)).Select(o => o.effectId));
                }

                bool hasOriginTaskProgressAdd = relatedEffectIds
                    .Distinct()
                    .SelectMany(id => registry.EffectOpsByEffectId.TryGetValue(id, out var ops) ? ops : new List<EffectOp>())
                    .Any(op => op.Scope.Kind == AffectScopeKind.OriginTask &&
                               string.Equals(op.StatKey, "TaskProgressDelta", StringComparison.OrdinalIgnoreCase) &&
                               op.Op == EffectOpType.Add);

                if (!hasOriginTaskProgressAdd)
                {
                    errors.Add($"Events[{ev.eventDefId}] BlockOriginTask requires OriginTask TaskProgressDelta Add op.");
                }
            }
        }
    }
}
