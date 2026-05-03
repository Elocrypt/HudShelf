using HudShelf.Core;
using HudShelf.Drag;
using HudShelf.EditMode;
using HudShelf.Internal;
using HudShelf.Persistence;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace HudShelf;

/// <summary>
/// HudShelf's <see cref="ModSystem"/> entry point. Acquire the API via
/// <c>api.ModLoader.GetModSystem&lt;HudShelfModSystem&gt;().Api</c>.
/// </summary>
/// <remarks>
/// Client-side only: this mod system loads on the client and is a
/// no-op on the server.
/// </remarks>
public sealed class HudShelfModSystem : ModSystem
{
    private const string DataSubdirectory = "ModConfig/hudshelf";
    private const string PositionsFilename = "positions.json";

    private ICoreClientAPI? _capi;
    private HudShelfApi? _api;
    private DragState? _dragState;
    private DragController? _dragController;
    private EditModeController? _editModeController;
    private EditModeRenderer? _editModeRenderer;

    /// <summary>
    /// The HudShelf API. Null on the server side and before
    /// <see cref="StartClientSide"/> has run; non-null on the client
    /// from <c>StartClientSide</c> onward.
    /// </summary>
    public IHudShelfApi? Api => _api;

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI capi)
    {
        base.StartClientSide(capi);

        _capi = capi;

        HudShelfLog.Init(
            notification: msg => capi.Logger.Notification(msg),
            warning: msg => capi.Logger.Warning(msg),
            error: msg => capi.Logger.Error(msg));

        var hudShelfDir = capi.GetOrCreateDataPath(DataSubdirectory);
        var positionsPath = Path.Combine(hudShelfDir, PositionsFilename);

        var positionStore = new PositionStore(positionsPath);

        _api = new HudShelfApi(positionStore);

        // Edit-mode infrastructure. Constructed in dependency order:
        // state holder, input controller (subscribes to mouse events),
        // renderer (subscribes to render frame), edit-mode controller
        // (registers hotkey, drives state transitions).
        _dragState = new DragState();
        _dragController = new DragController(capi, _api, _dragState);

        _editModeRenderer = new EditModeRenderer(capi, _api, _dragState);
        capi.Event.RegisterRenderer(_editModeRenderer, EnumRenderStage.Ortho, "hudshelf-editmode");

        _editModeController = new EditModeController(capi, _api, _dragState);

        HudShelfLog.Notification(
            $"HudShelf ready. Positions file: {positionsPath}. " +
            $"Edit-mode hotkey: {EditModeController.HotkeyCode} (default Ctrl+F8).");
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        // Tear down in reverse construction order. Each component
        // detaches its own event subscriptions in its Dispose.
        _editModeController?.Dispose();

        if (_editModeRenderer is not null && _capi is not null)
        {
            // Unregister so VS stops calling OnRenderFrame after we're
            // gone. Without this, the renderer would keep ticking with
            // dangling state references.
            _capi.Event.UnregisterRenderer(_editModeRenderer, EnumRenderStage.Ortho);
            _editModeRenderer.Dispose();
            _editModeRenderer = null;
        }

        _dragController?.Dispose();
        _dragController = null;
        _dragState = null;

        _api?.DisposeAll();
        _api = null;
        _capi = null;

        HudShelfLog.Notification("HudShelf disposed.");
        HudShelfLog.Shutdown();

        base.Dispose();
    }
}
