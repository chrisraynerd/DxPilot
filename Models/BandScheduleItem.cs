using System.Text.Json.Serialization;

namespace JtdxAutoResume.V3.Models;

public sealed class BandScheduleItem
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "";
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    [JsonIgnore]
    public DateTime LastFiredDate { get; set; } = DateTime.MinValue.Date;

    [JsonIgnore]
    public string Time
    {
        get => $"{Hour:00}:{Minute:00}";
        set
        {
            if (!TryParseTime(value, out var h, out var m))
                throw new FormatException("Time must be HH:mm");

            Hour = h;
            Minute = m;
        }
    }

    public static bool TryParseTime(string value, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(':');
        if (parts.Length != 2)
            return false;

        return int.TryParse(parts[0], out hour)
            && int.TryParse(parts[1], out minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59;
    }
}
