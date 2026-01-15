using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class ValidateUIPrefabs
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/UI/HUD.prefab",
        "Assets/Prefabs/UI/NodePanel.prefab",
        "Assets/Prefabs/UI/AgentPicker.prefab",
        "Assets/Prefabs/UI/AgentPickerItem.prefab",
        "Assets/Prefabs/UI/AnomalyManagementPanel.prefab",
        "Assets/Prefabs/UI/AnomalyListItem.prefab"
    };

    [MenuItem("Tools/Validate/UI Prefabs")]
    public static void Validate()
    {
        foreach (string prefabPath in PrefabPaths)
        {
            ValidatePrefab(prefabPath);
        }
    }

    private static void ValidatePrefab(string prefabPath)
    {
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[ERROR] {prefabPath} | Prefab | <missing>");
            return;
        }

        MonoBehaviour[] behaviours = prefabRoot.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            ValidateBehaviour(prefabRoot, prefabPath, behaviour);
        }
    }

    private static void ValidateBehaviour(GameObject prefabRoot, string prefabPath, MonoBehaviour behaviour)
    {
        SerializedObject serializedObject = new SerializedObject(behaviour);
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (property.name == "m_Script")
            {
                continue;
            }

            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                ValidateObjectReference(prefabRoot, prefabPath, behaviour, property);
            }
        }
    }

    private static void ValidateObjectReference(
        GameObject prefabRoot,
        string prefabPath,
        MonoBehaviour behaviour,
        SerializedProperty property)
    {
        string fieldPath = property.propertyPath;
        string fieldName = ExtractRootFieldName(fieldPath);
        string fieldDisplay = property.propertyPath;

        FieldInfo fieldInfo = FindFieldInfo(behaviour.GetType(), fieldName);
        Type fieldType = ResolveFieldType(fieldInfo);

        UnityEngine.Object reference = property.objectReferenceValue;
        if (reference != null)
        {
            string boundPath = GetObjectPath(reference);
            Debug.Log($"[OK] {prefabPath} | {behaviour.GetType().Name}.{fieldDisplay} | {boundPath}", behaviour);
            return;
        }

        string fallbackSource = GetScriptSource(behaviour);
        bool hasFallback = !string.IsNullOrEmpty(fallbackSource)
            && HasGetComponentFallback(fallbackSource, fieldName);

        string suggestedPath = FindSuggestedPath(prefabRoot, fieldType);
        string status = hasFallback ? "[WARN]" : "[ERROR]";
        string message = $"{status} {prefabPath} | {behaviour.GetType().Name}.{fieldDisplay} | {suggestedPath}";

        if (hasFallback)
        {
            Debug.LogWarning(message, behaviour);
        }
        else
        {
            Debug.LogError(message, behaviour);
        }
    }

    private static FieldInfo FindFieldInfo(Type behaviourType, string fieldName)
    {
        return behaviourType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static Type ResolveFieldType(FieldInfo fieldInfo)
    {
        if (fieldInfo == null)
        {
            return null;
        }

        Type fieldType = fieldInfo.FieldType;
        if (fieldType.IsArray)
        {
            return fieldType.GetElementType();
        }

        if (fieldType.IsGenericType && fieldType.GetGenericArguments().Length == 1)
        {
            return fieldType.GetGenericArguments()[0];
        }

        return fieldType;
    }

    private static string ExtractRootFieldName(string propertyPath)
    {
        int arrayIndex = propertyPath.IndexOf(".Array", StringComparison.Ordinal);
        if (arrayIndex >= 0)
        {
            return propertyPath.Substring(0, arrayIndex);
        }

        int dotIndex = propertyPath.IndexOf('.');
        if (dotIndex >= 0)
        {
            return propertyPath.Substring(0, dotIndex);
        }

        return propertyPath;
    }

    private static string GetScriptSource(MonoBehaviour behaviour)
    {
        MonoScript monoScript = MonoScript.FromMonoBehaviour(behaviour);
        if (monoScript == null)
        {
            return string.Empty;
        }

        string scriptPath = AssetDatabase.GetAssetPath(monoScript);
        if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
        {
            return string.Empty;
        }

        return File.ReadAllText(scriptPath);
    }

    private static bool HasGetComponentFallback(string source, string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return false;
        }

        string escapedField = Regex.Escape(fieldName);
        string assignmentPattern = $@"\\b{escapedField}\\b\\s*=.*GetComponent";
        string tryGetPattern = $@"TryGetComponent\\s*\\(.*out\\s+{escapedField}\\b";

        string[] lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (!line.Contains(fieldName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.Contains("GetComponent", StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(line, assignmentPattern) || Regex.IsMatch(line, tryGetPattern))
            {
                return true;
            }
        }

        return false;
    }

    private static string FindSuggestedPath(GameObject prefabRoot, Type fieldType)
    {
        if (prefabRoot == null)
        {
            return "<missing prefab>";
        }

        if (fieldType == null)
        {
            return "<unknown type>";
        }

        if (fieldType == typeof(GameObject))
        {
            return GetTransformPath(prefabRoot.transform);
        }

        if (fieldType == typeof(Transform))
        {
            return GetTransformPath(prefabRoot.transform);
        }

        if (typeof(Component).IsAssignableFrom(fieldType))
        {
            Component component = prefabRoot.GetComponentInChildren(fieldType, true);
            if (component != null)
            {
                return GetTransformPath(component.transform);
            }
        }

        return "<not found>";
    }

    private static string GetObjectPath(UnityEngine.Object reference)
    {
        if (reference == null)
        {
            return "<null>";
        }

        if (reference is Component component)
        {
            return GetTransformPath(component.transform);
        }

        if (reference is GameObject gameObject)
        {
            return GetTransformPath(gameObject.transform);
        }

        return AssetDatabase.GetAssetPath(reference);
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        List<string> parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
