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
    private readonly DragController _drag;

    // True between the hotkey's press and the matching key release.
    // OS key repeat re-fires the hotkey handler while the key is held,
    // which would toggle edit mode on and off repeatedly.
    private bool _hotkeyHeld;

    public EditModeController(ICoreClientAPI capi, HudShelfApi api, DragState state, DragController drag)
    {
        _capi = capi;
        _api = api;
        _state = state;
        _drag = drag;

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
        _capi.Event.KeyUp += OnKeyUp;
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
        _capi.Event.KeyUp -= OnKeyUp;
    }

    private bool OnHotkeyPressed(KeyCombination _)
    {
        if (_hotkeyHeld) return true;
        _hotkeyHeld = true;

        Toggle();
        return true; // Consumed; don't let VS re-handle the press.
    }

    private void OnKeyUp(KeyEvent e)
    {
        if (!_hotkeyHeld) return;

        // Release on the bound key. If the mapping can't be read, any
        // key release clears the latch so the hotkey can't get stuck.
        var boundKey = _capi.Input.GetHotKeyByCode(HotkeyCode)?.CurrentMapping?.KeyCode;
        if (boundKey is null || e.KeyCode == boundKey) _hotkeyHeld = false;
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
        // screen size, in case a position was saved at a larger
        // resolution. Window resizes while playing are handled by
        // EditModeRenderer's frame-size check.
        _api.ReclampAll(_capi.Render.FrameWidth, _capi.Render.FrameHeight);

        HudShelfLog.Notification("Edit mode ON.");
    }

    private void ExitEditMode()
    {
        _state.ExitEditMode();
        _drag.ClearSelection();
        _api.PositionStore.Flush();
        _api.SetEditModeActive(false);

        HudShelfLog.Notification("Edit mode OFF.");
    }
}
