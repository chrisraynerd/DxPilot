namespace JtdxAutoResume.V3.Services;

public static class AmateurBandMapper
{
    private static readonly (ulong Low, ulong High, string Band)[] Bands =
    [
        (135_700, 137_800, "2200m"),
        (472_000, 479_000, "630m"),
        (1_800_000, 2_000_000, "160m"),
        (3_500_000, 4_000_000, "80m"),
        (5_000_000, 5_500_000, "60m"),
        (7_000_000, 7_300_000, "40m"),
        (10_100_000, 10_150_000, "30m"),
        (14_000_000, 14_350_000, "20m"),
        (18_068_000, 18_168_000, "17m"),
        (21_000_000, 21_450_000, "15m"),
        (24_890_000, 24_990_000, "12m"),
        (28_000_000, 29_700_000, "10m"),
        (50_000_000, 54_000_000, "6m"),
        (69_900_000, 70_500_000, "4m"),
        (144_000_000, 148_000_000, "2m"),
        (219_000_000, 225_000_000, "1.25m"),
        (420_000_000, 450_000_000, "70cm"),
        (902_000_000, 928_000_000, "33cm"),
        (1_240_000_000, 1_300_000_000, "23cm"),
        (2_300_000_000, 2_450_000_000, "13cm"),
        (3_300_000_000, 3_500_000_000, "9cm"),
        (5_650_000_000, 5_925_000_000, "6cm"),
        (10_000_000_000, 10_500_000_000, "3cm")
    ];

    public static string FromDialFrequency(ulong dialFrequencyHz)
    {
        foreach (var (low, high, band) in Bands)
        {
            if (dialFrequencyHz >= low && dialFrequencyHz <= high)
                return band;
        }

        return "";
    }

    public static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "").Trim().ToUpperInvariant();
        return normalized switch
        {
            "FT8" or "~" => "FT8",
            "FT4" or "+" => "FT4",
            _ => normalized
        };
    }

    public static TimeSpan ReceivePeriod(string? mode, uint reportedTrPeriodSeconds = 0)
    {
        var normalized = NormalizeMode(mode);
        if (normalized == "FT4")
            return TimeSpan.FromSeconds(7.5);
        if (normalized == "FT8")
            return TimeSpan.FromSeconds(15);
        if (reportedTrPeriodSeconds > 0)
            return TimeSpan.FromSeconds(reportedTrPeriodSeconds);
        return TimeSpan.FromSeconds(15);
    }

    public static TimeSpan OwnTransmitCycle(string? mode, uint reportedTrPeriodSeconds = 0)
    {
        return TimeSpan.FromTicks(ReceivePeriod(mode, reportedTrPeriodSeconds).Ticks * 2);
    }
}
