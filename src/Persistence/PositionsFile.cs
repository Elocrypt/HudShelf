namespace HudShelf.Persistence;

internal sealed class PositionsFileDto
{
    public int Version { get; set; }

    /// <summary>
    /// GUI scale the offsets were saved at. Optional (absent in files
    /// written before 0.3.3, which are treated as the current scale);
    /// older HudShelf builds ignore it.
    /// </summary>
    public double? GuiScale { get; set; }

    public Dictionary<string, PositionEntryDto>? Huds { get; set; }
}

internal sealed class PositionEntryDto
{
    public string Anchor { get; set; } = string.Empty;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
}
