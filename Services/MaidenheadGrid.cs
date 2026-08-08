namespace JtdxAutoResume.V3.Services;

public readonly record struct NormalizedGrid(string Grid4, string Grid6)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Grid4);
}

public static class MaidenheadGrid
{
    public static NormalizedGrid Normalize(string value)
    {
        var grid = new string((value ?? "").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (grid.Length < 4 || !IsValidGrid4(grid))
            return new NormalizedGrid("", "");

        var grid4 = grid[..4];
        var grid6 = grid.Length >= 6 && IsValidGrid6(grid) ? grid[..6] : "";
        return new NormalizedGrid(grid4, grid6);
    }

    public static bool TryGetCentre(string value, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        var normalized = Normalize(value);
        if (!normalized.IsValid)
            return false;

        var grid = string.IsNullOrWhiteSpace(normalized.Grid6)
            ? normalized.Grid4
            : normalized.Grid6;
        longitude = (grid[0] - 'A') * 20 - 180 + (grid[2] - '0') * 2 + 1;
        latitude = (grid[1] - 'A') * 10 - 90 + (grid[3] - '0') + 0.5;

        if (grid.Length >= 6)
        {
            longitude += (grid[4] - 'A') * (5.0 / 60.0) - 1 + (2.5 / 60.0);
            latitude += (grid[5] - 'A') * (2.5 / 60.0) - 0.5 + (1.25 / 60.0);
        }

        return true;
    }

    private static bool IsValidGrid4(string grid)
    {
        return grid.Length >= 4
            && grid[0] is >= 'A' and <= 'R'
            && grid[1] is >= 'A' and <= 'R'
            && char.IsDigit(grid[2])
            && char.IsDigit(grid[3]);
    }

    private static bool IsValidGrid6(string grid)
    {
        return grid.Length >= 6
            && IsValidGrid4(grid)
            && grid[4] is >= 'A' and <= 'X'
            && grid[5] is >= 'A' and <= 'X';
    }
}
