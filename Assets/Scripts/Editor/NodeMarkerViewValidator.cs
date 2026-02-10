// NodeMarkerViewValidator - Editor validation script
// Author: Canvas
// Version: 1.0

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace UI.Map.Editor
{
    /// <summary>
    /// Validates NodeMarkerView implementation in the scene
    /// </summary>
    public class NodeMarkerViewValidator
    {
        [MenuItem("Tools/SCP/Validate NodeMarkerView Setup")]
        public static void ValidateSetup()
        {
            Debug.Log("[NodeMarkerValidator] Starting validation...");
            
            int errors = 0;
            int warnings = 0;
            int successes = 0;

            // Check 1: Prefab exists
            string prefabPath = "Assets/Prefabs/UI/Map/NodeMarkerView.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            
            if (prefab == null)
            {
                Debug.LogError($"[NodeMarkerValidator] ✗ Prefab not found at {prefabPath}. Run 'Generate NodeMarkerView Prefab' first.");
                errors++;
            }
            else
            {
                Debug.Log($"[NodeMarkerValidator] ✓ Prefab found at {prefabPath}");
                successes++;
                
                // Check prefab structure
                var markerView = prefab.GetComponent<NodeMarkerView>();
                if (markerView == null)
                {
                    Debug.LogError("[NodeMarkerValidator] ✗ Prefab missing NodeMarkerView component");
                    errors++;
                }
                else
                {
                    Debug.Log("[NodeMarkerValidator] ✓ Prefab has NodeMarkerView component");
                    successes++;
                }
                
                // Check required child objects
                string[] requiredChildren = { "Dot", "Name", "TaskBar", "EventBadge", "UnknownIcon" };
                foreach (var childName in requiredChildren)
                {
                    var child = prefab.transform.Find(childName);
                    if (child == null)
                    {
                        Debug.LogWarning($"[NodeMarkerValidator] ! Prefab missing child: {childName}");
                        warnings++;
                    }
                    else
                    {
                        Debug.Log($"[NodeMarkerValidator] ✓ Prefab has child: {childName}");
                        successes++;
                    }
                }
            }

            // Check 2: Scene setup
            var mapBootstrap = GameObject.Find("MapBootstrap");
            if (mapBootstrap == null)
            {
                Debug.LogWarning("[NodeMarkerValidator] ! MapBootstrap GameObject not found in scene");
                warnings++;
            }
            else
            {
                Debug.Log("[NodeMarkerValidator] ✓ MapBootstrap found in scene");
                successes++;
                
                var newMapRuntime = mapBootstrap.GetComponent<NewMapRuntime>();
                if (newMapRuntime == null)
                {
                    Debug.LogError("[NodeMarkerValidator] ✗ MapBootstrap missing NewMapRuntime component");
                    errors++;
                }
                else
                {
                    Debug.Log("[NodeMarkerValidator] ✓ MapBootstrap has NewMapRuntime component");
                    successes++;
                    
                    // Check prefab assignment using SerializedObject
                    SerializedObject so = new SerializedObject(newMapRuntime);
                    var prefabProp = so.FindProperty("nodeMarkerPrefab");
                    
                    if (prefabProp != null && prefabProp.objectReferenceValue != null)
                    {
                        Debug.Log("[NodeMarkerValidator] ✓ NodeMarkerPrefab is assigned to NewMapRuntime");
                        successes++;
                    }
                    else
                    {
                        Debug.LogWarning("[NodeMarkerValidator] ! NodeMarkerPrefab is NOT assigned to NewMapRuntime");
                        Debug.LogWarning("[NodeMarkerValidator]   → Select MapBootstrap in Hierarchy");
                        Debug.LogWarning("[NodeMarkerValidator]   → Drag NodeMarkerView.prefab to 'Node Marker Prefab' field");
                        warnings++;
                    }
                }
            }

            // Check 3: GameController exists
            var gameController = Object.FindFirstObjectByType<GameController>();
            if (gameController == null)
            {
                Debug.LogWarning("[NodeMarkerValidator] ! GameController not found in scene");
                warnings++;
            }
            else
            {
                Debug.Log("[NodeMarkerValidator] ✓ GameController found in scene");
                successes++;
            }

            // Summary
            Debug.Log("[NodeMarkerValidator] ========================================");
            Debug.Log($"[NodeMarkerValidator] Validation complete:");
            Debug.Log($"[NodeMarkerValidator]   Successes: {successes}");
            Debug.Log($"[NodeMarkerValidator]   Warnings:  {warnings}");
            Debug.Log($"[NodeMarkerValidator]   Errors:    {errors}");
            
            if (errors == 0 && warnings == 0)
            {
                Debug.Log("[NodeMarkerValidator] ✓ ALL CHECKS PASSED!");
                Debug.Log("[NodeMarkerValidator] Ready to test in Play mode.");
            }
            else if (errors == 0)
            {
                Debug.Log("[NodeMarkerValidator] ⚠ SETUP INCOMPLETE (see warnings above)");
            }
            else
            {
                Debug.LogError("[NodeMarkerValidator] ✗ SETUP HAS ERRORS (see above)");
            }
            
            Debug.Log("[NodeMarkerValidator] ========================================");
        }

        [MenuItem("Tools/SCP/Quick Setup NodeMarkerView")]
        public static void QuickSetup()
        {
            Debug.Log("[NodeMarkerValidator] Running quick setup...");
            
            // Step 1: Generate prefab if it doesn't exist
            string prefabPath = "Assets/Prefabs/UI/Map/NodeMarkerView.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            
            if (prefab == null)
            {
                Debug.Log("[NodeMarkerValidator] Generating prefab...");
                NodeMarkerPrefabGenerator.GeneratePrefab();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            
            if (prefab == null)
            {
                Debug.LogError("[NodeMarkerValidator] ✗ Failed to generate prefab");
                return;
            }
            
            Debug.Log("[NodeMarkerValidator] ✓ Prefab ready");
            
            // Step 2: Find and setup NewMapRuntime
            var mapBootstrap = GameObject.Find("MapBootstrap");
            if (mapBootstrap == null)
            {
                Debug.LogError("[NodeMarkerValidator] ✗ MapBootstrap not found. Cannot auto-assign prefab.");
                return;
            }
            
            var newMapRuntime = mapBootstrap.GetComponent<NewMapRuntime>();
            if (newMapRuntime == null)
            {
                Debug.LogError("[NodeMarkerValidator] ✗ NewMapRuntime component not found on MapBootstrap");
                return;
            }
            
            // Assign prefab using SerializedObject
            SerializedObject so = new SerializedObject(newMapRuntime);
            var prefabProp = so.FindProperty("nodeMarkerPrefab");
            
            if (prefabProp != null)
            {
                prefabProp.objectReferenceValue = prefab.GetComponent<NodeMarkerView>();
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(newMapRuntime);
                
                Debug.Log("[NodeMarkerValidator] ✓ Prefab assigned to NewMapRuntime");
                Debug.Log("[NodeMarkerValidator] ========================================");
                Debug.Log("[NodeMarkerValidator] QUICK SETUP COMPLETE!");
                Debug.Log("[NodeMarkerValidator] You can now enter Play mode to test.");
                Debug.Log("[NodeMarkerValidator] ========================================");
            }
            else
            {
                Debug.LogError("[NodeMarkerValidator] ✗ Could not find nodeMarkerPrefab field");
            }
        }
    }
}
#endif
