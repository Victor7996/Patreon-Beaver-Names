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
  /// Dynamically retrieves and filters a list of beaver names from a Patreon REST API v2 endpoint
  /// strictly adhering to the Patreon OpenAPI 3.1 schema (<c>openapi.json</c>).
  /// </summary>
  /// <remarks>
  /// Implements <see cref="INameProvider"/> to dispense names to beavers and
  /// <see cref="ILoadableSingleton"/> to initialize upon world/save load.
  /// Supports OpenAPI v2 endpoints (<c>/api/oauth2/v2/campaigns/{campaign_id}/members</c>),
  /// JSON:API compound documents with included tiers, Bearer token authentication,
  /// and tier filtering (Bronze, Silver, Gold, or custom tier titles).
  /// </remarks>
  public class ApiNameProvider : INameProvider, ILoadableSingleton {

    /// <summary>
    /// Default OpenAPI v2 endpoint URL matching the schema in openapi.json.
    /// </summary>
    public const string DefaultEndpointUrl = "http://localhost:3000/api/oauth2/v2/campaigns/default/members";

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
        Timeout = TimeSpan.FromSeconds(15)
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
    /// Optional Bearer authentication token for authenticating with Patreon API.
    /// </summary>
    public static string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include Bronze tier supporters ($5).
    /// </summary>
    public static bool IncludeBronze { get; set; } = true;

    /// <summary>
    /// Whether to include Silver tier supporters ($10).
    /// </summary>
    public static bool IncludeSilver { get; set; } = true;

    /// <summary>
    /// Whether to include Gold tier supporters ($25).
    /// </summary>
    public static bool IncludeGold { get; set; } = true;

    /// <summary>
    /// Whether to include Diamond tier supporters ($50).
    /// </summary>
    public static bool IncludeDiamond { get; set; } = true;

    /// <summary>
    /// Whether to include Master Architect tier supporters ($100).
    /// </summary>
    public static bool IncludeMasterArchitect { get; set; } = true;

    /// <summary>
    /// Whether to include any other unlisted custom tiers outside the standard ones.
    /// </summary>
    public static bool IncludeOtherCustomTiers { get; set; } = true;

    /// <summary>
    /// Comma-separated summary of all tiers discovered on the Patreon campaign.
    /// </summary>
    public static string DiscoveredTiersSummary { get; private set; } = "None detected yet";

    /// <summary>
    /// Event fired whenever new tiers are discovered during an API fetch.
    /// </summary>
    public static event Action<string> TiersDiscovered;

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
        bool diamond,
        bool masterArchitect,
        bool otherCustomTiers,
        string customTiers) {

      EndpointUrl = string.IsNullOrWhiteSpace(url) ? DefaultEndpointUrl : url.Trim();
      AuthToken = token ?? string.Empty;
      IncludeBronze = bronze;
      IncludeSilver = silver;
      IncludeGold = gold;
      IncludeDiamond = diamond;
      IncludeMasterArchitect = masterArchitect;
      IncludeOtherCustomTiers = otherCustomTiers;
      CustomTiers = customTiers ?? string.Empty;

      ModLogger.LogInfo(
          $"ApiNameProvider configuration updated: URL='{EndpointUrl}', AuthToken='{(string.IsNullOrEmpty(AuthToken) ? "None" : "***")}', " +
          $"Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}, Diamond={IncludeDiamond}, MasterArchitect={IncludeMasterArchitect}, OtherCustomTiers={IncludeOtherCustomTiers}, CustomTiersFilter='{CustomTiers}'");
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
    /// Performs the HTTP GET request conforming to the Patreon OpenAPI specification.
    /// Handles User-Agent, Bearer authentication, and JSON:API deserialization.
    /// </summary>
    /// <param name="url">The API endpoint to query.</param>
    public async Task FetchNamesAsync(string url) {
      try {
        // Automatically append query parameters for Patreon API v2 if not already present
        string queryUrl = PrepareRequestUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);

        // Required User-Agent per openapi.json components/parameters/userAgent
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd("PatreonBeaverNames-TimberbornMod/1.0");

        // Attach Authorization header if token is provided
        if (!string.IsNullOrWhiteSpace(AuthToken)) {
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken.Trim());
        }

        HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
          ModLogger.LogWarning(
              $"Patreon API request to {queryUrl} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) {
          ModLogger.LogWarning($"Patreon API returned empty response from {queryUrl}. Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        List<string> parsedNames = ParsePatreonNamesFromJson(
            json,
            IncludeBronze,
            IncludeSilver,
            IncludeGold,
            IncludeDiamond,
            IncludeMasterArchitect,
            IncludeOtherCustomTiers,
            CustomTiers);

        if (parsedNames.Count == 0) {
          ModLogger.LogWarning(
              $"No supporter names matched the active tier filter criteria from API response. Using fallback name.");
          MarkLoadedWithFallback();
          return;
        }

        lock (_lock) {
          _names.Clear();
          _names.AddRange(parsedNames);
          IsLoaded = true;
        }

        ModLogger.LogInfo(
            $"Successfully fetched and filtered {_names.Count} Patreon supporter name(s) conforming to OpenAPI schema " +
            $"(Tiers: Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}, Diamond={IncludeDiamond}, MasterArchitect={IncludeMasterArchitect}, OtherCustomTiers={IncludeOtherCustomTiers}).");

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
    /// Prepares request URL by ensuring standard OpenAPI v2 member query parameters are included.
    /// </summary>
    private static string PrepareRequestUrl(string url) {
      if (string.IsNullOrWhiteSpace(url)) {
        return DefaultEndpointUrl;
      }

      // If querying official campaigns endpoint and query string is omitted, append standard includes
      if (url.Contains("/campaigns/") && !url.Contains("?")) {
        return $"{url}?include=currently_entitled_tiers&fields[member]=full_name,patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents";
      }

      return url;
    }

    /// <summary>
    /// Parses a Patreon API v2 JSON:API response conforming to openapi.json.
    /// Dynamically discovers all tiers from the campaign (both standard and custom),
    /// and filters members based on active tier settings.
    /// </summary>
    public static List<string> ParsePatreonNamesFromJson(
        string json,
        bool includeBronze = true,
        bool includeSilver = true,
        bool includeGold = true,
        bool includeDiamond = true,
        bool includeMasterArchitect = true,
        bool includeOtherCustomTiers = true,
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

        if (response?.data != null && response.data.Length > 0) {
          // Build lookup map of tier ID -> tier title and amount from the "included" array (Patreon API v2 compound document)
          var tierIdToTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
          var tierIdToAmount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
          var allDiscoveredTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

          if (response.included != null) {
            foreach (PatreonIncluded inc in response.included) {
              if (inc != null && string.Equals(inc.type, "tier", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(inc.id)) {
                if (inc.attributes != null) {
                  string title = inc.attributes.title?.Trim();
                  if (!string.IsNullOrEmpty(title)) {
                    tierIdToTitle[inc.id] = title;
                    tierIdToAmount[inc.id] = inc.attributes.amount_cents;
                    allDiscoveredTiers[title] = inc.attributes.amount_cents;
                  }
                }
              }
            }
          }

          foreach (PatreonMember member in response.data) {
            if (member == null) continue;

            string fullName = member.attributes?.full_name?.Trim();
            if (string.IsNullOrEmpty(fullName)) {
              continue;
            }

            // Per openapi.json member schema, patron_status can be: "active_patron", "declined_patron", "former_patron"
            string patronStatus = member.attributes?.patron_status;
            if (!string.IsNullOrEmpty(patronStatus) &&
                !string.Equals(patronStatus, "active_patron", StringComparison.OrdinalIgnoreCase)) {
              // Ignore non-active patrons (declined or former)
              continue;
            }

            // Resolve member's tier title and contribution amount
            string resolvedTier = string.Empty;
            int memberAmount = member.attributes?.currently_entitled_amount_cents ?? 0;

            // 1. Direct tier_title in attributes (convenience or mock field)
            if (!string.IsNullOrEmpty(member.attributes?.tier_title)) {
              resolvedTier = member.attributes.tier_title.Trim();
            }
            // 2. Correlate through relationships.currently_entitled_tiers -> included tier
            else if (member.relationships?.currently_entitled_tiers?.data != null) {
              foreach (PatreonResourceIdentifier tierRef in member.relationships.currently_entitled_tiers.data) {
                if (tierRef != null && !string.IsNullOrEmpty(tierRef.id) && tierIdToTitle.TryGetValue(tierRef.id, out string title)) {
                  resolvedTier = title;
                  if (tierIdToAmount.TryGetValue(tierRef.id, out int amt) && amt > 0) {
                    memberAmount = amt;
                  }
                  break;
                }
              }
            }
            // 3. Fallback: match by currently_entitled_amount_cents
            if (string.IsNullOrEmpty(resolvedTier) && memberAmount > 0) {
              if (memberAmount >= 10000) resolvedTier = "Master Architect";
              else if (memberAmount >= 5000) resolvedTier = "Diamond";
              else if (memberAmount >= 2500) resolvedTier = "Gold";
              else if (memberAmount >= 1000) resolvedTier = "Silver";
              else if (memberAmount >= 500) resolvedTier = "Bronze";
            }

            // Record discovered tier
            if (!string.IsNullOrEmpty(resolvedTier) && !allDiscoveredTiers.ContainsKey(resolvedTier)) {
              allDiscoveredTiers[resolvedTier] = memberAmount;
            }

            // Filter by tier
            if (customTierSet != null && customTierSet.Count > 0) {
              if (string.IsNullOrEmpty(resolvedTier) || !customTierSet.Contains(resolvedTier.ToLowerInvariant())) {
                continue;
              }
            } else if (!string.IsNullOrEmpty(resolvedTier)) {
              if (string.Equals(resolvedTier, "Bronze", StringComparison.OrdinalIgnoreCase)) {
                if (!includeBronze) continue;
              } else if (string.Equals(resolvedTier, "Silver", StringComparison.OrdinalIgnoreCase)) {
                if (!includeSilver) continue;
              } else if (string.Equals(resolvedTier, "Gold", StringComparison.OrdinalIgnoreCase)) {
                if (!includeGold) continue;
              } else if (string.Equals(resolvedTier, "Diamond", StringComparison.OrdinalIgnoreCase)) {
                if (!includeDiamond) continue;
              } else if (string.Equals(resolvedTier, "Master Architect", StringComparison.OrdinalIgnoreCase)) {
                if (!includeMasterArchitect) continue;
              } else {
                if (!includeOtherCustomTiers) continue;
              }
            } else {
              if (!includeOtherCustomTiers) continue;
            }

            result.Add(fullName);
          }

          // Format discovered tiers summary sorted by contribution amount ascending
          if (allDiscoveredTiers.Count > 0) {
            var formattedTiers = allDiscoveredTiers
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value > 0 ? $"{kvp.Key} (${kvp.Value / 100})" : kvp.Key)
                .ToList();

            string summary = string.Join(", ", formattedTiers);
            DiscoveredTiersSummary = summary;
            TiersDiscovered?.Invoke(summary);
          }
        } else {
          // Robust regex fallback parser if JsonUtility returns empty array
          result = ParsePatreonNamesWithRegexFallback(
              json,
              includeBronze,
              includeSilver,
              includeGold,
              includeDiamond,
              includeMasterArchitect,
              includeOtherCustomTiers,
              customTierSet);
        }

      } catch (Exception ex) {
        ModLogger.LogError($"JsonUtility failed to parse OpenAPI Patreon response: {ex.Message}. Attempting fallback regex parser...");
        result = ParsePatreonNamesWithRegexFallback(
            json,
            includeBronze,
            includeSilver,
            includeGold,
            includeDiamond,
            includeMasterArchitect,
            includeOtherCustomTiers,
            customTierSet);
      }

      return result;
    }

    /// <summary>
    /// Robust regex fallback parser when JsonUtility fails or returns empty payload in non-standard environments.
    /// </summary>
    private static List<string> ParsePatreonNamesWithRegexFallback(
        string json,
        bool includeBronze,
        bool includeSilver,
        bool includeGold,
        bool includeDiamond,
        bool includeMasterArchitect,
        bool includeOtherCustomTiers,
        HashSet<string> customTierSet) {

      var result = new List<string>();
      var discoveredTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

      try {
        // Extract full_name and tier_title / amount_cents via regular expressions
        var memberMatches = System.Text.RegularExpressions.Regex.Matches(
            json,
            @"""full_name""\s*:\s*""([^""]+)""");

        var tierTitleMatches = System.Text.RegularExpressions.Regex.Matches(
            json,
            @"""tier_title""\s*:\s*""([^""]+)""");

        // Extract title and amount_cents blocks dynamically
        var tierBlockMatches = System.Text.RegularExpressions.Regex.Matches(
            json,
            @"""title""\s*:\s*""([^""]+)""[^}]*?""amount_cents""\s*:\s*(\d+)");

        foreach (System.Text.RegularExpressions.Match match in tierBlockMatches) {
          string t = match.Groups[1].Value.Trim();
          if (!string.IsNullOrEmpty(t) && !string.Equals(t, "member", StringComparison.OrdinalIgnoreCase)) {
            if (int.TryParse(match.Groups[2].Value, out int cents)) {
              discoveredTiers[t] = cents;
            }
          }
        }

        // Fallback for simple title matches if block matching didn't catch any
        if (discoveredTiers.Count == 0) {
          var titleMatches = System.Text.RegularExpressions.Regex.Matches(
              json,
              @"""title""\s*:\s*""([^""]+)""");

          foreach (System.Text.RegularExpressions.Match match in titleMatches) {
            string t = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(t) && !string.Equals(t, "member", StringComparison.OrdinalIgnoreCase)) {
              if (!discoveredTiers.ContainsKey(t)) {
                int cents = string.Equals(t, "Bronze", StringComparison.OrdinalIgnoreCase) ? 500 :
                            string.Equals(t, "Silver", StringComparison.OrdinalIgnoreCase) ? 1000 :
                            string.Equals(t, "Gold", StringComparison.OrdinalIgnoreCase) ? 2500 :
                            string.Equals(t, "Diamond", StringComparison.OrdinalIgnoreCase) ? 5000 :
                            string.Equals(t, "Master Architect", StringComparison.OrdinalIgnoreCase) ? 10000 : 0;
                discoveredTiers[t] = cents;
              }
            }
          }
        }

        for (int i = 0; i < memberMatches.Count; i++) {
          string name = memberMatches[i].Groups[1].Value.Trim();
          if (string.IsNullOrEmpty(name)) continue;

          string tier = i < tierTitleMatches.Count ? tierTitleMatches[i].Groups[1].Value.Trim() : string.Empty;

          if (customTierSet != null && customTierSet.Count > 0) {
            if (string.IsNullOrEmpty(tier) || !customTierSet.Contains(tier.ToLowerInvariant())) continue;
          } else if (!string.IsNullOrEmpty(tier)) {
            if (string.Equals(tier, "Bronze", StringComparison.OrdinalIgnoreCase) && !includeBronze) continue;
            if (string.Equals(tier, "Silver", StringComparison.OrdinalIgnoreCase) && !includeSilver) continue;
            if (string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) && !includeGold) continue;
            if (string.Equals(tier, "Diamond", StringComparison.OrdinalIgnoreCase) && !includeDiamond) continue;
            if (string.Equals(tier, "Master Architect", StringComparison.OrdinalIgnoreCase) && !includeMasterArchitect) continue;
            if (!string.Equals(tier, "Bronze", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Silver", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Diamond", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Master Architect", StringComparison.OrdinalIgnoreCase) && !includeOtherCustomTiers) continue;
          }

          result.Add(name);
        }

        if (discoveredTiers.Count > 0) {
          var formattedTiers = discoveredTiers
              .OrderBy(kvp => kvp.Value)
              .ThenBy(kvp => kvp.Key)
              .Select(kvp => kvp.Value > 0 ? $"{kvp.Key} (${kvp.Value / 100})" : kvp.Key)
              .ToList();

          string summary = string.Join(", ", formattedTiers);
          DiscoveredTiersSummary = summary;
          TiersDiscovered?.Invoke(summary);
        }

      } catch (Exception ex) {
        ModLogger.LogError($"Regex fallback parser error: {ex.Message}");
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
    // OpenAPI 3.1 / Patreon API v2 JSON:API Data Transfer Objects for JsonUtility

#pragma warning disable CS0649
    [Serializable]
    public class PatreonApiResponse {
      public PatreonMember[] data;
      public PatreonIncluded[] included;
    }

    [Serializable]
    public class PatreonMember {
      public string id;
      public string type;
      public PatreonMemberAttributes attributes;
      public PatreonMemberRelationships relationships;
    }

    [Serializable]
    public class PatreonMemberAttributes {
      public string full_name;
      public string patron_status;
      public int currently_entitled_amount_cents;
      public string tier_title;
    }

    [Serializable]
    public class PatreonMemberRelationships {
      public PatreonTierRelationship currently_entitled_tiers;
    }

    [Serializable]
    public class PatreonTierRelationship {
      public PatreonResourceIdentifier[] data;
    }

    [Serializable]
    public class PatreonResourceIdentifier {
      public string id;
      public string type;
    }

    [Serializable]
    public class PatreonIncluded {
      public string id;
      public string type;
      public PatreonTierAttributes attributes;
    }

    [Serializable]
    public class PatreonTierAttributes {
      public string title;
      public int amount_cents;
    }
#pragma warning restore CS0649

  }

}
