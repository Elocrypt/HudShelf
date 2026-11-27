using System.Text.Json;
using HudShelf.Core;

namespace HudShelf.Persistence;

/// <summary>
/// Per-install persistence for HUD positions. Reads/writes a JSON file
/// keyed by HUD ID, with an in-memory cache for hot reads.
/// </summary>
internal sealed class PositionStore
{
    internal const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly Dictionary<string, HudPosition> _cache = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _dirty;
    private double? _guiScale;

    public PositionStore(string filePath)
    {
        _filePath = filePath;
    }

    internal string FilePath => _filePath;

    public bool TryGet(string hudId, out HudPosition position)
    {
        EnsureLoaded();
        return _cache.TryGetValue(hudId, out position);
    }

    /// <summary>
    /// Store a position. With <paramref name="saveNow"/> false the
    /// write is deferred until <see cref="Flush"/>, so bursts of
    /// updates (keyboard nudges, re-clamping every HUD after a resize)
    /// cost one file write instead of one per change.
    /// </summary>
    public void Set(string hudId, HudPosition position, bool saveNow = true)
    {
        EnsureLoaded();
        _cache[hudId] = position;
        _dirty = true;
        if (saveNow) Flush();
    }

    /// <summary>
    /// GUI scale the stored offsets are expressed at, or null if the file
    /// predates scale tracking.
    /// </summary>
    internal double? GuiScale
    {
        get
        {
            EnsureLoaded();
            return _guiScale;
        }
    }

    /// <summary>
    /// Bring every stored offset to <paramref name="newScale"/>. Offsets are
    /// screen pixels, so a GUI scale change multiplies them by
    /// <c>newScale / oldScale</c>; that keeps each HUD at the same place
    /// relative to its anchor and its (equally rescaled) neighbours. A
    /// store with no recorded scale just adopts the new one. Not written
    /// until <see cref="Flush"/>.
    /// </summary>
    public void RescaleTo(double newScale)
    {
        EnsureLoaded();
        if (newScale <= 0) return;

        var old = _guiScale;
        if (old is > 0 && Math.Abs(old.Value - newScale) > 1e-6)
        {
            var factor = newScale / old.Value;
            foreach (var id in SnapshotKeys())
            {
                var p = _cache[id];
                _cache[id] = p with { OffsetX = p.OffsetX * factor, OffsetY = p.OffsetY * factor };
            }
        }

        if (old != newScale)
        {
            _guiScale = newScale;
            _dirty = true;
        }
    }

    // Snapshot of the keys so the cache can be updated while iterating.
    private string[] SnapshotKeys()
    {
        var keys = new string[_cache.Count];
        _cache.Keys.CopyTo(keys, 0);
        return keys;
    }

    /// <summary>True when there are changes not yet written to disk.</summary>
    internal bool IsDirty => _dirty;

    /// <summary>
    /// Write pending changes to disk. No-op when nothing changed.
    /// </summary>
    public void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        SaveToDisk();
    }

    internal int CachedCount
    {
        get
        {
            EnsureLoaded();
            return _cache.Count;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return;

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"Failed to read positions file '{_filePath}': {ex.Message}. " +
                "Falling back to registered defaults for all HUDs.");
            return;
        }

        PositionsFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PositionsFileDto>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            HudShelfLog.Warning(
                $"Positions file '{_filePath}' is malformed: {ex.Message}. " +
                "Falling back to registered defaults. The file is not deleted; " +
                "you may inspect or hand-edit it.");
            return;
        }

        if (dto is null)
        {
            HudShelfLog.Warning(
                $"Positions file '{_filePath}' deserialized to null. " +
                "Falling back to registered defaults.");
            return;
        }

        if (dto.Version != CurrentSchemaVersion)
        {
            HudShelfLog.Warning(
                $"Positions file '{_filePath}' has unknown schema version {dto.Version} " +
                $"(this HudShelf understands version {CurrentSchemaVersion}). " +
                "Falling back to registered defaults.");
            return;
        }

        _guiScale = dto.GuiScale is > 0 ? dto.GuiScale : null;

        if (dto.Huds is null) return;

        foreach (var (id, entry) in dto.Huds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (entry is null) continue;

            if (!Enum.TryParse<HudAnchor>(entry.Anchor, ignoreCase: false, out var anchor) ||
                !Enum.IsDefined(typeof(HudAnchor), anchor))
            {
                HudShelfLog.Warning(
                    $"Position entry for HUD '{id}' has unknown anchor '{entry.Anchor}'. " +
                    "Skipping this entry; the HUD will use its registered default.");
                continue;
            }

            _cache[id] = new HudPosition(anchor, entry.OffsetX, entry.OffsetY);
        }
    }

    private void SaveToDisk()
    {
        var dto = new PositionsFileDto
        {
            Version = CurrentSchemaVersion,
            GuiScale = _guiScale,
            Huds = BuildEntries(),
        };

        string json;
        try
        {
            json = JsonSerializer.Serialize(dto, SerializerOptions);
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"Failed to serialize positions for write: {ex.Message}. " +
                "In-session positions are unchanged but won't persist to the next session.");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            HudShelfLog.Warning(
                $"Failed to write positions file '{_filePath}': {ex.Message}. " +
                "In-session positions are unchanged but won't persist to the next session.");
        }
    }

    private Dictionary<string, PositionEntryDto> BuildEntries()
    {
        var entries = new Dictionary<string, PositionEntryDto>(_cache.Count, StringComparer.Ordinal);
        foreach (var (id, position) in _cache)
        {
            entries[id] = new PositionEntryDto
            {
                Anchor = position.Anchor.ToString(),
                OffsetX = position.OffsetX,
                OffsetY = position.OffsetY,
            };
        }
        return entries;
    }
}
