namespace HudShelf;

/// <summary>
/// One of nine reference points on the screen used to position a HUD.
/// The anchor identifies which corner, edge midpoint, or true center
/// of the screen the HUD's matching point is aligned to. The residual
/// pixel offset from that anchor is stored separately on
/// <see cref="HudPosition"/>.
/// </summary>
/// <remarks>
/// Sign convention for the offset that pairs with an anchor: positive X
/// is always rightward and positive Y is always downward, regardless of
/// which anchor is chosen. Consumers do not need to flip signs for
/// bottom- or right-aligned anchors.
/// </remarks>
public enum HudAnchor
{
    /// <summary>Top-left corner of the screen.</summary>
    TopLeft,

    /// <summary>Midpoint of the screen's top edge.</summary>
    TopCenter,

    /// <summary>Top-right corner of the screen.</summary>
    TopRight,

    /// <summary>Midpoint of the screen's left edge.</summary>
    CenterLeft,

    /// <summary>True center of the screen.</summary>
    Center,

    /// <summary>Midpoint of the screen's right edge.</summary>
    CenterRight,

    /// <summary>Bottom-left corner of the screen.</summary>
    BottomLeft,

    /// <summary>Midpoint of the screen's bottom edge.</summary>
    BottomCenter,

    /// <summary>Bottom-right corner of the screen.</summary>
    BottomRight,
}
