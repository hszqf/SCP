// Runtime verification script for Simple World Map UI
// Checks if all required components are properly set up
// Author: Canvas
// Version: 1.0

using UnityEngine;
using UI.Map;

#if UNITY_EDITOR
namespace Editor
{
    [UnityEditor.InitializeOnLoad]
    public static class MapUIVerifier
    {
        static MapUIVerifier()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                // Delay check to allow Awake/Start to run
                UnityEditor.EditorApplication.delayCall += VerifyMapSetup;
            }
        }

        private static void VerifyMapSetup()
        {
            Debug.Log("[MapUI Verifier] Checking Simple World Map setup...");

            bool hasIssues = false;

            // Check for SimpleWorldMapPanel
            var mapPanel = Object.FindAnyObjectByType<SimpleWorldMapPanel>();
            if (mapPanel == null)
            {
                Debug.LogWarning("[MapUI Verifier] SimpleWorldMapPanel not found in scene. Map will not be visible.");
                hasIssues = true;
            }
            else
            {
                Debug.Log("[MapUI Verifier] ✓ SimpleWorldMapPanel found");

                // Check prefab references
                var so = new UnityEditor.SerializedObject(mapPanel);
                var nodeMarkerPrefab = so.FindProperty("nodeMarkerPrefab").objectReferenceValue;
                var hqMarkerPrefab = so.FindProperty("hqMarkerPrefab").objectReferenceValue;

                if (nodeMarkerPrefab == null)
                {
                    Debug.LogWarning("[MapUI Verifier] NodeMarker prefab not assigned in SimpleWorldMapPanel");
                    hasIssues = true;
                }
                else
                {
                    Debug.Log("[MapUI Verifier] ✓ NodeMarker prefab assigned");
                }

                if (hqMarkerPrefab == null)
                {
                    Debug.LogWarning("[MapUI Verifier] HQMarker prefab not assigned in SimpleWorldMapPanel");
                    hasIssues = true;
                }
                else
                {
                    Debug.Log("[MapUI Verifier] ✓ HQMarker prefab assigned");
                }
            }

            // Check for DispatchLineFX
            var dispatchFX = Object.FindAnyObjectByType<DispatchLineFX>();
            if (dispatchFX == null)
            {
                Debug.LogWarning("[MapUI Verifier] DispatchLineFX not found. Animations will not play.");
                hasIssues = true;
            }
            else
            {
                Debug.Log("[MapUI Verifier] ✓ DispatchLineFX found");
            }

            // Check for MapSystemManager (optional)
            var mapManager = Object.FindAnyObjectByType<MapSystemManager>();
            if (mapManager != null)
            {
                Debug.Log("[MapUI Verifier] ✓ MapSystemManager found (old map toggle available)");
            }

            // Check for GameController
            if (GameController.I == null)
            {
                Debug.LogError("[MapUI Verifier] GameController not found! Map requires GameController to function.");
                hasIssues = true;
            }
            else
            {
                Debug.Log("[MapUI Verifier] ✓ GameController found");
            }

            // Check for UIPanelRoot
            if (UIPanelRoot.I == null)
            {
                Debug.LogWarning("[MapUI Verifier] UIPanelRoot not found. Panel interactions may not work.");
                hasIssues = true;
            }
            else
            {
                Debug.Log("[MapUI Verifier] ✓ UIPanelRoot found");
            }

            if (!hasIssues)
            {
                Debug.Log("[MapUI Verifier] ✅ All checks passed! Simple World Map should be fully functional.");
            }
            else
            {
                Debug.LogWarning("[MapUI Verifier] ⚠️ Some issues detected. Check warnings above and review Docs/SimpleWorldMapSetup.md");
            }
        }
    }
}
#endif

// Runtime component that can be added to scene for on-demand verification
public class MapUIRuntimeVerifier : MonoBehaviour
{
    [Header("Verification Settings")]
    [SerializeField] private bool verifyOnStart = true;
    [SerializeField] private bool showSuccessLogs = true;

    private void Start()
    {
        if (verifyOnStart)
        {
            Verify();
        }
    }

    [ContextMenu("Verify Map Setup")]
    public void Verify()
    {
        Debug.Log("[MapUI Runtime Verifier] Starting verification...");

        int warnings = 0;
        int errors = 0;

        // Check SimpleWorldMapPanel
        var mapPanel = FindAnyObjectByType<SimpleWorldMapPanel>();
        if (mapPanel == null)
        {
            Debug.LogError("[MapUI] SimpleWorldMapPanel not found in scene!");
            errors++;
        }
        else if (showSuccessLogs)
        {
            Debug.Log("[MapUI] ✓ SimpleWorldMapPanel present");
        }

        // Check DispatchLineFX
        var dispatchFX = FindAnyObjectByType<DispatchLineFX>();
        if (dispatchFX == null)
        {
            Debug.LogWarning("[MapUI] DispatchLineFX not found - animations disabled");
            warnings++;
        }
        else if (showSuccessLogs)
        {
            Debug.Log("[MapUI] ✓ DispatchLineFX present");
        }

        // Check GameController
        if (GameController.I == null)
        {
            Debug.LogError("[MapUI] GameController not found!");
            errors++;
        }
        else if (showSuccessLogs)
        {
            Debug.Log("[MapUI] ✓ GameController present");
        }

        // Check UIPanelRoot
        if (UIPanelRoot.I == null)
        {
            Debug.LogWarning("[MapUI] UIPanelRoot not found - panel interactions may fail");
            warnings++;
        }
        else if (showSuccessLogs)
        {
            Debug.Log("[MapUI] ✓ UIPanelRoot present");
        }

        // Summary
        if (errors == 0 && warnings == 0)
        {
            Debug.Log($"[MapUI] ✅ Verification complete: All systems operational");
        }
        else
        {
            Debug.LogWarning($"[MapUI] ⚠️ Verification complete: {errors} errors, {warnings} warnings");
        }
    }
}
