using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

public class SceneStructureFixer
{
    [MenuItem("Tools/SCP Manager/Fix UI Structure (Add Backgrounds)")]
    public static void Run()
    {
        var root = GameObject.FindObjectOfType<UIPanelRoot>();
        if (!root)
        {
            Debug.LogError("Current scene has no UIPanelRoot!");
            return;
        }

        // 使用 SerializedObject 访问 private 字段
        var so = new SerializedObject(root);
        FixPanel(so.FindProperty("nodePanel").objectReferenceValue as GameObject, "NodePanel");
        FixPanel(so.FindProperty("eventPanel").objectReferenceValue as GameObject, "EventPanel");
        FixPanel(so.FindProperty("newsPanel").objectReferenceValue as GameObject, "NewsPanel");

        Debug.Log("UI Structure Fixed: Panels are now self-contained modals!");
    }

    static void FixPanel(GameObject panel, string name)
    {
        if (!panel) return;
        
        // 1. 确保有 CanvasRenderer (UI 基本组件)
        if (!panel.GetComponent<CanvasRenderer>()) panel.AddComponent<CanvasRenderer>();

        // 2. 检查是否有全屏背景 Image
        var img = panel.GetComponent<Image>();
        if (!img)
        {
            // 如果自己没有，可能是作为空父物体存在的。我们给它加一个。
            img = panel.AddComponent<Image>();
            // 默认颜色：黑色半透明
            img.color = new Color(0, 0, 0, 0.7f);
            Debug.Log($"Added background Image to {name}");
        }
        else
        {
            // 如果已经有 Image 但它是完全透明的(纯容器)，或者颜色不对，修正它
            if (img.color.a < 0.1f)
            {
                img.color = new Color(0, 0, 0, 0.7f);
                Debug.Log($"Updated background color for {name}");
            }
        }

        // 3. 确保 Raycast Target 开启 (阻挡点击)
        img.raycastTarget = true;

        // 4. 确保填满屏幕
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }
}