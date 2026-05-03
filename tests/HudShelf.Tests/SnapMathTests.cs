using HudShelf.Bounds;
using Xunit;

namespace HudShelf.Tests;

public sealed class SnapMathTests
{
    private const double ScreenW = 1920;
    private const double ScreenH = 1080;

    // --- ClassifyCursorZone ---

    [Theory]
    // One sample point per zone, well inside the zone boundaries.
    [InlineData(100,  100, HudAnchor.TopLeft)]
    [InlineData(960,  100, HudAnchor.TopCenter)]
    [InlineData(1800, 100, HudAnchor.TopRight)]
    [InlineData(100,  540, HudAnchor.CenterLeft)]
    [InlineData(960,  540, HudAnchor.Center)]
    [InlineData(1800, 540, HudAnchor.CenterRight)]
    [InlineData(100,  1000, HudAnchor.BottomLeft)]
    [InlineData(960,  1000, HudAnchor.BottomCenter)]
    [InlineData(1800, 1000, HudAnchor.BottomRight)]
    public void ClassifyCursorZone_BasicZones(double x, double y, HudAnchor expected)
    {
        var got = SnapMath.ClassifyCursorZone(x, y, ScreenW, ScreenH);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void ClassifyCursorZone_NegativeCoords_ClampToTopLeft()
    {
        Assert.Equal(HudAnchor.TopLeft, SnapMath.ClassifyCursorZone(-100, -100, ScreenW, ScreenH));
    }

    [Fact]
    public void ClassifyCursorZone_BeyondScreenCoords_ClampToBottomRight()
    {
        Assert.Equal(HudAnchor.BottomRight, SnapMath.ClassifyCursorZone(9999, 9999, ScreenW, ScreenH));
    }

    [Fact]
    public void ClassifyCursorZone_DegenerateScreen_ReturnsTopLeft()
    {
        // 0×0 screen: any cursor lands in TopLeft (defensive default).
        Assert.Equal(HudAnchor.TopLeft, SnapMath.ClassifyCursorZone(50, 50, 0, 0));
    }

    // --- AnchorScreenPoint ---

    [Theory]
    [InlineData(HudAnchor.TopLeft,      0,    0)]
    [InlineData(HudAnchor.TopCenter,    960,  0)]
    [InlineData(HudAnchor.TopRight,     1920, 0)]
    [InlineData(HudAnchor.CenterLeft,   0,    540)]
    [InlineData(HudAnchor.Center,       960,  540)]
    [InlineData(HudAnchor.CenterRight,  1920, 540)]
    [InlineData(HudAnchor.BottomLeft,   0,    1080)]
    [InlineData(HudAnchor.BottomCenter, 960,  1080)]
    [InlineData(HudAnchor.BottomRight,  1920, 1080)]
    public void AnchorScreenPoint_AllAnchors(HudAnchor anchor, double expectedX, double expectedY)
    {
        var (x, y) = SnapMath.AnchorScreenPoint(anchor, ScreenW, ScreenH);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    // --- HudReferencePoint ---

    [Theory]
    [InlineData(HudAnchor.TopLeft,      0,   0)]
    [InlineData(HudAnchor.TopCenter,    100, 0)]
    [InlineData(HudAnchor.TopRight,     200, 0)]
    [InlineData(HudAnchor.CenterLeft,   0,   75)]
    [InlineData(HudAnchor.Center,       100, 75)]
    [InlineData(HudAnchor.CenterRight,  200, 75)]
    [InlineData(HudAnchor.BottomLeft,   0,   150)]
    [InlineData(HudAnchor.BottomCenter, 100, 150)]
    [InlineData(HudAnchor.BottomRight,  200, 150)]
    public void HudReferencePoint_200x150Hud(HudAnchor anchor, double expectedX, double expectedY)
    {
        var (x, y) = SnapMath.HudReferencePoint(anchor, 200, 150);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    // --- SnapToCursor ---

    [Fact]
    public void SnapToCursor_CenterOfTopLeftZone_HasZeroOffsetFromAnchor()
    {
        // Cursor at (100, 100) is inside the TopLeft zone (anchor at 0,0).
        // Offset = cursor - anchor = (100, 100).
        var pos = SnapMath.SnapToCursor(100, 100, hudW: 50, hudH: 30, ScreenW, ScreenH);
        Assert.Equal(HudAnchor.TopLeft, pos.Anchor);
        Assert.Equal(100, pos.OffsetX);
        Assert.Equal(100, pos.OffsetY);
    }

    [Fact]
    public void SnapToCursor_TopRightZone_HasNegativeXOffset()
    {
        // Cursor in top-right zone. Anchor at (1920, 0). Offset = cursor - anchor.
        var pos = SnapMath.SnapToCursor(1800, 50, hudW: 100, hudH: 50, ScreenW, ScreenH);
        Assert.Equal(HudAnchor.TopRight, pos.Anchor);
        Assert.Equal(1800 - 1920, pos.OffsetX); // -120
        Assert.Equal(50 - 0, pos.OffsetY);
    }

    [Fact]
    public void SnapToCursor_BottomCenterZone_HasNegativeYOffset()
    {
        // Cursor in bottom-center. Anchor at (960, 1080). Cursor (960, 1000).
        var pos = SnapMath.SnapToCursor(960, 1000, hudW: 100, hudH: 50, ScreenW, ScreenH);
        Assert.Equal(HudAnchor.BottomCenter, pos.Anchor);
        Assert.Equal(0, pos.OffsetX);
        Assert.Equal(1000 - 1080, pos.OffsetY); // -80
    }

    // --- ClampToScreen ---

    [Fact]
    public void ClampToScreen_PositionEntirelyOnScreen_Unchanged()
    {
        var pos = new HudPosition(HudAnchor.TopLeft, 100, 100);
        var clamped = SnapMath.ClampToScreen(pos, hudW: 200, hudH: 100, ScreenW, ScreenH);
        Assert.Equal(pos, clamped);
    }

    [Fact]
    public void ClampToScreen_OffScreenLeft_PullsBack()
    {
        // TopLeft anchor with offset -300 puts the HUD's left edge at -300,
        // its right edge at -300 + 200 = -100. That's fully off-screen on the
        // left. After clamping, the right edge must be at least 32px on-screen,
        // so left must be >= 32 - 200 = -168.
        var pos = new HudPosition(HudAnchor.TopLeft, -300, 100);
        var clamped = SnapMath.ClampToScreen(pos, hudW: 200, hudH: 100, ScreenW, ScreenH);

        Assert.Equal(HudAnchor.TopLeft, clamped.Anchor); // anchor preserved
        // After clamp: HUD left = -168, so OffsetX = HUD left - anchor screen X + refX = -168 - 0 + 0 = -168.
        Assert.Equal(-168, clamped.OffsetX);
        Assert.Equal(100, clamped.OffsetY); // Y wasn't off-screen, untouched.
    }

    [Fact]
    public void ClampToScreen_OffScreenRight_PullsBack()
    {
        // TopRight anchor with offset +300 puts HUD's left edge at
        // 1920 - 200 + 300 = 2020 (off the right edge). Clamp so left
        // edge <= 1920 - 32 = 1888.
        var pos = new HudPosition(HudAnchor.TopRight, 300, 50);
        var clamped = SnapMath.ClampToScreen(pos, hudW: 200, hudH: 100, ScreenW, ScreenH);

        Assert.Equal(HudAnchor.TopRight, clamped.Anchor);
        // After clamp: HUD left = 1888, anchor screen X = 1920, refX = 200.
        // OffsetX = HUD left + refX - anchorX = 1888 + 200 - 1920 = 168.
        Assert.Equal(168, clamped.OffsetX);
        Assert.Equal(50, clamped.OffsetY);
    }

    [Fact]
    public void ClampToScreen_OffScreenBottom_PullsBack()
    {
        // BottomLeft anchor with offset (0, +200) puts HUD top at
        // 1080 - 100 + 200 = 1180 (fully off bottom). Clamp so top
        // <= 1080 - 32 = 1048.
        var pos = new HudPosition(HudAnchor.BottomLeft, 0, 200);
        var clamped = SnapMath.ClampToScreen(pos, hudW: 200, hudH: 100, ScreenW, ScreenH);

        Assert.Equal(HudAnchor.BottomLeft, clamped.Anchor);
        // HUD top after clamp = 1048. anchorY = 1080, refY = 100.
        // OffsetY = HUD top + refY - anchorY = 1048 + 100 - 1080 = 68.
        Assert.Equal(0, clamped.OffsetX);
        Assert.Equal(68, clamped.OffsetY);
    }

    [Fact]
    public void ClampToScreen_AnchorIsPreserved()
    {
        // Important property: clamping never changes the anchor, only the offset.
        // The user picked this anchor (via drag); we don't second-guess it.
        var pos = new HudPosition(HudAnchor.Center, 5000, 5000);
        var clamped = SnapMath.ClampToScreen(pos, 100, 50, ScreenW, ScreenH);
        Assert.Equal(HudAnchor.Center, clamped.Anchor);
    }

    [Fact]
    public void ClampToScreen_OversizedHud_OverlapsByMinimum()
    {
        // HUD wider than the screen. Clamp should still ensure at least
        // 32px overlap on the chosen-anchor side; the opposite side will
        // hang off, that's acceptable.
        var pos = new HudPosition(HudAnchor.TopLeft, 0, 0);
        var clamped = SnapMath.ClampToScreen(pos, hudW: 3000, hudH: 100, ScreenW, ScreenH);

        // The min/max clamp range crosses (HUD wider than screen). We
        // verify the result lands somewhere reasonable — specifically,
        // that left edge is within the clamp range we'd expect.
        Assert.Equal(HudAnchor.TopLeft, clamped.Anchor);
        // HUD at (0,0) with TopLeft anchor: left edge is 0, which is
        // inside the valid range [-2968, 1888]. Clamping doesn't move it.
        Assert.Equal(0, clamped.OffsetX);
    }
}
