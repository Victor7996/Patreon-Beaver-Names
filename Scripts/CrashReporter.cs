using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Manages crash detection reporting and transmits crash details and session logs
  /// to a designated Discord webhook.
  /// </summary>
  public static class CrashReporter {

    private const string WebhookUrl =
        "https://discord.com/api/webhooks/1546136713913442398/tlMNQ5r80o9ywPlHykVOjZuxulXLCer3e2V7cDR7oshIIGUE14h50lrleUoCcY-G6phE";

    private const int MaxReportsPerSession = 5;
    private static readonly TimeSpan MinIntervalBetweenReports = TimeSpan.FromSeconds(5);

    private static readonly object LockObj = new();
    private static readonly HashSet<string> ReportedSignatures = [];
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static int _reportCount;
    private static DateTime _lastReportTime = DateTime.MinValue;

    /// <summary>
    /// Evaluates an exception or error log and sends a crash report to Discord if valid.
    /// </summary>
    /// <param name="condition">The exception message or log condition.</param>
    /// <param name="stackTrace">The associated stack trace, if available.</param>
    /// <param name="isUnhandled">True if triggered by an unhandled AppDomain exception.</param>
    public static void ReportCrash(string condition, string stackTrace, bool isUnhandled) {
      if (string.IsNullOrEmpty(condition)) {
        return;
      }

      string signature = GenerateSignature(condition, stackTrace);

      lock (LockObj) {
        if (_reportCount >= MaxReportsPerSession) {
          return;
        }

        if (ReportedSignatures.Contains(signature)) {
          return;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastReportTime < MinIntervalBetweenReports) {
          return;
        }

        ReportedSignatures.Add(signature);
        _reportCount++;
        _lastReportTime = now;
      }

      // Fire and forget on threadpool so the game thread is not delayed
      Task.Run(async () => {
        try {
          await SendCrashToDiscordAsync(condition, stackTrace, isUnhandled);
        } catch (Exception ex) {
          // Internal fallback to prevent any secondary crash
          Debug.LogWarning($"[CrashReporter] Failed to dispatch crash report to Discord: {ex.Message}");
        }
      });
    }

    /// <summary>
    /// Generates a deduplication key using the condition and the top line of the stack trace.
    /// </summary>
    private static string GenerateSignature(string condition, string stackTrace) {
      string topStack = string.Empty;
      if (!string.IsNullOrEmpty(stackTrace)) {
        using var reader = new StringReader(stackTrace);
        topStack = reader.ReadLine() ?? string.Empty;
      }
      return $"{condition.Trim()}||{topStack.Trim()}";
    }

    /// <summary>
    /// Serializes the crash report and dispatches it via multipart/form-data with the session log.
    /// </summary>
    private static async Task SendCrashToDiscordAsync(string condition, string stackTrace, bool isUnhandled) {
      string sessionId = ModLogger.SessionId.ToString();
      string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
      string crashType = isUnhandled ? "Unhandled Domain Exception (Fatal)" : "Engine / Script Exception";

      string safeCondition = EscapeJson(Truncate(condition, 900));
      string safeStackTrace = EscapeJson(Truncate(stackTrace ?? "No stack trace provided.", 900));

      string threadName = $"Crash - {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Truncate(condition, 30)}]";
      string safeThreadName = EscapeJson(threadName);

      string payloadJson = $$"""
      {
        "username": "Timberborn Crash Reporter",
        "thread_name": "{{safeThreadName}}",
        "embeds": [
          {
            "title": "🚨 Timberborn Mod Crash / Exception Detected",
            "color": 15158332,
            "fields": [
              { "name": "Session UUID", "value": "`{{sessionId}}`", "inline": true },
              { "name": "Time", "value": "{{timestamp}}", "inline": true },
              { "name": "Crash Type", "value": "{{crashType}}", "inline": false },
              { "name": "Condition / Error", "value": "```\n{{safeCondition}}\n```", "inline": false },
              { "name": "Stack Trace", "value": "```csharp\n{{safeStackTrace}}\n```", "inline": false }
            ],
            "footer": {
              "text": "Mod: Victor7996.PatreonBeaverNames v0.1.0"
            }
          }
        ]
      }
      """;

      string logPath = ModLogger.LogFilePath;
      bool hasLogFile = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);

      if (hasLogFile) {
        try {
          using var form = new MultipartFormDataContent();
          form.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");

          // Read the log file with FileShare.ReadWrite to prevent locking conflicts
          byte[] logBytes;
          await using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            await using var ms = new MemoryStream();
            await fs.CopyToAsync(ms);
            logBytes = ms.ToArray();
          }

          string fileName = Path.GetFileName(logPath);
          var fileContent = new ByteArrayContent(logBytes);
          fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
          form.Add(fileContent, "files[0]", fileName);

          HttpResponseMessage response = await HttpClient.PostAsync(WebhookUrl, form);
          if (response.IsSuccessStatusCode) {
            return;
          }
        } catch {
          // If multipart upload fails, fallback to standard JSON post below
        }
      }

      // Fallback: Plain JSON POST without attachment
      using var stringContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
      await HttpClient.PostAsync(WebhookUrl, stringContent);
    }

    /// <summary>
    /// Escapes characters for raw JSON injection.
    /// </summary>
    private static string EscapeJson(string value) {
      if (string.IsNullOrEmpty(value)) return string.Empty;
      return value
          .Replace("\\", "\\\\")
          .Replace("\"", "\\\"")
          .Replace("\r", "")
          .Replace("\n", "\\n");
    }

    /// <summary>
    /// Truncates string to a maximum allowed length.
    /// </summary>
    private static string Truncate(string value, int maxLength) {
      if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
      return value[..maxLength] + "... [truncated]";
    }

  }

}
