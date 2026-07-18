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
