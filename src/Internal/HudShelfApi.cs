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

    private readonly PositionStore _positionStore;
    private bool _editModeActive;

    public HudShelfApi(PositionStore positionStore)
    {
        _positionStore = positionStore;
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
            positionStore: _positionStore);

        _huds[registration.Id] = handle;

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
            HudShelfLog.Notification($"Unregistered HUD '{handle.Id}'");
        }
    }

    internal IReadOnlyDictionary<string, RegisteredHud> RegisteredHuds => _huds;

    internal PositionStore PositionStore => _positionStore;

    internal void DisposeAll()
    {
        _huds.Clear();
    }
}
