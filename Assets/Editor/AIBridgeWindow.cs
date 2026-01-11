using UnityEngine;
using UnityEditor;
using System.IO;

public class AIBridgeWindow : EditorWindow
{
    private string inputJson =
@"{
    ""batch_id"": ""demo_01"",
    ""clear_on_start"": true,
    ""commands"": [
        { ""id"": 1, ""action"": ""FindAssets"", ""param1"": ""t:Script AIBridge"" }
    ]
}";

    private string statusMessage = "Ready.";

    // 输出路径：Assets/AI_Output
    private string OutputFolder => Path.Combine(Application.dataPath, "AI_Output");
    private string OutputFilePath => Path.Combine(OutputFolder, "output.json");

    [MenuItem("AI Agent/Open Debug Window")]
    public static void ShowWindow()
    {
        GetWindow<AIBridgeWindow>("AI Bridge");
    }

    void OnGUI()
    {
        GUILayout.Label("AI Command Input (JSON)", EditorStyles.boldLabel);
        inputJson = EditorGUILayout.TextArea(inputJson, GUILayout.Height(150));

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Execute & Save", GUILayout.Height(30)))
        {
            RunAndSave();
        }
        if (GUILayout.Button("Reset Template", GUILayout.Width(100), GUILayout.Height(30)))
        {
            inputJson =
@"{
    ""batch_id"": ""template"",
    ""clear_on_start"": true,
    ""commands"": [
        { ""id"": 1, ""action"": ""FindAssets"", ""param1"": ""t:Script"" }
    ]
}";
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(15);
        GUILayout.Label("Status", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(statusMessage, MessageType.Info);

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Output File"))
        {
            Object obj = AssetDatabase.LoadAssetAtPath<Object>("Assets/AI_Output/output.json");
            if (obj != null) AssetDatabase.OpenAsset(obj);
            else if (File.Exists(OutputFilePath)) Application.OpenURL(OutputFilePath);
        }
        if (GUILayout.Button("Highlight Folder"))
        {
            Object folder = AssetDatabase.LoadAssetAtPath<Object>("Assets/AI_Output");
            if (folder != null) EditorGUIUtility.PingObject(folder);
        }
        GUILayout.EndHorizontal();
    }

    private void RunAndSave()
    {
        // 调用核心逻辑
        string resultJson = AIBridge.ProcessJsonRequest(inputJson);

        try
        {
            if (!Directory.Exists(OutputFolder)) Directory.CreateDirectory(OutputFolder);
            File.WriteAllText(OutputFilePath, resultJson);

            // 强制刷新，让 Unity 看到新文件
            AssetDatabase.Refresh();

            statusMessage = $"Success! \nSaved to: Assets/AI_Output/output.json\nTime: {System.DateTime.Now.ToString("HH:mm:ss")}";
            Debug.Log($"[AIBridge] Result saved to {OutputFilePath}");
        }
        catch (System.Exception e)
        {
            statusMessage = $"Error: {e.Message}";
            Debug.LogError(e);
        }
    }
}