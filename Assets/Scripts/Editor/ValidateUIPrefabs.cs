using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ValidateUIPrefabs
{
    private const string MenuPath = "Tools/Validate/UI Prefabs";

    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/UI/HUD.prefab",
        "Assets/Prefabs/UI/NodePanel.prefab",
        "Assets/Prefabs/UI/AgentPicker.prefab",
        "Assets/Prefabs/UI/AgentPickerItem.prefab",
        "Assets/Prefabs/UI/AnomalyManagementPanel.prefab",
        "Assets/Prefabs/UI/AnomalyListItem.prefab",
    };

    [MenuItem(MenuPath)]
    public static void Validate()
    {
        var errorCount = 0;
        var warnCount = 0;

        foreach (var path in PrefabPaths)
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null)
            {
                LogError(path, "Prefab", "Asset", "Suggest: check path and ensure prefab exists.", ref errorCount);
                continue;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (path.EndsWith("HUD.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateComponentFields(root, path, "HUD", new[]
                    {
                        new FieldRule("dayText", typeof(TMP_Text), true),
                        new FieldRule("moneyText", typeof(TMP_Text), true),
                        new FieldRule("panicText", typeof(TMP_Text), true),
                        new FieldRule("debugText", typeof(TMP_Text), true),
                        new FieldRule("endDayButton", typeof(Button), true),
                        new FieldRule("newsButton", typeof(Button), true),
                    }, ref errorCount, ref warnCount);
                }
                else if (path.EndsWith("NodePanel.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateComponentFields(root, path, "NodePanelView", new[]
                    {
                        new FieldRule("titleText", typeof(TMP_Text), true),
                        new FieldRule("statusText", typeof(TMP_Text), true),
                        new FieldRule("progressText", typeof(TMP_Text), true),
                        new FieldRule("investigateButton", typeof(Button), true),
                        new FieldRule("containButton", typeof(Button), true),
                        new FieldRule("manageButton", typeof(Button), true),
                        new FieldRule("closeButton", typeof(Button), true),
                        new FieldRule("backgroundButton", typeof(Button), true),
                    }, ref errorCount, ref warnCount);
                }
                else if (path.EndsWith("AgentPicker.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateComponentFields(root, path, "AgentPickerView", new[]
                    {
                        new FieldRule("itemPrefab", typeof(GameObject), true),
                        new FieldRule("contentRoot", typeof(Transform), true),
                        new FieldRule("confirmButton", typeof(Button), true),
                        new FieldRule("cancelButton", typeof(Button), true),
                        new FieldRule("backgroundButton", typeof(Button), true),
                        new FieldRule("titleText", typeof(TMP_Text), true),
                    }, ref errorCount, ref warnCount);
                }
                else if (path.EndsWith("AgentPickerItem.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateComponentFields(root, path, "AgentPickerItemView", new[]
                    {
                        new FieldRule("nameText", typeof(TMP_Text), true),
                        new FieldRule("attrText", typeof(TMP_Text), true),
                        new FieldRule("busyTagText", typeof(TMP_Text), true),
                    }, ref errorCount, ref warnCount);

                    ValidateOptionalFallback(root, path, "AgentPickerItemView", "button", typeof(Button), ref errorCount, ref warnCount);
                    ValidateOptionalFallback(root, path, "AgentPickerItemView", "background", typeof(Image), ref errorCount, ref warnCount);

                    ValidateOptionalField(root, path, "AgentPickerItemView", "selectedIcon", typeof(GameObject), ref errorCount, ref warnCount);
                }
                else if (path.EndsWith("AnomalyManagementPanel.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateComponentFields(root, path, "AnomalyManagePanel", new[]
                    {
                        new FieldRule("anomalyListContent", typeof(Transform), true),
                        new FieldRule("anomalyListItemPrefab", typeof(GameObject), true),
                        new FieldRule("agentListContent", typeof(Transform), true),
                        new FieldRule("agentPickerItemPrefab", typeof(GameObject), true),
                        new FieldRule("confirmButton", typeof(Button), true),
                        new FieldRule("closeButton", typeof(Button), true),
                        new FieldRule("headerText", typeof(TMP_Text), true),
                        new FieldRule("hintText", typeof(TMP_Text), true),
                    }, ref errorCount, ref warnCount);
                }
                else if (path.EndsWith("AnomalyListItem.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateAnomalyListItem(root, path, ref errorCount, ref warnCount);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"[SUMMARY] UI Prefab validation finished. ERROR={errorCount}, WARN={warnCount}.");
        if (errorCount > 0)
        {
            Debug.LogError("[SUMMARY] UI Prefab validation found errors. 阻断提交。");
        }
    }

    private static void ValidateComponentFields(
        GameObject root,
        string path,
        string componentTypeName,
        IEnumerable<FieldRule> rules,
        ref int errorCount,
        ref int warnCount)
    {
        var component = root.GetComponent(componentTypeName);
        if (component == null)
        {
            LogError(path, componentTypeName, "Component", $"Suggest: add {componentTypeName} to {root.name}.", ref errorCount);
            return;
        }

        var serializedObject = new SerializedObject(component);
        foreach (var rule in rules)
        {
            var property = serializedObject.FindProperty(rule.FieldName);
            if (property == null)
            {
                LogError(path, componentTypeName, rule.FieldName, "Suggest: check SerializeField name matches field.", ref errorCount);
                continue;
            }

            if (property.objectReferenceValue != null)
            {
                LogOk(path, componentTypeName, rule.FieldName, "Suggest: binding looks valid.");
                continue;
            }

            if (rule.Required)
            {
                var suggestion = BuildSuggestion(root, rule.FieldName, rule.ExpectedType);
                LogError(path, componentTypeName, rule.FieldName, suggestion, ref errorCount);
            }
            else
            {
                var suggestion = BuildSuggestion(root, rule.FieldName, rule.ExpectedType);
                LogWarn(path, componentTypeName, rule.FieldName, suggestion, ref warnCount);
            }
        }
    }

    private static void ValidateOptionalFallback(
        GameObject root,
        string path,
        string componentTypeName,
        string fieldName,
        Type fallbackType,
        ref int errorCount,
        ref int warnCount)
    {
        var component = root.GetComponent(componentTypeName);
        if (component == null)
        {
            LogError(path, componentTypeName, "Component", $"Suggest: add {componentTypeName} to {root.name}.", ref errorCount);
            return;
        }

        var serializedObject = new SerializedObject(component);
        var property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            LogError(path, componentTypeName, fieldName, "Suggest: check SerializeField name matches field.", ref errorCount);
            return;
        }

        if (property.objectReferenceValue != null)
        {
            LogOk(path, componentTypeName, fieldName, "Suggest: binding looks valid.");
            return;
        }

        var fallback = root.GetComponent(fallbackType);
        if (fallback != null)
        {
            LogWarn(path, componentTypeName, fieldName, $"Suggest: bind to {GetHierarchyPath(fallback.transform)} (fallback will use GetComponent at runtime).", ref warnCount);
        }
        else
        {
            LogError(path, componentTypeName, fieldName, $"Suggest: add {fallbackType.Name} to {root.name} or bind the field explicitly.", ref errorCount);
        }
    }

    private static void ValidateOptionalField(
        GameObject root,
        string path,
        string componentTypeName,
        string fieldName,
        Type expectedType,
        ref int errorCount,
        ref int warnCount)
    {
        var component = root.GetComponent(componentTypeName);
        if (component == null)
        {
            LogError(path, componentTypeName, "Component", $"Suggest: add {componentTypeName} to {root.name}.", ref errorCount);
            return;
        }

        var serializedObject = new SerializedObject(component);
        var property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            LogError(path, componentTypeName, fieldName, "Suggest: check SerializeField name matches field.", ref errorCount);
            return;
        }

        if (property.objectReferenceValue != null)
        {
            LogOk(path, componentTypeName, fieldName, "Suggest: binding looks valid.");
            return;
        }

        var suggestion = BuildSuggestion(root, fieldName, expectedType);
        LogOk(path, componentTypeName, fieldName, suggestion);
    }

    private static void ValidateAnomalyListItem(GameObject root, string path, ref int errorCount, ref int warnCount)
    {
        var buttons = root.GetComponentsInChildren<Button>(true);
        var texts = root.GetComponentsInChildren<TMP_Text>(true);

        if (buttons.Length > 0)
        {
            LogOk(path, "Prefab", "Button", $"Suggest: bind to {GetHierarchyPath(buttons[0].transform)}.");
        }
        else
        {
            LogError(path, "Prefab", "Button", "Suggest: add a Button component under the prefab root.", ref errorCount);
        }

        if (texts.Length > 0)
        {
            LogOk(path, "Prefab", "TMP_Text", $"Suggest: bind to {GetHierarchyPath(texts[0].transform)}.");
        }
        else
        {
            LogError(path, "Prefab", "TMP_Text", "Suggest: add at least one TMP_Text under the prefab root.", ref errorCount);
        }

        if (buttons.Length > 0 && texts.Length > 0)
        {
            LogOk(path, "Prefab", "Contract", "Suggest: prefab meets minimum requirements.");
        }
    }

    private static string BuildSuggestion(GameObject root, string fieldName, Type expectedType)
    {
        var suggestion = FindSuggestionTransform(root, fieldName, expectedType);
        if (suggestion != null)
        {
            return $"Suggest: bind to {GetHierarchyPath(suggestion)}.";
        }

        return expectedType == typeof(GameObject)
            ? "Suggest: bind to a prefab asset or a matching child in the hierarchy."
            : $"Suggest: bind to a {expectedType.Name} in the prefab hierarchy.";
    }

    private static Transform FindSuggestionTransform(GameObject root, string fieldName, Type expectedType)
    {
        var candidates = GetCandidateTransforms(root, expectedType)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var token = ExtractToken(fieldName);
        if (!string.IsNullOrEmpty(token))
        {
            var matched = candidates.FirstOrDefault(candidate =>
                candidate.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            if (matched != null)
            {
                return matched;
            }
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static IEnumerable<Transform> GetCandidateTransforms(GameObject root, Type expectedType)
    {
        var transforms = new List<Transform>();
        if (expectedType == typeof(GameObject))
        {
            transforms.AddRange(root.GetComponentsInChildren<Transform>(true));
            return transforms;
        }

        if (expectedType == typeof(Transform))
        {
            transforms.AddRange(root.GetComponentsInChildren<Transform>(true));
            return transforms;
        }

        foreach (var component in root.GetComponentsInChildren(expectedType, true))
        {
            if (component is Component unityComponent)
            {
                transforms.Add(unityComponent.transform);
            }
        }

        return transforms;
    }

    private static string ExtractToken(string fieldName)
    {
        var lowered = fieldName.Replace("_", string.Empty).ToLowerInvariant();
        var tokens = new[] { "text", "button", "image", "root", "content", "prefab", "icon" };
        foreach (var token in tokens)
        {
            lowered = lowered.Replace(token, string.Empty);
        }

        return lowered.Trim();
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static void LogOk(string path, string componentType, string fieldName, string suggestion)
    {
        Debug.Log($"[OK] {path} | {componentType} | {fieldName} | {suggestion}");
    }

    private static void LogWarn(string path, string componentType, string fieldName, string suggestion, ref int warnCount)
    {
        warnCount++;
        Debug.LogWarning($"[WARN] {path} | {componentType} | {fieldName} | {suggestion}");
    }

    private static void LogError(string path, string componentType, string fieldName, string suggestion, ref int errorCount)
    {
        errorCount++;
        Debug.LogError($"[ERROR] {path} | {componentType} | {fieldName} | {suggestion}");
    }

    private readonly struct FieldRule
    {
        public FieldRule(string fieldName, Type expectedType, bool required)
        {
            FieldName = fieldName;
            ExpectedType = expectedType;
            Required = required;
        }

        public string FieldName { get; }
        public Type ExpectedType { get; }
        public bool Required { get; }
    }
}
