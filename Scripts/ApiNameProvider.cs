using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Dynamically retrieves a list of beaver names from a mock Patreon REST API endpoint
  /// adhering to the Patreon API v2 response schema.
  /// </summary>
  /// <remarks>
  /// Implements <see cref="INameProvider"/> to dispense names to beavers and
  /// <see cref="ILoadableSingleton"/> to initialize upon world/save load.
  /// Network calls are executed asynchronously off the main thread to avoid freezing Unity.
  /// </remarks>
  public class ApiNameProvider : INameProvider, ILoadableSingleton {

    /// <summary>
    /// Default mock endpoint URL serving Patreon member data.
    /// </summary>
    public const string DefaultEndpointUrl = "http://localhost:3000/api/patreons";

    /// <summary>
    /// Fallback placeholder dispensed when names are still loading or if the network request fails,
    /// guaranteeing the game never crashes or receives null.
    /// </summary>
    public const string FallbackName = "[Patreon Pending]";

    /// <summary>
    /// Reusable static HttpClient to avoid socket exhaustion across requests.
    /// Connection lease timeout is managed via <see cref="ServicePointManager"/> to prevent DNS caching issues.
    /// </summary>
    private static readonly HttpClient HttpClient;

    static ApiNameProvider() {
      // Configure DNS refresh timeout on Unity's ServicePointManager to prevent stale DNS records
      // while reusing the static HttpClient to avoid socket exhaustion.
      try {
        var uri = new Uri(DefaultEndpointUrl);
        var sp = ServicePointManager.FindServicePoint(uri);
        sp.ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
        ServicePointManager.DnsRefreshTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
      } catch {
        // Ignored if URI parsing fails or platform does not support ServicePointManager
      }

      HttpClient = new HttpClient {
        Timeout = TimeSpan.FromSeconds(10)
      };
    }

    /// <summary>
    /// The currently active singleton instance in the Game context.
    /// </summary>
    public static ApiNameProvider Current { get; private set; }

    /// <summary>
    /// Synchronization lock protecting access to the internal names list.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// In-memory storage for names retrieved from the API.
    /// </summary>
    private readonly List<string> _names = new();

    /// <summary>
    /// Indicates whether the network request has concluded (either successfully or with a fallback).
    /// </summary>
    public bool IsLoaded { get; private set; }

    // -------------------------------------------------------------------------
    // INameProvider Implementation

    /// <inheritdoc/>
    public int Count {
      get {
        lock (_lock) {
          return _names.Count;
        }
      }
    }

    /// <inheritdoc/>
    public string GetName(int index) {
      lock (_lock) {
        if (_names.Count == 0 || index < 0 || index >= _names.Count) {
          return FallbackName;
        }
        return _names[index];
      }
    }

    // -------------------------------------------------------------------------
    // ILoadableSingleton Implementation

    /// <summary>
    /// Invoked by Timberborn's singleton system when the game world is loaded.
    /// Seeds the list with a fallback and initiates asynchronous API retrieval
    /// without blocking the Unity main thread.
    /// </summary>
    public void Load() {
      Current = this;

      lock (_lock) {
        _names.Clear();
        _names.Add(FallbackName);
      }

      ModLogger.LogInfo($"ApiNameProvider initialized. Triggering asynchronous fetch from {DefaultEndpointUrl}...");

      // Kick off asynchronous fetch on the thread pool so Unity world loading continues smoothly
      Task.Run(async () => {
        await FetchNamesAsync(DefaultEndpointUrl);
      });
    }

    // -------------------------------------------------------------------------
    // Network & Parsing

    /// <summary>
    /// Performs the HTTP GET request and deserializes the Patreon API v2 payload.
    /// Thread-safe and resilient against connection drops, timeouts, and malformed JSON.
    /// </summary>
    /// <param name="url">The API endpoint to query.</param>
    public async Task FetchNamesAsync(string url) {
      try {
        HttpResponseMessage response = await HttpClient.GetAsync(url).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
          ModLogger.LogWarning(
              $"Patreon API request to {url} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) {
          ModLogger.LogWarning($"Patreon API returned empty response from {url}. Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        List<string> parsedNames = ParsePatreonNamesFromJson(json);
        if (parsedNames.Count == 0) {
          ModLogger.LogWarning($"No valid supporter names parsed from Patreon API response. Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        lock (_lock) {
          _names.Clear();
          _names.AddRange(parsedNames);
          IsLoaded = true;
        }

        ModLogger.LogInfo($"Successfully fetched and loaded {_names.Count} Patreon supporter name(s) from API.");

      } catch (TaskCanceledException ex) {
        ModLogger.LogWarning($"Patreon API request timed out: {ex.Message}. Falling back to default name.");
        MarkLoadedWithFallback();
      } catch (HttpRequestException ex) {
        ModLogger.LogWarning($"Patreon API network error connecting to {url}: {ex.Message}. Falling back to default name.");
        MarkLoadedWithFallback();
      } catch (Exception ex) {
        ModLogger.LogError($"Unexpected error while fetching/parsing Patreon API data: {ex}. Falling back to default name.");
        MarkLoadedWithFallback();
      }
    }

    /// <summary>
    /// Parses Patreon API v2 JSON response using Unity's built-in <see cref="JsonUtility"/>.
    /// Expected structure: <c>{"data": [{"attributes": {"full_name": "Alice"}}]}</c>.
    /// </summary>
    /// <param name="json">Raw JSON string from the API response.</param>
    /// <returns>List of valid, non-empty full names.</returns>
    public static List<string> ParsePatreonNamesFromJson(string json) {
      var result = new List<string>();

      try {
        PatreonApiResponse response = JsonUtility.FromJson<PatreonApiResponse>(json);

        if (response?.data != null) {
          foreach (PatreonData item in response.data) {
            string fullName = item?.attributes?.full_name?.Trim();
            if (!string.IsNullOrEmpty(fullName)) {
              result.Add(fullName);
            }
          }
        }
      } catch (Exception ex) {
        ModLogger.LogError($"JsonUtility failed to parse Patreon API response: {ex.Message}");
      }

      return result;
    }

    /// <summary>
    /// Ensures the list contains at least the fallback placeholder when network calls fail.
    /// </summary>
    private void MarkLoadedWithFallback() {
      lock (_lock) {
        if (_names.Count == 0) {
          _names.Add(FallbackName);
        }
        IsLoaded = true;
      }
    }

    /// <summary>
    /// Provides the raw names joined by newline for UI inspectability (e.g. in Mod Settings).
    /// </summary>
    /// <returns>Multiline string of loaded names or fallback placeholder.</returns>
    public static string GetRawNamesText() {
      if (Current != null) {
        lock (Current._lock) {
          if (Current._names.Count > 0) {
            return string.Join(Environment.NewLine, Current._names);
          }
        }
      }
      return FallbackName;
    }

    // -------------------------------------------------------------------------
    // Patreon API v2 Data Transfer Objects (DTOs) for JsonUtility

#pragma warning disable CS0649
    [Serializable]
    private class PatreonApiResponse {
      public List<PatreonData> data;
    }

    [Serializable]
    private class PatreonData {
      public PatreonAttributes attributes;
    }

    [Serializable]
    private class PatreonAttributes {
      public string full_name;
    }
#pragma warning restore CS0649

  }

}
