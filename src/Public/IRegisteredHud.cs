namespace HudShelf;

/// <summary>
/// Handle returned from <see cref="IHudShelfApi.Register"/> for a
/// successfully-registered HUD. Hold this for the lifetime of your HUD;
/// call <see cref="Unregister"/> on shutdown.
/// </summary>
public interface IRegisteredHud
{
    /// <summary>
    /// The HUD ID this handle was registered with. Stable across the
    /// handle's lifetime.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The current resolved position for this HUD. Populated to the
    /// persisted value (or the registered defaults if no persisted
    /// value exists) at registration time, and updated whenever
    /// HudShelf changes the position. Read this once after registering
    /// to apply the initial position; rely on
    /// <see cref="HudRegistration.OnPositionChanged"/> for updates
    /// thereafter.
    /// </summary>
    HudPosition CurrentPosition { get; }

    /// <summary>
    /// Move this HUD by a screen-pixel delta, keeping its anchor. The
    /// result is clamped to the screen, persisted, and delivered through
    /// <see cref="HudRegistration.OnPositionChanged"/> like a drag.
    /// </summary>
    /// <remarks>
    /// For HUDs that should follow another HUD (e.g. a widget docked to
    /// the hotbar): apply the leader's movement to the follower. No-op
    /// after <see cref="Unregister"/>.
    /// </remarks>
    void MoveBy(double dx, double dy);

    /// <summary>
    /// Removes this HUD from HudShelf. Idempotent: subsequent calls are
    /// no-ops. After unregistering, the handle is inert — its
    /// properties keep their last values but no further callbacks fire.
    /// </summary>
    void Unregister();
}
