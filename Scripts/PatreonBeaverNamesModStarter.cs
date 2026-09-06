using Timberborn.ModManagerScene;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Early startup entry point for the mod executed by Timberborn's mod system
  /// when assemblies are loaded during game initialization.
  /// </summary>
  public class PatreonBeaverNamesModStarter : IModStarter {

    /// <summary>
    /// Invoked by the game's <c>ModCodeStarter</c> upon mod loading.
    /// Sets up session file logging, crash hooks, and environment paths.
    /// </summary>
    /// <param name="modEnvironment">The environment metadata supplied by Timberborn.</param>
    public void StartMod(IModEnvironment modEnvironment) {
      string modPath = modEnvironment?.ModPath;
      ModLogger.Initialize(modPath);
      ModLogger.LogInfo($"Patreon Beaver Names initialized at startup. ModPath: {modPath}");
    }

  }

}
