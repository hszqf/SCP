using UnityEngine;

/// <summary>
/// Auto-attaches LogOverlay to the scene at runtime.
/// Uses RuntimeInitializeOnLoadMethod to ensure it runs automatically.
/// Prevents duplicate instances with DontDestroyOnLoad.
/// No logging to prevent recursion issues.
/// </summary>
public static class LogOverlayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        try
        {
            // Check if LogOverlay already exists to prevent duplicates
            var existing = Object.FindObjectOfType<LogOverlay>();
            if (existing != null)
            {
                return; // Already exists, skip creation silently
            }

            // Create new GameObject with LogOverlay component
            var go = new GameObject("LogOverlay");
            var overlay = go.AddComponent<LogOverlay>();
            Object.DontDestroyOnLoad(go);
            
            // No Debug.Log to avoid recursion
            // Note: Initial visibility is set in LogOverlay.OnEnable() based on platform and URL
        }
        catch
        {
            // Silently fail - overlay initialization should not break main flow
        }
    }
}
