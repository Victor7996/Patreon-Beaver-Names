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
  }

  /// <summary>
  /// ModSettingsOwner providing an in-game UI to view, add, and remove Patreon beaver names.
  /// Supports multi-line editing via <see cref="LongStringModSetting"/>.
  /// </summary>
  public class PatreonNamesSettings : ModSettingsOwner {

    public LongStringModSetting NamesSetting { get; }

    public PatreonNamesSettings(
        DefaultModFileStoredSettings defaultModFileStoredSettings,
        ModSettingsOwnerRegistry modSettingsOwnerRegistry,
        ModRepository modRepository)
        : base(defaultModFileStoredSettings, modSettingsOwnerRegistry, modRepository) {

      string initialText = PatreonSettingsApi.GetNames?.Invoke() ?? string.Empty;

      NamesSetting = new LongStringModSetting(
          initialText,
          ModSettingDescriptor.Create("Patreon Beaver Names")
              .SetTooltip("List of beaver names (one per line). Adding or removing lines updates the active game and patreons.csv.")
      );
    }

    public override int Order => 10;

    public override ModSettingsContext ChangeableOn =>
        ModSettingsContext.MainMenu | ModSettingsContext.Game;

    protected override string ModId => "Victor7996.PatreonBeaverNames";

    protected override void OnAfterLoad() {
      // Synchronize with patreons.csv if available
      string currentCsv = PatreonSettingsApi.GetNames?.Invoke();
      if (!string.IsNullOrEmpty(currentCsv) && NamesSetting.Value != currentCsv) {
        NamesSetting.SetValue(currentCsv);
      }

      NamesSetting.ValueChanged += OnNamesSettingChanged;
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
