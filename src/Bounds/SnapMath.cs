namespace HudShelf.Bounds;

/// <summary>
/// Pure-function snap and clamp math. No state, no I/O, no VS types.
/// Lives here separately from the input/render code so it can be
/// exercised in tests without any VS-side scaffolding.
/// </summary>
/// <remarks>
/// Coordinate convention throughout: origin is screen top-left,
/// positive X = right, positive Y = down. All functions operate in
/// GUI-scaled pixels — the same units the consumer's
/// <c>GetBounds</c> returns and the same units VS's mouse events
/// report.
/// </remarks>
internal static class SnapMath
{
    /// <summary>
    /// Minimum number of pixels a HUD's bounding box must overlap
    /// the screen on each axis. Acts as a pixel floor for small HUDs;
    /// large HUDs are governed by <see cref="MinEdgeOverlapFraction"/>
    /// instead (whichever is larger wins).
    /// </summary>
    internal const double MinEdgeOverlapPx = 32.0;

    /// <summary>
    /// Minimum fraction of a HUD's size that must remain on-screen on
    /// each axis. Applied independently per axis so a wide-but-short
    /// HUD (e.g. a hotbar) is constrained correctly on both dimensions.
    /// The effective minimum overlap per axis is
    /// <c>max(MinEdgeOverlapPx, hudDimension * MinEdgeOverlapFraction)</c>.
    /// </summary>
    internal const double MinEdgeOverlapFraction = 0.5;

    /// <summary>
    /// Classify a cursor position into one of the nine snap zones.
    /// Screen is divided into thirds horizontally and vertically;
    /// cursor's third×third determines the anchor.
    /// </summary>
    /// <remarks>
    /// Cursor exactly on a zone boundary lands in the higher-index
    /// zone (right/bottom side). The choice is arbitrary but
    /// deterministic — any consistent rule avoids hysteresis at
    /// boundaries.
    /// <para/>
    /// Out-of-screen cursor coordinates clamp to the nearest zone:
    /// negative X picks the left column, oversized X picks the right.
    /// </remarks>
    public static HudAnchor ClassifyCursorZone(double cursorX, double cursorY, double screenW, double screenH)
    {
        // Defensive: a degenerate screen (0×0, e.g. during early
        // startup) should not divide by zero. Pick TopLeft as the
        // safe default; the caller will re-classify on the next
        // frame once the screen has real dimensions.
        if (screenW <= 0 || screenH <= 0)
        {
            return HudAnchor.TopLeft;
        }

        var thirdW = screenW / 3.0;
        var thirdH = screenH / 3.0;

        // Column: 0 = left, 1 = center, 2 = right.
        var col = cursorX < thirdW ? 0 : (cursorX < 2 * thirdW ? 1 : 2);
        // Row: 0 = top, 1 = middle, 2 = bottom.
        var row = cursorY < thirdH ? 0 : (cursorY < 2 * thirdH ? 1 : 2);

        // Lookup table reads more obviously than chained conditionals.
        return (col, row) switch
        {
            (0, 0) => HudAnchor.TopLeft,
            (1, 0) => HudAnchor.TopCenter,
            (2, 0) => HudAnchor.TopRight,
            (0, 1) => HudAnchor.CenterLeft,
            (1, 1) => HudAnchor.Center,
            (2, 1) => HudAnchor.CenterRight,
            (0, 2) => HudAnchor.BottomLeft,
            (1, 2) => HudAnchor.BottomCenter,
            (2, 2) => HudAnchor.BottomRight,
            _ => HudAnchor.TopLeft,
        };
    }

    /// <summary>
    /// Compute the screen-pixel point that a given anchor refers to,
    /// given the screen dimensions. E.g. <c>TopRight</c> on a
    /// 1920×1080 screen is <c>(1920, 0)</c>.
    /// </summary>
    public static (double X, double Y) AnchorScreenPoint(HudAnchor anchor, double screenW, double screenH)
    {
        var x = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.CenterLeft or HudAnchor.BottomLeft => 0.0,
            HudAnchor.TopCenter or HudAnchor.Center or HudAnchor.BottomCenter => screenW / 2.0,
            HudAnchor.TopRight or HudAnchor.CenterRight or HudAnchor.BottomRight => screenW,
            _ => 0.0,
        };

        var y = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.TopCenter or HudAnchor.TopRight => 0.0,
            HudAnchor.CenterLeft or HudAnchor.Center or HudAnchor.CenterRight => screenH / 2.0,
            HudAnchor.BottomLeft or HudAnchor.BottomCenter or HudAnchor.BottomRight => screenH,
            _ => 0.0,
        };

        return (x, y);
    }

    /// <summary>
    /// The HUD's reference point relative to its own top-left corner,
    /// for a given anchor. E.g. for <c>TopRight</c> the HUD's reference
    /// point is its top-right corner, which is <c>(width, 0)</c>
    /// relative to its own origin.
    /// </summary>
    /// <remarks>
    /// This is what gets aligned to the anchor's screen point. The
    /// residual offset moves the HUD relative to that alignment.
    /// </remarks>
    public static (double X, double Y) HudReferencePoint(HudAnchor anchor, double hudW, double hudH)
    {
        var x = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.CenterLeft or HudAnchor.BottomLeft => 0.0,
            HudAnchor.TopCenter or HudAnchor.Center or HudAnchor.BottomCenter => hudW / 2.0,
            HudAnchor.TopRight or HudAnchor.CenterRight or HudAnchor.BottomRight => hudW,
            _ => 0.0,
        };

        var y = anchor switch
        {
            HudAnchor.TopLeft or HudAnchor.TopCenter or HudAnchor.TopRight => 0.0,
            HudAnchor.CenterLeft or HudAnchor.Center or HudAnchor.CenterRight => hudH / 2.0,
            HudAnchor.BottomLeft or HudAnchor.BottomCenter or HudAnchor.BottomRight => hudH,
            _ => 0.0,
        };

        return (x, y);
    }

    /// <summary>
    /// Compute a snap position given the cursor, HUD size, and screen
    /// size. The HUD's reference point (matching the chosen anchor)
    /// will be placed at the cursor; offset is the delta between the
    /// chosen anchor's screen point and that target.
    /// </summary>
    /// <remarks>
    /// In words: the user moved the HUD so its anchor-matching point
    /// is now at the cursor. We pick the anchor based on which zone
    /// the cursor is in, then compute how far from that anchor's
    /// screen point the cursor sits.
    /// <para/>
    /// The result is NOT edge-clamped. Caller composes with
    /// <see cref="ClampToScreen"/> if clamping is desired.
    /// </remarks>
    public static HudPosition SnapToCursor(
        double cursorX, double cursorY,
        double hudW, double hudH,
        double screenW, double screenH)
    {
        var anchor = ClassifyCursorZone(cursorX, cursorY, screenW, screenH);
        var (anchorX, anchorY) = AnchorScreenPoint(anchor, screenW, screenH);

        // The HUD's top-left will land at (cursor - hudReference).
        // The anchor refers to that same reference point on the HUD,
        // so the offset from anchor screen point to HUD reference is
        // simply (cursor - anchor).
        var offsetX = cursorX - anchorX;
        var offsetY = cursorY - anchorY;

        return new HudPosition(anchor, offsetX, offsetY);
    }

    /// <summary>
    /// Clamp a position so the HUD's bounding rect overlaps the screen
    /// by at least half its size on each axis (floored to
    /// <see cref="MinEdgeOverlapPx"/> for small HUDs). Overlap is
    /// evaluated independently per axis using
    /// <see cref="MinEdgeOverlapFraction"/>.
    /// </summary>
    /// <remarks>
    /// Computes the HUD's screen-space bounding rect from the position
    /// and HUD size, clamps the rect, then converts the clamped rect
    /// back to a position with the same anchor. This way the anchor
    /// the user picked is preserved; only the offset moves.
    /// <para/>
    /// If the HUD is wider or taller than the screen, clamping does
    /// the best it can — it ensures at least half-overlap on the
    /// chosen-anchor side but the HUD will extend off the opposite
    /// edge. That's the right trade-off for an oversized HUD: the
    /// user can still see and grab some of it.
    /// </remarks>
    public static HudPosition ClampToScreen(
        HudPosition position,
        double hudW, double hudH,
        double screenW, double screenH)
    {
        // Compute the HUD's top-left in screen pixels, given anchor + offset + HUD reference.
        var (anchorX, anchorY) = AnchorScreenPoint(position.Anchor, screenW, screenH);
        var (refX, refY) = HudReferencePoint(position.Anchor, hudW, hudH);

        // Anchor screen point + offset = HUD's reference point on screen.
        // HUD top-left = HUD's reference point - reference offset within HUD.
        var hudLeft = anchorX + position.OffsetX - refX;
        var hudTop = anchorY + position.OffsetY - refY;

        // Per-axis minimum overlap: at least half the HUD must be on-screen,
        // floored to MinEdgeOverlapPx for HUDs smaller than 64px on that axis.
        // Applied independently so a wide-short HUD (hotbar) isn't over-
        // constrained on its short axis.
        var minOverlapX = Math.Max(MinEdgeOverlapPx, hudW * MinEdgeOverlapFraction);
        var minOverlapY = Math.Max(MinEdgeOverlapPx, hudH * MinEdgeOverlapFraction);

        var minLeft = minOverlapX - hudW;
        var maxLeft = screenW - minOverlapX;
        var minTop  = minOverlapY - hudH;
        var maxTop  = screenH - minOverlapY;

        // Defensive: if the HUD is wider/taller than the screen by a lot,
        // the min/max can cross. In that case, prefer min (left edge of
        // the HUD at the rightmost legal position) since "anchored to a
        // corner the user picked, sticking out the opposite side" is the
        // expected oversized-HUD behavior.
        if (minLeft > maxLeft) maxLeft = minLeft;
        if (minTop > maxTop) maxTop = minTop;

        var clampedLeft = Math.Clamp(hudLeft, minLeft, maxLeft);
        var clampedTop = Math.Clamp(hudTop, minTop, maxTop);

        // Re-derive offset from the clamped top-left.
        var clampedOffsetX = clampedLeft + refX - anchorX;
        var clampedOffsetY = clampedTop + refY - anchorY;

        return new HudPosition(position.Anchor, clampedOffsetX, clampedOffsetY);
    }
}
