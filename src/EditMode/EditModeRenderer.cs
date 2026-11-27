using HudShelf.Bounds;
using HudShelf.Drag;
using HudShelf.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace HudShelf.EditMode;

/// <summary>
/// Renders the edit-mode visual affordance: outlines around all
/// registered HUDs, a highlight on the HUD under the cursor, and the
/// drag-preview rectangle showing where the dragged HUD will land if
/// released right now.
/// </summary>
/// <remarks>
/// Stage 2 keeps this minimal per the design lock: outline rectangles,
/// no fancy gradients or animation. Future polish (snap-zone grid
/// overlay, anchor-point dots, distance lines) is v2.
/// <para/>
/// Render stage: <see cref="EnumRenderStage.Ortho"/>, render order
/// <c>1.01</c>. That's above the GuiManager (1.0), which renders all
/// registered dialogs, and below the crosshair (1.02). Rendering above
/// the GuiManager ensures the outline is never buried beneath an opaque
/// dialog background. Outside edit mode the only per-frame work is
/// the window-size check in <see cref="TrackWindowSize"/>.
/// </remarks>
internal sealed class EditModeRenderer : IRenderer
{
    /// <summary>
    /// Render order. See class remarks for the rationale.
    /// </summary>
    public double RenderOrder => 1.01;

    public int RenderRange => 1;

    private readonly ICoreClientAPI _capi;
    private readonly HudShelfApi _api;
    private readonly DragState _state;

    /// <summary>
    /// 1×1 white pixel texture used to paint solid-color rects via
    /// <see cref="IRenderAPI.Render2DTexture(int, float, float, float, float, float, Vec4f)"/>.
    /// VS doesn't ship a "draw rect" primitive; the standard idiom is
    /// to render a 1×1 texture stretched to the desired size with a
    /// color tint.
    /// </summary>
    /// <remarks>
    /// Held for the renderer's lifetime and disposed in
    /// <see cref="Dispose"/>. Dropping the <see cref="LoadedTexture"/>
    /// leaks the GL texture on every world join and makes its finalizer
    /// log "Texture ... is leaking memory".
    /// </remarks>
    private LoadedTexture? _whiteTexture;

    // Resize detection. VS has no resize event, so the frame size is
    // compared every frame (two int compares). "Seen" is the latest size,
    // "applied" the size HUDs were last clamped against.
    private int _seenW, _seenH;
    private long _seenAtMs;
    private int _appliedW, _appliedH;

    // GUI scale positions were last converted to; 0 until the first frame.
    private double _appliedScale;

    /// <summary>Frame sizes below this are ignored (minimized window).</summary>
    private const int MinReclampSize = 200;

    /// <summary>
    /// How long the size must hold before re-clamping. Live window
    /// resizing passes through many sizes; clamping against an
    /// intermediate small one would pull HUDs inward for good.
    /// </summary>
    private const long ResizeSettleMs = 500;

    // Reusable Vec4f buffers — allocated once, mutated per draw to
    // avoid per-frame allocation in the render path.
    private readonly Vec4f _outlineColor = new(0.4f, 0.7f, 1.0f, 0.7f);
    private readonly Vec4f _hoverColor   = new(0.4f, 0.9f, 1.0f, 0.9f);
    private readonly Vec4f _previewColor = new(0.2f, 1.0f, 0.4f, 0.7f);

    public EditModeRenderer(ICoreClientAPI capi, HudShelfApi api, DragState state)
    {
        _capi = capi;
        _api = api;
        _state = state;

        _whiteTexture = CreateWhitePixelTexture(capi);
    }

    public void Dispose()
    {
        _whiteTexture?.Dispose();
        _whiteTexture = null;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var screenW = _capi.Render.FrameWidth;
        var screenH = _capi.Render.FrameHeight;

        TrackGuiScale(screenW, screenH);
        TrackWindowSize(screenW, screenH);

        // Everything below is edit-mode only.
        if (_state.Phase == DragPhase.Inactive) return;
        if (_whiteTexture is null || _whiteTexture.TextureId == 0) return;

        // Outline every registered HUD so the user can see what's
        // draggable. Highlight the one being dragged or under cursor.
        var dragged = _state.DraggedHud;
        var hovered = HitTester.Find(_capi.Input.MouseX, _capi.Input.MouseY, screenW, screenH, _api.HudList);

        foreach (var hud in _api.HudList)
        {
            if (!TryGetOutlineRect(hud, hud.CurrentPosition, useComposer: true, screenW, screenH,
                    out var l, out var t, out var w, out var h))
            {
                continue;
            }

            var color = ReferenceEquals(hud, dragged) || ReferenceEquals(hud, hovered)
                ? _hoverColor
                : _outlineColor;

            // outlinePad extends the outline a few pixels outward so it
            // remains visible on very thin HUDs (e.g. a 10px-tall stat bar).
            const double outlinePad = 2.0;
            DrawRectOutline(
                l - outlinePad, t - outlinePad,
                w + outlinePad * 2, h + outlinePad * 2,
                thickness: 2, color);
        }

        // Drag preview: where the dragged HUD will land if released now.
        // Computed from the clamped snap position, not the cursor, so it
        // matches what the drop produces.
        if (_state.Phase == DragPhase.Dragging &&
            dragged is not null &&
            _state.PreviewPosition is { } preview &&
            dragged.TryGetLayoutSize(out var lw, out var lh))
        {
            var clamped = SnapMath.ClampToScreen(preview, lw, lh, screenW, screenH);
            if (TryGetOutlineRect(dragged, clamped, useComposer: false, screenW, screenH,
                    out var pl, out var pt, out var pw, out var ph))
            {
                DrawRectOutline(pl, pt, pw, ph, thickness: 3, _previewColor);
            }
        }
    }

    /// <summary>
    /// Re-clamp all HUDs once the window has settled at a new size, so a
    /// shrink can't strand a HUD off-screen. Runs whether or not edit
    /// mode is on. The first settled size is just recorded.
    /// </summary>
    private void TrackWindowSize(int screenW, int screenH)
    {
        var now = Environment.TickCount64;
        if (screenW != _seenW || screenH != _seenH)
        {
            _seenW = screenW;
            _seenH = screenH;
            _seenAtMs = now;
            return;
        }

        if (_seenW == _appliedW && _seenH == _appliedH) return;
        if (now - _seenAtMs < ResizeSettleMs) return;
        if (screenW < MinReclampSize || screenH < MinReclampSize) return;

        var first = _appliedW == 0;
        _appliedW = screenW;
        _appliedH = screenH;
        if (!first) _api.ReclampAll(screenW, screenH, notifyAll: true);
    }

    /// <summary>
    /// Convert positions when the GUI scale setting changes (a float read
    /// and compare per frame). The first frame only records the scale; the
    /// startup conversion happened when the API was created.
    /// </summary>
    private void TrackGuiScale(int screenW, int screenH)
    {
        double scale = RuntimeEnv.GUIScale;
        if (scale <= 0 || scale == _appliedScale) return;

        var old = _appliedScale;
        _appliedScale = scale;
        if (old > 0) _api.ApplyGuiScale(old, scale, screenW, screenH);
    }

    /// <summary>
    /// Screen rect of the HUD's visible content (consumer
    /// <c>GetBounds</c>), centered in its layout footprint. With
    /// <paramref name="useComposer"/> the footprint is read from the
    /// composed dialog; otherwise (or if unavailable) it is computed
    /// from <paramref name="position"/>.
    /// </summary>
    /// <remarks>
    /// Centering the content size in the footprint places the outline at
    /// the panel edge for dialogs with symmetric padding
    /// (<c>WithFixedPadding</c>), and reduces to the footprint for HUDs
    /// without padding.
    /// </remarks>
    private static bool TryGetOutlineRect(
        RegisteredHud hud, HudPosition position, bool useComposer, double screenW, double screenH,
        out double left, out double top, out double width, out double height)
    {
        left = top = 0;
        if (!hud.TryGetContentSize(out width, out height)) return false;

        double fl, ft, fw, fh;
        if (useComposer && HitTester.TryGetComposerRect(hud, out fl, out ft, out fw, out fh))
        {
            // Composed footprint available.
        }
        else
        {
            if (!hud.TryGetLayoutSize(out fw, out fh)) return false;
            (fl, ft) = SnapMath.TopLeft(position, fw, fh, screenW, screenH);
        }

        left = fl + (fw - width) / 2.0;
        top = ft + (fh - height) / 2.0;
        return true;
    }

    /// <summary>
    /// Draw a rectangular outline at the given screen coordinates.
    /// Uses the 1×1 white texture stretched to four edge strips.
    /// </summary>
    private void DrawRectOutline(double left, double top, double width, double height, float thickness, Vec4f color)
    {
        var l = (float)left;
        var t = (float)top;
        var w = (float)width;
        var h = (float)height;
        var tex = _whiteTexture!.TextureId;

        // Top edge.
        _capi.Render.Render2DTexture(tex, l, t, w, thickness, 50, color);
        // Bottom edge.
        _capi.Render.Render2DTexture(tex, l, t + h - thickness, w, thickness, 50, color);
        // Left edge (skip the top/bottom thickness already drawn to avoid double-blending corners).
        _capi.Render.Render2DTexture(tex, l, t + thickness, thickness, h - 2 * thickness, 50, color);
        // Right edge.
        _capi.Render.Render2DTexture(tex, l + w - thickness, t + thickness, thickness, h - 2 * thickness, 50, color);
    }

    /// <summary>
    /// Create a 1×1 white texture for solid-color rect rendering.
    /// VS doesn't ship a "draw rect" primitive; the standard idiom is
    /// a stretched 1×1 texture with a color tint.
    /// </summary>
    private static LoadedTexture CreateWhitePixelTexture(ICoreClientAPI capi)
    {
        // Use Cairo to create a 1×1 white surface, then upload as a
        // texture via the standard Gui.LoadOrUpdateCairoTexture path.
        using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 1, 1);
        using (var ctx = new Cairo.Context(surface))
        {
            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Paint();
        }

        var loaded = new LoadedTexture(capi);
        capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref loaded);
        return loaded;
    }
}
