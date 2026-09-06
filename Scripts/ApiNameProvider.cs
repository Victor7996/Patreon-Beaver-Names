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
    /// Default campaign ID used for local testing or mock API fallback.
    /// </summary>
    public const string DefaultCampaignId = "default";

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
        var sp = ServicePointManager.FindServicePoint(new Uri("https://www.patreon.com"));
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
    /// Current Patreon Campaign ID (or local/mock URL).
    /// </summary>
    public static string CampaignId { get; set; } = DefaultCampaignId;

    /// <summary>
    /// Creator's Access Token for authenticating with Patreon API.
    /// </summary>
    public static string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Backward-compatibility alias for AuthToken.
    /// </summary>
    public static string AuthToken {
      get => AccessToken;
      set => AccessToken = value;
    }

    /// <summary>
    /// Backward-compatibility property for EndpointUrl.
    /// </summary>
    public static string EndpointUrl {
      get => PrepareRequestUrl(CampaignId);
      set => CampaignId = value;
    }

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
    /// Whether to include all custom tiers outside standard Bronze/Silver/Gold tiers.
    /// Enabled by default ("stöd för custom tiers som standard").
    /// </summary>
    public static bool IncludeCustomTiers { get; set; } = true;

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
        string campaignId,
        string token,
        bool bronze,
        bool silver,
        bool gold,
        bool customTiersEnabled,
        string customTiers) {

      CampaignId = string.IsNullOrWhiteSpace(campaignId) ? DefaultCampaignId : campaignId.Trim();
      AccessToken = token ?? string.Empty;
      IncludeBronze = bronze;
      IncludeSilver = silver;
      IncludeGold = gold;
      IncludeCustomTiers = customTiersEnabled;
      CustomTiers = customTiers ?? string.Empty;

      ModLogger.LogInfo(
          $"ApiNameProvider configuration updated: CampaignId='{CampaignId}', AccessToken='{(string.IsNullOrEmpty(AccessToken) ? "None" : "***")}', " +
          $"Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}, IncludeCustomTiers={IncludeCustomTiers}, CustomTiersFilter='{CustomTiers}'");
    }

    /// <summary>
    /// Event fired whenever beaver names are updated from an API fetch.
    /// </summary>
    public static event Action<string> NamesUpdated;

    /// <summary>
    /// Static names cache for Main Menu settings preview when Current instance is not active.
    /// </summary>
    private static readonly object StaticLock = new();
    private static readonly List<string> StaticNames = new();

    /// <summary>
    /// Refetches names from the configured API endpoint.
    /// </summary>
    public static void TriggerFetch() {
      Task.Run(async () => {
        if (Current != null) {
          await Current.FetchNamesAsync(CampaignId);
        } else {
          await FetchNamesStaticAsync(CampaignId);
        }
      });
    }

    /// <summary>
    /// Performs a static API fetch for Main Menu previews.
    /// </summary>
    public static async Task FetchNamesStaticAsync(string campaignId) {
      try {
        if (string.IsNullOrWhiteSpace(campaignId)) {
          ModLogger.LogWarning("Campaign ID is missing in Mod Settings.");
          return;
        }

        string queryUrl = PrepareRequestUrl(campaignId);
        if (queryUrl.Contains("patreon.com") && string.IsNullOrWhiteSpace(AccessToken)) {
          ModLogger.LogWarning("Patreon Creator's Access Token is missing in Mod Settings. Unable to query Patreon API.");
          return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd("PatreonBeaverNames-TimberbornMod/1.0");

        if (!string.IsNullOrWhiteSpace(AccessToken)) {
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Trim());
        }

        HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return;

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return;

        List<string> parsedNames = ParsePatreonNamesFromJson(
            json,
            IncludeBronze,
            IncludeSilver,
            IncludeGold,
            IncludeCustomTiers,
            CustomTiers);

        if (parsedNames.Count > 0) {
          lock (StaticLock) {
            StaticNames.Clear();
            StaticNames.AddRange(parsedNames);
          }
          string text = string.Join(Environment.NewLine, parsedNames);
          NamesUpdated?.Invoke(text);
        }
      } catch {
        // Ignore static preview fetch errors
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

      ModLogger.LogInfo($"ApiNameProvider initialized. Triggering asynchronous fetch for CampaignId '{CampaignId}'...");

      // Kick off asynchronous fetch on the thread pool so Unity world loading continues smoothly
      Task.Run(async () => {
        await FetchNamesAsync(CampaignId);
      });
    }

    // -------------------------------------------------------------------------
    // Network & Parsing

    /// <summary>
    /// Performs the HTTP GET request conforming to the Patreon OpenAPI specification.
    /// Handles User-Agent, Bearer authentication, and JSON:API deserialization.
    /// </summary>
    /// <param name="campaignId">Patreon Campaign ID or full URL to query.</param>
    public async Task FetchNamesAsync(string campaignId) {
      try {
        if (string.IsNullOrWhiteSpace(campaignId)) {
          ModLogger.LogWarning("Campaign ID is missing. Please configure your Patreon Campaign ID in Mod Settings.");
          MarkLoadedWithFallback();
          return;
        }

        // Automatically construct the Patreon API v2 URL
        string queryUrl = PrepareRequestUrl(campaignId);

        if (queryUrl.Contains("patreon.com") && string.IsNullOrWhiteSpace(AccessToken)) {
          ModLogger.LogWarning("Patreon Creator's Access Token is missing. Please enter your Creator's Access Token in Mod Settings.");
          MarkLoadedWithFallback();
          return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);

        // Required User-Agent per openapi.json components/parameters/userAgent
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd("PatreonBeaverNames-TimberbornMod/1.0");

        // Attach Authorization header if token is provided
        if (!string.IsNullOrWhiteSpace(AccessToken)) {
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Trim());
        }

        HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
          if (response.StatusCode == HttpStatusCode.NotFound) {
            ModLogger.LogWarning(
                $"Patreon API request to {queryUrl} failed with HTTP 404 (Not Found). " +
                $"Please verify that Campaign ID '{campaignId}' is correct (Patreon Campaign IDs are siffer-ID:n like '1234567', not tokens). Using fallback name.");
          } else {
            ModLogger.LogWarning(
                $"Patreon API request to {queryUrl} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Using fallback name.");
          }
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
            IncludeCustomTiers,
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

        lock (StaticLock) {
          StaticNames.Clear();
          StaticNames.AddRange(parsedNames);
        }

        string namesText = string.Join(Environment.NewLine, parsedNames);
        NamesUpdated?.Invoke(namesText);

        ModLogger.LogInfo(
            $"Successfully fetched and filtered {_names.Count} Patreon supporter name(s) conforming to OpenAPI schema " +
            $"(Tiers: Bronze={IncludeBronze}, Silver={IncludeSilver}, Gold={IncludeGold}, IncludeCustomTiers={IncludeCustomTiers}).");

      } catch (TaskCanceledException ex) {
        ModLogger.LogWarning($"Patreon API request timed out: {ex.Message}. Falling back to default name.");
        MarkLoadedWithFallback();
      } catch (HttpRequestException ex) {
        ModLogger.LogWarning($"Patreon API network error connecting for CampaignId '{campaignId}': {ex.Message}. Falling back to default name.");
        MarkLoadedWithFallback();
      } catch (Exception ex) {
        ModLogger.LogError($"Unexpected error while fetching/parsing Patreon API data: {ex}. Falling back to default name.");
        MarkLoadedWithFallback();
      }
    }

    /// <summary>
    /// Constructs the full Patreon API v2 URL for the specified campaign ID,
    /// or returns custom/mock URL if provided directly.
    /// </summary>
    private static string PrepareRequestUrl(string campaignId) {
      if (string.IsNullOrWhiteSpace(campaignId)) {
        campaignId = DefaultCampaignId;
      }

      string trimmed = campaignId.Trim();

      // If user enters a full HTTP/HTTPS URL (e.g. for mock API or local testing), use it directly
      if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
          trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
        if (!trimmed.Contains("?")) {
          return $"{trimmed}?include=currently_entitled_tiers&fields[member]=full_name,patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents";
        }
        return trimmed;
      }

      // If campaign ID is "default", fallback to local mock server URL for testing
      if (string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase)) {
        return "http://localhost:3000/api/oauth2/v2/campaigns/default/members?include=currently_entitled_tiers&fields[member]=full_name,patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents";
      }

      // Official Patreon API v2 endpoint format per requirement
      return $"https://www.patreon.com/api/oauth2/v2/campaigns/{trimmed}/members?include=currently_entitled_tiers&fields[member]=full_name,patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents";
    }

    /// <summary>
    /// Parses a Patreon API v2 JSON:API response conforming to openapi.json.
    /// Dynamically discovers all tiers from the campaign (both standard and custom),
    /// and filters members based on active tier settings.
    /// </summary>
    /// <param name="json">Raw JSON:API string.</param>
    /// <param name="includeBronze">Whether to include Bronze tier ($5).</param>
    /// <param name="includeSilver">Whether to include Silver tier ($10).</param>
    /// <param name="includeGold">Whether to include Gold tier ($25).</param>
    /// <param name="includeCustomTiers">Whether to include custom tiers as standard (default true).</param>
    /// <param name="customTiers">Optional comma-separated custom tier titles to restrict selection.</param>
    /// <returns>List of filtered full names.</returns>
    public static List<string> ParsePatreonNamesFromJson(
        string json,
        bool includeBronze = true,
        bool includeSilver = true,
        bool includeGold = true,
        bool includeCustomTiers = true,
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
              if (memberAmount >= 2500) resolvedTier = "Gold";
              else if (memberAmount >= 1000) resolvedTier = "Silver";
              else if (memberAmount >= 500) resolvedTier = "Bronze";
            }

            // Record discovered tier
            if (!string.IsNullOrEmpty(resolvedTier) && !allDiscoveredTiers.ContainsKey(resolvedTier)) {
              allDiscoveredTiers[resolvedTier] = memberAmount;
            }

            // Filter by tier dynamically
            if (customTierSet != null && customTierSet.Count > 0) {
              // Explicit custom tier filter specified -> check match
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
              } else {
                // ANY dynamic custom tier (e.g. Diamond, Master Architect, VIP Beaver, etc.)
                if (!includeCustomTiers) continue;
              }
            } else {
              if (!includeCustomTiers) continue;
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
              includeCustomTiers,
              customTierSet);
        }

      } catch (Exception ex) {
        ModLogger.LogError($"JsonUtility failed to parse OpenAPI Patreon response: {ex.Message}. Attempting fallback regex parser...");
        result = ParsePatreonNamesWithRegexFallback(
            json,
            includeBronze,
            includeSilver,
            includeGold,
            includeCustomTiers,
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
        bool includeCustomTiers,
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
                discoveredTiers[t] = 0;
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
            if (!string.Equals(tier, "Bronze", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Silver", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tier, "Gold", StringComparison.OrdinalIgnoreCase) && !includeCustomTiers) continue;
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
    /// Works seamlessly in both Main Menu and Game context.
    /// </summary>
    /// <returns>Multiline string of loaded names or fallback placeholder.</returns>
    public static string GetRawNamesText() {
      if (Current != null) {
        lock (Current._lock) {
          if (Current._names.Count > 0 && !(Current._names.Count == 1 && Current._names[0] == FallbackName)) {
            return string.Join(Environment.NewLine, Current._names);
          }
        }
      }

      lock (StaticLock) {
        if (StaticNames.Count > 0) {
          return string.Join(Environment.NewLine, StaticNames);
        }
      }

      // Auto-trigger background fetch so Main Menu preview populates automatically
      TriggerFetch();

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

      lock (StaticLock) {
        StaticNames.Clear();
        StaticNames.AddRange(lines);
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
