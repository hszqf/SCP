using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using UnityEngine;

namespace Data
{
    public sealed class EffectContext
    {
        public GameState State;
        public NodeState Node;
        public NodeTask OriginTask;
        public string EventDefId;
        public string OptionId;
    }

    public static class EffectOpExecutor
    {
        public static int ApplyEffect(string effectId, EffectContext ctx, IReadOnlyCollection<AffectScope> allowedScopes = null)
        {
            if (ctx?.State == null || string.IsNullOrEmpty(effectId)) return 0;

            var registry = DataRegistry.Instance;
            if (!registry.EffectOpsByEffectId.TryGetValue(effectId, out var ops) || ops == null || ops.Count == 0)
            {
                Debug.LogWarning($"[EffectOpExecutor] effectId={effectId} has no ops.");
                return 0;
            }

            var allowedSet = allowedScopes != null && allowedScopes.Count > 0
                ? new HashSet<string>(allowedScopes.Select(s => s.Raw))
                : null;

            int applied = 0;
            foreach (var op in ops)
            {
                if (op == null) continue;
                if (allowedSet != null && !allowedSet.Contains(op.Scope.Raw)) continue;
                ApplyOp(op, ctx);
                applied++;
            }

            return applied;
        }

        private static void ApplyOp(EffectOp op, EffectContext ctx)
        {
            switch (op.Scope.Kind)
            {
                case AffectScopeKind.Node:
                    ApplyToNode(op, ctx.Node);
                    break;
                case AffectScopeKind.OriginTask:
                    ApplyToTask(op, ctx.OriginTask);
                    break;
                case AffectScopeKind.Global:
                    ApplyToGlobal(op, ctx.State);
                    break;
                case AffectScopeKind.TaskType:
                    ApplyToTaskType(op, ctx.State, op.Scope.TaskType);
                    break;
                default:
                    Debug.LogWarning($"[EffectOpExecutor] Unsupported scope {op.Scope}");
                    break;
            }
        }

        private static void ApplyToNode(EffectOp op, NodeState node)
        {
            if (node == null) return;

            if (StatEquals(op.StatKey, "LocalPanic"))
            {
                node.LocalPanic = ApplyInt(node.LocalPanic, op, clampMin: 0);
            }
            else if (StatEquals(op.StatKey, "Population"))
            {
                node.Population = ApplyInt(node.Population, op, clampMin: 0);
            }
            else
            {
                Debug.LogWarning($"[EffectOpExecutor] Unknown node statKey={op.StatKey}");
            }
        }

        private static void ApplyToTask(EffectOp op, NodeTask task)
        {
            if (task == null) return;
            if (StatEquals(op.StatKey, "TaskProgressDelta"))
            {
                var value = ApplyFloat(task.Progress, op);
                task.Progress = Mathf.Clamp01(value);
                return;
            }

            Debug.LogWarning($"[EffectOpExecutor] Unknown task statKey={op.StatKey}");
        }

        private static void ApplyToGlobal(EffectOp op, GameState state)
        {
            if (state == null) return;
            var registry = DataRegistry.Instance;

            if (StatEquals(op.StatKey, "WorldPanic") || StatEquals(op.StatKey, "Panic"))
            {
                var next = ApplyFloat(state.WorldPanic, op);
                float clampMin = registry.GetBalanceFloatWithWarn("ClampWorldPanicMin", 0f);
                state.WorldPanic = Mathf.Max(clampMin, next);
            }
            else if (StatEquals(op.StatKey, "Money"))
            {
                int next = ApplyInt(state.Money, op);
                int clampMin = registry.GetBalanceIntWithWarn("ClampMoneyMin", 0);
                state.Money = Math.Max(clampMin, next);
            }
            else if (StatEquals(op.StatKey, "NegEntropy"))
            {
                state.NegEntropy = ApplyInt(state.NegEntropy, op, clampMin: 0);
            }
            else
            {
                Debug.LogWarning($"[EffectOpExecutor] Unknown global statKey={op.StatKey}");
            }
        }

        private static void ApplyToTaskType(EffectOp op, GameState state, TaskType? taskType)
        {
            if (state?.Cities == null || !taskType.HasValue) return;
            foreach (var node in state.Cities)
            {
                if (node?.Tasks == null) continue;
                foreach (var task in node.Tasks)
                {
                    if (task == null || task.State != TaskState.Active) continue;
                    if (task.Type != taskType.Value) continue;
                    ApplyToTask(op, task);
                }
            }
        }

        private static bool StatEquals(string statKey, string expected)
            => string.Equals(statKey, expected, StringComparison.OrdinalIgnoreCase);

        private static int ApplyInt(int current, EffectOp op, int? clampMin = null, int? clampMax = null)
        {
            var value = ApplyFloat(current, op);
            var rounded = Mathf.RoundToInt(value);
            if (clampMin.HasValue) rounded = Mathf.Max(clampMin.Value, rounded);
            if (clampMax.HasValue) rounded = Mathf.Min(clampMax.Value, rounded);
            return rounded;
        }

        private static float ApplyFloat(float current, EffectOp op)
        {
            float next = current;
            switch (op.Op)
            {
                case EffectOpType.Add:
                    next = current + op.Value;
                    break;
                case EffectOpType.Mul:
                    next = current * op.Value;
                    break;
                case EffectOpType.Set:
                    next = op.Value;
                    break;
                case EffectOpType.ClampAdd:
                    next = current + op.Value;
                    if (op.Min.HasValue) next = Mathf.Max(op.Min.Value, next);
                    if (op.Max.HasValue) next = Mathf.Min(op.Max.Value, next);
                    break;
                default:
                    Debug.LogWarning($"[EffectOpExecutor] Unsupported op {op.Op}");
                    break;
            }

            if (op.Op != EffectOpType.ClampAdd)
            {
                if (op.Min.HasValue) next = Mathf.Max(op.Min.Value, next);
                if (op.Max.HasValue) next = Mathf.Min(op.Max.Value, next);
            }

            return next;
        }
    }
}
