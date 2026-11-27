using HudShelf.Core;
using HudShelf.Persistence;

namespace HudShelf.Internal;

/// <summary>
/// Internal implementation of <see cref="IHudShelfApi"/>. Owns the
/// registration dictionary, resolves initial positions via the
/// persistence store with fallback to registration defaults, and
/// passes the store to each <see cref="RegisteredHud"/> so subsequent
/// position mutations persist automatically.
/// </summary>
internal sealed class HudShelfApi : IHudShelfApi
{
    private readonly Dictionary<string, RegisteredHud> _huds = new(StringComparer.Ordinal);

    // Mirror of _huds.Values kept as a concrete List so per-frame
    // iteration (hit-testing and outline drawing in edit mode) uses the
    // struct enumerator. Iterating the dictionary through
    // IReadOnlyDictionary boxes an enumerator on every frame.
    private readonly List<RegisteredHud> _hudList = new();

    private readonly PositionStore _positionStore;
    private readonly Func<(double Width, double Height)>? _screenSize;
    private bool _editModeActive;

    /// <param name="positionStore">Persistence for HUD positions.</param>
    /// <param name="screenSize">
    /// Current frame size, used to clamp programmatic moves
    /// (<see cref="IRegisteredHud.MoveBy"/>). Null skips clamping.
    /// </param>
    /// <param name="guiScale">
    /// GUI scale at startup. Saved positions recorded at a different scale
    /// are converted before any HUD registers. Null skips the conversion.
    /// </param>
    public HudShelfApi(
        PositionStore positionStore,
        Func<(double Width, double Height)>? screenSize = null,
        double? guiScale = null)
    {
        _positionStore = positionStore;
        _screenSize = screenSize;

        if (guiScale is > 0)
        {
            _positionStore.RescaleTo(guiScale.Value);
            _positionStore.Flush();
        }
    }

    /// <inheritdoc />
    public IRegisteredHud Register(HudRegistration registration)
    {
        if (registration is null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        if (string.IsNullOrWhiteSpace(registration.Id))
        {
            throw new ArgumentException(
                "HudRegistration.Id must be a non-empty string. " +
                "Convention: prefix with your mod ID, e.g. 'hudclock:main'.",
                nameof(registration));
        }

        if (registration.Element is null)
        {
            throw new ArgumentException(
                $"HudRegistration.Element is null for HUD '{registration.Id}'. " +
                "Pass the GuiDialog instance whose position HudShelf should manage.",
                nameof(registration));
        }

        if (registration.GetBounds is null)
        {
            throw new ArgumentException(
                $"HudRegistration.GetBounds is null for HUD '{registration.Id}'. " +
                "HudShelf needs a way to query the HUD's current size for hit-testing. " +
                "Typical implementation: " +
                "() => (Element.SingleComposer?.Bounds.OuterWidth ?? 0, " +
                "Element.SingleComposer?.Bounds.OuterHeight ?? 0).",
                nameof(registration));
        }

        if (_huds.ContainsKey(registration.Id))
        {
            throw new ArgumentException(
                $"A HUD with ID '{registration.Id}' is already registered. " +
                "HUD IDs must be unique across all consumer mods. " +
                "Convention: prefix your IDs with your mod ID, e.g. 'mymod:main'.",
                nameof(registration));
        }

        var initialPosition = ResolveInitialPosition(registration, _positionStore);

        var handle = new RegisteredHud(
            registration,
            initialPosition,
            unregisterCallback: HandleUnregister,
            positionStore: _positionStore,
            screenSize: _screenSize);

        _huds[registration.Id] = handle;
        _hudList.Add(handle);

        HudShelfLog.Notification(
            $"Registered HUD '{registration.Id}' at " +
            $"{initialPosition.Anchor}+({initialPosition.OffsetX:0.#}, {initialPosition.OffsetY:0.#})");

        return handle;
    }

    /// <inheritdoc />
    public bool IsEditModeActive => _editModeActive;

    /// <inheritdoc />
    public event Action<bool>? EditModeChanged;

    /// <summary>
    /// Set edit mode active state, firing <see cref="EditModeChanged"/>
    /// if the value changed. Called by the EditModeController; not
    /// exposed publicly — consumers don't toggle edit mode, the user
    /// does (via the hotkey).
    /// </summary>
    internal void SetEditModeActive(bool active)
    {
        if (_editModeActive == active) return;
        _editModeActive = active;

        // Defensive guard: a throwing event subscriber must not crash
        // HudShelf or prevent other subscribers from firing.
        var handler = EditModeChanged;
        if (handler is null) return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<bool>)subscriber)(active);
            }
            catch (Exception ex)
            {
                HudShelfLog.Warning(
                    $"EditModeChanged subscriber threw: {ex}");
            }
        }
    }

    private static HudPosition ResolveInitialPosition(
        HudRegistration registration,
        PositionStore store)
    {
        try
        {
            if (store.TryGet(registration.Id, out var persisted))
            {
                return persisted;
            }
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"Unexpected error reading persisted position for HUD '{registration.Id}': {ex}. " +
                "Using registered defaults.");
        }

        return new HudPosition(
            registration.DefaultAnchor,
            registration.DefaultOffsetX,
            registration.DefaultOffsetY);
    }

    private void HandleUnregister(RegisteredHud handle)
    {
        if (_huds.TryGetValue(handle.Id, out var stored) && ReferenceEquals(stored, handle))
        {
            _huds.Remove(handle.Id);
            _hudList.Remove(handle);
            HudShelfLog.Notification($"Unregistered HUD '{handle.Id}'");
        }
    }

    internal IReadOnlyDictionary<string, RegisteredHud> RegisteredHuds => _huds;

    /// <summary>
    /// Registered HUDs in registration order. Read-only by convention;
    /// typed as <see cref="List{T}"/> so hot-path foreach doesn't allocate.
    /// </summary>
    internal List<RegisteredHud> HudList => _hudList;

    internal PositionStore PositionStore => _positionStore;

    /// <summary>
    /// Re-clamp every registered HUD against the given screen size and
    /// write the result once. Used on edit-mode entry and when the game
    /// window changes size.
    /// </summary>
    /// <param name="screenW">Frame width.</param>
    /// <param name="screenH">Frame height.</param>
    /// <param name="notifyAll">
    /// Deliver <c>OnPositionChanged</c> to every HUD, moved or not. Set
    /// after a resize so consumers that place HUDs at absolute pixels
    /// re-resolve them against the new screen.
    /// </param>
    internal void ReclampAll(double screenW, double screenH, bool notifyAll = false)
    {
        if (screenW <= 0 || screenH <= 0) return;

        // Indexed loop: SetPosition fires consumer callbacks, and a
        // callback that unregisters would break a foreach.
        for (var i = _hudList.Count - 1; i >= 0; i--)
        {
            if (i >= _hudList.Count) continue;
            var hud = _hudList[i];
            hud.SetPositionClamped(hud.CurrentPosition, screenW, screenH, saveNow: false, notifyIfUnchanged: notifyAll);
        }

        _positionStore.Flush();
    }

    /// <summary>
    /// Adapt every position to a new GUI scale, then re-clamp. Offsets are
    /// screen pixels, so they scale with the GUI; without this a HUD kept
    /// its pixel offset while its neighbours grew or shrank around it
    /// (e.g. stat bars overlapping or separating from the hotbar).
    /// </summary>
    internal void ApplyGuiScale(double oldScale, double newScale, double screenW, double screenH)
    {
        if (oldScale <= 0 || newScale <= 0 || Math.Abs(oldScale - newScale) < 1e-6) return;

        var factor = newScale / oldScale;
        _positionStore.RescaleTo(newScale);

        for (var i = _hudList.Count - 1; i >= 0; i--)
        {
            if (i >= _hudList.Count) continue;
            var hud = _hudList[i];
            hud.ScaleDefault(factor);

            // Stored positions were rescaled above; HUDs still on their
            // registered default are scaled here.
            var next = _positionStore.TryGet(hud.Id, out var stored)
                ? stored
                : hud.CurrentPosition with
                {
                    OffsetX = hud.CurrentPosition.OffsetX * factor,
                    OffsetY = hud.CurrentPosition.OffsetY * factor,
                };
            hud.SetPosition(next, saveNow: false, notifyIfUnchanged: true);
        }

        ReclampAll(screenW, screenH);
    }

    internal void DisposeAll()
    {
        _huds.Clear();
        _hudList.Clear();
        EditModeChanged = null;
    }
}
