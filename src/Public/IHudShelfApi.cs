namespace HudShelf;

/// <summary>
/// HudShelf's service interface. Acquire it via
/// <c>api.ModLoader.GetModSystem&lt;HudShelfModSystem&gt;().Api</c>.
/// </summary>
/// <remarks>
/// All members are safe to call from the client main thread.
/// HudShelf is a client-side mod and does not support being called
/// from worker threads.
/// </remarks>
public interface IHudShelfApi
{
    /// <summary>
    /// Register a HUD for drag-to-position support. Returns a handle
    /// the consumer holds for the lifetime of the HUD.
    /// </summary>
    /// <param name="registration">
    /// Describes the HUD. <see cref="HudRegistration.Id"/>,
    /// <see cref="HudRegistration.Element"/>, and
    /// <see cref="HudRegistration.GetBounds"/> are required; other
    /// fields have defaults.
    /// </param>
    /// <returns>
    /// A handle whose <see cref="IRegisteredHud.CurrentPosition"/> is
    /// already populated with the resolved position. Apply that
    /// position once, then rely on
    /// <see cref="HudRegistration.OnPositionChanged"/> for updates.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A registration with the same <see cref="HudRegistration.Id"/>
    /// already exists, or a required field is null/empty.
    /// </exception>
    IRegisteredHud Register(HudRegistration registration);

    /// <summary>
    /// True while edit mode is active. In edit mode, registered HUDs
    /// can be repositioned by dragging.
    /// </summary>
    /// <remarks>
    /// Edit mode is toggled by the user via the <c>hudshelf:editmode</c>
    /// hotkey (default Ctrl+F10, user-configurable in VS controls).
    /// </remarks>
    bool IsEditModeActive { get; }

    /// <summary>
    /// Raised when <see cref="IsEditModeActive"/> changes. The argument
    /// is the new value.
    /// </summary>
    /// <remarks>
    /// Useful for consumers that want to react to edit-mode toggling
    /// (e.g. dimming their HUD's normal contents while it's about to be
    /// dragged). Most consumers do not need to subscribe; the position
    /// change after a drag is delivered through
    /// <see cref="HudRegistration.OnPositionChanged"/>.
    /// </remarks>
    event Action<bool> EditModeChanged;
}
