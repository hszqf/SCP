using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen log overlay for WebGL/mobile debugging.
/// Displays filtered logs ([Boot], [DataRegistry], Exception) with timestamps.
/// Toggle visibility with backtick (`) key.
/// </summary>
public class LogOverlay : MonoBehaviour
{
    private const int MaxLogCount = 60;
    private const int FontSize = 18;
    
    private readonly List<LogEntry> _logBuffer = new List<LogEntry>(MaxLogCount);
    private bool _isVisible = true;
    private Vector2 _scrollPosition;
    private GUIStyle _logStyle;
    private GUIStyle _backgroundStyle;
    private bool _stylesInitialized = false;

    private struct LogEntry
    {
        public string FormattedLine;     // Pre-formatted: "[HH:mm:ss] message"
        public string[] StackTraceLines; // Only for Exception/Error, max 2 lines
        public LogType Type;
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
        
        // Default visibility: enabled only if WebGL with ?debug=1
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            _isVisible = IsDebugModeEnabled();
        }
        
        Debug.Log("[LogOverlay] Enabled - press ` (backtick) to toggle display");
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void Update()
    {
        // Toggle visibility with backtick key
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            _isVisible = !_isVisible;
            Debug.Log($"[LogOverlay] Display {(_isVisible ? "enabled" : "disabled")}");
        }
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

        // Pre-format the log line once (don't repeat every frame)
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string formattedLine = $"[{timestamp}] {message}";

        // Extract stack trace lines only for Exception/Error (max 2 lines)
        string[] stackLines = null;
        if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
        {
            string[] allLines = stackTrace.Split('\n');
            int maxLines = Mathf.Min(allLines.Length, 2);
            stackLines = new string[maxLines];
            for (int i = 0; i < maxLines; i++)
            {
                stackLines[i] = allLines[i];
            }
        }

        var entry = new LogEntry
        {
            FormattedLine = formattedLine,
            StackTraceLines = stackLines,
            Type = type
        };

        // Ring buffer: remove oldest if at capacity
        if (_logBuffer.Count >= MaxLogCount)
            _logBuffer.RemoveAt(0);

        _logBuffer.Add(entry);
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

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!_isVisible || _logBuffer.Count == 0)
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
        GUILayout.Label("<b>[Log Overlay]</b> Press ` to toggle", _logStyle);

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(width), GUILayout.Height(height - 30));

        // Draw line-by-line: no large string concatenation
        foreach (var entry in _logBuffer)
        {
            Color color = GetColorForLogType(entry.Type);
            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            string displayText = $"<color=#{colorHex}>{entry.FormattedLine}</color>";
            
            GUI.Label(GUILayoutUtility.GetRect(new GUIContent(entry.FormattedLine), _logStyle), displayText, _logStyle);

            // Show stack trace lines only for Exception/Error (already limited to 1-2 lines)
            if (entry.StackTraceLines != null)
            {
                foreach (string line in entry.StackTraceLines)
                {
                    string stackLine = $"<color=#ff8888>  {line}</color>";
                    GUI.Label(GUILayoutUtility.GetRect(new GUIContent(line), _logStyle), stackLine, _logStyle);
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
