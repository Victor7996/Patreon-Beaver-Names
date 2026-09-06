using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Singleton that owns the current position in the Patreon name list and
  /// persists it inside the game's save file via <see cref="ISaveableSingleton"/>.
  /// Inject this class wherever the next name is needed.
  /// </summary>
  public class NameManager : ISaveableSingleton, ILoadableSingleton {

    // -------------------------------------------------------------------------
    // Persistence keys — must be unique across all mods.

    private static readonly SingletonKey SaveKey =
        new SingletonKey("Victor7996.PatreonBeaverNames.NameManager");

    private static readonly PropertyKey<int> CurrentIndexKey =
        new PropertyKey<int>("CurrentIndex");

    // -------------------------------------------------------------------------

    private readonly INameProvider _nameProvider;
    private readonly ISingletonLoader _singletonLoader;

    private readonly object _lock = new object();
    private int _currentIndex;

    /// <summary>
    /// Initialises the manager with injected name provider and save loader.
    /// </summary>
    public NameManager(INameProvider nameProvider, ISingletonLoader singletonLoader) {
      _nameProvider = nameProvider;
      _singletonLoader = singletonLoader;
    }

    // -------------------------------------------------------------------------
    // Public API

    /// <summary>
    /// Returns the next name in the Patreon list and advances the index.
    /// Loops back to the beginning once the end of the list is reached.
    /// Thread-safe lock ensures consistent assignment.
    /// </summary>
    /// <returns>The next name to assign to a newborn beaver.</returns>
    public string GetNextName() {
      lock (_lock) {
        if (_nameProvider.Count == 0) {
          ModLogger.LogWarning("Name list is empty — returning fallback name.");
          return "[Patreon Pending]";
        }

        if (_currentIndex >= _nameProvider.Count) {
          _currentIndex = 0;
        }

        string name = _nameProvider.GetName(_currentIndex);
        _currentIndex = (_currentIndex + 1) % _nameProvider.Count;
        ModLogger.LogInfo($"Dispensed Patreon name '{name}' (Next Index: {_currentIndex}/{_nameProvider.Count}).");
        return name;
      }
    }

    // -------------------------------------------------------------------------
    // ISaveableSingleton

    /// <summary>
    /// Writes the current index to the game's save file.
    /// Called automatically by the game whenever it saves.
    /// </summary>
    public void Save(ISingletonSaver singletonSaver) {
      IObjectSaver obj = singletonSaver.GetSingleton(SaveKey);
      obj.Set(CurrentIndexKey, _currentIndex);
      ModLogger.LogInfo($"Saved Patreon name index {_currentIndex} to savegame.");
    }

    // -------------------------------------------------------------------------
    // ILoadableSingleton

    /// <summary>
    /// Restores the saved index when a world is loaded.
    /// If no saved data exists (e.g. first load), index starts at 0.
    /// </summary>
    public void Load() {
      if (!_singletonLoader.TryGetSingleton(SaveKey, out IObjectLoader obj)) {
        _currentIndex = 0;
        ModLogger.LogInfo("No previous saved Patreon name index found. Starting at index 0.");
        return;
      }

      _currentIndex = obj.Has(CurrentIndexKey) ? obj.Get(CurrentIndexKey) : 0;

      // Guard against a shorter list after a CSV update between sessions.
      if (_nameProvider.Count > 0) {
        _currentIndex = _currentIndex % _nameProvider.Count;
      } else {
        _currentIndex = 0;
      }

      ModLogger.LogInfo($"Loaded Patreon name index {_currentIndex} from save.");
    }

  }

}
