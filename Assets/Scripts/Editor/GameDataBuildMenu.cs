using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class GameDataBuildMenu
{
    private const string K_PY = "GAME_DATA_PYTHON_EXE";
    private const string K_XLSX = "GAME_DATA_XLSX_PATH";
    private const string K_OUT = "GAME_DATA_JSON_OUT_PATH";
    private const string K_LEVEL = "GAME_DATA_LOG_LEVEL";

    private const string DefaultXlsx = "GameData/Local/game_data.xlsx";
    private const string DefaultOut = "Assets/StreamingAssets/game_data.json";

    [MenuItem("Tools/GameData/Build game_data.json (from XLSX)")]
    private static void Build()
    {
        var repoRoot = GetRepoRoot();
        var python = ResolvePython(repoRoot);

        var xlsx = GetConfiguredPath(K_XLSX, repoRoot, DefaultXlsx);
        var outJson = GetConfiguredPath(K_OUT, repoRoot, DefaultOut);
        var logLevel = EditorPrefs.GetString(K_LEVEL, "INFO");

        if (string.IsNullOrEmpty(python))
        {
            EditorUtility.DisplayDialog("GameData", "Python not found. Set it in Tools/GameData/Settings...", "OK");
            return;
        }

        if (!EnsurePythonDependency(repoRoot, python, "openpyxl"))
        {
            EditorUtility.DisplayDialog(
                "GameData Build Failed",
                "Required Python package 'openpyxl' is missing and could not be installed automatically.\n" +
                "Install it manually with:\npython -m pip install openpyxl",
                "OK"
            );
            return;
        }

        if (!File.Exists(xlsx))
        {
            EditorUtility.DisplayDialog("GameData", $"XLSX not found:\n{xlsx}", "OK");
            return;
        }

        var script = Path.GetFullPath(Path.Combine(repoRoot, "tools/xlsx_to_json.py"));
        if (!File.Exists(script))
        {
            EditorUtility.DisplayDialog("GameData", $"Script not found:\n{script}", "OK");
            return;
        }

        var outDir = Path.GetDirectoryName(outJson);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        var args = $"-u \"{script}\" --xlsx \"{xlsx}\" --out \"{outJson}\" --log-level {logLevel}";
        RunProcess(repoRoot, python, args, out var stdout, out var stderr, out var exitCode);

        // IMPORTANT: stderr is not always "error" (Python logging may write INFO to stderr)
        LogOutput(stdout, false); // stdout
        LogOutput(stderr, true);  // stderr

        if (exitCode != 0)
        {
            EditorUtility.DisplayDialog("GameData Build Failed", $"ExitCode={exitCode}\nSee Console for details.", "OK");
            return;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[GameData] Generated: {outJson}");
    }

    [MenuItem("Tools/GameData/Settings...")]
    private static void Settings()
    {
        GameDataSettingsWindow.ShowWindow();
    }

    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string GetConfiguredPath(string key, string repoRoot, string defaultRelativePath)
    {
        var defaultPath = Path.Combine(repoRoot, defaultRelativePath);
        var configured = EditorPrefs.GetString(key, defaultPath);
        return Path.GetFullPath(configured);
    }

    private static string ResolvePython(string repoRoot)
    {
        var fromPref = EditorPrefs.GetString(K_PY, string.Empty);
        if (!string.IsNullOrEmpty(fromPref))
        {
            return fromPref;
        }

        if (CanRun(repoRoot, "python3", "--version")) return "python3";
        if (CanRun(repoRoot, "python", "--version")) return "python";
        return string.Empty;
    }

    private static bool CanRun(string wd, string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = wd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(1500);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunProcess(string wd, string exe, string args, out string stdout, out string stderr, out int exitCode)
    {
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();
        using var outputDone = new System.Threading.ManualResetEventSlim(false);
        using var errorDone = new System.Threading.ManualResetEventSlim(false);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = wd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        using var p = Process.Start(psi);
        if (p == null) throw new Exception("Failed to start process.");
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                outputDone.Set();
                return;
            }
            outputBuilder.AppendLine(e.Data);
            LogLineSmart(e.Data, false);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                errorDone.Set();
                return;
            }
            errorBuilder.AppendLine(e.Data);
            LogLineSmart(e.Data, true);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var lastHeartbeat = DateTime.UtcNow;
        while (!p.WaitForExit(200))
        {
            if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 5)
            {
                Debug.Log("[GameData] running...");
                lastHeartbeat = DateTime.UtcNow;
            }
        }
        outputDone.Wait();
        errorDone.Wait();
        stdout = outputBuilder.ToString();
        stderr = errorBuilder.ToString();
        exitCode = p.ExitCode;
    }

    private static bool EnsurePythonDependency(string repoRoot, string pythonExe, string packageName)
    {
        RunProcess(repoRoot, pythonExe, $"-u -m pip show {packageName}", out var showStdout, out var showStderr, out var showExitCode);
        LogOutput(showStdout, false);
        LogOutput(showStderr, false);
        if (showExitCode == 0) return true;

        Debug.Log($"[GameData] Installing missing Python dependency: {packageName}");
        RunProcess(
            repoRoot,
            pythonExe,
            $"-u -m pip install {packageName}",
            out var installStdout,
            out var installStderr,
            out var installExitCode
        );
        // IMPORTANT: 2nd arg must mean "isStderr", NOT "isError"
        LogOutput(installStdout, false); // stdout
        LogOutput(installStderr, true);  // stderr
        if (installExitCode != 0)
            Debug.LogError($"[GameData] pip install failed: {packageName} (exitCode={installExitCode})");
        return installExitCode == 0;
    }

    private static void LogOutput(string text, bool isStderr)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            LogLineSmart(line, isStderr);
        }
    }

    private static void LogLineSmart(string line, bool isStderr)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("[ERROR]", StringComparison.Ordinal))
        {
            Debug.LogError(line);
        }
        else if (trimmed.StartsWith("[WARN]", StringComparison.Ordinal))
        {
            Debug.LogWarning(line);
        }
        else if (trimmed.StartsWith("[SUCCESS]", StringComparison.Ordinal)
            || trimmed.StartsWith("[INFO]", StringComparison.Ordinal))
        {
            Debug.Log(line);
        }
        else
        {
            // No prefix: treat stderr as warning, stdout as normal log
            if (isStderr) Debug.LogWarning(line);
            else Debug.Log(line);
        }
    }

    private sealed class GameDataSettingsWindow : EditorWindow
    {
        private string pythonExe = string.Empty;
        private string xlsxPath = string.Empty;
        private string outPath = string.Empty;
        private string logLevel = "INFO";

        private static readonly string[] LogLevels = { "DEBUG", "INFO", "WARN", "ERROR" };

        public static void ShowWindow()
        {
            var window = GetWindow<GameDataSettingsWindow>(true, "GameData Settings");
            window.minSize = new Vector2(520f, 220f);
            window.Load();
            window.Show();
        }

        private void Load()
        {
            var repoRoot = GetRepoRoot();
            pythonExe = EditorPrefs.GetString(K_PY, string.Empty);
            xlsxPath = EditorPrefs.GetString(K_XLSX, Path.Combine(repoRoot, DefaultXlsx));
            outPath = EditorPrefs.GetString(K_OUT, Path.Combine(repoRoot, DefaultOut));
            logLevel = EditorPrefs.GetString(K_LEVEL, "INFO");
        }

        private void OnGUI()
        {
            var repoRoot = GetRepoRoot();
            EditorGUILayout.LabelField("Repo Root", repoRoot);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("Leave Python empty to auto-detect python3/python.", MessageType.Info);
            pythonExe = EditorGUILayout.TextField("Python Executable", pythonExe);
            xlsxPath = EditorGUILayout.TextField("XLSX Path", xlsxPath);
            outPath = EditorGUILayout.TextField("JSON Output Path", outPath);

            var currentIndex = Array.IndexOf(LogLevels, logLevel);
            if (currentIndex < 0) currentIndex = 1;
            var selectedIndex = EditorGUILayout.Popup("Log Level", currentIndex, LogLevels);
            logLevel = LogLevels[selectedIndex];

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Defaults"))
                {
                    pythonExe = string.Empty;
                    xlsxPath = Path.Combine(repoRoot, DefaultXlsx);
                    outPath = Path.Combine(repoRoot, DefaultOut);
                    logLevel = "INFO";
                }

                if (GUILayout.Button("Save"))
                {
                    Save(repoRoot);
                    Close();
                }
            }
        }

        private void Save(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(pythonExe)) EditorPrefs.DeleteKey(K_PY);
            else EditorPrefs.SetString(K_PY, pythonExe.Trim());

            var xlsxFull = Path.GetFullPath(string.IsNullOrWhiteSpace(xlsxPath) ? Path.Combine(repoRoot, DefaultXlsx) : xlsxPath);
            var outFull = Path.GetFullPath(string.IsNullOrWhiteSpace(outPath) ? Path.Combine(repoRoot, DefaultOut) : outPath);

            EditorPrefs.SetString(K_XLSX, xlsxFull);
            EditorPrefs.SetString(K_OUT, outFull);
            EditorPrefs.SetString(K_LEVEL, logLevel);
        }
    }
}
