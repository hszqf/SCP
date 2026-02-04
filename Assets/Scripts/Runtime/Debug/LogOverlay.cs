using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// On-screen log overlay for WebGL/mobile debugging.
/// Displays filtered logs ([Boot], [DataRegistry], Exception) with timestamps.
/// Toggle visibility with on-screen button or keyboard (Input System only).
/// Supports log deduplication with counter and copy to clipboard.
/// </summary>
public class LogOverlay : MonoBehaviour
{
    private const int MaxLogCount = 100;
    private const int FontSize = 18;
    private const int ButtonHeight = 30;
    
    private readonly Dictionary<string, LogEntry> _logDict = new Dictionary<string, LogEntry>();
    private readonly List<string> _logKeys = new List<string>(); // Ordered keys for display
    private bool _isVisible = true;
    private Vector2 _scrollPosition;
    private GUIStyle _logStyle;
    private GUIStyle _backgroundStyle;
    private GUIStyle _buttonStyle;
    private bool _stylesInitialized = false;

    /// <summary>
    /// Log entry with deduplication support.
    /// Class (not struct) is required to enable in-place updates to Count and LastTimestamp
    /// when the same log occurs multiple times.
    /// </summary>
    private class LogEntry
    {
        public string Message;           // Original message (without timestamp)
        public string[] StackTraceLines; // Only for Exception/Error, max 2 lines
        public LogType Type;
        public int Count;                // Number of times this log occurred
        public string LastTimestamp;     // Last occurrence timestamp
        public string FirstTimestamp;    // First occurrence timestamp
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
        
        // Default visibility: enabled only if WebGL with ?debug=1
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            _isVisible = IsDebugModeEnabled();
        }
        
        Debug.Log("[LogOverlay] Enabled - use on-screen buttons to toggle display");
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // Optional keyboard toggle using Input System (only when available)
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
        {
            _isVisible = !_isVisible;
            Debug.Log($"[LogOverlay] Display {(_isVisible ? "enabled" : "disabled")}");
        }
#endif
        // Keyboard input requires Input System package to be enabled
    }

    private void HandleLog(string message, string stackTrace, LogType type)
    {
        // Filter: only show logs containing [Boot], [DataRegistry], or Exception
        bool shouldShow = message.Contains("[Boot]") || 
                         message.Contains("[DataRegistry]") || 
                         message.Contains("Exception") ||
                         type == LogType.Exception;

        if (!shouldShow)
            return;

        // Extract first line of stack trace for deduplication key
        string stackFirstLine = "";
        string[] stackLines = null;
        if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
        {
            string[] allLines = stackTrace.Split('\n');
            if (allLines.Length > 0)
            {
                stackFirstLine = allLines[0].Trim();
            }
            
            // Store max 2 lines for display
            int maxLines = Mathf.Min(allLines.Length, 2);
            stackLines = new string[maxLines];
            for (int i = 0; i < maxLines; i++)
            {
                stackLines[i] = allLines[i];
            }
        }

        // Generate deduplication key: LogType + message + first stack trace line
        string key = $"{type}|{message}|{stackFirstLine}";
        
        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        if (_logDict.TryGetValue(key, out var existingEntry))
        {
            // Update existing entry
            existingEntry.Count++;
            existingEntry.LastTimestamp = timestamp;
            // Stack trace lines remain the same
        }
        else
        {
            // Create new entry
            var entry = new LogEntry
            {
                Message = message,
                StackTraceLines = stackLines,
                Type = type,
                Count = 1,
                FirstTimestamp = timestamp,
                LastTimestamp = timestamp
            };
            
            _logDict[key] = entry;
            _logKeys.Add(key);
            
            // Ring buffer: remove oldest if at capacity
            if (_logKeys.Count > MaxLogCount)
            {
                string oldestKey = _logKeys[0];
                _logKeys.RemoveAt(0);
                _logDict.Remove(oldestKey);
            }
        }
    }

    private void InitializeStyles()
    {
        if (_stylesInitialized)
            return;

        _logStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = FontSize,
            wordWrap = false,
            richText = true,
            normal = { textColor = Color.white }
        };

        _backgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTexture(2, 2, new Color(0, 0, 0, 0.8f)) }
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = FontSize - 2,
            normal = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.2f, 0.9f)) },
            hover = { textColor = Color.yellow, background = MakeTexture(2, 2, new Color(0.3f, 0.3f, 0.3f, 0.9f)) }
        };

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (_logDict.Count == 0)
            return;

        InitializeStyles();

        // Position overlay in top-right area to avoid HUD panels
        float width = Mathf.Min(Screen.width * 0.4f, 600f);
        float height = Mathf.Min(Screen.height * 0.6f, 400f);
        float x = Screen.width - width - 10f;
        float y = 10f;

        Rect windowRect = new Rect(x, y, width, height);

        GUI.Box(windowRect, "", _backgroundStyle);

        GUILayout.BeginArea(windowRect);
        
        // Top button bar
        GUILayout.BeginHorizontal();
        
        // Toggle visibility button
        if (GUILayout.Button(_isVisible ? "Hide" : "Show", _buttonStyle, GUILayout.Height(ButtonHeight)))
        {
            _isVisible = !_isVisible;
            Debug.Log($"[LogOverlay] Display {(_isVisible ? "enabled" : "disabled")}");
        }
        
        // Copy logs button
        if (GUILayout.Button("Copy", _buttonStyle, GUILayout.Height(ButtonHeight)))
        {
            CopyLogsToClipboard();
        }
        
        GUILayout.EndHorizontal();

        if (!_isVisible)
        {
            GUILayout.EndArea();
            return;
        }

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(width), GUILayout.Height(height - ButtonHeight - 10));

        // Draw deduplicated logs with counter
        foreach (string key in _logKeys)
        {
            if (!_logDict.TryGetValue(key, out var entry))
                continue; // Should never happen, but defensive check
                
            Color color = GetColorForLogType(entry.Type);
            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            
            // Format: [timestamp] message (xN if count > 1)
            string countSuffix = entry.Count > 1 ? $" (x{entry.Count})" : "";
            string plainText = $"[{entry.LastTimestamp}] {entry.Message}{countSuffix}";
            string displayText = $"<color=#{colorHex}>{plainText}</color>";
            
            // Use plain text for size calculation to avoid rich text markup issues
            GUIContent content = new GUIContent(plainText);
            GUI.Label(GUILayoutUtility.GetRect(content, _logStyle), displayText, _logStyle);

            // Show stack trace lines only for Exception/Error (already limited to 1-2 lines)
            if (entry.StackTraceLines != null)
            {
                foreach (string line in entry.StackTraceLines)
                {
                    string plainStackLine = $"  {line}";
                    string coloredStackLine = $"<color=#ff8888>{plainStackLine}</color>";
                    GUI.Label(GUILayoutUtility.GetRect(new GUIContent(plainStackLine), _logStyle), coloredStackLine, _logStyle);
                }
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // Auto-scroll to bottom when new logs arrive
        if (Event.current.type == EventType.Repaint)
        {
            _scrollPosition.y = float.MaxValue;
        }
    }

    private Color GetColorForLogType(LogType type)
    {
        return type switch
        {
            LogType.Error => new Color(1f, 0.3f, 0.3f),
            LogType.Assert => new Color(1f, 0.3f, 0.3f),
            LogType.Warning => new Color(1f, 0.8f, 0.2f),
            LogType.Log => new Color(0.8f, 0.8f, 0.8f),
            LogType.Exception => new Color(1f, 0.2f, 0.2f),
            _ => Color.white
        };
    }

    /// <summary>
    /// Copy all current logs to clipboard as plain text
    /// </summary>
    private void CopyLogsToClipboard()
    {
        if (_logKeys.Count == 0)
        {
            Debug.Log("[LogOverlay] No logs to copy");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== LogOverlay Export ===");
        sb.AppendLine($"Exported at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total unique logs: {_logKeys.Count}");
        sb.AppendLine();

        foreach (string key in _logKeys)
        {
            if (!_logDict.TryGetValue(key, out var entry))
                continue;

            string countInfo = entry.Count > 1 ? $" (occurred {entry.Count} times)" : "";
            sb.AppendLine($"[{entry.LastTimestamp}] {entry.Type}: {entry.Message}{countInfo}");

            if (entry.StackTraceLines != null)
            {
                foreach (string line in entry.StackTraceLines)
                {
                    sb.AppendLine($"  {line}");
                }
            }
            sb.AppendLine();
        }

        GUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log($"[LogOverlay] Copied {_logKeys.Count} log entries to clipboard");
    }

    /// <summary>
    /// Check if debug mode is enabled via URL parameter (?debug=1)
    /// </summary>
    private bool IsDebugModeEnabled()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer)
            return true; // Non-WebGL: show by default for development
        
        try
        {
            string url = Application.absoluteURL;
            return !string.IsNullOrEmpty(url) && url.Contains("debug=1");
        }
        catch
        {
            return false; // If absoluteURL fails, default to hidden
        }
    }

    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
