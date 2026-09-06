using System;
using ModSettings.Common;
using ModSettings.Core;
using Timberborn.Modding;

namespace Mods.PatreonBeaverNames.Settings {

  /// <summary>
  /// Static bridge delegate holder allowing decoupling between the ModSettings integration
  /// assembly and the main mod assembly.
  /// </summary>
  public static class PatreonSettingsApi {
    public static Func<string> GetNames { get; set; }
    public static Action<string> SetNames { get; set; }
    public static Func<string> GetDiscoveredTiers { get; set; }
    public static Action<string> OnTiersDiscovered { get; set; }

    public static Action<string, string, bool, bool, bool, bool, string> UpdateApiConfig { get; set; }
    public static Action TriggerFetch { get; set; }
  }

  /// <summary>
  /// ModSettingsOwner providing an in-game UI to configure the Patreon REST API endpoint,
  /// authentication token, and tier filters, as well as previewing the loaded names.
  /// </summary>
  public class PatreonNamesSettings : ModSettingsOwner {

    private readonly ModSettingsOwnerRegistry _modSettingsOwnerRegistry;

    /// <summary>
    /// REST API endpoint URL to fetch Patreon supporters from.
    /// </summary>
    public ModSetting<string> EndpointUrlSetting { get; }

    /// <summary>
    /// Optional Bearer authentication token for authenticating with the Patreon API.
    /// </summary>
    public ModSetting<string> AuthTokenSetting { get; }

    /// <summary>
    /// All tiers discovered automatically from the Patreon campaign API response.
    /// </summary>
    public ModSetting<string> DiscoveredTiersSetting { get; }

    /// <summary>
    /// Whether to include supporters belonging to the Bronze tier ($5+).
    /// </summary>
    public ModSetting<bool> IncludeBronzeTierSetting { get; }

    /// <summary>
    /// Whether to include supporters belonging to the Silver tier ($10+).
    /// </summary>
    public ModSetting<bool> IncludeSilverTierSetting { get; }

    /// <summary>
    /// Whether to include supporters belonging to the Gold tier ($25+).
    /// </summary>
    public ModSetting<bool> IncludeGoldTierSetting { get; }

    /// <summary>
    /// Whether to include all custom tiers outside the standard Bronze/Silver/Gold tiers (Standard: Yes).
    /// </summary>
    public ModSetting<bool> IncludeCustomTiersSetting { get; }

    /// <summary>
    /// Optional comma-separated custom tier titles to match against.
    /// When set, this restricts to only these specific custom tiers.
    /// </summary>
    public ModSetting<string> CustomTiersSetting { get; }

    /// <summary>
    /// Multi-line text field displaying the active/preview list of loaded supporter names.
    /// </summary>
    public LongStringModSetting NamesSetting { get; }

    public PatreonNamesSettings(
        DefaultModFileStoredSettings defaultModFileStoredSettings,
        ModSettingsOwnerRegistry modSettingsOwnerRegistry,
        ModRepository modRepository)
        : base(defaultModFileStoredSettings, modSettingsOwnerRegistry, modRepository) {

      _modSettingsOwnerRegistry = modSettingsOwnerRegistry;

      EndpointUrlSetting = new ModSetting<string>(
          "http://localhost:3000/api/patreons",
          ModSettingDescriptor.Create("Patreon API Endpoint URL")
              .SetTooltip("The REST API URL serving Patreon supporters in Patreon API v2 schema.")
      );

      AuthTokenSetting = new ModSetting<string>(
          string.Empty,
          ModSettingDescriptor.Create("API Bearer Token (Authentication)")
              .SetTooltip("Optional Bearer token sent in the Authorization header. Use to test secure API endpoints.")
      );

      string initialDiscoveredTiers = PatreonSettingsApi.GetDiscoveredTiers?.Invoke();
      DiscoveredTiersSetting = new ModSetting<string>(
          string.IsNullOrEmpty(initialDiscoveredTiers) ? "Pending API fetch..." : initialDiscoveredTiers,
          ModSettingDescriptor.Create("Discovered Campaign Tiers (Auto-detected)")
              .SetTooltip("Tiers detected automatically from your Patreon campaign API response.")
      );

      IncludeBronzeTierSetting = new ModSetting<bool>(
          true,
          ModSettingDescriptor.Create("Include Bronze Tier ($5)")
              .SetTooltip("Check to include Bronze tier Patreon supporters in the beaver name pool.")
      );

      IncludeSilverTierSetting = new ModSetting<bool>(
          true,
          ModSettingDescriptor.Create("Include Silver Tier ($10)")
              .SetTooltip("Check to include Silver tier Patreon supporters in the beaver name pool.")
      );

      IncludeGoldTierSetting = new ModSetting<bool>(
          true,
          ModSettingDescriptor.Create("Include Gold Tier ($25)")
              .SetTooltip("Check to include Gold tier Patreon supporters in the beaver name pool.")
      );

      IncludeCustomTiersSetting = new ModSetting<bool>(
          true,
          ModSettingDescriptor.Create("Include All Custom Tiers (Standard: Yes)")
              .SetTooltip("Automatically include supporters from any custom tiers outside Bronze/Silver/Gold.")
      );

      CustomTiersSetting = new ModSetting<string>(
          string.Empty,
          ModSettingDescriptor.Create("Custom Tier Filter (Optional)")
              .SetTooltip("Optional comma-separated list of custom tier names (e.g. 'Diamond, Master Architect') to restrict selection.")
      );

      string initialText = PatreonSettingsApi.GetNames?.Invoke() ?? string.Empty;
      NamesSetting = new LongStringModSetting(
          initialText,
          ModSettingDescriptor.Create("Loaded Beaver Names (Preview)")
              .SetTooltip("List of currently active names fetched from the Patreon API. Updates automatically when settings change.")
      );
    }

    public override int Order => 10;

    public override ModSettingsContext ChangeableOn =>
        ModSettingsContext.MainMenu | ModSettingsContext.Game;

    protected override string ModId => "Victor7996.PatreonBeaverNames";

    protected override void OnAfterLoad() {
      // Defensive deduplication to ensure exactly one owner entry exists in the registry
      DeduplicateRegistry();

      // Listen to setting changes and push updates to the API client
      EndpointUrlSetting.ValueChanged += (_, _) => OnConfigChanged();
      AuthTokenSetting.ValueChanged += (_, _) => OnConfigChanged();
      IncludeBronzeTierSetting.ValueChanged += (_, _) => OnConfigChanged();
      IncludeSilverTierSetting.ValueChanged += (_, _) => OnConfigChanged();
      IncludeGoldTierSetting.ValueChanged += (_, _) => OnConfigChanged();
      IncludeCustomTiersSetting.ValueChanged += (_, _) => OnConfigChanged();
      CustomTiersSetting.ValueChanged += (_, _) => OnConfigChanged();

      NamesSetting.ValueChanged += OnNamesSettingChanged;

      // Register listener for live tier discoveries from ApiNameProvider
      PatreonSettingsApi.OnTiersDiscovered = OnTiersDiscoveredCallback;

      // Push initial stored settings to the mod
      PushSettingsToMod();

      // Synchronize names and discovered tiers preview
      SyncPreviews();
    }

    private void OnTiersDiscoveredCallback(string summary) {
      if (!string.IsNullOrEmpty(summary) && DiscoveredTiersSetting.Value != summary) {
        DiscoveredTiersSetting.SetValue(summary);
      }
    }

    private void OnConfigChanged() {
      PushSettingsToMod();
      PatreonSettingsApi.TriggerFetch?.Invoke();
      SyncPreviews();
    }

    private void PushSettingsToMod() {
      PatreonSettingsApi.UpdateApiConfig?.Invoke(
          EndpointUrlSetting.Value,
          AuthTokenSetting.Value,
          IncludeBronzeTierSetting.Value,
          IncludeSilverTierSetting.Value,
          IncludeGoldTierSetting.Value,
          IncludeCustomTiersSetting.Value,
          CustomTiersSetting.Value
      );
    }

    private void SyncPreviews() {
      string currentNames = PatreonSettingsApi.GetNames?.Invoke();
      if (!string.IsNullOrEmpty(currentNames) && NamesSetting.Value != currentNames) {
        NamesSetting.SetValue(currentNames);
      }

      string currentTiers = PatreonSettingsApi.GetDiscoveredTiers?.Invoke();
      if (!string.IsNullOrEmpty(currentTiers) && DiscoveredTiersSetting.Value != currentTiers) {
        DiscoveredTiersSetting.SetValue(currentTiers);
      }
    }

    private void DeduplicateRegistry() {
      try {
        var field = typeof(ModSettingsOwnerRegistry).GetField(
            "_modSettingOwners",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(_modSettingsOwnerRegistry) is System.Collections.IDictionary dict) {
          foreach (System.Collections.DictionaryEntry entry in dict) {
            if (entry.Value is System.Collections.IList list) {
              for (int i = list.Count - 1; i >= 0; i--) {
                var item = list[i];
                if (item != null && item.GetType() == GetType() && !ReferenceEquals(item, this)) {
                  list.RemoveAt(i);
                }
              }
            }
          }
        }
      } catch {
        // Ignored
      }
    }

    private void OnNamesSettingChanged(object sender, string newRaw) {
      PatreonSettingsApi.SetNames?.Invoke(newRaw);
    }

  }

  /// <summary>
  /// Configurator for registering PatreonNamesSettings in Bindito containers.
  /// </summary>
  public class PatreonNamesSettingsConfigurator : Bindito.Core.Configurator {

    protected override void Configure() {
      Bind<PatreonNamesSettings>().AsSingleton();
    }

  }

}
