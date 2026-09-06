using System;
using Timberborn.Beavers;
using Timberborn.Characters;
using Timberborn.EntityNaming;
using Timberborn.SingletonSystem;

namespace Mods.PatreonBeaverNames.Scripts {

  /// <summary>
  /// Listens to character creation events and automatically assigns the next
  /// sequential Patreon supporter name to newborn beavers.
  /// </summary>
  public class SequentialBeaverNamer : ILoadableSingleton, IUnloadableSingleton {

    private readonly NameManager _nameManager;
    private readonly EventBus _eventBus;

    /// <summary>
    /// Initialises the namer service with dependencies injected via Bindito.
    /// </summary>
    public SequentialBeaverNamer(NameManager nameManager, EventBus eventBus) {
      _nameManager = nameManager;
      _eventBus = eventBus;
    }

    // -------------------------------------------------------------------------
    // Lifecycle

    /// <summary>
    /// Registers this service with the <see cref="EventBus"/> when the game world is loaded.
    /// </summary>
    public void Load() {
      _eventBus.Register(this);
      ModLogger.LogInfo("SequentialBeaverNamer subscribed to EventBus (Game context).");
    }

    /// <summary>
    /// Unregisters this service from the <see cref="EventBus"/> when the world unloads.
    /// </summary>
    public void Unload() {
      _eventBus.Unregister(this);
      ModLogger.LogInfo("SequentialBeaverNamer unsubscribed from EventBus.");
    }

    // -------------------------------------------------------------------------
    // Event Handler

    /// <summary>
    /// Intercepts character creation and applies a sequential Patreon name to beavers.
    /// </summary>
    /// <param name="createdEvent">The character creation event fired by the game.</param>
    [OnEvent]
    public void OnCharacterCreated(CharacterCreatedEvent createdEvent) {
      Character character = createdEvent?.Character;
      if (character == null) {
        return;
      }

      // Ensure the spawned character is actually a beaver (not a bot or other entity)
      if (!character.HasComponent<Beaver>()) {
        return;
      }

      NamedEntity namedEntity = character.GetComponent<NamedEntity>();
      if (namedEntity == null) {
        return;
      }

      try {
        string nextName = _nameManager.GetNextName();
        namedEntity.SetEntityName(nextName);
        ModLogger.LogInfo($"Assigned sequential Patreon name '{nextName}' to newborn beaver.");
      } catch (Exception ex) {
        ModLogger.LogError($"Unexpected error while assigning name to beaver: {ex.Message}");
      }
    }

  }

}
