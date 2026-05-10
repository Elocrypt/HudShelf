# HudShelf — Integration Guide for Mod Authors

**HudShelf** is an opt-in, client-side library mod for Vintage Story
that gives HUD mods drag-to-position support. When installed, your
HUD becomes draggable via HudShelf's edit-mode hotkey; when absent,
your mod behaves exactly as it did before.

This guide covers everything you need to integrate HudShelf into
your mod. HUD Clock uses this exact pattern — see its source for a
real-world example.

---

## How it works

1. Your mod **registers** its HUD with HudShelf at startup.
2. HudShelf returns a **resolved position** (persisted from a
   previous session, or the defaults you provided).
3. You apply that position to your HUD's bounds.
4. When the user drags your HUD in edit mode, HudShelf calls your
   **`OnPositionChanged` callback** with the new position.
5. You apply the new position and recompose.

That's the entire contract. HudShelf handles persistence, edit-mode
UI, snap-to-anchor math, and edge clamping. Your mod handles
rendering and composing.

---

## Choosing: soft-depend vs hard-depend

| | Soft-depend (recommended) | Hard-depend |
|-|---------------------------|-------------|
| Your mod loads without HudShelf? | Yes | No |
| Extra files needed? | Two bridge files (~130 lines total) | None |
| HudShelf types in your code? | Only in the bridge's inner file | Anywhere |
| When to use | Most mods | Mods that exist solely for HUD management |

**Soft-depend** means your mod works with or without HudShelf.
Users who don't want HudShelf get your original positioning.
Users who install HudShelf get drag-to-position for free.

---

## Soft-depend integration (step by step)

### 1. Copy the bridge files

Copy these two files into your mod's source tree (e.g. under
`Infrastructure/Integration/` or wherever you keep glue code).
Change the namespace to match your project.

**`HudShelfBridge.cs`** — the public surface. All types are
primitives (`string`, `double`, `object?`). Safe to reference
from anywhere in your mod.

```csharp
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

    public static (string Anchor, double OffsetX, double OffsetY)?
        TryGetPosition(object? handle)
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
```

**`HudShelfCall.cs`** — references HudShelf types directly. Only
compiled by the JIT when HudShelf is present (because it's only
called from inside `IsLoaded` checks).

```csharp
using System;
using HudShelf;
using Vintagestory.API.Client;

namespace YourMod.Integration;

internal static class HudShelfCall
{
    public static object? Register(
        ICoreClientAPI api, string id, GuiDialog element,
        string defaultAnchor, double defaultOffsetX,
        double defaultOffsetY,
        Action<string, double, double> onPositionChanged,
        Func<(double Width, double Height)> getBounds)
    {
        var shelf = api.ModLoader.GetModSystem<HudShelfModSystem>();
        if (shelf?.Api is null) return null;

        if (!Enum.TryParse<HudAnchor>(defaultAnchor, ignoreCase: false,
            out var anchor))
            anchor = HudAnchor.TopLeft;

        try
        {
            return shelf.Api.Register(new HudRegistration
            {
                Id = id,
                Element = element,
                DefaultAnchor = anchor,
                DefaultOffsetX = defaultOffsetX,
                DefaultOffsetY = defaultOffsetY,
                GetBounds = getBounds,
                OnPositionChanged = p =>
                    onPositionChanged(p.Anchor.ToString(),
                                     p.OffsetX, p.OffsetY),
            });
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                $"[HudShelfBridge] Register failed: {ex.Message}");
            return null;
        }
    }

    public static (string, double, double) GetPosition(object handle)
    {
        var r = (IRegisteredHud)handle;
        var p = r.CurrentPosition;
        return (p.Anchor.ToString(), p.OffsetX, p.OffsetY);
    }

    public static void Unregister(object handle) =>
        ((IRegisteredHud)handle).Unregister();
}
```

### 2. Add a HudShelf DLL reference

Your `.csproj` needs to reference `HudShelf.dll` so `HudShelfCall`
compiles. The DLL is `Private=false` (not copied to output — HudShelf
ships its own copy):

```xml
<Reference Include="HudShelf">
  <HintPath>..\HudShelf\bin\Debug\HudShelf.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Adjust the path to match your directory layout.

### 3. Add the modinfo dependency

In your `modinfo.json`, add `"hudshelf": ""` to `dependencies`.
This ensures HudShelf's `StartClientSide` runs before yours (load
ordering), while still allowing your mod to load when HudShelf is
absent:

```json
"dependencies": {
    "game": "1.22.0",
    "hudshelf": ""
}
```

### 4. Register your HUD

In your `StartClientSide` (or `OnPlayerReady`), after creating your
HUD dialog:

```csharp
// Store as object? — no HudShelf types in field declarations.
private object? _shelfHandle;

// After creating your HUD:
_shelfHandle = HudShelfBridge.TryRegister(
    api,
    id: "yourmod:main",          // unique, stable across sessions
    element: _myHud,              // your GuiDialog / HudElement
    defaultAnchor: "TopRight",    // fallback when no persisted position
    defaultOffsetX: 0,
    defaultOffsetY: 0,
    onPositionChanged: (anchor, x, y) =>
    {
        // HudShelf moved your HUD. Apply the new position.
        _myHud.RepositionFromShelf(anchor, x, y);
    },
    getBounds: () => (
        _myHud.SingleComposer?.Bounds.OuterWidth ?? 0,
        _myHud.SingleComposer?.Bounds.OuterHeight ?? 0));

// Apply the initial position (persisted or default).
if (HudShelfBridge.TryGetPosition(_shelfHandle) is { } p)
{
    _myHud.RepositionFromShelf(p.Anchor, p.OffsetX, p.OffsetY);
}
```

### 5. Apply the position

Add a `RepositionFromShelf` method to your HUD class. This mutates
the already-composed bounds in place — no recompose needed:

```csharp
public void RepositionFromShelf(
    string anchorName, double screenOffsetX, double screenOffsetY)
{
    if (SingleComposer?.Bounds is null) return;

    // Map string to EnumDialogArea.
    var area = anchorName switch
    {
        "TopLeft"      => EnumDialogArea.LeftTop,
        "TopCenter"    => EnumDialogArea.CenterTop,
        "TopRight"     => EnumDialogArea.RightTop,
        "CenterLeft"   => EnumDialogArea.LeftMiddle,
        "Center"       => EnumDialogArea.CenterMiddle,
        "CenterRight"  => EnumDialogArea.RightMiddle,
        "BottomLeft"   => EnumDialogArea.LeftBottom,
        "BottomCenter" => EnumDialogArea.CenterBottom,
        "BottomRight"  => EnumDialogArea.RightBottom,
        _ => EnumDialogArea.LeftTop,
    };

    // HudShelf offsets are in screen pixels; VS's
    // fixedOffset expects "fixed" units (pre-GUI-scale).
    var guiScale = GuiElement.scaled(1.0);
    if (guiScale <= 0) guiScale = 1.0;

    SingleComposer.Bounds.Alignment = area;
    SingleComposer.Bounds.fixedOffsetX = screenOffsetX / guiScale;
    SingleComposer.Bounds.fixedOffsetY = screenOffsetY / guiScale;
    SingleComposer.Bounds.CalcWorldBounds();
}
```

### 6. Reapply after recompose

If your HUD recomposes (settings change, content change), the
composed bounds reset to whatever your `Compose`/`Rebuild` method
set. You need to reapply the shelf position afterward:

```csharp
// In your controller, after every Rebuild/Compose call:
private bool _shelfActive;
private string _shelfAnchor;
private double _shelfX, _shelfY;

private void ReapplyShelfPosition()
{
    if (_shelfActive)
        _myHud.RepositionFromShelf(_shelfAnchor, _shelfX, _shelfY);
}

// Store position in the callback:
void OnShelfPositionChanged(string anchor, double x, double y)
{
    _shelfActive = true;
    _shelfAnchor = anchor;
    _shelfX = x;
    _shelfY = y;
    _myHud.RepositionFromShelf(anchor, x, y);
}
```

### 7. Unregister on dispose

```csharp
public override void Dispose()
{
    HudShelfBridge.Unregister(_shelfHandle);
    _shelfHandle = null;
    // ... rest of cleanup
}
```

---

## Hard-depend integration

If your mod requires HudShelf (and you're fine with that
dependency), skip the bridge files and reference HudShelf's types
directly:

```csharp
using HudShelf;

var shelf = api.ModLoader.GetModSystem<HudShelfModSystem>();
var handle = shelf.Api.Register(new HudRegistration
{
    Id            = "yourmod:main",
    Element       = _myHud,
    DefaultAnchor = HudAnchor.TopRight,
    DefaultOffsetX = 0,
    DefaultOffsetY = 0,
    GetBounds     = () => (_myHud.SingleComposer?.Bounds.OuterWidth ?? 0,
                           _myHud.SingleComposer?.Bounds.OuterHeight ?? 0),
    OnPositionChanged = p =>
    {
        // Use the extension helper:
        p.ApplyTo(_myHud.SingleComposer.Bounds);
        // Or recompose with the position.
    },
});

// Apply initial position using the helper:
handle.CurrentPosition.ApplyTo(_myHud.DialogBounds);
```

The `HudPosition.ApplyTo(ElementBounds)` extension handles the
`EnumDialogArea` mapping and GUI scale conversion in one call.

---

## Registration parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Id` | `string` | Yes | Stable identifier, used as the persistence key. Prefix with your mod ID: `"yourmod:main"` |
| `Element` | `GuiDialog` | Yes | Your HUD dialog (or any `HudElement`). HudShelf reads its bounds but never modifies it directly. |
| `GetBounds` | `Func<(double,double)>` | Yes | Returns `(width, height)` in GUI-scaled pixels. Typically `(SingleComposer?.Bounds.OuterWidth ?? 0, ...)`. |
| `DefaultAnchor` | `HudAnchor` / `string` | No (default: TopLeft) | Fallback anchor when no persisted position exists. |
| `DefaultOffsetX/Y` | `double` | No (default: 0) | Fallback offset in screen pixels. |
| `DisplayName` | `string?` | No (default: Id) | Shown in edit-mode UI and log messages. |
| `OnPositionChanged` | `Action<HudPosition>` | No | Called on drag completion with the new position. Not called during `Register` — read `CurrentPosition` for the initial value. |

---

## Coordinate convention

HudShelf stores and delivers offsets in **screen pixels** — the
same units as `capi.Render.FrameWidth`, `capi.Input.MouseX`, and
`ElementBounds.OuterWidth`. Positive X is rightward; positive Y is
downward, regardless of which anchor is selected.

VS's `WithFixedOffset` expects **"fixed" units** (logical
pixels pre-GUI-scale). The conversion is:

```
fixedOffset = screenOffset / GuiElement.scaled(1.0)
```

The `ApplyTo` extension and the `RepositionFromShelf` example above
both handle this. If you implement your own apply path, remember to
divide by GUI scale.

---

## Anchors

HudShelf supports nine snap zones (screen divided into thirds):

| | Left | Center | Right |
|---|---|---|---|
| **Top** | TopLeft | TopCenter | TopRight |
| **Center** | CenterLeft | Center | CenterRight |
| **Bottom** | BottomLeft | BottomCenter | BottomRight |

Your mod's default anchor can be any of the six standard corners
(which is what most HUDs use). HudShelf adds the three center-row
zones via drag — users can place HUDs there even if your settings
dropdown only offers six options.

---

## Persistence

HudShelf manages persistence automatically. Positions are saved to
`ModConfig/hudshelf/positions.json`, keyed by HUD ID. Your mod
doesn't read or write this file. When HudShelf is uninstalled and
reinstalled, persisted positions are restored automatically.

---

## Verifying your integration

1. Build and deploy your mod + HudShelf.
2. Enter edit mode (default <kbd>Ctrl</kbd>+<kbd>F8</kbd>).
   Your HUD should get a blue outline.
3. Drag the HUD to a different zone. A green preview rectangle
   shows where it will land.
4. Release. The HUD should appear at the preview location.
5. Exit edit mode. The outline disappears.
6. Restart the game. The HUD should appear where you left it.
7. Remove HudShelf from your Mods folder. Your HUD should
   appear at its default/settings-configured position.

---

## FAQ

**My HUD recomposes frequently (every tick). Will that break HudShelf?**
No. HudShelf only cares about your HUD's position and bounds. If you
recompose and then call `ReapplyShelfPosition`, the position stays
correct. Text updates via `SetNewText` (without recompose) don't
affect positioning at all.

**Can I register multiple HUDs?**
Yes. Give each a unique `Id` (e.g. `"yourmod:main"`,
`"yourmod:sidebar"`). Each gets independent drag-to-position and
persistence.

**What if HudShelf adds new anchors in the future?**
Your `OnPositionChanged` callback receives the anchor as a string.
Unknown anchors fall through to your default case. No code change
needed — the position still works because the offset carries the
actual screen location.

**Does HudShelf handle window resize?**
Yes. On edit-mode entry, HudShelf re-clamps every registered HUD
to the current screen size (32px minimum overlap on each axis).

---

## Links

- [HudShelf source](https://github.com/Elocrypt/HudShelf)
- [Bridge pattern details](https://github.com/Elocrypt/HudShelf/blob/main/docs/BRIDGE.md)
- [HUD Clock integration](https://github.com/Elocrypt/HudClock) — real-world example using the soft-depend pattern
