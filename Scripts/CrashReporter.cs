using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Manages crash detection reporting and transmits crash details and session log attachments
  /// to the central crash reporting ingest endpoint (https://crash.dindoman.se/api/v1/report).
  /// </summary>
  public static class CrashReporter {

    private const string IngestUrl = "https://crash.dindoman.se/api/v1/report";

    private const int MaxReportsPerSession = 5;
    private static readonly TimeSpan MinIntervalBetweenReports = TimeSpan.FromSeconds(5);

    private static readonly object LockObj = new();
    private static readonly HashSet<string> ReportedSignatures = new();

    /// <summary>
    /// Reusable static HttpClient instance preventing socket exhaustion across requests.
    /// Configures DNS refresh and connection lease timeout on ServicePointManager for Unity/Mono compatibility.
    /// </summary>
    private static readonly HttpClient HttpClient;

    static CrashReporter() {
      // Workaround for Unity / Mono DNS caching issue with static HttpClient:
      // Setting ConnectionLeaseTimeout ensures connection pool recreates TCP sockets periodically,
      // triggering fresh DNS lookups while avoiding socket exhaustion.
      try {
        var uri = new Uri(IngestUrl);
        var sp = System.Net.ServicePointManager.FindServicePoint(uri);
        sp.ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
        System.Net.ServicePointManager.DnsRefreshTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
      } catch {
        // Ignored if platform does not support ServicePointManager
      }

      HttpClient = new HttpClient {
        Timeout = TimeSpan.FromSeconds(15)
      };
    }

    private static int _reportCount;
    private static DateTime _lastReportTime = DateTime.MinValue;

    /// <summary>
    /// Evaluates an exception or error log and dispatches a crash report to the ingest backend if valid.
    /// </summary>
    /// <param name="condition">The exception message or log condition.</param>
    /// <param name="stackTrace">The associated stack trace string, if available.</param>
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

      // Dispatch asynchronously on thread pool to keep main thread unblocked
      Task.Run(async () => {
        try {
          await SendCrashReportAsync(condition, stackTrace, isUnhandled);
        } catch (Exception ex) {
          Debug.LogWarning($"[CrashReporter] Failed to dispatch crash report to ingest endpoint: {ex.Message}");
        }
      });
    }

    /// <summary>
    /// Generates a deduplication key combining condition text and top stack trace line.
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
    /// Serializes crash metadata into clean JSON and posts as multipart/form-data with attached .log file.
    /// </summary>
    private static async Task SendCrashReportAsync(string condition, string stackTrace, bool isUnhandled) {
      string sessionId = ModLogger.SessionId.ToString();
      string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
      string crashType = isUnhandled ? "Unhandled Domain Exception (Fatal)" : "Engine / Script Exception";

      var payload = new {
        SessionId = sessionId,
        Timestamp = timestamp,
        CrashType = crashType,
        Condition = condition ?? string.Empty,
        StackTrace = stackTrace ?? string.Empty
      };

      string payloadJson = JsonSerializer.Serialize(payload);

      string logPath = ModLogger.LogFilePath;
      bool hasLogFile = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);

      if (hasLogFile) {
        try {
          using var form = new MultipartFormDataContent();
          form.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload");

          // Read log file safely with FileShare.ReadWrite to avoid file locking conflicts
          byte[] logBytes;
          await using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            await using var ms = new MemoryStream();
            await fs.CopyToAsync(ms);
            logBytes = ms.ToArray();
          }

          string fileName = Path.GetFileName(logPath);
          var fileContent = new ByteArrayContent(logBytes);
          fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
          form.Add(fileContent, "log_file", fileName);

          HttpResponseMessage response = await HttpClient.PostAsync(IngestUrl, form);
          if (response.IsSuccessStatusCode) {
            return;
          }
        } catch {
          // Fallback to plain JSON POST if multipart file attach fails
        }
      }

      // Fallback: Plain JSON POST without attachment
      using var stringContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
      await HttpClient.PostAsync(IngestUrl, stringContent);
    }

  }

}
