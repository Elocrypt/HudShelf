# Soft-depending on HudShelf

This document explains how to integrate HudShelf into a mod such that
your mod still loads and runs cleanly when HudShelf is **not** installed.

## The footgun this avoids

In .NET, a method that references a missing type fails at JIT-compile
time, not at assembly-load time. So a method whose body uses HudShelf
types will only fail when that method is actually called — which is
fine if you can guarantee it's never called when HudShelf is absent.

The naive null-check works for this:

```csharp
var shelf = api.ModLoader.GetModSystem<HudShelfModSystem>();
if (shelf is not null)
{
    var handle = shelf.Api.Register(/* ... */);
}
```

But there's a hidden trap: a **field** of a HudShelf type at the top
level of your mod system class fails at type-init even with the null
check. The runtime loads type metadata before any constructor runs.

The bridge pattern below isolates that risk: HudShelf-typed code lives
in one helper file, your mod's fields stay typed as `object?` only.

## The bridge

Two files. Copy them into your mod's source tree (typically under
`YourMod/Integration/`), then change the namespaces.

- **`HudShelfBridge.cs`** — public surface for your mod to call. All
  methods take/return primitives.
- **`HudShelfCall.cs`** — the inner implementation. References HudShelf
  types directly. Only ever invoked from inside `IsLoaded` checks.

Both files are reproduced under `examples/integration/`.

### Why we don't ship the bridge as a HudShelf type

- **Frozen at paste time.** When you copy the bridge, it captures
  HudShelf's API surface as it stood the day you copied it. HudShelf
  can later change internal details freely without breaking your mod.
- **No JIT risk.** A shipped bridge type would have to live in
  HudShelf's assembly, defeating the point.

## modinfo.json

In your mod's `modinfo.json`, list HudShelf as a dependency with no
version constraint:

```json
{
  "modid": "yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "side": "Client",
  "dependencies": {
    "game": "",
    "hudshelf": ""
  }
}
```

This is **soft** despite looking required: VS's mod loader uses the
declaration only for **load order**, not **presence enforcement**.

## Call site

In your `StartClientSide`:

```csharp
public override void StartClientSide(ICoreClientAPI api)
{
    base.StartClientSide(api);

    _hud = new MyHud(api);
    api.Gui.RegisterDialog(_hud);

    _shelfHandle = HudShelfBridge.TryRegister(
        api,
        id: "yourmod:main",
        element: _hud,
        defaultAnchor: "TopRight",
        defaultOffsetX: -10,
        defaultOffsetY: 10,
        onPositionChanged: (anchorStr, x, y) =>
        {
            _hud.SetPosition(anchorStr, x, y);
            _hud.Recompose();
        },
        getBounds: () => (
            _hud.SingleComposer?.Bounds.OuterWidth ?? 0,
            _hud.SingleComposer?.Bounds.OuterHeight ?? 0));

    if (HudShelfBridge.TryGetPosition(_shelfHandle) is { } p)
    {
        _hud.SetPosition(p.Anchor, p.OffsetX, p.OffsetY);
    }
    else
    {
        _hud.SetPosition("TopRight", -10, 10);
    }

    _hud.Recompose();
}

public override void Dispose()
{
    HudShelfBridge.Unregister(_shelfHandle);
    base.Dispose();
}
```

`_shelfHandle` is typed as `object?` on your mod system class.

## When NOT to use the bridge

If your mod hard-depends on HudShelf, skip the bridge and reference
`IHudShelfApi` directly.

## Persistence

Position changes are persisted automatically. Your `OnPositionChanged`
callback fires on every update; you don't need to do anything to save
the position. HudShelf manages the file
(`ModConfig/hudshelf/positions.json`) itself. If HudShelf is removed,
the data isn't deleted — reinstalling restores everything keyed by
HUD ID.

## Edit mode

HudShelf provides an edit-mode hotkey (default <kbd>Ctrl</kbd>+<kbd>F10</kbd>,
user-rebindable in the standard VS controls UI under "HudShelf:
toggle edit mode"). When edit mode is on, all registered HUDs get a
visible outline and become draggable. Releasing the mouse button
snaps the HUD to the nearest of nine anchor zones (corners, edge
midpoints, true center) and saves the new position.

You don't need to do anything to participate in edit mode beyond
registering — your `OnPositionChanged` callback just fires when the
user drops the HUD in a new spot. Toggling edit mode does not affect
your HUD's normal operation.
