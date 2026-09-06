using System;
using System.IO;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Central logging service that captures all engine and mod output to a unique session
  /// log file within the mod's logs directory and drives crash detection.
  /// </summary>
  public static class ModLogger {

    private static readonly object FileLock = new();
    private static bool _initialized;
    private static StreamWriter _writer;

    [ThreadStatic]
    private static bool _isLogging;

    /// <summary>
    /// Unique identifier for the current game execution run.
    /// </summary>
    public static Guid SessionId { get; private set; }

    /// <summary>
    /// The timestamp when logging was initiated for this session.
    /// </summary>
    public static DateTime SessionStartTime { get; private set; }

    /// <summary>
    /// Absolute path to the current session log file.
    /// </summary>
    public static string LogFilePath { get; private set; }

    /// <summary>
    /// Absolute path to the mod root directory.
    /// </summary>
    public static string ModPath { get; private set; }

    /// <summary>
    /// Initializes session logging, hooks Unity and AppDomain events, and prepares the log file.
    /// </summary>
    /// <param name="resolvedModPath">Optional explicit path to the mod folder.</param>
    public static void Initialize(string resolvedModPath = null) {
      lock (FileLock) {
        if (_initialized) {
          return;
        }

        SessionId = Guid.NewGuid();
        SessionStartTime = DateTime.UtcNow;

        ModPath = ResolveModPath(resolvedModPath);
        string logsDir = Path.Combine(ModPath, "logs");

        try {
          if (!Directory.Exists(logsDir)) {
            Directory.CreateDirectory(logsDir);
          }

          string fileName = $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{SessionId}.log";
          LogFilePath = Path.Combine(logsDir, fileName);

          var fs = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
          _writer = new StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };

          WriteRawLine("================================================================================");
          WriteRawLine(" Timberborn Patreon Beaver Names Mod — Session Log");
          WriteRawLine($" Session UUID : {SessionId}");
          WriteRawLine($" Start Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local) / {SessionStartTime:yyyy-MM-dd HH:mm:ss} (UTC)");
          WriteRawLine($" Mod Version  : 1.0.0 (Victor7996.PatreonBeaverNames)");
          WriteRawLine($" Unity Version: {Application.unityVersion}");
          WriteRawLine($" Mod Path     : {ModPath}");
          WriteRawLine("================================================================================");
          WriteRawLine("");
        } catch (Exception ex) {
          Debug.LogError($"[ModLogger] Failed to create session log file at {logsDir}: {ex.Message}");
        }

        Application.logMessageReceivedThreaded += OnLogMessageReceived;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _initialized = true;
      }

      LogInfo("ModLogger initialized successfully.");
    }

    /// <summary>
    /// Formats and logs an informational message.
    /// </summary>
    public static void LogInfo(string message) {
      Debug.Log($"[PatreonNames] {message}");
    }

    /// <summary>
    /// Formats and logs a warning message.
    /// </summary>
    public static void LogWarning(string message) {
      Debug.LogWarning($"[PatreonNames] {message}");
    }

    /// <summary>
    /// Formats and logs an error message.
    /// </summary>
    public static void LogError(string message) {
      Debug.LogError($"[PatreonNames] {message}");
    }

    /// <summary>
    /// Formats and logs an exception.
    /// </summary>
    public static void LogException(Exception ex) {
      Debug.LogException(ex);
    }

    /// <summary>
    /// Handles log messages forwarded from Unity's logging system.
    /// </summary>
    private static void OnLogMessageReceived(string condition, string stackTrace, LogType type) {
      if (_isLogging) {
        return;
      }

      _isLogging = true;
      try {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logLine = $"[{timestamp}] [{type}] {condition}";

        lock (FileLock) {
          WriteRawLine(logLine);
          if (!string.IsNullOrEmpty(stackTrace)) {
            WriteRawLine($"    Stack: {stackTrace.TrimEnd().Replace("\n", "\n    ")}");
          }
        }

        if (type == LogType.Exception || type == LogType.Assert) {
          CrashReporter.ReportCrash(condition, stackTrace, isUnhandled: false);
        }
      } finally {
        _isLogging = false;
      }
    }

    /// <summary>
    /// Catches unhandled thread exceptions from the current AppDomain.
    /// </summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
      string exceptionDetails = e.ExceptionObject?.ToString() ?? "Unknown unhandled exception";
      string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

      lock (FileLock) {
        WriteRawLine($"[{timestamp}] [CRITICAL_UNHANDLED_EXCEPTION] IsTerminating: {e.IsTerminating}");
        WriteRawLine(exceptionDetails);
      }

      CrashReporter.ReportCrash(exceptionDetails, null, isUnhandled: true);
    }

    /// <summary>
    /// Writes a line directly to the stream writer with error tolerance.
    /// </summary>
    private static void WriteRawLine(string line) {
      try {
        _writer?.WriteLine(line);
      } catch {
        // Suppress writing errors to prevent cascading log failures
      }
    }

    /// <summary>
    /// Resolves the mod root folder from the environment, assembly location, or default paths.
    /// </summary>
    private static string ResolveModPath(string providedPath) {
      if (!string.IsNullOrWhiteSpace(providedPath) && Directory.Exists(providedPath)) {
        return providedPath;
      }

      try {
        string assemblyLoc = typeof(ModLogger).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLoc)) {
          string dir = Path.GetDirectoryName(assemblyLoc);
          if (dir != null) {
            // Check if assembly is in Scripts/ subfolder
            if (dir.EndsWith("Scripts", StringComparison.OrdinalIgnoreCase)) {
              string parent = Path.GetDirectoryName(dir);
              if (parent != null && Directory.Exists(parent)) {
                return parent;
              }
            }
            return dir;
          }
        }
      } catch {
        // Fallback to Documents below
      }

      string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
      return Path.Combine(docs, "Timberborn", "Mods", "Patreon-Beaver-Names");
    }

  }

}
