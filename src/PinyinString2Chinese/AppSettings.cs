namespace PinyinString2Chinese;

public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;

    public bool RequireAlt { get; init; }

    public string? ImeLayoutName { get; init; }

    public long ImeHkl { get; init; }

    public bool StartWithWindows { get; init; }

    public int? FloatBallX { get; init; }

    public int? FloatBallY { get; init; }
}
