using HudShelf.Bounds;
using HudShelf.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace HudShelf.Drag;

/// <summary>
/// Handles input during edit mode: left-drag to move a HUD,
/// right-click to reset it to its registered default, arrow keys to
/// nudge it. Subscribes to the low-level <c>capi.Event</c> mouse and
/// key events and consumes them (sets <c>Handled</c>) only while in
/// edit mode and only when they act on a HUD.
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
/// touching <c>Handled</c>; events flow normally.
/// </remarks>
internal sealed class DragController
{
    /// <summary>Arrow-key nudge step in screen pixels.</summary>
    internal const double NudgeStep = 1;

    /// <summary>Arrow-key nudge step with Shift held.</summary>
    internal const double NudgeStepLarge = 10;

    /// <summary>
    /// Delay before nudged positions are written to disk. Holding an
    /// arrow key repeats KeyDown many times a second; this collapses
    /// the burst into one write.
    /// </summary>
    private const int NudgeSaveDelayMs = 750;

    private readonly ICoreClientAPI _capi;
    private readonly HudShelfApi _api;
    private readonly DragState _state;

    // Where on the HUD the user clicked, relative to its top-left, at
    // the moment the drag started. Kept here rather than in DragState
    // because they're input-processing detail — the state machine only
    // needs to know what HUD is being dragged and what the preview is.
    private double _grabOffsetX;
    private double _grabOffsetY;

    // Last HUD the user dragged or reset. Arrow keys act on the HUD
    // under the cursor, falling back to this one so the user can nudge
    // without keeping the mouse parked on a tiny HUD.
    private RegisteredHud? _lastTouched;

    private long _saveCallbackId;

    public DragController(ICoreClientAPI capi, HudShelfApi api, DragState state)
    {
        _capi = capi;
        _api = api;
        _state = state;

        _capi.Event.MouseDown += OnMouseDown;
        _capi.Event.MouseMove += OnMouseMove;
        _capi.Event.MouseUp += OnMouseUp;
        _capi.Event.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Detach event subscriptions and write any pending nudges. Called
    /// from the mod system's Dispose path.
    /// </summary>
    public void Dispose()
    {
        _capi.Event.MouseDown -= OnMouseDown;
        _capi.Event.MouseMove -= OnMouseMove;
        _capi.Event.MouseUp -= OnMouseUp;
        _capi.Event.KeyDown -= OnKeyDown;

        CancelPendingSave();
        _api.PositionStore.Flush();
        _lastTouched = null;
    }

    /// <summary>
    /// Forget the last-touched HUD. Called on edit-mode exit so a stale
    /// selection can't receive nudges in the next session.
    /// </summary>
    internal void ClearSelection() => _lastTouched = null;

    private void OnMouseDown(MouseEvent e)
    {
        if (_state.Phase != DragPhase.Browsing) return;

        if (e.Button == EnumMouseButton.Right)
        {
            HandleResetClick(e);
            return;
        }

        // Only left-click starts a drag; other buttons pass through.
        if (e.Button != EnumMouseButton.Left) return;

        var (screenW, screenH) = ScreenSize();
        var hit = HitTester.Find(e.X, e.Y, screenW, screenH, _api.HudList);
        if (hit is null) return;

        // Record where within the HUD the user clicked so the HUD
        // follows the cursor without jumping. Fall back to (0,0) if
        // the composer rect is unavailable (not yet composed).
        if (HitTester.TryGetHudRect(hit, screenW, screenH, out var hudLeft, out var hudTop, out _, out _))
        {
            _grabOffsetX = e.X - hudLeft;
            _grabOffsetY = e.Y - hudTop;
        }
        else
        {
            _grabOffsetX = 0;
            _grabOffsetY = 0;
        }

        hit.TryGetLayoutSize(out var w, out var h);
        var initialPreview = SnapMath.SnapToCursor(
            e.X, e.Y, w, h, screenW, screenH, _grabOffsetX, _grabOffsetY);

        _state.StartDrag(hit, initialPreview);
        _lastTouched = hit;

        // Consume the click so it doesn't fall through to the consumer's
        // HUD (which might have its own mouse handlers).
        e.Handled = true;
    }

    private void HandleResetClick(MouseEvent e)
    {
        var (screenW, screenH) = ScreenSize();
        var hit = HitTester.Find(e.X, e.Y, screenW, screenH, _api.HudList);
        if (hit is null) return;

        hit.SetPositionClamped(hit.DefaultPosition, screenW, screenH);
        _lastTouched = hit;

        e.Handled = true;
    }

    private void OnMouseMove(MouseEvent e)
    {
        // In Browsing we don't consume mouse-move events — the user
        // might still be interacting with other dialogs while edit mode
        // is on. Only Dragging consumes.
        if (_state.Phase != DragPhase.Dragging) return;
        if (_state.DraggedHud is not { } hud) return;

        var (screenW, screenH) = ScreenSize();
        hud.TryGetLayoutSize(out var w, out var h);

        var preview = SnapMath.SnapToCursor(
            e.X, e.Y, w, h, screenW, screenH, _grabOffsetX, _grabOffsetY);
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

        // The preview went through the same clamp, so the final
        // position matches what the user saw unless the HUD changed
        // size mid-drag; then the clamp is still right for the new size.
        hud.SetPositionClamped(candidate, screenW, screenH);

        e.Handled = true;
    }

    private void OnKeyDown(KeyEvent e)
    {
        if (_state.Phase != DragPhase.Browsing) return;

        double dx = 0, dy = 0;
        var step = e.ShiftPressed ? NudgeStepLarge : NudgeStep;
        switch (e.KeyCode)
        {
            case (int)GlKeys.Left: dx = -step; break;
            case (int)GlKeys.Right: dx = step; break;
            case (int)GlKeys.Up: dy = -step; break;
            case (int)GlKeys.Down: dy = step; break;
            default: return;
        }

        var (screenW, screenH) = ScreenSize();
        var target = HitTester.Find(_capi.Input.MouseX, _capi.Input.MouseY, screenW, screenH, _api.HudList);
        if (target is null && _lastTouched is { IsUnregistered: false } last) target = last;
        if (target is null) return;

        target.SetPositionClamped(
            SnapMath.Nudge(target.CurrentPosition, dx, dy), screenW, screenH, saveNow: false);
        _lastTouched = target;
        ScheduleSave();

        e.Handled = true;
    }

    private void ScheduleSave()
    {
        CancelPendingSave();
        _saveCallbackId = _capi.Event.RegisterCallback(_ =>
        {
            _saveCallbackId = 0;
            _api.PositionStore.Flush();
        }, NudgeSaveDelayMs, permittedWhilePaused: true);
    }

    private void CancelPendingSave()
    {
        if (_saveCallbackId == 0) return;
        _capi.Event.UnregisterCallback(_saveCallbackId);
        _saveCallbackId = 0;
    }

    private (double W, double H) ScreenSize() =>
        (_capi.Render.FrameWidth, _capi.Render.FrameHeight);
}
