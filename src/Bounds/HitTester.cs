using HudShelf.Internal;

namespace HudShelf.Bounds;

/// <summary>
/// Hit-testing for registered HUDs. Given the cursor position and
/// screen dimensions, finds which (if any) registered HUD's bounding
/// rect contains the cursor.
/// </summary>
/// <remarks>
/// Hit-test order is unspecified for v1. If two registered HUDs
/// overlap, which one wins is implementation-defined. Stage 2 picks
/// "first match in dictionary iteration order" which is effectively
/// arbitrary; we'll revisit if anyone hits a real overlap problem.
/// <para/>
/// The rect is read from <c>SingleComposer.Bounds</c> after
/// composition rather than recomputed from our own snap math. This
/// guarantees the outline draws exactly where VS placed the dialog,
/// regardless of GUI scale or any other VS-internal positioning
/// quirks.
/// </remarks>
internal static class HitTester
{
    /// <summary>
    /// Find the registered HUD under the cursor, if any.
    /// </summary>
    public static RegisteredHud? Find(
        double cursorX, double cursorY,
        double screenW, double screenH,
        IReadOnlyDictionary<string, RegisteredHud> huds)
    {
        foreach (var hud in huds.Values)
        {
            if (TryGetHudRect(hud, screenW, screenH, out var left, out var top, out var width, out var height))
            {
                if (cursorX >= left && cursorX <= left + width &&
                    cursorY >= top && cursorY <= top + height)
                {
                    return hud;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get the HUD's actual screen-space rectangle by reading its
    /// composed bounds. Returns false if the HUD hasn't been composed
    /// yet or has zero size.
    /// </summary>
    /// <remarks>
    /// Uses <c>absX</c>/<c>absY</c> (set by
    /// <c>CalcWorldBounds</c> during composition) to get the actual
    /// pixel position VS computed, rather than replicating VS's
    /// internal alignment math ourselves. This eliminates coordinate-
    /// space mismatches between our outline and the real HUD position.
    /// </remarks>
    public static bool TryGetHudRect(
        RegisteredHud hud,
        double screenW, double screenH,
        out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;

        try
        {
            var bounds = hud.Registration.Element?.SingleComposer?.Bounds;
            if (bounds is null) return false;

            // absX/absY are the absolute screen-pixel position
            // computed by CalcWorldBounds during Compose(). OuterWidth/
            // OuterHeight are GUI-scaled pixel dimensions.
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
