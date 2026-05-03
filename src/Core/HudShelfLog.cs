namespace HudShelf.Core;

/// <summary>
/// Internal logging shim. Initialized once from
/// <c>HudShelfModSystem.StartClientSide</c>, then used by any module
/// that needs to log without plumbing the client API through.
/// </summary>
/// <remarks>
/// This file deliberately contains no Vintage Story types. The VS-typed
/// logger is wrapped in <see cref="Action{T}"/> at the call site in
/// <c>HudShelfModSystem</c>, which keeps the rest of the assembly
/// (notably <see cref="Persistence.PositionStore"/>) free of any
/// transitive dependency on VintagestoryAPI.dll. That matters for
/// testability: tests can exercise PositionStore without the VS DLL
/// being on the runtime load path.
/// <para/>
/// All messages get prefixed with <c>[HudShelf]</c> so they're
/// findable in a noisy <c>client-main.log</c>. Calls before
/// <see cref="Init"/> are silently dropped — that should never happen
/// in practice (we initialize before any other HudShelf code runs)
/// but a missed init must not cascade into NREs.
/// </remarks>
internal static class HudShelfLog
{
    private static Action<string>? _notification;
    private static Action<string>? _warning;
    private static Action<string>? _error;

    internal static void Init(
        Action<string> notification,
        Action<string> warning,
        Action<string> error)
    {
        _notification = notification;
        _warning = warning;
        _error = error;
    }

    internal static void Shutdown()
    {
        _notification = null;
        _warning = null;
        _error = null;
    }

    internal static void Notification(string message)
        => _notification?.Invoke($"[HudShelf] {message}");

    internal static void Warning(string message)
        => _warning?.Invoke($"[HudShelf] {message}");

    internal static void Error(string message)
        => _error?.Invoke($"[HudShelf] {message}");
}
