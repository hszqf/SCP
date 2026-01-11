using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Globalization;

public class AIBridge
{
    // ==================================================================================
    // 1. Session Context
    // ==================================================================================
    public class BridgeSession : IDisposable
    {
        public string BatchId;
        public bool DryRun;
        public bool IsDirty;
        public Scene EntryActiveScene;
        public Dictionary<string, GameObject> LoadedPrefabContents = new Dictionary<string, GameObject>();
        public List<Scene> DryRunLoadedScenes = new List<Scene>();
        public HashSet<string> BackedUpPaths = new HashSet<string>();
        public string BackupRootDir;
        public string ProjectRootPath;

        public BridgeSession(string batchId, bool dryRun)
        {
            this.BatchId = batchId;
            this.DryRun = dryRun;
            this.IsDirty = false;
            this.EntryActiveScene = SceneManager.GetActiveScene();
            this.ProjectRootPath = Path.GetDirectoryName(Application.dataPath);
            this.BackupRootDir = Path.Combine(this.ProjectRootPath, "AI_Backups", BatchId + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        public void Dispose()
        {
            foreach (var kvp in LoadedPrefabContents)
            {
                try { if (kvp.Value != null) PrefabUtility.UnloadPrefabContents(kvp.Value); }
                catch (Exception e) { Debug.LogError($"[Cleanup Prefab] {e.Message}"); }
            }
            LoadedPrefabContents.Clear();

            if (DryRun && DryRunLoadedScenes.Count > 0)
            {
                try
                {
                    for (int i = DryRunLoadedScenes.Count - 1; i >= 0; i--)
                    {
                        Scene s = DryRunLoadedScenes[i];
                        if (s.isLoaded) EditorSceneManager.CloseScene(s, true);
                    }
                }
                catch (Exception e) { Debug.LogError($"[Cleanup Scene] {e.Message}"); }
                DryRunLoadedScenes.Clear();

                if (EntryActiveScene.IsValid() && EntryActiveScene.isLoaded)
                    SceneManager.SetActiveScene(EntryActiveScene);
            }

            if (!DryRun && IsDirty) AssetDatabase.Refresh();
        }

        public string GetAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            if (Path.IsPathRooted(assetPath)) return assetPath;
            return Path.Combine(ProjectRootPath, assetPath);
        }
    }

    // ==================================================================================
    // 2. 入口函数 (Fail-Fast Logic Added)
    // ==================================================================================
    public static string ProcessJsonRequest(string jsonContent)
    {
        CommandBatch batch = null;
        ResponseBatch response = new ResponseBatch();
        BridgeSession session = null;

        try
        {
            batch = JsonUtility.FromJson<CommandBatch>(jsonContent);
            response.batch_id = batch != null ? batch.batch_id : "unknown";
            response.results = new List<CommandResult>();

            if (batch == null || batch.commands == null) throw new Exception("JSON parse failed.");

            using (session = new BridgeSession(batch.batch_id, batch.dryRun))
            {
                foreach (var cmd in batch.commands)
                {
                    CommandResult result = new CommandResult { id = cmd.id };
                    try
                    {
                        ExecuteCommand(cmd, session, result);
                        result.status = "success";
                    }
                    catch (Exception e)
                    {
                        result.status = "error";
                        result.error = e.Message;
                        Debug.LogError($"[AIBridge Cmd {cmd.id} Error] {e.Message}");
                    }

                    response.results.Add(result);

                    // [Fix B] Fail-Fast: 出错即停
                    if (result.status == "error" && !batch.continueOnError)
                    {
                        Debug.LogWarning($"[AIBridge] Batch aborted at command {cmd.id} because continueOnError is false.");
                        break;
                    }
                }
            }
        }
        catch (Exception e) { return JsonUtility.ToJson(new { error = e.Message }, true); }

        return JsonUtility.ToJson(response, true);
    }

    // ==================================================================================
    // 3. 指令分发
    // ==================================================================================
    private static void ExecuteCommand(Command cmd, BridgeSession session, CommandResult result)
    {
        switch (cmd.action)
        {
            case "OpenPrefab": ExecuteOpenPrefab(session, cmd.param1); result.data = "Prefab loaded."; break;
            case "SavePrefab": ExecuteSavePrefab(session, cmd.param1); result.data = "Prefab saved."; break;
            case "OpenScene": ExecuteOpenScene(session, cmd.param1); result.data = "Scene opened."; break;
            case "SaveScene": ExecuteSaveScene(session); result.data = "Scene saved."; break;
            case "WriteScript": ExecuteWriteScript(session, cmd.param1, cmd.param2); result.data = "Script written."; break;
            case "ReadScript": result.data = ExecuteReadScript(session, cmd.param1, cmd.param2, cmd.param3); break;
            case "ReplaceSnippet": ExecuteReplaceSnippet(session, cmd.param1, cmd.param2, cmd.param3); result.data = "Snippet replaced."; break;
            case "FindAssets": result.data = ExecuteFindAssets(cmd.param1); break;
            case "GetHierarchy": result.data = ExecuteGetHierarchy(session, cmd.param1); break;
            case "InspectComponent": result.data = ExecuteInspectComponent(session, cmd.param1, cmd.param2, result); break;
            case "SetProperty":
                string val = ExecuteSetProperty(session, cmd.param1, cmd.param2, cmd.param3, cmd.param4, result);
                if (session.DryRun) { result.data = $"[DryRun] Would set {val}"; result.changed = false; }
                else { result.data = $"Set {val}"; result.changed = true; }
                break;
            default: throw new Exception($"Unknown action: {cmd.action}");
        }
    }

    // ==================================================================================
    // 4. 核心功能
    // ==================================================================================

    // [Helper] 统一路径分割 (Fix A: Path Normalization)
    private static string[] SplitPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return new string[0];
        // 移除空项 (//, 尾部/), 去除空格
        return path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToArray();
    }

    private static void ExecuteOpenPrefab(BridgeSession session, string path)
    {
        if (string.IsNullOrEmpty(path)) throw new Exception("Prefab path is empty.");
        if (session.LoadedPrefabContents.ContainsKey(path)) return;
        string fullPath = session.GetAbsolutePath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Prefab file not found: {path}");

        try { session.LoadedPrefabContents.Add(path, PrefabUtility.LoadPrefabContents(path)); }
        catch (Exception e) { throw new Exception($"LoadPrefabContents failed: {e.Message}"); }
    }

    private static void ExecuteSavePrefab(BridgeSession session, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            if (session.LoadedPrefabContents.Count == 1) path = session.LoadedPrefabContents.Keys.First();
            else throw new Exception("SavePrefab param1 empty but multiple prefabs open.");
        }
        if (!session.LoadedPrefabContents.TryGetValue(path, out GameObject root)) throw new Exception($"Prefab not open: {path}");
        if (session.DryRun) return;

        CreateBackup(session, path);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        session.IsDirty = true;
    }

    private static void ExecuteOpenScene(BridgeSession session, string path)
    {
        if (string.IsNullOrEmpty(path)) throw new Exception("Scene path empty.");
        string fullPath = session.GetAbsolutePath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Scene not found: {path}");

        if (!session.DryRun)
        {
            for (int i = 0; i < SceneManager.loadedSceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new Exception($"[Safety Abort] Unsaved changes in '{EditorSceneManager.GetSceneAt(i).name}'");
        }

        if (session.DryRun)
        {
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            session.DryRunLoadedScenes.Add(s);
            SceneManager.SetActiveScene(s);
        }
        else
        {
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SceneManager.SetActiveScene(s);
        }
    }

    private static void ExecuteSaveScene(BridgeSession session)
    {
        if (session.DryRun) return;
        Scene activeScene = SceneManager.GetActiveScene();
        CreateBackup(session, activeScene.path);
        EditorSceneManager.SaveScene(activeScene);
        session.IsDirty = true;
    }

    private static void CreateBackup(BridgeSession session, string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return;
        if (session.BackedUpPaths.Contains(assetPath)) return;
        string fullSource = session.GetAbsolutePath(assetPath);
        if (!File.Exists(fullSource)) { Debug.LogWarning($"[Backup Skipped] No file: {fullSource}"); return; }

        try
        {
            if (!Directory.Exists(session.BackupRootDir)) Directory.CreateDirectory(session.BackupRootDir);

            // 保留扩展名
            string extension = Path.GetExtension(assetPath);
            string nameNoExt = Path.GetFileNameWithoutExtension(assetPath);
            string dir = Path.GetDirectoryName(assetPath) ?? "";
            string flatDir = dir.Replace("/", "_").Replace("\\", "_").Replace(":", "");
            string flatName = $"{flatDir}_{nameNoExt}{extension}".TrimStart('_');

            File.Copy(fullSource, Path.Combine(session.BackupRootDir, flatName), true);
            session.BackedUpPaths.Add(assetPath);
        }
        catch (Exception e) { Debug.LogWarning($"[Backup Failed] {assetPath}: {e.Message}"); }
    }

    // [Updated] ResolveTarget using SplitPath
    private static GameObject ResolveTarget(BridgeSession session, string pathStr)
    {
        if (string.IsNullOrEmpty(pathStr)) throw new Exception("Target path empty");

        // --- Prefab Mode ---
        if (pathStr.StartsWith("Prefab:"))
        {
            string internalPath = pathStr.Substring(7);
            if (session.LoadedPrefabContents.Count == 1)
            {
                var root = session.LoadedPrefabContents.Values.First();
                if (internalPath == root.name) return root;

                return FindChildSecure(root.transform, internalPath);
            }
            throw new Exception($"Cannot resolve Prefab path: {internalPath}");
        }

        // --- Scene Mode ---
        if (pathStr.StartsWith("Scene:") || pathStr.StartsWith("/"))
        {
            string cleanPath = pathStr.Replace("Scene:", "");
            string[] parts = SplitPath(cleanPath); // 使用 SplitPath

            if (parts.Length == 0) return null;

            // 1. Root Check
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var matches = roots.Where(r => r.name == parts[0]).ToList();
            if (matches.Count == 0) throw new Exception($"Scene Root '{parts[0]}' not found.");
            if (matches.Count > 1) throw new Exception($"[Ambiguous Target] {matches.Count} roots named '{parts[0]}'.");

            GameObject cur = matches[0];
            if (parts.Length == 1) return cur;

            // 2. Child Check
            // 重构：将剩余部分重新组合传给 FindChildSecure (它会再次 split，但这是最安全的)
            // 或者我们可以重载 FindChildSecure 接受 string[]
            // 为了简单，我们构建一个 relative path 字符串
            string relativePath = string.Join("/", parts.Skip(1));
            return FindChildSecure(cur.transform, relativePath);
        }

        return GameObject.Find(pathStr);
    }

    // [Updated] FindChildSecure using SplitPath
    private static GameObject FindChildSecure(Transform root, string path)
    {
        string[] steps = SplitPath(path); // 使用 SplitPath
        Transform current = root;

        foreach (var step in steps)
        {
            var candidates = new List<Transform>();
            foreach (Transform child in current)
            {
                if (child.name == step) candidates.Add(child);
            }

            if (candidates.Count == 0) throw new Exception($"Child '{step}' not found under '{current.name}'");
            if (candidates.Count > 1) throw new Exception($"[Ambiguous Target] {candidates.Count} children named '{step}' under '{current.name}'.");

            current = candidates[0];
        }
        return current.gameObject;
    }

    // ==================================================================================
    // 5. 属性修改
    // ==================================================================================
    private static string ExecuteSetProperty(BridgeSession session, string targetPath, string compName, string propPath, string jsonVal, CommandResult result)
    {
        GameObject target = ResolveTarget(session, targetPath);
        result.resolved_target = target.name;

        Type compType = GetTypeByName(compName);
        if (compType == null) throw new Exception($"Type '{compName}' not found.");
        Component comp = target.GetComponent(compType);
        if (comp == null) throw new Exception($"Component '{compName}' not found.");

        SerializedObject so = new SerializedObject(comp);
        SerializedProperty prop = so.FindProperty(propPath);
        if (prop == null) throw new Exception($"Property '{propPath}' not found.");

        ApplyValueToProperty(prop, jsonVal);

        if (!session.DryRun)
        {
            so.ApplyModifiedProperties();
            session.IsDirty = true;
        }
        return $"{propPath}={jsonVal}";
    }

    private static void ApplyValueToProperty(SerializedProperty prop, string jsonValue)
    {
        try
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (float.TryParse(jsonValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) prop.floatValue = f;
                    else throw new Exception($"Invalid Float: {jsonValue}"); break;
                case SerializedPropertyType.Integer:
                    if (int.TryParse(jsonValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) prop.intValue = i;
                    else throw new Exception($"Invalid Int: {jsonValue}"); break;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(jsonValue, out bool b)) prop.boolValue = b;
                    else throw new Exception($"Invalid Bool: {jsonValue}"); break;
                case SerializedPropertyType.String: prop.stringValue = jsonValue; break;
                case SerializedPropertyType.Vector3: prop.vector3Value = JsonUtility.FromJson<Vector3>(jsonValue); break;
                case SerializedPropertyType.Color: prop.colorValue = JsonUtility.FromJson<Color>(jsonValue); break;
                case SerializedPropertyType.Enum:
                    if (int.TryParse(jsonValue, out int idx)) prop.enumValueIndex = idx;
                    else
                    {
                        int index = Array.IndexOf(prop.enumNames, jsonValue);
                        if (index >= 0) prop.enumValueIndex = index;
                        else throw new Exception($"Invalid Enum: {jsonValue}");
                    }
                    break;
                case SerializedPropertyType.ObjectReference: if (jsonValue == "null") prop.objectReferenceValue = null; break;
                default: throw new Exception($"Type {prop.propertyType} not supported.");
            }
        }
        catch (Exception e) { throw new Exception($"Parse Error '{prop.name}': {e.Message}"); }
    }

    // ==================================================================================
    // 6. 文件与辅助
    // ==================================================================================
    private static void ExecuteWriteScript(BridgeSession session, string path, string content)
    {
        if (session.DryRun) return;
        CreateBackup(session, path);
        string full = session.GetAbsolutePath(path);
        string dir = Path.GetDirectoryName(full);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
        session.IsDirty = true;
    }

    private static string ExecuteReadScript(BridgeSession session, string path, string start, string end)
    {
        string full = session.GetAbsolutePath(path);
        if (!File.Exists(full)) throw new FileNotFoundException(path);
        string[] lines = File.ReadAllLines(full);
        int s = 0, e = lines.Length;
        if (int.TryParse(start, out int _s)) s = Mathf.Clamp(_s - 1, 0, lines.Length);
        if (int.TryParse(end, out int _e)) e = Mathf.Clamp(_e, s, lines.Length);
        if (s == 0 && e == lines.Length) return string.Join("\n", lines);
        return string.Join("\n", lines.Skip(s).Take(e - s));
    }

    private static void ExecuteReplaceSnippet(BridgeSession session, string path, string old, string newVal)
    {
        string full = session.GetAbsolutePath(path);
        if (!File.Exists(full)) throw new FileNotFoundException(path);
        if (session.DryRun) return;

        CreateBackup(session, path);
        string originalText = File.ReadAllText(full);

        string lineEnding = originalText.Contains("\r\n") ? "\r\n" : "\n";
        string searchFixed = old.Replace("\r\n", "\n").Replace("\n", lineEnding);
        string replaceFixed = newVal.Replace("\r\n", "\n").Replace("\n", lineEnding);

        if (!originalText.Contains(searchFixed)) throw new Exception("Old snippet not found strictly.");

        File.WriteAllText(full, originalText.Replace(searchFixed, replaceFixed));
        session.IsDirty = true;
    }

    private static string ExecuteFindAssets(string filter)
    {
        return string.Join(";", AssetDatabase.FindAssets(filter).Select(g => AssetDatabase.GUIDToAssetPath(g)));
    }

    private static string ExecuteGetHierarchy(BridgeSession session, string target)
    {
        StringBuilder sb = new StringBuilder();
        if (target != null && target.StartsWith("Prefab:"))
        {
            foreach (var kvp in session.LoadedPrefabContents)
            {
                sb.AppendLine($"[Opened Prefab] {kvp.Key}");
                TraverseHierarchy(kvp.Value.transform, 1, sb);
            }
        }
        else if (target == "Scene" || string.IsNullOrEmpty(target))
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                TraverseHierarchy(root.transform, 0, sb);
        }
        else
        {
            GameObject t = ResolveTarget(session, target);
            if (t) TraverseHierarchy(t.transform, 0, sb);
        }
        return sb.ToString();
    }

    private static void TraverseHierarchy(Transform t, int depth, StringBuilder sb)
    {
        sb.Append(new string('-', depth * 2));
        sb.Append(t.name);
        if (!t.gameObject.activeSelf) sb.Append(" (Inactive)");
        var comps = t.GetComponents<Component>();
        List<string> cNames = new List<string>();
        foreach (var c in comps) if (c != null) cNames.Add(c.GetType().Name);
        sb.Append($" [{string.Join(",", cNames)}]");
        sb.AppendLine();
        foreach (Transform child in t) TraverseHierarchy(child, depth + 1, sb);
    }

    private static string ExecuteInspectComponent(BridgeSession session, string path, string compName, CommandResult result)
    {
        GameObject t = ResolveTarget(session, path);
        result.resolved_target = t.name;
        Type type = GetTypeByName(compName);
        if (type == null) throw new Exception($"Type {compName} not found");
        Component c = t.GetComponent(type);
        if (c == null) throw new Exception($"Component {compName} missing");

        SerializedObject so = new SerializedObject(c);
        SerializedProperty p = so.GetIterator();
        List<string> lines = new List<string>();
        bool enter = true;
        while (p.NextVisible(enter))
        {
            enter = false;
            string v = "Unk";
            try
            {
                if (p.propertyType == SerializedPropertyType.Float) v = p.floatValue.ToString(CultureInfo.InvariantCulture);
                else if (p.propertyType == SerializedPropertyType.Integer) v = p.intValue.ToString();
                else if (p.propertyType == SerializedPropertyType.Boolean) v = p.boolValue.ToString();
                else if (p.propertyType == SerializedPropertyType.String) v = p.stringValue;
                else if (p.propertyType == SerializedPropertyType.Vector3) v = p.vector3Value.ToString();
                else if (p.propertyType == SerializedPropertyType.Enum) v = p.enumNames[p.enumValueIndex];
                else if (p.propertyType == SerializedPropertyType.ObjectReference) v = p.objectReferenceValue ? p.objectReferenceValue.name : "null";
            }
            catch { }
            lines.Add($"{p.name} ({p.propertyType}): {v}");
        }
        return string.Join("\n", lines);
    }

    private static Type GetTypeByName(string name)
    {
        Type t = Type.GetType(name);
        if (t != null) return t;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = a.GetType(name);
            if (t != null) return t;
            if (!name.Contains(".")) { t = a.GetType("UnityEngine." + name); if (t != null) return t; }
        }
        return null;
    }

    [Serializable] public class CommandBatch { public string batch_id; public bool dryRun; public bool continueOnError; public List<Command> commands; }
    [Serializable] public class Command { public int id; public string action; public string param1; public string param2; public string param3; public string param4; }
    [Serializable] public class ResponseBatch { public string batch_id; public List<CommandResult> results; }
    [Serializable] public class CommandResult { public int id; public string status; public string error; public bool changed; public string resolved_target; public string data; }
}