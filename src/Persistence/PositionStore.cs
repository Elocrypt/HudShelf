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

    public void Set(string hudId, HudPosition position)
    {
        EnsureLoaded();
        _cache[hudId] = position;
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
