using Bindito.Core;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Bindito DI configurator for the MainMenu context.
  /// Installs the Mod Settings UI integration if the Mod Settings mod is active.
  /// </summary>
  [Context("MainMenu")]
  public class PatreonNamesMainMenuConfigurator : Configurator {

    /// <inheritdoc/>
    protected override void Configure() {
      ModLogger.Initialize();
      ModSettingsBridge.TryInstall(Install);
    }

  }

}
