using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Bindito.Core;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Manages safe runtime discovery and dynamic loading of the ModSettings integration.
  /// If the "Mod Settings" mod (eMka.ModSettings) is not installed or enabled,
  /// this bridge does nothing and prevents any missing-assembly crashes.
  /// </summary>
  public static class ModSettingsBridge {

    private static Assembly _settingsAssembly;
    private static bool _apiBound;

    /// <summary>
    /// Attempts to install the PatreonNamesSettings configurator into the current Bindito container.
    /// Safe to call in both MainMenu and Game contexts.
    /// </summary>
    /// <param name="installer">The Install action provided by the caller Configurator.</param>
    public static void TryInstall(Action<IConfigurator> installer) {
      try {
        if (!IsModSettingsLoaded()) {
          ModLogger.LogInfo("Mod Settings mod not detected — skipping settings integration.");
          return;
        }

        if (_settingsAssembly == null) {
          _settingsAssembly = LoadEmbeddedSettingsAssembly();
          if (_settingsAssembly == null) {
            ModLogger.LogWarning("Failed to load embedded ModSettings integration assembly.");
            return;
          }
        }

        if (!_apiBound) {
          BindApiCallbacks(_settingsAssembly);
          _apiBound = true;
        }

        Type configuratorType = _settingsAssembly.GetType(
            "Mods.PatreonBeaverNames.Settings.PatreonNamesSettingsConfigurator");

        if (configuratorType != null) {
          var settingsConfigurator = (IConfigurator)Activator.CreateInstance(configuratorType);
          installer?.Invoke(settingsConfigurator);
          ModLogger.LogInfo("Patreon Beaver Names settings successfully registered with Mod Settings!");
        } else {
          ModLogger.LogWarning("PatreonNamesSettingsConfigurator type not found in embedded assembly.");
        }

      } catch (Exception ex) {
        ModLogger.LogError($"Error initializing ModSettings integration: {ex}");
      }
    }

    private static bool IsModSettingsLoaded() {
      return AppDomain.CurrentDomain.GetAssemblies()
          .Any(a => string.Equals(a.GetName().Name, "ModSettings.Core", StringComparison.OrdinalIgnoreCase));
    }

    private static void BindApiCallbacks(Assembly settingsAssembly) {
      Type apiType = settingsAssembly.GetType("Mods.PatreonBeaverNames.Settings.PatreonSettingsApi");
      if (apiType == null) {
        ModLogger.LogWarning("PatreonSettingsApi type not found in settings assembly.");
        return;
      }

      var getNamesProp = apiType.GetProperty("GetNames", BindingFlags.Public | BindingFlags.Static);
      var setNamesProp = apiType.GetProperty("SetNames", BindingFlags.Public | BindingFlags.Static);

      if (getNamesProp != null) {
        getNamesProp.SetValue(null, new Func<string>(() => {
          if (ApiNameProvider.Current != null) {
            return ApiNameProvider.GetRawNamesText();
          }
          return CsvNameProvider.GetRawNamesText();
        }));
      }
      if (setNamesProp != null) {
        setNamesProp.SetValue(null, new Action<string>(CsvNameProvider.UpdateNamesFromRawText));
      }

      ModLogger.LogInfo("ModSettings bridge API callbacks successfully bound.");
    }

    private static Assembly LoadEmbeddedSettingsAssembly() {
      Assembly mainAsm = typeof(ModSettingsBridge).Assembly;
      string resourceName = mainAsm.GetManifestResourceNames()
          .FirstOrDefault(n => n.EndsWith("Mods.PatreonBeaverNames.Settings.dll", StringComparison.OrdinalIgnoreCase));

      if (resourceName == null) {
        ModLogger.LogError("Embedded resource 'Mods.PatreonBeaverNames.Settings.dll' not found in main assembly.");
        return null;
      }

      using Stream stream = mainAsm.GetManifestResourceStream(resourceName);
      if (stream == null) {
        ModLogger.LogError($"Could not open manifest resource stream for '{resourceName}'.");
        return null;
      }

      byte[] buffer = new byte[stream.Length];
      int read = 0;
      while (read < buffer.Length) {
        int r = stream.Read(buffer, read, buffer.Length - read);
        if (r <= 0) break;
        read += r;
      }

      Assembly loaded = Assembly.Load(buffer);
      ModLogger.LogInfo($"Loaded embedded assembly '{loaded.FullName}'.");
      return loaded;
    }

  }

}
