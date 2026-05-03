using HudShelf.Drag;
using Xunit;

namespace HudShelf.Tests;

/// <summary>
/// Tests for <see cref="DragState"/>. Validates state transitions
/// without involving any input simulation — the controller does the
/// input-to-state mapping; this test exercises the state machine
/// directly.
/// </summary>
public sealed class DragStateTests
{
    [Fact]
    public void InitialPhase_IsInactive()
    {
        var s = new DragState();
        Assert.Equal(DragPhase.Inactive, s.Phase);
        Assert.Null(s.DraggedHud);
        Assert.Null(s.PreviewPosition);
    }

    [Fact]
    public void EnterEditMode_FromInactive_GoesToBrowsing()
    {
        var s = new DragState();
        s.EnterEditMode();
        Assert.Equal(DragPhase.Browsing, s.Phase);
    }

    [Fact]
    public void EnterEditMode_WhileBrowsing_StaysBrowsing()
    {
        var s = new DragState();
        s.EnterEditMode();
        s.EnterEditMode(); // idempotent
        Assert.Equal(DragPhase.Browsing, s.Phase);
    }

    [Fact]
    public void ExitEditMode_FromBrowsing_GoesToInactive()
    {
        var s = new DragState();
        s.EnterEditMode();
        s.ExitEditMode();
        Assert.Equal(DragPhase.Inactive, s.Phase);
    }

    [Fact]
    public void ExitEditMode_WhileDragging_CancelsDragAndGoesToInactive()
    {
        // Cancellation semantics: toggling edit mode mid-drag does NOT
        // commit the in-flight position. The user has to release the
        // mouse to commit.
        var s = new DragState();
        s.EnterEditMode();
        s.StartDrag(null!, new HudPosition(HudAnchor.TopLeft, 0, 0));
        Assert.Equal(DragPhase.Dragging, s.Phase);

        s.ExitEditMode();
        Assert.Equal(DragPhase.Inactive, s.Phase);
        Assert.Null(s.DraggedHud);
        Assert.Null(s.PreviewPosition);
    }

    [Fact]
    public void StartDrag_OutsideBrowsing_DoesNothing()
    {
        var s = new DragState();
        // Inactive → StartDrag must be ignored.
        s.StartDrag(null!, new HudPosition(HudAnchor.TopLeft, 0, 0));
        Assert.Equal(DragPhase.Inactive, s.Phase);
    }

    [Fact]
    public void UpdatePreview_OnlyAffectsDraggingPhase()
    {
        var s = new DragState();
        s.UpdatePreview(new HudPosition(HudAnchor.Center, 5, 5));
        Assert.Null(s.PreviewPosition);

        s.EnterEditMode();
        s.UpdatePreview(new HudPosition(HudAnchor.Center, 5, 5));
        Assert.Null(s.PreviewPosition);

        s.StartDrag(null!, new HudPosition(HudAnchor.TopLeft, 1, 1));
        s.UpdatePreview(new HudPosition(HudAnchor.Center, 5, 5));
        Assert.Equal(new HudPosition(HudAnchor.Center, 5, 5), s.PreviewPosition);
    }

    [Fact]
    public void EndDrag_ReturnsPreviewAndGoesToBrowsing()
    {
        var s = new DragState();
        s.EnterEditMode();
        s.StartDrag(null!, new HudPosition(HudAnchor.TopLeft, 1, 1));
        s.UpdatePreview(new HudPosition(HudAnchor.BottomRight, -10, -20));

        var result = s.EndDrag();
        Assert.NotNull(result);
        Assert.Equal(new HudPosition(HudAnchor.BottomRight, -10, -20), result!.Value.Position);
        Assert.Equal(DragPhase.Browsing, s.Phase);
        Assert.Null(s.DraggedHud);
        Assert.Null(s.PreviewPosition);
    }

    [Fact]
    public void EndDrag_OutsideDragging_ReturnsNull()
    {
        var s = new DragState();
        Assert.Null(s.EndDrag());

        s.EnterEditMode();
        Assert.Null(s.EndDrag()); // browsing, no drag in flight
    }
}
