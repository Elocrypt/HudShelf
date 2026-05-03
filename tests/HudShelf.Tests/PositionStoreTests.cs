using System.Text.Json;
using HudShelf.Persistence;
using Xunit;

namespace HudShelf.Tests;

public sealed class PositionStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _filePath;

    public PositionStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "hudshelf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _filePath = Path.Combine(_tmpDir, "positions.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    [Fact]
    public void TryGet_OnEmptyStore_ReturnsFalse()
    {
        var store = new PositionStore(_filePath);
        Assert.False(store.TryGet("anymod:hud", out _));
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsSamePosition()
    {
        var store = new PositionStore(_filePath);
        var pos = new HudPosition(HudAnchor.BottomRight, -42.5, 17);

        store.Set("mymod:hud1", pos);
        var found = store.TryGet("mymod:hud1", out var roundTripped);

        Assert.True(found);
        Assert.Equal(pos, roundTripped);
    }

    [Fact]
    public void Set_WritesFileToDisk()
    {
        var store = new PositionStore(_filePath);
        store.Set("mymod:hud1", new HudPosition(HudAnchor.TopLeft, 0, 0));
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public void TryGet_AfterReload_ReturnsPersistedValue()
    {
        var pos = new HudPosition(HudAnchor.Center, 5, -5);

        var writer = new PositionStore(_filePath);
        writer.Set("mymod:hud1", pos);

        var reader = new PositionStore(_filePath);
        var found = reader.TryGet("mymod:hud1", out var loaded);

        Assert.True(found);
        Assert.Equal(pos, loaded);
    }

    [Fact]
    public void TryGet_WhenFileMissing_ReturnsFalseAndDoesNotThrow()
    {
        var store = new PositionStore(_filePath);
        var ex = Record.Exception(() => store.TryGet("mymod:hud1", out _));
        Assert.Null(ex);
        store.TryGet("mymod:hud1", out _);
        Assert.Equal(0, store.CachedCount);
    }

    [Fact]
    public void TryGet_WhenFileMalformed_ReturnsFalseAndDoesNotThrow()
    {
        File.WriteAllText(_filePath, "{ this is not valid json");
        var store = new PositionStore(_filePath);
        var ex = Record.Exception(() => store.TryGet("mymod:hud1", out _));
        Assert.Null(ex);
        Assert.False(store.TryGet("mymod:hud1", out _));
    }

    [Fact]
    public void TryGet_WhenSchemaVersionUnknown_FallsBackToEmpty()
    {
        var futureFile = """
            {
              "version": 99,
              "huds": {
                "mymod:hud1": { "anchor": "TopLeft", "offsetX": 0, "offsetY": 0 }
              }
            }
            """;
        File.WriteAllText(_filePath, futureFile);

        var store = new PositionStore(_filePath);
        Assert.False(store.TryGet("mymod:hud1", out _));
    }

    [Fact]
    public void TryGet_WhenAnchorUnknown_SkipsThatEntryButLoadsOthers()
    {
        var mixedFile = """
            {
              "version": 1,
              "huds": {
                "mymod:bad":  { "anchor": "NotARealAnchor", "offsetX": 1, "offsetY": 2 },
                "mymod:good": { "anchor": "BottomLeft",     "offsetX": 3, "offsetY": 4 }
              }
            }
            """;
        File.WriteAllText(_filePath, mixedFile);

        var store = new PositionStore(_filePath);
        Assert.False(store.TryGet("mymod:bad", out _));

        var foundGood = store.TryGet("mymod:good", out var goodPos);
        Assert.True(foundGood);
        Assert.Equal(new HudPosition(HudAnchor.BottomLeft, 3, 4), goodPos);
    }

    [Fact]
    public void TryGet_WhenAnchorIsOutOfRangeNumber_SkipsEntry()
    {
        var file = """
            {
              "version": 1,
              "huds": {
                "mymod:hud": { "anchor": "99", "offsetX": 0, "offsetY": 0 }
              }
            }
            """;
        File.WriteAllText(_filePath, file);

        var store = new PositionStore(_filePath);
        Assert.False(store.TryGet("mymod:hud", out _));
    }

    [Fact]
    public void Set_PreservesExistingEntries()
    {
        var store = new PositionStore(_filePath);
        store.Set("mymod:a", new HudPosition(HudAnchor.TopLeft, 1, 1));
        store.Set("mymod:b", new HudPosition(HudAnchor.BottomRight, 2, 2));

        var reader = new PositionStore(_filePath);
        Assert.True(reader.TryGet("mymod:a", out var a));
        Assert.True(reader.TryGet("mymod:b", out var b));
        Assert.Equal(new HudPosition(HudAnchor.TopLeft, 1, 1), a);
        Assert.Equal(new HudPosition(HudAnchor.BottomRight, 2, 2), b);
    }

    [Fact]
    public void Set_OverwritesPreviousValueForSameId()
    {
        var store = new PositionStore(_filePath);
        store.Set("mymod:hud", new HudPosition(HudAnchor.TopLeft, 0, 0));
        store.Set("mymod:hud", new HudPosition(HudAnchor.BottomRight, 99, 99));

        var reader = new PositionStore(_filePath);
        Assert.True(reader.TryGet("mymod:hud", out var pos));
        Assert.Equal(new HudPosition(HudAnchor.BottomRight, 99, 99), pos);
        Assert.Equal(1, reader.CachedCount);
    }

    [Fact]
    public void Set_ProducesValidJsonOnDisk()
    {
        var store = new PositionStore(_filePath);
        store.Set("mymod:hud", new HudPosition(HudAnchor.TopRight, 10, 20));

        var raw = File.ReadAllText(_filePath);
        using var doc = JsonDocument.Parse(raw);

        var version = doc.RootElement.GetProperty("version").GetInt32();
        Assert.Equal(1, version);

        var anchor = doc.RootElement
            .GetProperty("huds")
            .GetProperty("mymod:hud")
            .GetProperty("anchor")
            .GetString();
        Assert.Equal("TopRight", anchor);
    }
}
