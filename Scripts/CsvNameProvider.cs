using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Reads a list of beaver names from a UTF-8 CSV file located in the mod's
  /// folder (<c>Documents/Timberborn/Mods/Patreon-Beaver-Names/patreons.csv</c>).
  /// Each non-empty line is treated as one name.
  /// Loaded once at world start via <see cref="ILoadableSingleton"/>;
  /// never reads the file again during gameplay.
  /// </summary>
  public class CsvNameProvider : INameProvider, ILoadableSingleton {

    /// <summary>
    /// Name shown when the CSV is missing or empty, so the game never crashes.
    /// </summary>
    private const string FallbackName = "[Patreon Pending]";

    private static readonly object FileLock = new object();

    /// <summary>
    /// Current active instance in the Game context (if any).
    /// </summary>
    public static CsvNameProvider Current { get; private set; }

    /// <summary>
    /// Resolves the absolute path to patreons.csv, prioritizing ModLogger.ModPath.
    /// </summary>
    public static string GetCsvPath() {
      if (!string.IsNullOrEmpty(ModLogger.ModPath)) {
        string modCsv = Path.Combine(ModLogger.ModPath, "patreons.csv");
        if (File.Exists(modCsv)) {
          return modCsv;
        }
      }

      string docsPath = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
          "Timberborn", "Mods", "Patreon-Beaver-Names", "patreons.csv");
      return docsPath;
    }

    /// <summary>
    /// Returns the raw list of names joined with newlines, either from memory (if game is running)
    /// or by reading patreons.csv directly.
    /// </summary>
    public static string GetRawNamesText() {
      if (Current != null) {
        lock (Current._names) {
          if (Current._names.Count > 0) {
            return string.Join(Environment.NewLine, Current._names);
          }
        }
      }

      string csvPath = GetCsvPath();
      try {
        if (File.Exists(csvPath)) {
          lock (FileLock) {
            var lines = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l));
            return string.Join(Environment.NewLine, lines);
          }
        }
      } catch (Exception ex) {
        ModLogger.LogError($"Error reading patreons.csv: {ex.Message}");
      }

      return FallbackName;
    }

    /// <summary>
    /// Updates the active names list from a raw multiline string and writes it back to patreons.csv.
    /// Called when the user modifies settings via Mod Settings.
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

      // Update in-memory list if game is active
      if (Current != null) {
        lock (Current._names) {
          Current._names.Clear();
          Current._names.AddRange(lines);
        }
      }

      // Write back to patreons.csv
      string csvPath = GetCsvPath();
      try {
        lock (FileLock) {
          string dir = Path.GetDirectoryName(csvPath);
          if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
          }
          File.WriteAllLines(csvPath, lines, System.Text.Encoding.UTF8);
        }
        ModLogger.LogInfo($"Successfully updated patreons.csv with {lines.Count} name(s).");
      } catch (Exception ex) {
        ModLogger.LogError($"Failed to write updated names to {csvPath}: {ex.Message}");
      }
    }

    private readonly List<string> _names = new();

    // -------------------------------------------------------------------------
    // INameProvider

    /// <inheritdoc/>
    public int Count {
      get {
        lock (_names) {
          return _names.Count;
        }
      }
    }

    /// <inheritdoc/>
    public string GetName(int index) {
      lock (_names) {
        if (index < 0 || index >= _names.Count) {
          return FallbackName;
        }
        return _names[index];
      }
    }

    // -------------------------------------------------------------------------
    // ILoadableSingleton

    /// <summary>
    /// Called once when the world is loaded. Reads and sanitizes the CSV file.
    /// Falls back to a single placeholder name on any error so gameplay continues.
    /// </summary>
    public void Load() {
      Current = this;
      string csvPath = GetCsvPath();
      try {
        if (!File.Exists(csvPath)) {
          throw new FileNotFoundException($"Name list not found at: {csvPath}");
        }

        List<string> loaded;
        lock (FileLock) {
          loaded = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8)
              .Select(line => line.Trim())
              .Where(line => !string.IsNullOrEmpty(line))
              .ToList();
        }

        if (loaded.Count == 0) {
          throw new InvalidOperationException(
              $"patreons.csv exists but contains no valid names: {csvPath}");
        }

        lock (_names) {
          _names.Clear();
          _names.AddRange(loaded);
        }
        ModLogger.LogInfo($"Loaded {_names.Count} name(s) from {csvPath}");

      } catch (Exception ex) {
        ModLogger.LogError($"Failed to load name list — beavers will be named \"{FallbackName}\". Error: {ex.Message}");
        lock (_names) {
          _names.Clear();
          _names.Add(FallbackName);
        }
      }
    }

  }

}
