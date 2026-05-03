# HudShelf

A drag-to-position library for Vintage Story HUDs.

HudShelf is an opt-in, client-side library mod. Mod authors take a
dependency on it to give their HUDs drag-to-position support without
re-implementing snapping, persistence, and edge clamping themselves.
Mods that don't depend on HudShelf are completely unaffected by it.

> **Status: pre-1.0.** API may shift between minor versions. See
> `CHANGELOG.md` for what changes between releases. After 1.0,
> HudShelf follows strict semver - breaking changes only at major
> versions.

## What HudShelf does

- A registration API mod authors call to opt their HUD in.
- An edit mode triggered by a hotkey
  (default <kbd>Ctrl</kbd>+<kbd>F8</kbd>, user-rebindable),
  in which registered HUDs become draggable.
- Snap-to-anchor positioning at nine reference points (corners, edge
  midpoints, true center) with residual pixel offset.
- Edge clamping so HUDs can't be lost off-screen (32px minimum
  overlap on each axis).
- Per-HUD position persistence across game sessions, keyed by stable
  HUD ID. File: `ModConfig/hudshelf/positions.json`.

## What HudShelf does not do

- It does not position HUDs that haven't registered. No automatic
  capture of arbitrary dialogs.
- It does not solve overlap between HUDs from different mods.
- It does not provide alignment guides between HUDs.
- It does not resize HUDs or position dialog-internal elements.
- It does not run on the server.

## For mod authors

The integration story has two paths:

1. **Soft-depend (recommended)**: your mod still loads when HudShelf
   isn't installed, falling back to your own positioning. See
   [`docs/BRIDGE.md`](docs/BRIDGE.md). Copy-paste the files in
   [`examples/integration/`](examples/integration/) into your mod.

2. **Hard-depend**: your mod requires HudShelf. Reference
   `IHudShelfApi` directly; no bridge needed.

In both cases, the typical call site looks like:

```csharp
var shelf = api.ModLoader.GetModSystem<HudShelfModSystem>();
var handle = shelf.Api.Register(new HudRegistration
{
    Id            = "yourmod:main",
    Element       = yourHud,
    DefaultAnchor = HudAnchor.TopRight,
    DefaultOffsetX = -10,
    DefaultOffsetY = 10,
    GetBounds     = () => (yourHud.SingleComposer?.Bounds.OuterWidth ?? 0,
                           yourHud.SingleComposer?.Bounds.OuterHeight ?? 0),
    OnPositionChanged = p =>
    {
        // Apply via the helper extension:
        p.ApplyTo(yourHud.MainBounds);
        yourHud.Recompose();
    },
});

// Apply the initial resolved position once.
handle.CurrentPosition.ApplyTo(yourHud.MainBounds);
yourHud.Recompose();
```

The `HudPosition.ApplyTo` extension (and the underlying
`HudAnchor.ToDialogArea`) hide the VS-internal `EnumDialogArea`
mapping and any sign-convention details.

## End-user usage

Press <kbd>Ctrl</kbd>+<kbd>F8</kbd> (or your rebound key) to enter
edit mode. Registered HUDs get a blue outline. Click and drag any
HUD to reposition it; release to snap to the nearest of nine anchor
zones. Press the hotkey again to exit edit mode.

Positions persist across sessions. Window resizing automatically
re-clamps positions to fit the new screen.

<!-- COMPATIBILITY (hand-maintained - do not auto-overwrite) -->
## Compatibility

- Vintage Story 1.22.*
- .NET 10
- Client-side only

<!-- /COMPATIBILITY -->

## Building from source

1. Set the `VINTAGE_STORY` environment variable to your VS install dir
   (the directory containing `VintagestoryAPI.dll`):
   - Windows: `%APPDATA%\Vintagestory`
   - Linux: `~/.local/share/Vintagestory`
2. Close Vintage Story (it locks PDBs while running).
3. `dotnet build -c Release` from the repo root.
4. The output is `bin/Release/HudShelf.dll` plus `modinfo.json`.

## Running tests

```
dotnet test HudShelf.sln
```

Tests live in `tests/HudShelf.Tests/` and cover the persistence
round-trip, snap math (zone classification, anchor points, snap-to-
cursor, edge clamp), and drag-state-machine transitions. They're
VS-DLL-free, so they run without a Vintage Story install on the test
runner.

<!-- CREDITS (hand-maintained - do not auto-overwrite) -->
## Credits

Written by Elocrypt. MIT licensed; see `LICENSE`.

<!-- /CREDITS -->
