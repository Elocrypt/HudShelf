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
    private readonly Func<(double Width, double Height)>? _screenSize;
    private bool _unregistered;

    public RegisteredHud(
        HudRegistration registration,
        HudPosition initialPosition,
        Action<RegisteredHud> unregisterCallback,
        PositionStore? positionStore,
        Func<(double Width, double Height)>? screenSize = null)
    {
        Registration = registration;
        CurrentPosition = initialPosition;
        _unregisterCallback = unregisterCallback;
        _positionStore = positionStore;
        _screenSize = screenSize;
    }

    public HudRegistration Registration { get; }

    public string Id => Registration.Id;

    public HudPosition CurrentPosition { get; private set; }

    public bool IsUnregistered => _unregistered;

    // Registration defaults are screen pixels at the GUI scale in effect
    // when the HUD registered; this tracks later scale changes.
    private double _defaultScale = 1.0;

    /// <summary>The registered default position, at the current GUI scale.</summary>
    internal HudPosition DefaultPosition => new(
        Registration.DefaultAnchor,
        Registration.DefaultOffsetX * _defaultScale,
        Registration.DefaultOffsetY * _defaultScale);

    /// <summary>Account for a GUI scale change in <see cref="DefaultPosition"/>.</summary>
    internal void ScaleDefault(double factor) => _defaultScale *= factor;

    /// <inheritdoc />
    public void MoveBy(double dx, double dy)
    {
        if (_unregistered) return;

        var next = SnapMath.Nudge(CurrentPosition, dx, dy);
        if (_screenSize is null)
        {
            SetPosition(next);
            return;
        }

        var (w, h) = _screenSize();
        SetPositionClamped(next, w, h);
    }

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
    /// <param name="next">The new position.</param>
    /// <param name="saveNow">
    /// False defers the file write until <see cref="PositionStore.Flush"/>.
    /// Used for bursts (keyboard nudges, re-clamping all HUDs).
    /// </param>
    /// <param name="notifyIfUnchanged">
    /// Fire the consumer callback even when the position is unchanged.
    /// Used after a window resize or GUI-scale change: the anchor+offset
    /// is the same, but the screen pixel it resolves to has moved, and
    /// consumers that place their HUD at an absolute pixel must re-apply.
    /// </param>
    /// <remarks>
    /// Order is intentional: persist first, callback second. If
    /// persistence fails, the consumer still gets the in-session
    /// update; the next restart will reset to the previously
    /// persisted (or registered default) value. The callback firing
    /// after persistence means the consumer can rely on
    /// <c>CurrentPosition</c> reflecting the persisted state.
    /// </remarks>
    internal void SetPosition(HudPosition next, bool saveNow = true, bool notifyIfUnchanged = false)
    {
        if (_unregistered) return;

        var changed = CurrentPosition != next;
        if (!changed && !notifyIfUnchanged) return;

        if (changed)
        {
            CurrentPosition = next;

            try
            {
                _positionStore?.Set(Id, next, saveNow);
            }
            catch (Exception ex)
            {
                HudShelfLog.Warning(
                    $"Unexpected error persisting position for HUD '{Id}': {ex}");
            }
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
    /// Clamp a candidate position against the screen, then apply it.
    /// Used by drag completion, keyboard nudges, reset, and re-clamping
    /// on edit-mode entry or window resize.
    /// </summary>
    /// <remarks>
    /// If the layout size is unknown (HUD not composed yet), clamping
    /// is skipped and the candidate is applied as-is; the next position
    /// update will clamp normally.
    /// </remarks>
    internal void SetPositionClamped(
        HudPosition candidate, double screenW, double screenH,
        bool saveNow = true, bool notifyIfUnchanged = false)
    {
        if (!TryGetLayoutSize(out var w, out var h))
        {
            SetPosition(candidate, saveNow, notifyIfUnchanged);
            return;
        }

        SetPosition(SnapMath.ClampToScreen(candidate, w, h, screenW, screenH), saveNow, notifyIfUnchanged);
    }

    /// <summary>
    /// The size VS aligns to the anchor: the composer's outer footprint,
    /// padding included. Falls back to <see cref="HudRegistration.GetBounds"/>
    /// when the composer isn't available.
    /// </summary>
    /// <remarks>
    /// Snap and clamp math must use this, not <c>GetBounds</c>. Consumers
    /// may return a content size that excludes dialog padding (so the
    /// outline hugs the panel), but <c>ApplyTo</c> aligns the whole
    /// dialog. Using the content size put right- and bottom-anchored
    /// padded HUDs one padding-width away from the drag preview.
    /// </remarks>
    internal bool TryGetLayoutSize(out double width, out double height)
    {
        if (HitTester.TryGetComposerRect(this, out _, out _, out width, out height))
        {
            return true;
        }

        return TryGetContentSize(out width, out height);
    }

    /// <summary>
    /// The consumer-reported visible size (<see cref="HudRegistration.GetBounds"/>).
    /// Used for drawing outlines. False if the consumer throws or reports
    /// a non-positive size.
    /// </summary>
    internal bool TryGetContentSize(out double width, out double height)
    {
        try
        {
            (width, height) = Registration.GetBounds();
        }
        catch
        {
            width = height = 0;
            return false;
        }

        return width > 0 && height > 0;
    }
}
