using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
  /// automatic campaign discovery, and dynamic tier filtering.
  /// Uses <see cref="System.Text.Json"/> for robust JSON parsing.
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
    /// Reusable static HttpClient instance preventing socket exhaustion across requests.
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
        string resolvedCampaignId = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : campaignId.Trim();
        bool campaignIsDefault = string.Equals(resolvedCampaignId, DefaultCampaignId, StringComparison.OrdinalIgnoreCase);
        bool campaignIsUrl = resolvedCampaignId.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(AccessToken) && !campaignIsUrl) {
          var discoveredCampaigns = await DiscoverCampaignsAsync(AccessToken).ConfigureAwait(false);
          DiscoveredCampaigns = discoveredCampaigns;

          if (discoveredCampaigns.Count == 1) {
            resolvedCampaignId = discoveredCampaigns[0].Id;
            CampaignId = resolvedCampaignId;
          } else if (discoveredCampaigns.Count > 1) {
            bool matchesSelected = discoveredCampaigns.Any(c => string.Equals(c.Id, campaignId?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!matchesSelected) {
              string multiNotice = "MULTIPLE PATREON CAMPAIGNS DISCOVERED!" + Environment.NewLine +
                  "Please choose which campaign to use by typing its Campaign ID into 'Patreon Campaign ID':" + Environment.NewLine + Environment.NewLine +
                  string.Join(Environment.NewLine + Environment.NewLine, discoveredCampaigns.Select((c, idx) =>
                      $"{idx + 1}. {c.Name} (Campaign ID: {c.Id})" + Environment.NewLine +
                      $"   Link: {c.Url}"));

              DiscoveredTiersSummary = multiNotice;
              TiersDiscovered?.Invoke(multiNotice);
              return;
            }
          } else if (campaignIsDefault || string.IsNullOrWhiteSpace(resolvedCampaignId)) {
            return;
          }
        } else if (campaignIsDefault || string.IsNullOrWhiteSpace(resolvedCampaignId)) {
          return;
        }

        if (string.IsNullOrWhiteSpace(resolvedCampaignId)) {
          ModLogger.LogWarning("Campaign ID is missing in Mod Settings.");
          return;
        }

        string queryUrl = PrepareRequestUrl(resolvedCampaignId);
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

      // If neither a token nor a real campaign ID has been configured yet,
      // skip the fetch entirely to avoid adding a [Patreon Pending] placeholder.
      bool hasToken = !string.IsNullOrWhiteSpace(AccessToken);
      bool hasCampaign = !string.IsNullOrWhiteSpace(CampaignId) &&
                         !string.Equals(CampaignId, DefaultCampaignId, StringComparison.OrdinalIgnoreCase);

      if (!hasToken && !hasCampaign) {
        ModLogger.LogInfo("ApiNameProvider: No Access Token or Campaign ID configured. Skipping Patreon fetch.");
        IsLoaded = true;
        return;
      }

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
        // Automatically construct the Patreon API v2 URL, auto-discovering Campaign ID if necessary.
        // Campaign auto-discovery runs whenever an AccessToken is present, regardless of the campaignId value.
        // This allows users who have only configured their token to automatically resolve their campaign.
        string resolvedCampaignId = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : campaignId.Trim();

        bool campaignIsDefault = string.Equals(resolvedCampaignId, DefaultCampaignId, StringComparison.OrdinalIgnoreCase);
        bool campaignIsUrl = resolvedCampaignId.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(AccessToken) && !campaignIsUrl) {
          // Run campaign discovery when a token is present and no explicit URL was provided.
          // This resolves "default" or missing campaign IDs automatically via /api/oauth2/v2/campaigns.
          var discoveredCampaigns = await DiscoverCampaignsAsync(AccessToken).ConfigureAwait(false);
          DiscoveredCampaigns = discoveredCampaigns;

          if (discoveredCampaigns.Count == 1) {
            resolvedCampaignId = discoveredCampaigns[0].Id;
            CampaignId = resolvedCampaignId;
            ModLogger.LogInfo($"Single Patreon campaign detected (ID: {resolvedCampaignId}, Name: '{discoveredCampaigns[0].Name}'). Auto-selected.");
          } else if (discoveredCampaigns.Count > 1) {
            bool matchesSelected = discoveredCampaigns.Any(c => string.Equals(c.Id, campaignId?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!matchesSelected) {
              string multiNotice = "MULTIPLE PATREON CAMPAIGNS DISCOVERED!" + Environment.NewLine +
                  "Please choose which campaign to use by typing its Campaign ID into 'Patreon Campaign ID':" + Environment.NewLine + Environment.NewLine +
                  string.Join(Environment.NewLine + Environment.NewLine, discoveredCampaigns.Select((c, idx) =>
                      $"{idx + 1}. {c.Name} (Campaign ID: {c.Id})" + Environment.NewLine +
                      $"   Link: {c.Url}"));

              DiscoveredTiersSummary = multiNotice;
              TiersDiscovered?.Invoke(multiNotice);

              ModLogger.LogWarning(
                  $"Multiple Patreon campaigns found ({discoveredCampaigns.Count}). User selection required in Mod Settings:\n" +
                  string.Join("\n", discoveredCampaigns.Select(c => $" - {c.Name} (ID: {c.Id}): {c.Url}")));

              MarkLoadedWithFallback();
              return;
            }
          } else if (campaignIsDefault || string.IsNullOrWhiteSpace(resolvedCampaignId)) {
            // No campaigns discovered and no real ID was configured — nothing to fetch.
            ModLogger.LogWarning("Campaign auto-discovery returned no results. Please verify your Creator's Access Token in Mod Settings.");
            MarkLoadedWithFallback();
            return;
          }
        } else if (campaignIsDefault || string.IsNullOrWhiteSpace(resolvedCampaignId)) {
          // No token and no real campaign ID — silently abort to avoid [Patreon Pending].
          ModLogger.LogInfo("Patreon fetch skipped: no Access Token or Campaign ID configured.");
          MarkLoadedWithFallback();
          return;
        }

        string queryUrl = PrepareRequestUrl(resolvedCampaignId);

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
                $"Please verify that Campaign ID '{resolvedCampaignId}' is correct (Patreon Campaign IDs are siffer-ID:n like '1234567', not tokens). Using fallback name.");
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
    /// Represents a discovered Patreon campaign owned by the creator.
    /// </summary>
    public class DiscoveredCampaign {
      public string Id;
      public string Name;
      public string Url;
    }

    /// <summary>
    /// List of all campaigns discovered for the current Access Token.
    /// </summary>
    public static List<DiscoveredCampaign> DiscoveredCampaigns { get; private set; } = new List<DiscoveredCampaign>();

    /// <summary>
    /// Fetches all campaigns owned by the user via /api/oauth2/v2/campaigns using System.Text.Json.
    /// </summary>
    public static async Task<List<DiscoveredCampaign>> DiscoverCampaignsAsync(string token) {
      var campaigns = new List<DiscoveredCampaign>();
      if (string.IsNullOrWhiteSpace(token)) return campaigns;

      try {
        string url = "https://www.patreon.com/api/oauth2/v2/campaigns?fields[campaign]=name,creation_name,url,vanity";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd("PatreonBeaverNames-TimberbornMod/1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return campaigns;

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return campaigns;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array) {
          foreach (var item in dataEl.EnumerateArray()) {
            if (item.TryGetProperty("id", out var idEl)) {
              string id = idEl.GetString();
              if (string.IsNullOrEmpty(id)) continue;

              string name = null;
              string campaignUrl = null;

              if (item.TryGetProperty("attributes", out var attrEl)) {
                if (attrEl.TryGetProperty("name", out var nEl)) name = nEl.GetString()?.Trim();
                if (string.IsNullOrEmpty(name) && attrEl.TryGetProperty("creation_name", out var cnEl)) name = cnEl.GetString()?.Trim();
                if (attrEl.TryGetProperty("url", out var uEl)) campaignUrl = uEl.GetString()?.Trim();
                if (string.IsNullOrEmpty(campaignUrl) && attrEl.TryGetProperty("vanity", out var vEl) && !string.IsNullOrEmpty(vEl.GetString())) {
                  campaignUrl = $"https://www.patreon.com/{vEl.GetString()}";
                }
              }

              if (string.IsNullOrEmpty(name)) name = "Campaign " + id;
              if (string.IsNullOrEmpty(campaignUrl)) campaignUrl = $"https://www.patreon.com/campaigns/{id}";

              campaigns.Add(new DiscoveredCampaign {
                Id = id,
                Name = name,
                Url = campaignUrl
              });
            }
          }
        }
      } catch (Exception ex) {
        ModLogger.LogWarning($"Failed to discover campaigns via System.Text.Json: {ex.Message}");
      }

      return campaigns;
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

      // Official Patreon API v2 endpoint format per requirement
      return $"https://www.patreon.com/api/oauth2/v2/campaigns/{trimmed}/members?include=currently_entitled_tiers&fields[member]=full_name,patron_status,currently_entitled_amount_cents&fields[tier]=title,amount_cents";
    }

    /// <summary>
    /// Parses a Patreon API v2 JSON:API response conforming to openapi.json using System.Text.Json.
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
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tierIdToTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tierIdToAmount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var allDiscoveredTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Parse 'included' array for campaign tier definitions
        if (root.TryGetProperty("included", out var includedElement) && includedElement.ValueKind == JsonValueKind.Array) {
          foreach (var item in includedElement.EnumerateArray()) {
            if (item.TryGetProperty("type", out var typeEl) &&
                string.Equals(typeEl.GetString(), "tier", StringComparison.OrdinalIgnoreCase) &&
                item.TryGetProperty("id", out var idEl)) {

              string tierId = idEl.GetString();
              if (item.TryGetProperty("attributes", out var attrEl)) {
                string title = attrEl.TryGetProperty("title", out var titleEl) ? titleEl.GetString()?.Trim() : null;
                int amount = attrEl.TryGetProperty("amount_cents", out var amtEl) && amtEl.TryGetInt32(out int cents) ? cents : 0;

                if (!string.IsNullOrEmpty(tierId) && !string.IsNullOrEmpty(title)) {
                  tierIdToTitle[tierId] = title;
                  tierIdToAmount[tierId] = amount;
                  allDiscoveredTiers[title] = amount;
                }
              }
            }
          }
        }

        // Parse 'data' array for campaign member records
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array) {
          foreach (var member in dataElement.EnumerateArray()) {
            if (!member.TryGetProperty("attributes", out var attrEl)) continue;

            string fullName = attrEl.TryGetProperty("full_name", out var fnEl) ? fnEl.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(fullName)) continue;

            string patronStatus = attrEl.TryGetProperty("patron_status", out var psEl) ? psEl.GetString() : null;
            if (!string.IsNullOrEmpty(patronStatus) &&
                !string.Equals(patronStatus, "active_patron", StringComparison.OrdinalIgnoreCase)) {
              continue;
            }

            string resolvedTier = string.Empty;
            int memberAmount = attrEl.TryGetProperty("currently_entitled_amount_cents", out var amtEl) && amtEl.TryGetInt32(out int cents) ? cents : 0;

            if (attrEl.TryGetProperty("tier_title", out var ttEl) && !string.IsNullOrEmpty(ttEl.GetString())) {
              resolvedTier = ttEl.GetString().Trim();
            } else if (member.TryGetProperty("relationships", out var relEl) &&
                       relEl.TryGetProperty("currently_entitled_tiers", out var cetEl) &&
                       cetEl.TryGetProperty("data", out var cetDataEl) &&
                       cetDataEl.ValueKind == JsonValueKind.Array) {
              foreach (var tierRef in cetDataEl.EnumerateArray()) {
                if (tierRef.TryGetProperty("id", out var trIdEl)) {
                  string trId = trIdEl.GetString();
                  if (!string.IsNullOrEmpty(trId) && tierIdToTitle.TryGetValue(trId, out string title)) {
                    resolvedTier = title;
                    if (tierIdToAmount.TryGetValue(trId, out int a) && a > 0) {
                      memberAmount = a;
                    }
                    break;
                  }
                }
              }
            }

            if (string.IsNullOrEmpty(resolvedTier) && memberAmount > 0) {
              if (memberAmount >= 2500) resolvedTier = "Gold";
              else if (memberAmount >= 1000) resolvedTier = "Silver";
              else if (memberAmount >= 500) resolvedTier = "Bronze";
            }

            if (!string.IsNullOrEmpty(resolvedTier) && !allDiscoveredTiers.ContainsKey(resolvedTier)) {
              allDiscoveredTiers[resolvedTier] = memberAmount;
            }

            // Apply active tier filter criteria
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
              } else {
                if (!includeCustomTiers) continue;
              }
            } else {
              if (!includeCustomTiers) continue;
            }

            result.Add(fullName);
          }
        }

        // Format discovered tiers summary
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

      } catch (Exception ex) {
        ModLogger.LogError($"System.Text.Json parsing failed: {ex.Message}");
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

  }

}
