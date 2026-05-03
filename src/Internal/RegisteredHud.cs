using HudShelf.Bounds;
using HudShelf.Core;
using HudShelf.Persistence;

namespace HudShelf.Internal;

/// <summary>
/// Internal implementation of <see cref="IRegisteredHud"/>. Owns the
/// registration data, current position, and unregister callback.
/// </summary>
/// <remarks>
/// Mutation of <see cref="CurrentPosition"/> happens through
/// <see cref="SetPosition"/> — never directly — so that we can fire
/// the consumer callback and persistence call at exactly one site.
/// </remarks>
internal sealed class RegisteredHud : IRegisteredHud
{
    private readonly Action<RegisteredHud> _unregisterCallback;
    private readonly PositionStore? _positionStore;
    private bool _unregistered;

    public RegisteredHud(
        HudRegistration registration,
        HudPosition initialPosition,
        Action<RegisteredHud> unregisterCallback,
        PositionStore? positionStore)
    {
        Registration = registration;
        CurrentPosition = initialPosition;
        _unregisterCallback = unregisterCallback;
        _positionStore = positionStore;
    }

    public HudRegistration Registration { get; }

    public string Id => Registration.Id;

    public HudPosition CurrentPosition { get; private set; }

    public bool IsUnregistered => _unregistered;

    public void Unregister()
    {
        if (_unregistered) return;
        _unregistered = true;

        _unregisterCallback(this);
    }

    /// <summary>
    /// Mutate the current position, persist the new value, then fire
    /// the consumer callback. Caller is responsible for not invoking
    /// this during registration — the consumer's callback may not be
    /// ready while their <c>StartClientSide</c> is still executing.
    /// </summary>
    /// <remarks>
    /// Order is intentional: persist first, callback second. If
    /// persistence fails, the consumer still gets the in-session
    /// update; the next restart will reset to the previously
    /// persisted (or registered default) value. The callback firing
    /// after persistence means the consumer can rely on
    /// <c>CurrentPosition</c> reflecting the persisted state.
    /// </remarks>
    internal void SetPosition(HudPosition next)
    {
        if (CurrentPosition == next) return;

        CurrentPosition = next;

        try
        {
            _positionStore?.Set(Id, next);
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"Unexpected error persisting position for HUD '{Id}': {ex}");
        }

        try
        {
            Registration.OnPositionChanged?.Invoke(next);
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"OnPositionChanged callback for HUD '{Id}' threw: {ex}");
        }
    }

    /// <summary>
    /// Convenience for callers that want to clamp a candidate position
    /// against the current screen size before applying it. Used by the
    /// drag completion path and by edit-mode-entry re-clamping.
    /// </summary>
    /// <remarks>
    /// If <c>GetBounds</c> reports zero size (HUD not composed yet),
    /// clamping is skipped — there's nothing meaningful to clamp
    /// against. The unclamped position is still applied so the next
    /// frame's compose can show the HUD wherever it ended up; the
    /// next position update will clamp normally.
    /// </remarks>
    internal void SetPositionClamped(HudPosition candidate, double screenW, double screenH)
    {
        double w, h;
        try
        {
            (w, h) = Registration.GetBounds();
        }
        catch
        {
            // Treat as un-clampable; apply candidate as-is.
            SetPosition(candidate);
            return;
        }

        if (w <= 0 || h <= 0)
        {
            SetPosition(candidate);
            return;
        }

        var clamped = SnapMath.ClampToScreen(candidate, w, h, screenW, screenH);
        SetPosition(clamped);
    }
}
