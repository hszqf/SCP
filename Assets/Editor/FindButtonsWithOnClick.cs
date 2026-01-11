#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FindButtonsWithOnClick
{
    [MenuItem("Tools/QA/Find Buttons With Persistent OnClick (Open Scenes)")]
    public static void FindInOpenScenes()
    {
        int totalButtons = 0;
        int hitButtons = 0;

        var sb = new StringBuilder();
        sb.AppendLine("=== Buttons with Persistent OnClick (Inspector bindings) ===");

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                var buttons = root.GetComponentsInChildren<Button>(true);
                totalButtons += buttons.Length;

                foreach (var btn in buttons)
                {
                    if (btn == null) continue;

                    int n = btn.onClick.GetPersistentEventCount();
                    if (n <= 0) continue;

                    hitButtons++;

                    sb.AppendLine($"- Scene: {scene.name} | {GetPath(btn.transform)} | PersistentCount={n}");

                    for (int i = 0; i < n; i++)
                    {
                        var target = btn.onClick.GetPersistentTarget(i);
                        var method = btn.onClick.GetPersistentMethodName(i);
                        sb.AppendLine($"    [{i}] {TargetName(target)}  ->  {method}");
                    }

                    // 方便你点 Console 直接定位
                    Debug.Log($"[OnClick] {scene.name} | {GetPath(btn.transform)} | PersistentCount={n}", btn.gameObject);
                }
            }
        }

        sb.AppendLine($"=== Summary: buttons={totalButtons}, withPersistentOnClick={hitButtons} ===");
        Debug.Log(sb.ToString());
    }

    private static string TargetName(Object o)
    {
        if (o == null) return "(null)";
        return $"{o.name} ({o.GetType().Name})";
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "(null)";
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
#endif
