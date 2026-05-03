using Vintagestory.API.Client;

namespace HudShelf;

/// <summary>
/// Describes a HUD to register with HudShelf for drag-to-position
/// support. Pass an instance to <see cref="IHudShelfApi.Register"/>.
/// </summary>
/// <remarks>
/// <see cref="Id"/>, <see cref="Element"/>, and <see cref="GetBounds"/>
/// are required. Everything else has a sensible default; a registration
/// with only the required fields produces working behavior.
/// </remarks>
public sealed class HudRegistration
{
    /// <summary>
    /// Stable identifier for this HUD. Used as the persistence key, so
    /// it must be the same string across game sessions. Convention:
    /// prefix with your mod ID, e.g. <c>"hudclock:main"</c>, to avoid
    /// collisions with other mods that register HUDs.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The <see cref="GuiDialog"/> (or any HudElement-derived dialog)
    /// whose position HudShelf will manage. HudShelf does not modify
    /// this object directly; it reports position changes via
    /// <see cref="OnPositionChanged"/> and the consumer applies them.
    /// </summary>
    public required GuiDialog Element { get; init; }

    /// <summary>
    /// Returns the current GUI-scaled bounds of the HUD as
    /// <c>(width, height)</c> in pixels. HudShelf calls this on demand
    /// for hit-testing and snap calculation.
    /// </summary>
    /// <remarks>
    /// The typical implementation is
    /// <c>() =&gt; (Element.SingleComposer?.Bounds.OuterWidth ?? 0,
    /// Element.SingleComposer?.Bounds.OuterHeight ?? 0)</c>. Use
    /// <c>OuterWidth</c>/<c>OuterHeight</c> (GUI-scaled), not
    /// <c>TotalWidth</c>/<c>TotalHeight</c> (logical pixels) — HudShelf
    /// expects GUI-scaled bounds because mouse coordinates are
    /// GUI-scaled. Returning <c>(0, 0)</c> is safe and indicates the
    /// HUD is not currently hittable (e.g. not yet composed).
    /// </remarks>
    public required Func<(double Width, double Height)> GetBounds { get; init; }

    /// <summary>
    /// Anchor used when no persisted position exists for this HUD ID.
    /// Defaults to <see cref="HudAnchor.TopLeft"/>.
    /// </summary>
    public HudAnchor DefaultAnchor { get; init; } = HudAnchor.TopLeft;

    /// <summary>
    /// Horizontal offset in logical pixels from
    /// <see cref="DefaultAnchor"/>. Positive X = right.
    /// </summary>
    public double DefaultOffsetX { get; init; }

    /// <summary>
    /// Vertical offset in logical pixels from
    /// <see cref="DefaultAnchor"/>. Positive Y = down.
    /// </summary>
    public double DefaultOffsetY { get; init; }

    /// <summary>
    /// Human-readable name of this HUD. If null, <see cref="Id"/> is
    /// used wherever HudShelf displays the HUD's name (edit-mode UI,
    /// log lines).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Invoked after HudShelf updates this HUD's position — drag
    /// completion, edit-mode reset, or a future programmatic API. Not
    /// invoked synchronously during <see cref="IHudShelfApi.Register"/>;
    /// read <see cref="IRegisteredHud.CurrentPosition"/> for the
    /// initial value, then rely on this callback for updates.
    /// </summary>
    public Action<HudPosition>? OnPositionChanged { get; init; }
}
