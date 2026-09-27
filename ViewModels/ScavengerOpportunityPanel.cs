using System.Collections.ObjectModel;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.ViewModels;

// Display-only snapshots: never used to select a station or issue radio commands.
public sealed record ScavengerOpportunityRow(DecodeMessage Decode, string Reason, string Status, bool IsTarget)
{
    public string Category { get; init; } = "";
    public NeedStatus Need { get; init; }
    public bool IsInQso { get; init; }
    public string OpportunityClass => Category switch
    {
        "DXCC" => Need == NeedStatus.NeverWorked ? "NewDxcc" : "UnconfirmedDxcc",
        "grid" => "NewGrid",
        "state" => "NewState",
        _ => Decode.OpportunityClass
    };
    public string ActionStateClass => Status switch
    {
        "Suppressed indefinitely" => "PermanentlySuppressed",
        "Temporarily suppressed" => "Suppressed",
        "Not selectable yet" or "Band not permitted" => "NotContactable",
        "Waiting for fresh source" => "Failed",
        "Worked recently" => "Worked",
        _ when Status.StartsWith("Resting", StringComparison.Ordinal) => "Suppressed",
        _ when IsTarget => IsInQso ? "InProgress" : "Calling",
        _ => "Actionable"
    };
}

public sealed class ScavengerOpportunityPanel : ObservableObject
{
    public ScavengerOpportunityPanel(string title, string category)
    {
        Title = title;
        Category = category;
        Rows.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(Heading)); OnPropertyChanged(nameof(IsEmpty)); };
    }
    public string Title { get; }
    public string Category { get; }
    public string Heading => $"{Title} · {Rows.Count}";
    public bool IsEmpty => Rows.Count == 0;
    public ObservableCollection<ScavengerOpportunityRow> Rows { get; } = new();
}
