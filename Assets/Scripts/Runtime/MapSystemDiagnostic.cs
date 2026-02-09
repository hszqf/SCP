// Runtime diagnostic script to detect and log map system issues
// This script runs at game startup and helps identify why SimpleWorldMap isn't showing
// Author: Canvas
// Version: 1.0

using UnityEngine;
using UI.Map;

public class MapSystemDiagnostic : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void DiagnoseMapSystem()
    {
        Debug.Log("===========================================");
        Debug.Log("[MapUI] Map System Diagnostic Starting...");
        Debug.Log("===========================================");

        // Check for SimpleWorldMapPanel
        var simpleMapPanel = FindAnyObjectByType<SimpleWorldMapPanel>();
        if (simpleMapPanel == null)
        {
            Debug.LogError("[MapUI] ❌ ISSUE FOUND: SimpleWorldMapPanel NOT in scene!");
            Debug.LogError("[MapUI] The new simplified map is not instantiated.");
            Debug.LogError("[MapUI] SOLUTION: Open Unity Editor and run 'Tools > SCP > Setup Simple Map (Full)'");
        }
        else
        {
            Debug.Log($"[MapUI] ✓ SimpleWorldMapPanel found: {simpleMapPanel.gameObject.name}");
            Debug.Log($"[MapUI] ✓ SimpleWorldMapPanel active: {simpleMapPanel.gameObject.activeInHierarchy}");
        }

        // Check for old map system
        var oldMapSpawner = FindAnyObjectByType<MapNodeSpawner>();
        if (oldMapSpawner != null)
        {
            Debug.LogWarning($"[MapUI] ⚠ Old map system still active: {oldMapSpawner.gameObject.name}");
            Debug.LogWarning($"[MapUI] Old map GameObject: {oldMapSpawner.gameObject.name}, Active: {oldMapSpawner.gameObject.activeInHierarchy}");
            
            // Check parent hierarchy
            Transform parent = oldMapSpawner.transform.parent;
            string hierarchy = oldMapSpawner.gameObject.name;
            while (parent != null)
            {
                hierarchy = parent.name + " > " + hierarchy;
                parent = parent.parent;
            }
            Debug.LogWarning($"[MapUI] Old map hierarchy: {hierarchy}");
            
            if (simpleMapPanel == null)
            {
                Debug.LogError("[MapUI] ❌ PROBLEM: Old map is active but new map is missing!");
                Debug.LogError("[MapUI] This is why you're seeing the old map interface.");
            }
        }
        else
        {
            Debug.Log("[MapUI] ✓ Old map system not found (good if using new map)");
        }

        // Check for MapSystemManager
        var mapManager = FindAnyObjectByType<MapSystemManager>();
        if (mapManager != null)
        {
            Debug.Log($"[MapUI] ✓ MapSystemManager found: {mapManager.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[MapUI] ⚠ MapSystemManager not found");
            Debug.LogWarning("[MapUI] MapSystemManager is optional but helps toggle between old/new maps");
        }

        // Check for DispatchLineFX
        var dispatchFX = FindAnyObjectByType<DispatchLineFX>();
        if (dispatchFX != null)
        {
            Debug.Log($"[MapUI] ✓ DispatchLineFX found: {dispatchFX.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[MapUI] ⚠ DispatchLineFX not found (animations will not play)");
        }

        // Check for GameController
        if (GameController.I != null)
        {
            Debug.Log("[MapUI] ✓ GameController found");
        }
        else
        {
            Debug.LogError("[MapUI] ❌ GameController not found!");
        }

        // Check for UIPanelRoot
        if (UIPanelRoot.I != null)
        {
            Debug.Log("[MapUI] ✓ UIPanelRoot found");
        }
        else
        {
            Debug.LogWarning("[MapUI] ⚠ UIPanelRoot not found");
        }

        // Check for Canvas
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null)
        {
            Debug.Log($"[MapUI] ✓ Canvas found: {canvas.gameObject.name}");
            
            // List all direct children of Canvas
            Debug.Log($"[MapUI] Canvas children ({canvas.transform.childCount}):");
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                var child = canvas.transform.GetChild(i);
                Debug.Log($"[MapUI]   [{i}] {child.name} (Active: {child.gameObject.activeInHierarchy})");
            }
        }
        else
        {
            Debug.LogError("[MapUI] ❌ Canvas not found!");
        }

        Debug.Log("===========================================");
        Debug.Log("[MapUI] Map System Diagnostic Complete");
        Debug.Log("===========================================");

        // Summary
        if (simpleMapPanel == null && oldMapSpawner != null)
        {
            Debug.LogError("");
            Debug.LogError("╔═══════════════════════════════════════════════════════════════╗");
            Debug.LogError("║  DIAGNOSIS: Old map is showing because new map is missing!    ║");
            Debug.LogError("║                                                               ║");
            Debug.LogError("║  FIX REQUIRED:                                                ║");
            Debug.LogError("║  1. Open project in Unity Editor                              ║");
            Debug.LogError("║  2. Go to: Tools > SCP > Setup Simple Map (Full)              ║");
            Debug.LogError("║  3. This will generate prefabs and add SimpleWorldMapPanel    ║");
            Debug.LogError("║  4. Play the game again                                       ║");
            Debug.LogError("╚═══════════════════════════════════════════════════════════════╝");
            Debug.LogError("");
        }
        else if (simpleMapPanel != null && !simpleMapPanel.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("");
            Debug.LogWarning("╔═══════════════════════════════════════════════════════════════╗");
            Debug.LogWarning("║  DIAGNOSIS: SimpleWorldMapPanel exists but is inactive!       ║");
            Debug.LogWarning("║                                                               ║");
            Debug.LogWarning("║  FIX REQUIRED:                                                ║");
            Debug.LogWarning("║  1. Open Assets/Scenes/Main.unity in Unity Editor             ║");
            Debug.LogWarning("║  2. Find SimpleWorldMapPanel in hierarchy                     ║");
            Debug.LogWarning("║  3. Enable it (check the checkbox in Inspector)               ║");
            Debug.LogWarning("║  4. Save the scene                                            ║");
            Debug.LogWarning("╚═══════════════════════════════════════════════════════════════╝");
            Debug.LogWarning("");
        }
        else if (simpleMapPanel != null && oldMapSpawner != null && oldMapSpawner.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("");
            Debug.LogWarning("╔═══════════════════════════════════════════════════════════════╗");
            Debug.LogWarning("║  DIAGNOSIS: Both old and new maps are active!                 ║");
            Debug.LogWarning("║                                                               ║");
            Debug.LogWarning("║  FIX REQUIRED:                                                ║");
            Debug.LogWarning("║  1. Add MapSystemManager to Canvas (or disable old map)       ║");
            Debug.LogWarning("║  2. Run: Tools > SCP > Setup Simple Map (Full)                ║");
            Debug.LogWarning("╚═══════════════════════════════════════════════════════════════╝");
            Debug.LogWarning("");
        }
        else if (simpleMapPanel != null && simpleMapPanel.gameObject.activeInHierarchy)
        {
            Debug.Log("");
            Debug.Log("╔═══════════════════════════════════════════════════════════════╗");
            Debug.Log("║  ✅ Map system looks good! SimpleWorldMapPanel is active.    ║");
            Debug.Log("╚═══════════════════════════════════════════════════════════════╝");
            Debug.Log("");
        }
    }
}
