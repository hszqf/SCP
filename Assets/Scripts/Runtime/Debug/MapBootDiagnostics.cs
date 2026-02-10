using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Startup diagnostics for EventSystem, InputModule, and GraphicRaycaster.
/// Outputs diagnostic logs with [MapBoot] prefix to help debug UI interaction issues.
/// </summary>
public static class MapBootDiagnostics
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // Use coroutine to delay 1 frame to ensure all objects are created
        var bootstrapObject = new GameObject("MapBootDiagnostics_Bootstrap");
        var coroutineRunner = bootstrapObject.AddComponent<CoroutineRunner>();
        coroutineRunner.StartCoroutine(RunDiagnosticsAfterFrame());
    }

    private static IEnumerator RunDiagnosticsAfterFrame()
    {
        // Wait one frame to ensure all RuntimeInitializeOnLoadMethod have run
        yield return null;
        
        RunDiagnostics();
        
        // Clean up the bootstrap object
        Object.Destroy(GameObject.Find("MapBootDiagnostics_Bootstrap"));
    }

    private static void RunDiagnostics()
    {
        // 1. Check LogOverlay existence
        var logOverlay = Object.FindAnyObjectByType<LogOverlay>();
        bool overlayAlive = logOverlay != null;
        string overlayType = overlayAlive ? logOverlay.GetType().Name : "None";
        Debug.Log($"[MapBoot] OverlayAlive={overlayAlive} overlayType={overlayType}");

        // 2. Check EventSystem count
        var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        Debug.Log($"[MapBoot] EventSystem count={eventSystems.Length}");

        // 3. Check EventSystem InputModule type
        string moduleType = "None";
        if (eventSystems.Length > 0)
        {
            var eventSystem = eventSystems[0];
            
#if ENABLE_INPUT_SYSTEM
            var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule != null)
            {
                moduleType = "InputSystemUIInputModule";
            }
            else
#endif
            {
                var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (standaloneModule != null)
                {
                    moduleType = "StandaloneInputModule";
                }
            }
        }
        Debug.Log($"[MapBoot] EventSystem module={moduleType}");

        // 4. Check GraphicRaycaster count
        var raycasters = Object.FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
        Debug.Log($"[MapBoot] CanvasRaycaster count={raycasters.Length}");

        // 5. Check UICamera count (optional, for Overlay mode check)
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        int uiCameraCount = 0;
        foreach (var cam in cameras)
        {
            // Count cameras that are likely UI cameras (checking if they're used by Canvas)
            if (cam.gameObject.name.Contains("UI") || cam.gameObject.name.Contains("Canvas"))
            {
                uiCameraCount++;
            }
        }
        Debug.Log($"[MapBoot] UICamera count={uiCameraCount}");

        // 6. Final completion marker
        Debug.Log("[MapBoot] Done");
    }

    /// <summary>
    /// Helper MonoBehaviour to run coroutines in static context
    /// </summary>
    private class CoroutineRunner : MonoBehaviour
    {
    }
}
