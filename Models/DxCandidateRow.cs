using System.ComponentModel;

namespace JtdxAutoResume.V3.Models;

public sealed class DxCandidateRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string JtdxRow { get; set; } = "";
    public int Rank { get; set; }
    public string RankText { get; set; } = "";
    public string Call { get; set; } = "";
    public bool WasCallWorkedBefore { get; set; }
    public bool WasCallWorkedInSelectedProfile { get; set; }
    public bool WasCallWorkedUnderAnotherProfileOnly { get; set; }
    public string WorkedCallToolTip { get; set; } = "";
    public string Country { get; set; } = "";
    public string Continent { get; set; } = "";
    public string Iota { get; set; } = "";
    public string Dxcc { get; set; } = "";
    public string Tier { get; set; } = "";
    public string WantedReason { get; set; } = "";
    public string DxccStatus { get; set; } = "";
    public int? RarityRank { get; set; }
    public int RarityScore { get; set; }
    public string Grid { get; set; } = "";
    public string GridSource { get; set; } = "";
    public string GridStatus { get; set; } = "";
    public string State { get; set; } = "";
    public string LocationDetail { get; set; } = "";
    public string StateSource { get; set; } = "";
    public string StateStatus { get; set; } = "";
    public string QrzStatus { get; set; } = "";
    public string Rarity { get; set; } = "";
    public double? DistanceMiles { get; set; }
    public string Age { get; set; } = "";
    public int Snr { get; set; }
    public string SourceType { get; set; } = "";
    public int Score { get; set; }
    public string TargetStatus { get; set; } = "";
    public string PriorityClass { get; set; } = "";
    public string OpportunityClass { get; set; } = "";
    public string ActionStateClass { get; set; } = "";
    public bool IsPermanentlySuppressed { get; set; }
    public string Details { get; set; } = "";
    public DxTarget Target { get; set; } = new();

    public void UpdateFrom(DxCandidateRow source)
    {
        UpdateValue(nameof(JtdxRow), JtdxRow, source.JtdxRow, value => JtdxRow = value);
        UpdateValue(nameof(Rank), Rank, source.Rank, value => Rank = value);
        UpdateValue(nameof(RankText), RankText, source.RankText, value => RankText = value);
        UpdateValue(nameof(Call), Call, source.Call, value => Call = value);
        UpdateValue(
            nameof(WasCallWorkedBefore),
            WasCallWorkedBefore,
            source.WasCallWorkedBefore,
            value => WasCallWorkedBefore = value);
        UpdateValue(
            nameof(WasCallWorkedInSelectedProfile),
            WasCallWorkedInSelectedProfile,
            source.WasCallWorkedInSelectedProfile,
            value => WasCallWorkedInSelectedProfile = value);
        UpdateValue(
            nameof(WasCallWorkedUnderAnotherProfileOnly),
            WasCallWorkedUnderAnotherProfileOnly,
            source.WasCallWorkedUnderAnotherProfileOnly,
            value => WasCallWorkedUnderAnotherProfileOnly = value);
        UpdateValue(
            nameof(WorkedCallToolTip),
            WorkedCallToolTip,
            source.WorkedCallToolTip,
            value => WorkedCallToolTip = value);
        UpdateValue(nameof(Country), Country, source.Country, value => Country = value);
        UpdateValue(nameof(Continent), Continent, source.Continent, value => Continent = value);
        UpdateValue(nameof(Iota), Iota, source.Iota, value => Iota = value);
        UpdateValue(nameof(Dxcc), Dxcc, source.Dxcc, value => Dxcc = value);
        UpdateValue(nameof(Tier), Tier, source.Tier, value => Tier = value);
        UpdateValue(nameof(WantedReason), WantedReason, source.WantedReason, value => WantedReason = value);
        UpdateValue(nameof(DxccStatus), DxccStatus, source.DxccStatus, value => DxccStatus = value);
        UpdateValue(nameof(RarityRank), RarityRank, source.RarityRank, value => RarityRank = value);
        UpdateValue(nameof(RarityScore), RarityScore, source.RarityScore, value => RarityScore = value);
        UpdateValue(nameof(Grid), Grid, source.Grid, value => Grid = value);
        UpdateValue(nameof(GridSource), GridSource, source.GridSource, value => GridSource = value);
        UpdateValue(nameof(GridStatus), GridStatus, source.GridStatus, value => GridStatus = value);
        UpdateValue(nameof(State), State, source.State, value => State = value);
        UpdateValue(nameof(LocationDetail), LocationDetail, source.LocationDetail, value => LocationDetail = value);
        UpdateValue(nameof(StateSource), StateSource, source.StateSource, value => StateSource = value);
        UpdateValue(nameof(StateStatus), StateStatus, source.StateStatus, value => StateStatus = value);
        UpdateValue(nameof(QrzStatus), QrzStatus, source.QrzStatus, value => QrzStatus = value);
        UpdateValue(nameof(Rarity), Rarity, source.Rarity, value => Rarity = value);
        UpdateValue(nameof(DistanceMiles), DistanceMiles, source.DistanceMiles, value => DistanceMiles = value);
        UpdateValue(nameof(Age), Age, source.Age, value => Age = value);
        UpdateValue(nameof(Snr), Snr, source.Snr, value => Snr = value);
        UpdateValue(nameof(SourceType), SourceType, source.SourceType, value => SourceType = value);
        UpdateValue(nameof(Score), Score, source.Score, value => Score = value);
        UpdateValue(nameof(TargetStatus), TargetStatus, source.TargetStatus, value => TargetStatus = value);
        UpdateValue(nameof(PriorityClass), PriorityClass, source.PriorityClass, value => PriorityClass = value);
        UpdateValue(nameof(OpportunityClass), OpportunityClass, source.OpportunityClass, value => OpportunityClass = value);
        UpdateValue(nameof(ActionStateClass), ActionStateClass, source.ActionStateClass, value => ActionStateClass = value);
        UpdateValue(
            nameof(IsPermanentlySuppressed),
            IsPermanentlySuppressed,
            source.IsPermanentlySuppressed,
            value => IsPermanentlySuppressed = value);
        UpdateValue(nameof(Details), Details, source.Details, value => Details = value);
        Target = source.Target;
    }

    private void UpdateValue<T>(string propertyName, T currentValue, T newValue, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return;

        assign(newValue);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
