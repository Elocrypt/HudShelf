using HudShelf.Bounds;
using HudShelf.Drag;
using HudShelf.Internal;
using Vintagestory.API.Client;
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
/// dialog background. The early-exit at the top of
/// <see cref="OnRenderFrame"/> keeps cost zero when edit mode is off.
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
    private int _whiteTextureId;

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

        _whiteTextureId = CreateWhitePixelTexture(capi);
    }

    public void Dispose()
    {
        // VS owns texture lifetime via the Render API; releasing
        // explicitly isn't required and there's no public Delete API
        // for textures created via the surface route. Drop the ref.
        _whiteTextureId = 0;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        // Zero-cost short-circuit when edit mode is off — this
        // renderer is registered permanently but does nothing in
        // Inactive state.
        if (_state.Phase == DragPhase.Inactive) return;
        if (_whiteTextureId == 0) return;

        var screenW = _capi.Render.FrameWidth;
        var screenH = _capi.Render.FrameHeight;

        // Outline every registered HUD so the user can see what's
        // draggable. Highlight the one being dragged or under cursor.
        var draggedId = _state.DraggedHud?.Id;
        var (mouseX, mouseY) = (_capi.Input.MouseX, _capi.Input.MouseY);
        var hovered = HitTester.Find(mouseX, mouseY, screenW, screenH, _api.RegisteredHuds);
        var hoveredId = hovered?.Id;

        foreach (var hud in _api.RegisteredHuds.Values)
        {
            // Ask the consumer what their visible content size is. This is
            // the authoritative size for the outline — consumers subtract
            // dialog padding so the outline hugs the panel, not the full
            // dialog footprint.
            double bw, bh;
            try { (bw, bh) = hud.Registration.GetBounds(); }
            catch { continue; }

            double l, t, w, h;

            if (bw > 0 && bh > 0 &&
                HitTester.TryGetHudRect(hud, screenW, screenH,
                    out var cl, out var ct, out var cw, out var ch))
            {
                // Composer footprint is available. Use GetBounds() for size
                // and center it within the footprint. For dialogs with
                // symmetric padding (WithFixedPadding), (cw - bw) / 2 equals
                // the padding in screen pixels, placing the outline exactly at
                // the visible panel edge. For HUDs with no padding the formula
                // reduces to (cl, ct), so nothing changes for them.
                w = bw;
                h = bh;
                l = cl + (cw - bw) / 2.0;
                t = ct + (ch - bh) / 2.0;
            }
            else if (bw > 0 && bh > 0)
            {
                // Composer bounds unavailable — derive position from the
                // stored snap position instead.
                var pos = hud.CurrentPosition;
                var (anchorX, anchorY) = SnapMath.AnchorScreenPoint(pos.Anchor, screenW, screenH);
                var (refX, refY) = SnapMath.HudReferencePoint(pos.Anchor, bw, bh);
                l = anchorX + pos.OffsetX - refX;
                t = anchorY + pos.OffsetY - refY;
                w = bw;
                h = bh;
            }
            else
            {
                continue;
            }

            var color = hud.Id == draggedId || hud.Id == hoveredId
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

        // Drag preview: show where the dragged HUD would land if
        // released right now. Draws as a filled-translucent rect plus
        // a stronger outline.
        if (_state.Phase == DragPhase.Dragging &&
            _state.DraggedHud is { } draggedHud &&
            _state.PreviewPosition is { } preview)
        {
            // Compute preview rect from the snap-target position, not
            // the cursor. ClampToScreen gives us the same final
            // position the drop will produce, so the preview matches
            // reality.
            double w, h;
            try { (w, h) = draggedHud.Registration.GetBounds(); }
            catch { return; }

            if (w <= 0 || h <= 0) return;

            var clamped = SnapMath.ClampToScreen(preview, w, h, screenW, screenH);
            var (anchorX, anchorY) = SnapMath.AnchorScreenPoint(clamped.Anchor, screenW, screenH);
            var (refX, refY) = SnapMath.HudReferencePoint(clamped.Anchor, w, h);
            var previewLeft = anchorX + clamped.OffsetX - refX;
            var previewTop = anchorY + clamped.OffsetY - refY;

            DrawRectOutline(previewLeft, previewTop, w, h, thickness: 3, _previewColor);
        }
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

        // Top edge.
        _capi.Render.Render2DTexture(_whiteTextureId, l, t, w, thickness, 50, color);
        // Bottom edge.
        _capi.Render.Render2DTexture(_whiteTextureId, l, t + h - thickness, w, thickness, 50, color);
        // Left edge (skip the top/bottom thickness already drawn to avoid double-blending corners).
        _capi.Render.Render2DTexture(_whiteTextureId, l, t + thickness, thickness, h - 2 * thickness, 50, color);
        // Right edge.
        _capi.Render.Render2DTexture(_whiteTextureId, l + w - thickness, t + thickness, thickness, h - 2 * thickness, 50, color);
    }

    /// <summary>
    /// Create a 1×1 white texture for solid-color rect rendering.
    /// VS doesn't ship a "draw rect" primitive; the standard idiom is
    /// a stretched 1×1 texture with a color tint.
    /// </summary>
    private static int CreateWhitePixelTexture(ICoreClientAPI capi)
    {
        // Use Cairo to create a 1×1 white surface, then upload as a
        // texture via the standard Gui.LoadOrCreateTexture path. The
        // texture lives until VS shuts down; for a single 1×1 pixel
        // that's fine.
        using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 1, 1);
        using (var ctx = new Cairo.Context(surface))
        {
            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Paint();
        }

        var loaded = new LoadedTexture(capi);
        capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref loaded);
        return loaded.TextureId;
    }
}
