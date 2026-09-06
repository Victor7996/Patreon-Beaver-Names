using Bindito.Core;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Bindito DI configurator for the Patreon Beaver Names mod.
  /// Registered in the <c>Game</c> context so all bindings are active during
  /// active gameplay (not in the main menu or map editor).
  /// </summary>
  [Context("Game")]
  public class PatreonNamesConfigurator : Configurator {

    /// <summary>
    /// Wires up the mod's dependency graph in the Game context:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="INameProvider"/> → <see cref="ApiNameProvider"/> (singleton).
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="NameManager"/> (singleton) — owns the sequential index and save persistence.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="SequentialBeaverNamer"/> (singleton) — hooks <c>CharacterCreatedEvent</c>.
    ///   </description></item>
    /// </list>
    /// </summary>
    protected override void Configure() {
      ModLogger.Initialize();
      ModLogger.LogInfo("Configuring Patreon Beaver Names Bindito bindings (Game context).");

      ModSettingsBridge.TryInstall(Install);

      Bind<INameProvider>().To<ApiNameProvider>().AsSingleton();
      Bind<NameManager>().AsSingleton();
      Bind<SequentialBeaverNamer>().AsSingleton();
    }

  }

}
