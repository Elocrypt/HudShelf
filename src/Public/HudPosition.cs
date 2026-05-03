namespace HudShelf;

/// <summary>
/// A HUD's screen position, expressed as a snap anchor plus a residual
/// pixel offset. This is the persistence-stable representation: pixel
/// positions alone break across resolutions and GUI scales, but
/// anchor + offset survives both.
/// </summary>
/// <param name="Anchor">
/// Which of the nine screen reference points the offset is measured
/// from.
/// </param>
/// <param name="OffsetX">
/// Horizontal offset in logical pixels. Positive X is always rightward,
/// regardless of <paramref name="Anchor"/>.
/// </param>
/// <param name="OffsetY">
/// Vertical offset in logical pixels. Positive Y is always downward,
/// regardless of <paramref name="Anchor"/>.
/// </param>
public readonly record struct HudPosition(
    HudAnchor Anchor,
    double OffsetX,
    double OffsetY);
