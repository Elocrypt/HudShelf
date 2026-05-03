namespace HudShelf.Persistence;

internal sealed class PositionsFileDto
{
    public int Version { get; set; }
    public Dictionary<string, PositionEntryDto>? Huds { get; set; }
}

internal sealed class PositionEntryDto
{
    public string Anchor { get; set; } = string.Empty;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
}
