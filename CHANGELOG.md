# Changelog

All notable changes to HudShelf will be documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
HudShelf follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
**after 1.0**. Pre-1.0 minor versions may include breaking API changes;
those are called out below when they happen.

## [Unreleased]

(Nothing parked.)

## [0.3.1] — 2026/05/10

Edit-mode outline fixes found during first-consumer integration
(Hydrate or Diedrate and HUD Clock).

### Fixed
- **Outline render order** raised from `0.99` to `1.01`. VS's
  `GuiManager` renders registered dialogs at `1.0`, so the previous
  order buried outlines beneath any dialog with an opaque background.
  The outline now reliably renders on top of all HUDs; the early-exit
  when edit mode is off keeps cost zero outside edit mode.
- **Outline dimensions** now derived from `GetBounds()` centered
  within the composer footprint rather than taken directly from
  `SingleComposer.Bounds.OuterWidth/Height`. For dialogs built with
  `WithFixedPadding`, `OuterWidth/Height` includes the padding on
  both sides, which pushed the outline visibly outside the panel
  edge. The new formula `left = absX + (OuterWidth − bw) / 2`
  places the outline at the panel boundary for any dialog with
  symmetric padding without requiring HudShelf to know the padding
  value.
- **Thin-HUD outline** now has 2 px of outward padding so it remains
  visible on very short elements such as stat bars (10 px `fixedHeight`
  composes to ~15 px at 1.5× GUI scale — a 2 px border on 15 px
  was nearly invisible without the pad).
- **Composer-unavailable fallback** in the outline loop now derives
  position from `CurrentPosition` + `GetBounds()` via SnapMath when
  `TryGetHudRect` fails, so a HUD whose `SingleComposer` is null or
  whose root element is a wrapper still gets an outline.

## [0.3.0] — 2026/05/03

Stage 2: drag, edit mode, snap-to-anchor, edge clamping. With this
release HudShelf delivers its core promise — a registered HUD can be
dragged and dropped to any of nine snap zones, and the position
persists across sessions.

### Added
- **Edit mode**, toggled by user-configurable hotkey
  (default <kbd>Ctrl</kbd>+<kbd>F8</kbd>, registered as
  `hudshelf:editmode`). When edit mode is on, registered HUDs are
  outlined and draggable; when off, HudShelf is invisible and
  inactive.
- **Drag-and-snap**: clicking a registered HUD starts a drag; cursor
  position determines which of nine snap zones the HUD will land in
  (screen divided into thirds × thirds); release commits.
- **Edge clamping**: HUDs can't be dropped fully off-screen. Minimum
  overlap is 32px in each axis. Clamping also re-runs on edit-mode
  entry, so a position persisted at higher resolution still lands
  on-screen if the user has since shrunk the window.
- **Drag preview**: a green outline shows where the HUD will land if
  released right now. The preview snaps to the chosen zone in real
  time, so the user can see anchor selection before committing.
- **`IHudShelfApi.IsEditModeActive`** and **`EditModeChanged`** event
  now functional (stage 1/3 stubbed them).
- **`HudShelfExtensions`** static class (the seventh public type):
  `HudAnchor.ToDialogArea()` and `HudPosition.ApplyTo(ElementBounds)`
  helpers. The latter is the recommended apply path; both hide
  VS-internal `EnumDialogArea` mapping and sign-convention details
  from consumers.
- **Tests** for snap math (zone classification, anchor screen points,
  HUD reference points, snap-to-cursor, edge clamp) and for the drag
  state machine (transitions, idempotency, mid-drag cancellation
  semantics).

### Changed
- `RegisteredHud.SetPosition` now persists before firing the consumer
  callback. Stage 3 already did this; documenting it here because
  drag-completion is the first production caller.

### Notes
- Pre-1.0 release. The public API surface is now seven types; v1.0
  freezes those.
- `HudShelfExtensions` was parked in 0.2.0's Unreleased section and
  ships in this version.

## [0.2.0] — 2026/05/02

### Added
- Per-install position persistence at
  `ModConfig/hudshelf/positions.json`. JSON file with a versioned
  schema (`version: 1`); atomic writes; silent fallback to registered
  defaults on missing/corrupt/unknown-version files.
- `Register` now checks the persistence store first, falls back to
  registration defaults if nothing is saved.
- Test project covering the persistence round-trip and failure modes.

### Changed
- `HudShelfLog` no longer references VS types directly; uses
  `Action<string>` callbacks for testability of internals like
  `PositionStore`.
- `HudShelf.csproj` now leans on `Directory.Build.props` for shared
  language settings.

## [0.1.0] — 2026/05/02

Walking skeleton: registration and position resolution only. No drag,
no edit mode, no persistence.

### Added
- Public API: `HudShelfModSystem`, `IHudShelfApi`, `IRegisteredHud`,
  `HudRegistration`, `HudPosition`, `HudAnchor`.
- Soft-depend bridge pattern documented in `docs/BRIDGE.md` with
  copy-paste files in `examples/integration/`.
