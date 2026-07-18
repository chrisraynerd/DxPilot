using System.Collections.ObjectModel;

namespace JtdxAutoResume.V3.Models;

public sealed class DxTarget
{
    public DecodeMessage Decode { get; set; } = new();
    public CandidateRanking Ranking { get; set; } = new();
    public int Score { get; set; }
    public ObservableCollection<string> Reasons { get; } = new();
    public string PrimaryReason => Reasons.Count == 0 ? "No scoring reason" : Reasons[0];
    public string Callsign => Decode.Callsign;
    public string Band => Decode.Band;
    public string Mode => Decode.Mode;
}
