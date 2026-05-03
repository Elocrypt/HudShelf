using HudShelf.Internal;

namespace HudShelf.Drag;

/// <summary>
/// State of the drag interaction. Three values; transitions happen
/// only via methods on <see cref="DragController"/>.
/// </summary>
internal enum DragPhase
{
    /// <summary>Edit mode is off. No drag input handling.</summary>
    Inactive,

    /// <summary>Edit mode on, no drag in progress.</summary>
    Browsing,

    /// <summary>Edit mode on, dragging a specific HUD.</summary>
    Dragging,
}

/// <summary>
/// Tracks the current drag interaction state. Owns the in-flight
/// snap preview (anchor + offset) so the renderer can read it for
/// drawing the placeholder rectangle without driving HUD recomposes.
/// </summary>
/// <remarks>
/// Pure state holder. No input handling, no rendering — those live
/// in <see cref="DragController"/> and the renderer respectively.
/// Splitting them this way means tests can drive the state machine
/// directly without simulating mouse events.
/// </remarks>
internal sealed class DragState
{
    public DragPhase Phase { get; private set; } = DragPhase.Inactive;

    /// <summary>
    /// The HUD currently being dragged. Non-null only in
    /// <see cref="DragPhase.Dragging"/>.
    /// </summary>
    public RegisteredHud? DraggedHud { get; private set; }

    /// <summary>
    /// Live snap preview during a drag — what the position would be
    /// if the user released right now. Updated on every mouse-move
    /// while dragging. Read by the renderer to draw the placeholder.
    /// </summary>
    public HudPosition? PreviewPosition { get; private set; }

    public void EnterEditMode()
    {
        if (Phase == DragPhase.Inactive)
        {
            Phase = DragPhase.Browsing;
        }
    }

    public void ExitEditMode()
    {
        // Cancel any in-flight drag without applying the position.
        // If the user toggled edit mode while dragging, we treat that
        // as cancellation rather than commit — they didn't release the
        // mouse, so they didn't confirm the position.
        Phase = DragPhase.Inactive;
        DraggedHud = null;
        PreviewPosition = null;
    }

    public void StartDrag(RegisteredHud hud, HudPosition initialPreview)
    {
        if (Phase != DragPhase.Browsing) return;
        Phase = DragPhase.Dragging;
        DraggedHud = hud;
        PreviewPosition = initialPreview;
    }

    public void UpdatePreview(HudPosition preview)
    {
        if (Phase != DragPhase.Dragging) return;
        PreviewPosition = preview;
    }

    /// <summary>
    /// End the drag, returning the final preview position so the
    /// caller can apply it. Returns null if nothing was being dragged
    /// (idempotent: calling twice in a row does nothing).
    /// </summary>
    public (RegisteredHud Hud, HudPosition Position)? EndDrag()
    {
        if (Phase != DragPhase.Dragging) return null;
        var result = (DraggedHud!, PreviewPosition!.Value);
        Phase = DragPhase.Browsing;
        DraggedHud = null;
        PreviewPosition = null;
        return result;
    }
}
