using HudShelf.Internal;

namespace HudShelf.Bounds;

/// <summary>
/// Hit-testing for registered HUDs. Given the cursor position and
/// screen size, finds which (if any) registered HUD's bounding rect
/// contains the cursor.
/// </summary>
/// <remarks>
/// When HUDs overlap, the smallest one under the cursor wins. A small
/// HUD parked on top of a large one (a clock over a hotbar) would
/// otherwise be impossible to grab if the large one happened to come
/// first in registration order.
/// <para/>
/// The rect is read from <c>SingleComposer.Bounds</c> after
/// composition rather than recomputed from our own snap math, so hits
/// land exactly where VS placed the dialog. HUDs without a single
/// composer (vanilla HUDs keep theirs under other keys, and HUD Shelf:
/// Essentials registers individual stat bars of one dialog) fall back
/// to <see cref="HudRegistration.GetBounds"/> placed at the HUD's
/// current position.
/// </remarks>
internal static class HitTester
{
    /// <summary>
    /// Find the registered HUD under the cursor, if any. Allocation-free;
    /// called every frame while edit mode is on.
    /// </summary>
    public static RegisteredHud? Find(
        double cursorX, double cursorY,
        double screenW, double screenH,
        List<RegisteredHud> huds)
    {
        RegisteredHud? best = null;
        var bestArea = double.MaxValue;

        foreach (var hud in huds)
        {
            if (!TryGetHudRect(hud, screenW, screenH, out var left, out var top, out var width, out var height)) continue;
            if (!Contains(left, top, width, height, cursorX, cursorY)) continue;

            var area = width * height;
            if (area < bestArea)
            {
                best = hud;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>Inclusive point-in-rect test.</summary>
    internal static bool Contains(double left, double top, double width, double height, double x, double y) =>
        x >= left && x <= left + width && y >= top && y <= top + height;

    /// <summary>
    /// Get the HUD's screen-space rectangle: the composed dialog bounds
    /// when available, otherwise the consumer-reported size placed at
    /// the HUD's current position.
    /// </summary>
    /// <remarks>
    /// Signature and parameter names are relied on by HUD Shelf: Essentials
    /// 0.5.1 and older, which postfix this method. Keep them stable.
    /// </remarks>
    public static bool TryGetHudRect(
        RegisteredHud hud,
        double screenW, double screenH,
        out double left, out double top, out double width, out double height)
    {
        if (TryGetComposerRect(hud, out left, out top, out width, out height)) return true;

        if (!hud.TryGetContentSize(out width, out height))
        {
            left = top = 0;
            return false;
        }

        (left, top) = SnapMath.TopLeft(hud.CurrentPosition, width, height, screenW, screenH);
        return true;
    }

    /// <summary>
    /// The composed dialog rect from <c>SingleComposer.Bounds</c>. False
    /// if the HUD has no single composer, hasn't been composed yet, or
    /// has zero size.
    /// </summary>
    /// <remarks>
    /// Uses <c>absX</c>/<c>absY</c> (set by <c>CalcWorldBounds</c> during
    /// composition) to get the pixel position VS computed rather than
    /// replicating its alignment math.
    /// </remarks>
    internal static bool TryGetComposerRect(
        RegisteredHud hud,
        out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;

        try
        {
            var bounds = hud.Registration.Element?.SingleComposer?.Bounds;
            if (bounds is null) return false;

            left = bounds.absX;
            top = bounds.absY;
            width = bounds.OuterWidth;
            height = bounds.OuterHeight;

            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }
}
