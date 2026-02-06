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
    private const int MaxMessageLength = 500;
    private const int MaxStackTraceLineLength = 300;
    private const int MaxExportLength = 20480; // 20KB
    private const int MaxCallsPerFrame = 200;
    
    private readonly Dictionary<string, LogEntry> _logDict = new Dictionary<string, LogEntry>();
    private readonly List<string> _logKeys = new List<string>(); // Ordered keys for display
    private bool _isVisible = true;
    private Vector2 _scrollPosition;
    private GUIStyle _logStyle;
    private GUIStyle _backgroundStyle;
    private GUIStyle _buttonStyle;
    private bool _stylesInitialized = false;
    
    // Export panel state
    private bool _exportPanelOpen = false;
    private Vector2 _exportScrollPosition;
    private string _exportText = "";
    private string _copyMessage = "";
    private float _copyMessageEndTime = 0f;
    
    // Re-entrancy and frame protection
    private static bool _inLogHandler = false;
    private static int _logHandlerCallsThisFrame = 0;
    private static int _lastFrameCount = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int SCP_CopyToClipboard(string str);
#endif

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
        
        // No Debug.Log here to avoid recursion
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void Update()
    {
        // Reset frame counter at frame boundary
        int currentFrame = Time.frameCount;
        if (currentFrame != _lastFrameCount)
        {
            _lastFrameCount = currentFrame;
            _logHandlerCallsThisFrame = 0;
        }
        
#if ENABLE_INPUT_SYSTEM
        // Optional keyboard toggle using Input System (only when available)
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
        {
            _isVisible = !_isVisible;
            // No Debug.Log to avoid recursion
        }
#endif
        // Keyboard input requires Input System package to be enabled
        
        // Clear copy message after timeout
        if (Time.time > _copyMessageEndTime)
        {
            _copyMessage = "";
        }
    }

    private void HandleLog(string message, string stackTrace, LogType type)
    {
        // Re-entrancy guard: prevent recursion
        if (_inLogHandler)
            return;
        
        // Frame-based protection: prevent stack overflow from excessive calls
        if (_logHandlerCallsThisFrame >= MaxCallsPerFrame)
            return;
        
        _inLogHandler = true;
        _logHandlerCallsThisFrame++;
        
        try
        {
            // Safe string handling: null checks and truncation
            if (message == null)
                message = "";
            if (message.Length > MaxMessageLength)
                message = message.Substring(0, MaxMessageLength);
            
            // Filter: only show logs containing [Boot], [DataRegistry], [News], or Exception
            bool shouldShow = message.Contains("[Boot]") || 
                             message.Contains("[DataRegistry]") || 
                             message.Contains("[News]") ||
                             message.Contains("Exception") ||
                             type == LogType.Exception;

            if (!shouldShow)
                return;

            // Strip non-ASCII characters to diagnose WebGL tofu issue
            message = StripNonAscii(message);
            
            // Safe stackTrace handling
            if (stackTrace != null)
            {
                stackTrace = StripNonAscii(stackTrace);
            }

            // Extract first line of stack trace for deduplication key
            string stackFirstLine = "";
            string[] stackLines = null;
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
            {
                string[] allLines = stackTrace.Split('\n');
                if (allLines != null && allLines.Length > 0)
                {
                    stackFirstLine = allLines[0].Trim();
                    if (stackFirstLine.Length > MaxStackTraceLineLength)
                        stackFirstLine = stackFirstLine.Substring(0, MaxStackTraceLineLength);
                }
                
                // Store max 2 lines for display
                if (allLines != null)
                {
                    int maxLines = Mathf.Min(allLines.Length, 2);
                    stackLines = new string[maxLines];
                    for (int i = 0; i < maxLines; i++)
                    {
                        string line = allLines[i];
                        if (line != null && line.Length > MaxStackTraceLineLength)
                            line = line.Substring(0, MaxStackTraceLineLength);
                        stackLines[i] = line;
                    }
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
        catch
        {
            // Swallow exceptions - DO NOT log here to avoid recursion
        }
        finally
        {
            _inLogHandler = false;
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
            richText = false, // Temporarily disabled to diagnose WebGL tofu issue
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
        InitializeStyles();

        // ASCII self-check: Always visible at top-left to verify overlay rendering
        GUI.Label(new Rect(10, 10, 400, 30), $"OVERLAY_OK entries={_logDict.Count}", _logStyle);

        if (_logDict.Count == 0)
            return;

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
            // No Debug.Log to avoid recursion
        }
        
        // Copy logs button
        if (GUILayout.Button("Copy", _buttonStyle, GUILayout.Height(ButtonHeight)))
        {
            CopyLogsToClipboard();
        }
        
        // Export button
        if (GUILayout.Button("Export", _buttonStyle, GUILayout.Height(ButtonHeight)))
        {
            GenerateExportText();
            _exportPanelOpen = true;
        }
        
        // Close button (only when Export panel is open)
        if (_exportPanelOpen)
        {
            if (GUILayout.Button("Close", _buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                _exportPanelOpen = false;
            }
        }
        
        // Refresh button (only when Export panel is open)
        if (_exportPanelOpen)
        {
            if (GUILayout.Button("Refresh", _buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                GenerateExportText();
            }
        }
        
        GUILayout.EndHorizontal();
        
        // Show copy message if present
        if (!string.IsNullOrEmpty(_copyMessage))
        {
            GUILayout.Label(_copyMessage, _buttonStyle);
        }

        if (!_isVisible)
        {
            GUILayout.EndArea();
            return;
        }

        // Show either the log view or export panel
        if (_exportPanelOpen)
        {
            // Export panel with scrollable textarea
            float panelHeight = height - ButtonHeight - 40;
            _exportScrollPosition = GUILayout.BeginScrollView(_exportScrollPosition, GUILayout.Width(width - 10), GUILayout.Height(panelHeight));
            
            // Multi-line text area (users can long-press to select and copy)
            GUILayout.TextArea(_exportText, GUILayout.ExpandHeight(true));
            
            GUILayout.EndScrollView();
        }
        else
        {
            // Normal log view
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(width), GUILayout.Height(height - ButtonHeight - 10));

            // Draw deduplicated logs with counter
            foreach (string key in _logKeys)
            {
                if (!_logDict.TryGetValue(key, out var entry))
                    continue; // Should never happen, but defensive check
                    
                // Format: [timestamp] message (xN if count > 1)
                string countSuffix = entry.Count > 1 ? $" (x{entry.Count})" : "";
                string displayText = $"[{entry.LastTimestamp}] {entry.Message}{countSuffix}";
                
                // Use plain text without rich text markup (richText is disabled)
                GUIContent content = new GUIContent(displayText);
                GUI.Label(GUILayoutUtility.GetRect(content, _logStyle), displayText, _logStyle);

                // Show stack trace lines only for Exception/Error (already limited to 1-2 lines)
                if (entry.StackTraceLines != null)
                {
                    foreach (string line in entry.StackTraceLines)
                    {
                        string stackLine = $"  {line}";
                        GUI.Label(GUILayoutUtility.GetRect(new GUIContent(stackLine), _logStyle), stackLine, _logStyle);
                    }
                }
            }

            GUILayout.EndScrollView();
            
            // Auto-scroll to bottom when new logs arrive
            if (Event.current.type == EventType.Repaint)
            {
                _scrollPosition.y = float.MaxValue;
            }
        }

        GUILayout.EndArea();
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
    /// Pure local operation - no logging to prevent recursion
    /// </summary>
    private void CopyLogsToClipboard()
    {
        if (_logKeys.Count == 0)
        {
            return; // Silently fail - no logging
        }

        try
        {
            string text = ExportText();
            
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: use JS clipboard bridge
            int result = SCP_CopyToClipboard(text);
            if (result == 1)
            {
                _copyMessage = "Copied!";
                _copyMessageEndTime = Time.time + 1.5f;
            }
            else
            {
                _copyMessage = "Copy failed, use Export";
                _copyMessageEndTime = Time.time + 1.5f;
            }
#else
            // Non-WebGL: use system clipboard
            GUIUtility.systemCopyBuffer = text;
            _copyMessage = "Copied!";
            _copyMessageEndTime = Time.time + 1.5f;
#endif
        }
        catch
        {
            // Silently fail - no logging to prevent recursion
            _copyMessage = "Copy failed, use Export";
            _copyMessageEndTime = Time.time + 1.5f;
        }
    }
    
    /// <summary>
    /// Generate aggregated log text for export/copy
    /// No logging - safe for recursion prevention
    /// </summary>
    private string ExportText()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== LogOverlay Export ===");
            sb.AppendLine($"Exported at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total unique logs: {_logKeys.Count}");
            sb.AppendLine();

            foreach (string key in _logKeys)
            {
                if (!_logDict.TryGetValue(key, out var entry))
                    continue;

                string countInfo = entry.Count > 1 ? $" (x{entry.Count})" : "";
                sb.AppendLine($"[{entry.LastTimestamp}] {entry.Type}: {entry.Message}{countInfo}");

                if (entry.StackTraceLines != null)
                {
                    foreach (string line in entry.StackTraceLines)
                    {
                        sb.AppendLine($"  {line}");
                    }
                }
                sb.AppendLine();
                
                // Check length limit
                if (sb.Length > MaxExportLength)
                {
                    sb.AppendLine("...(truncated)");
                    break;
                }
            }

            return sb.ToString();
        }
        catch
        {
            // Silently fail - return minimal text
            return "Export failed";
        }
    }
    
    /// <summary>
    /// Generate aggregated log text for export/copy
    /// </summary>
    private string GenerateAggregatedLogText()
    {
        return ExportText();
    }
    
    /// <summary>
    /// Generate text for export panel
    /// </summary>
    private void GenerateExportText()
    {
        _exportText = GenerateAggregatedLogText();
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

    /// <summary>
    /// Strip non-ASCII characters to diagnose WebGL text rendering issues (tofu blocks).
    /// Replaces characters > 127 with '?' to ensure only ASCII is displayed.
    /// </summary>
    private string StripNonAscii(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            // Keep only ASCII characters (0-127)
            if (c <= 127)
                sb.Append(c);
            else
                sb.Append('?'); // Replace non-ASCII with '?'
        }
        return sb.ToString();
    }
}
