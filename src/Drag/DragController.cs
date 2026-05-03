using HudShelf.Bounds;
using HudShelf.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace HudShelf.Drag;

/// <summary>
/// Handles mouse input during edit mode. Subscribes to the low-level
/// <c>capi.Event.MouseDown/Move/Up</c> events; consumes them
/// (sets <c>e.Handled = true</c>) only while in edit mode and only
/// when the cursor is over a registered HUD or a drag is in progress.
/// </summary>
/// <remarks>
/// Why low-level handlers and not a GuiDialog overlay: VS routes
/// input through <c>capi.Event.MouseDown</c> first, then through the
/// GuiManager which dispatches to dialogs. Setting <c>e.Handled</c>
/// from the low-level handler suppresses the GuiManager step, which
/// is exactly the behavior we want — clicks during edit mode go to
/// HudShelf, not to the consumer's HUD or to other dialogs.
/// <para/>
/// When edit mode is off, the handlers return immediately without
/// touching <c>e.Handled</c>; events flow normally.
/// </remarks>
internal sealed class DragController
{
    private readonly ICoreClientAPI _capi;
    private readonly HudShelfApi _api;
    private readonly DragState _state;

    public DragController(ICoreClientAPI capi, HudShelfApi api, DragState state)
    {
        _capi = capi;
        _api = api;
        _state = state;

        _capi.Event.MouseDown += OnMouseDown;
        _capi.Event.MouseMove += OnMouseMove;
        _capi.Event.MouseUp += OnMouseUp;
    }

    /// <summary>
    /// Detach event subscriptions. Called from the mod system's
    /// Dispose path.
    /// </summary>
    public void Dispose()
    {
        _capi.Event.MouseDown -= OnMouseDown;
        _capi.Event.MouseMove -= OnMouseMove;
        _capi.Event.MouseUp -= OnMouseUp;
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (_state.Phase != DragPhase.Browsing) return;

        // Only left-click starts a drag. Right-click and other buttons
        // should pass through normally — a future refinement might use
        // right-click for "reset to default" but v1 keeps it simple.
        if (e.Button != EnumMouseButton.Left) return;

        var (screenW, screenH) = ScreenSize();
        var hit = HitTester.Find(e.X, e.Y, screenW, screenH, _api.RegisteredHuds);
        if (hit is null) return;

        var w = SafeBoundsWidth(hit);
        var h = SafeBoundsHeight(hit);
        var initialPreview = SnapMath.SnapToCursor(e.X, e.Y, w, h, screenW, screenH);

        _state.StartDrag(hit, initialPreview);

        // Consume the click so it doesn't fall through to the consumer's
        // HUD (which might have its own mouse handlers).
        e.Handled = true;
    }

    private void OnMouseMove(MouseEvent e)
    {
        // In Browsing we don't consume mouse-move events — the user
        // might still be interacting with other dialogs (e.g. opening
        // a context menu while edit mode is on). Only Dragging consumes.
        if (_state.Phase != DragPhase.Dragging) return;
        if (_state.DraggedHud is null) return;

        var (screenW, screenH) = ScreenSize();
        var hud = _state.DraggedHud;
        var w = SafeBoundsWidth(hud);
        var h = SafeBoundsHeight(hud);

        var preview = SnapMath.SnapToCursor(e.X, e.Y, w, h, screenW, screenH);
        _state.UpdatePreview(preview);

        e.Handled = true;
    }

    private void OnMouseUp(MouseEvent e)
    {
        if (_state.Phase != DragPhase.Dragging) return;
        if (e.Button != EnumMouseButton.Left) return;

        var ended = _state.EndDrag();
        if (ended is null) return;

        var (hud, candidate) = ended.Value;
        var (screenW, screenH) = ScreenSize();

        // Apply with clamping. SetPositionClamped fires the consumer
        // callback and persists. The preview rectangle the user saw
        // and the final HUD position will match (both go through the
        // same clamp), unless the HUD has somehow changed size between
        // mouse-down and mouse-up — in which case the clamped final
        // position is still correct for the actual HUD size.
        hud.SetPositionClamped(candidate, screenW, screenH);

        e.Handled = true;
    }

    private (double W, double H) ScreenSize() =>
        (_capi.Render.FrameWidth, _capi.Render.FrameHeight);

    private static double SafeBoundsWidth(RegisteredHud hud)
    {
        try { return hud.Registration.GetBounds().Width; }
        catch { return 0; }
    }

    private static double SafeBoundsHeight(RegisteredHud hud)
    {
        try { return hud.Registration.GetBounds().Height; }
        catch { return 0; }
    }
}
