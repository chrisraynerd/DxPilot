namespace JtdxAutoResume.V3.Services;

public sealed class GridDistanceCalculator
{
    public double? DistanceKm(string homeGrid, string targetGrid)
    {
        var home = ToLatLon(homeGrid);
        var target = ToLatLon(targetGrid);
        if (home == null || target == null)
            return null;

        const double earthRadiusKm = 6371.0;
        var dLat = ToRadians(target.Value.Lat - home.Value.Lat);
        var dLon = ToRadians(target.Value.Lon - home.Value.Lon);
        var lat1 = ToRadians(home.Value.Lat);
        var lat2 = ToRadians(target.Value.Lat);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    private static (double Lat, double Lon)? ToLatLon(string grid)
    {
        if (string.IsNullOrWhiteSpace(grid) || grid.Length < 4)
            return null;

        grid = grid.Trim().ToUpperInvariant();
        if (!char.IsLetter(grid[0]) || !char.IsLetter(grid[1]) || !char.IsDigit(grid[2]) || !char.IsDigit(grid[3]))
            return null;

        var lon = (double)((grid[0] - 'A') * 20 - 180 + (grid[2] - '0') * 2 + 1);
        var lat = (grid[1] - 'A') * 10 - 90 + (grid[3] - '0') + 0.5;

        if (grid.Length >= 6 && char.IsLetter(grid[4]) && char.IsLetter(grid[5]))
        {
            lon += (grid[4] - 'A') * 5.0 / 60.0 - 1 + 2.5 / 60.0;
            lat += (grid[5] - 'A') * 2.5 / 60.0 - 0.5 + 1.25 / 60.0;
        }

        return (lat, lon);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
