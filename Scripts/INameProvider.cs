namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Abstraction over a source of beaver names.
  /// Implement this interface to swap the data source (CSV, API, etc.)
  /// without changing any other mod logic.
  /// </summary>
  public interface INameProvider {

    /// <summary>
    /// Total number of names currently loaded.
    /// Returns 0 if the source failed to load.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Returns the name at the given zero-based index.
    /// The caller is responsible for keeping <paramref name="index"/> in range.
    /// </summary>
    /// <param name="index">Zero-based index into the name list.</param>
    /// <returns>The name at <paramref name="index"/>.</returns>
    string GetName(int index);

  }

}
