using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Dynamically retrieves and filters a list of beaver names from a Patreon REST API endpoint
  /// adhering to the Patreon API v2 response schema.
  /// </summary>
  /// <remarks>
  /// Implements <see cref="INameProvider"/> to dispense names to beavers and
  /// <see cref="ILoadableSingleton"/> to initialize upon world/save load.
  /// Supports authentication tokens (Bearer), tier filtering (Bronze, Silver, Gold, or custom),
  /// and executes network requests asynchronously to avoid freezing the Unity main thread.
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

    // -------------------------------------------------------------------------
    // Configuration Properties (Controlled via Mod Settings)

    /// <summary>
    /// Current endpoint URL.
    /// </summary>
    public static string EndpointUrl { get; set; } = DefaultEndpointUrl;

    /// <summary>
    /// Optional Bearer authentication token for testing secured endpoints.
    /// </summary>
    public static string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include Bronze tier supporters.
    /// </summary>
    public static bool IncludeBronze { get; set; } = true;

    /// <summary>
    /// Whether to include Silver tier supporters.
    /// </summary>
    public static bool IncludeSilver { get; set; } = true;

    /// <summary>
    /// Whether to include Gold tier supporters.
    /// </summary>
    public static bool IncludeGold { get; set; } = true;

    /// <summary>
    /// Optional custom comma-separated list of tier titles. Overrides boolean tier toggles if set.
    /// </summary>
    public static string CustomTiers { get; set; } = string.Empty;

    /// <summary>
    /// Updates configuration from Mod Settings and triggers an asynchronous refresh.
    /// </summary>
    public static void Configure(
        string url,
        string token,
        bool bronze,
        bool silver,
        bool gold,
        string customTiers) {

      EndpointUrl = string.IsNullOrWhiteSpace(url) ? DefaultEndpointUrl : url.Trim();
      AuthToken = token ?? string.Empty;
      IncludeBronze = bronze;
      IncludeSilver = silver;
      IncludeGold = gold;
      CustomTiers = customTiers ?? string.Empty;

      ModLogger.LogInfo(
          $"ApiNameProvider configuration updated: URL='{EndpointUrl}', AuthToken='{(string.IsNullOrEmpty(AuthToken) ? "None" : "***")}', " +
          $"Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}, CustomTiers='{CustomTiers}'");
    }

    /// <summary>
    /// Refetches names from the configured API endpoint if an instance is active.
    /// </summary>
    public static void TriggerFetch() {
      if (Current != null) {
        Task.Run(async () => {
          await Current.FetchNamesAsync(EndpointUrl);
        });
      }
    }

    // -------------------------------------------------------------------------
    // State Fields

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

      ModLogger.LogInfo($"ApiNameProvider initialized. Triggering asynchronous fetch from {EndpointUrl}...");

      // Kick off asynchronous fetch on the thread pool so Unity world loading continues smoothly
      Task.Run(async () => {
        await FetchNamesAsync(EndpointUrl);
      });
    }

    // -------------------------------------------------------------------------
    // Network & Parsing

    /// <summary>
    /// Performs the HTTP GET request with optional Bearer authentication and deserializes
    /// the Patreon API v2 payload, filtering results according to tier settings.
    /// </summary>
    /// <param name="url">The API endpoint to query.</param>
    public async Task FetchNamesAsync(string url) {
      try {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Attach Authorization header if token is provided
        if (!string.IsNullOrWhiteSpace(AuthToken)) {
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken.Trim());
        }

        HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);

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

        List<string> parsedNames = ParsePatreonNamesFromJson(
            json,
            IncludeBronze,
            IncludeSilver,
            IncludeGold,
            CustomTiers);

        if (parsedNames.Count == 0) {
          ModLogger.LogWarning(
              $"No supporter names matched the active tier filter criteria. Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        lock (_lock) {
          _names.Clear();
          _names.AddRange(parsedNames);
          IsLoaded = true;
        }

        ModLogger.LogInfo(
            $"Successfully fetched and filtered {_names.Count} Patreon supporter name(s) from API " +
            $"(Tiers: Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}).");

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
    /// Parses Patreon API v2 JSON response using Unity's built-in <see cref="JsonUtility"/>
    /// and filters supporters based on tier selections.
    /// </summary>
    /// <param name="json">Raw JSON string from the API response.</param>
    /// <param name="includeBronze">Whether to include Bronze tier supporters.</param>
    /// <param name="includeSilver">Whether to include Silver tier supporters.</param>
    /// <param name="includeGold">Whether to include Gold tier supporters.</param>
    /// <param name="customTiers">Optional comma-separated custom tier titles.</param>
    /// <returns>List of valid, non-empty, filtered full names.</returns>
    public static List<string> ParsePatreonNamesFromJson(
        string json,
        bool includeBronze = true,
        bool includeSilver = true,
        bool includeGold = true,
        string customTiers = "") {

      var result = new List<string>();

      HashSet<string> customTierSet = null;
      if (!string.IsNullOrWhiteSpace(customTiers)) {
        customTierSet = new HashSet<string>(
            customTiers.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim().ToLowerInvariant()));
      }

      try {
        PatreonApiResponse response = JsonUtility.FromJson<PatreonApiResponse>(json);

        if (response?.data != null) {
          foreach (PatreonData item in response.data) {
            string fullName = item?.attributes?.full_name?.Trim();
            if (string.IsNullOrEmpty(fullName)) {
              continue;
            }

            string tier = item?.attributes?.tier_title?.Trim() ?? string.Empty;

            // Tier filtering
            if (customTierSet != null && customTierSet.Count > 0) {
              if (string.IsNullOrEmpty(tier) || !customTierSet.Contains(tier.ToLowerInvariant())) {
                continue;
              }
            } else if (!string.IsNullOrEmpty(tier)) {
              if (string.Equals(tier, "Bronze", StringComparison.OrdinalIgnoreCase) && !includeBronze) {
                continue;
              }
              if (string.Equals(tier, "Silver", StringComparison.OrdinalIgnoreCase) && !includeSilver) {
                continue;
              }
              if (string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) && !includeGold) {
                continue;
              }
            }

            result.Add(fullName);
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

    /// <summary>
    /// Updates the active names list from a raw multiline string.
    /// </summary>
    public static void UpdateNamesFromRawText(string rawText) {
      var lines = (rawText ?? string.Empty)
          .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
          .Select(l => l.Trim())
          .Where(l => !string.IsNullOrEmpty(l))
          .ToList();

      if (lines.Count == 0) {
        lines.Add(FallbackName);
      }

      if (Current != null) {
        lock (Current._lock) {
          Current._names.Clear();
          Current._names.AddRange(lines);
        }
      }
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
      public string tier_title;
    }
#pragma warning restore CS0649

  }

}
