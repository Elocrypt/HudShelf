using Vintagestory.API.Client;

namespace HudShelf;

/// <summary>
/// Extension helpers for applying <see cref="HudPosition"/> to VS GUI
/// bounds. Both methods are pure and free to call from any thread.
/// </summary>
/// <remarks>
/// These are convenience wrappers; consumers who want full control can
/// implement the same logic themselves and skip this class entirely.
/// The advantage of using these is that any future change to the
/// <c>EnumDialogArea</c> mapping or sign-convention semantics happens
/// in one place that consumers never have to revisit.
/// </remarks>
public static class HudShelfExtensions
{
    /// <summary>
    /// Map a <see cref="HudAnchor"/> to its corresponding VS
    /// <see cref="EnumDialogArea"/>. The mapping is one-to-one; every
    /// HudShelf anchor has a direct VS equivalent.
    /// </summary>
    public static EnumDialogArea ToDialogArea(this HudAnchor anchor) => anchor switch
    {
        HudAnchor.TopLeft      => EnumDialogArea.LeftTop,
        HudAnchor.TopCenter    => EnumDialogArea.CenterTop,
        HudAnchor.TopRight     => EnumDialogArea.RightTop,
        HudAnchor.CenterLeft   => EnumDialogArea.LeftMiddle,
        HudAnchor.Center       => EnumDialogArea.CenterMiddle,
        HudAnchor.CenterRight  => EnumDialogArea.RightMiddle,
        HudAnchor.BottomLeft   => EnumDialogArea.LeftBottom,
        HudAnchor.BottomCenter => EnumDialogArea.CenterBottom,
        HudAnchor.BottomRight  => EnumDialogArea.RightBottom,
        _ => EnumDialogArea.LeftTop,
    };

    /// <summary>
    /// Apply this position to the given <see cref="ElementBounds"/>:
    /// sets the alignment and the fixed alignment offset in one call.
    /// Returns the same bounds for chaining.
    /// </summary>
    /// <remarks>
    /// HudShelf stores offsets in screen pixels (same coordinate space
    /// as mouse events and <c>FrameWidth</c>/<c>FrameHeight</c>). VS's
    /// <c>WithFixedAlignmentOffset</c> expects "fixed" units that get
    /// multiplied by the GUI scale during <c>CalcWorldBounds</c>. This
    /// method divides by the current GUI scale factor so the final
    /// rendered position matches what HudShelf computed.
    /// </remarks>
    public static ElementBounds ApplyTo(this HudPosition position, ElementBounds bounds)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        // GuiElement.scaled(1) returns the current GUI scale factor.
        // HudShelf's offsets are in screen pixels; dividing by scale
        // converts to the "fixed" units WithFixedAlignmentOffset needs.
        var guiScale = GuiElement.scaled(1.0);
        if (guiScale <= 0) guiScale = 1.0;

        return bounds
            .WithAlignment(position.Anchor.ToDialogArea())
            .WithFixedAlignmentOffset(
                position.OffsetX / guiScale,
                position.OffsetY / guiScale);
    }
}
