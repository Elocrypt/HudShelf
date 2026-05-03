using HudShelf.Core;
using HudShelf.Drag;
using HudShelf.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace HudShelf.EditMode;

/// <summary>
/// Owns the edit-mode hotkey and the toggle logic. Coordinates the
/// state machine in <see cref="DragState"/> and the
/// <see cref="HudShelfApi.SetEditModeActive"/> path so the public
/// <c>EditModeChanged</c> event fires.
/// </summary>
/// <remarks>
/// Hotkey: <c>hudshelf:editmode</c>, default <c>Ctrl+F8</c>.
/// User-rebindable in the standard VS controls UI; we register
/// through <c>capi.Input.RegisterHotKey</c> rather than capturing
/// keys directly. F-keys are mostly free in vanilla VS; <c>Ctrl</c>
/// modifier reduces accidental triggering.
/// </remarks>
internal sealed class EditModeController
{
    /// <summary>
    /// Hotkey code registered with VS's input API. Stable identifier;
    /// the user-visible label and key combo can be rebound but this
    /// code is what HudShelf uses to look up the binding.
    /// </summary>
    internal const string HotkeyCode = "hudshelf:editmode";

    private readonly ICoreClientAPI _capi;
    private readonly HudShelfApi _api;
    private readonly DragState _state;

    public EditModeController(ICoreClientAPI capi, HudShelfApi api, DragState state)
    {
        _capi = capi;
        _api = api;
        _state = state;

        // Default: F8 with Ctrl modifier. Users rebind via the
        // standard VS controls UI.
        _capi.Input.RegisterHotKey(
            HotkeyCode,
            "HudShelf: toggle edit mode",
            GlKeys.F8,
            HotkeyType.GUIOrOtherControls,
            ctrlPressed: true,
            altPressed: false,
            shiftPressed: false);

        _capi.Input.SetHotKeyHandler(HotkeyCode, OnHotkeyPressed);
    }

    /// <summary>
    /// Detach hotkey handler. Called from the mod system's Dispose
    /// path. We don't unregister the hotkey itself — VS doesn't
    /// expose an Unregister API and re-registering at next mod load
    /// is harmless.
    /// </summary>
    public void Dispose()
    {
        // Set a no-op handler so any latent hotkey press during
        // shutdown doesn't reach our (about to be torn down) state.
        _capi.Input.SetHotKeyHandler(HotkeyCode, _ => false);
    }

    private bool OnHotkeyPressed(KeyCombination _)
    {
        Toggle();
        return true; // Consumed; don't let VS re-handle the press.
    }

    /// <summary>
    /// Toggle edit mode. Public-internal so a future
    /// non-hotkey trigger (config menu button, etc.) can drive it.
    /// </summary>
    internal void Toggle()
    {
        if (_state.Phase == DragPhase.Inactive)
        {
            EnterEditMode();
        }
        else
        {
            ExitEditMode();
        }
    }

    private void EnterEditMode()
    {
        _state.EnterEditMode();
        _api.SetEditModeActive(true);

        // On entry, re-clamp every registered HUD against the current
        // screen size. This handles the case where the window was
        // resized smaller since a position was persisted, and the
        // persisted position now would land off-screen. Clamping on
        // entry (rather than on every Register call) avoids work for
        // mods loaded before the player has resized.
        var (w, h) = (_capi.Render.FrameWidth, _capi.Render.FrameHeight);
        foreach (var hud in _api.RegisteredHuds.Values)
        {
            hud.SetPositionClamped(hud.CurrentPosition, w, h);
        }

        HudShelfLog.Notification("Edit mode ON.");
    }

    private void ExitEditMode()
    {
        _state.ExitEditMode();
        _api.SetEditModeActive(false);

        HudShelfLog.Notification("Edit mode OFF.");
    }
}
