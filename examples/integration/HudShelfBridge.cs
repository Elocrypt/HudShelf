// COPY THIS FILE INTO YOUR MOD if you want to soft-depend on HudShelf.
// See docs/BRIDGE.md for the full pattern.

using System;
using Vintagestory.API.Client;

namespace YourMod.Integration;

internal static class HudShelfBridge
{
    public static bool IsLoaded(ICoreClientAPI api) =>
        api.ModLoader.IsModEnabled("hudshelf");

    public static object? TryRegister(
        ICoreClientAPI api,
        string id,
        GuiDialog element,
        string defaultAnchor,
        double defaultOffsetX,
        double defaultOffsetY,
        Action<string, double, double> onPositionChanged,
        Func<(double Width, double Height)> getBounds)
    {
        if (!IsLoaded(api)) return null;
        return HudShelfCall.Register(
            api, id, element, defaultAnchor, defaultOffsetX, defaultOffsetY,
            onPositionChanged, getBounds);
    }

    public static (string Anchor, double OffsetX, double OffsetY)? TryGetPosition(object? handle)
    {
        if (handle is null) return null;
        return HudShelfCall.GetPosition(handle);
    }

    public static void Unregister(object? handle)
    {
        if (handle is null) return;
        HudShelfCall.Unregister(handle);
    }
}
