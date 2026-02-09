using UnityEngine;

/// <summary>
/// Auto-attaches LogOverlay to the scene at runtime.
/// Uses RuntimeInitializeOnLoadMethod to ensure it runs automatically.
/// Prevents duplicate instances with DontDestroyOnLoad.
/// </summary>
public static class LogOverlayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // Check if LogOverlay already exists to prevent duplicates
        var existing = Object.FindAnyObjectByType<LogOverlay>();
        if (existing != null)
        {
            Debug.Log("[LogOverlayBootstrap] LogOverlay already exists, skipping creation");
            return;
        }

        // Create new GameObject with LogOverlay component
        var go = new GameObject("LogOverlay");
        var overlay = go.AddComponent<LogOverlay>();
        Object.DontDestroyOnLoad(go);
        
        Debug.Log("[LogOverlayBootstrap] LogOverlay created and initialized");
        
        // Note: Initial visibility is set in LogOverlay.OnEnable() based on platform and URL
    }
}
