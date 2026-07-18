namespace JtdxAutoResume.V3.Models;

public sealed class AdifQso
{
    public string Call { get; set; } = "";
    public string Band { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Submode { get; set; } = "";
    public string EffectiveMode => string.IsNullOrWhiteSpace(Submode) ? Mode : Submode;
    public string QsoDateText { get; set; } = "";
    public string TimeOn { get; set; } = "";
    public string Freq { get; set; } = "";
    public string Dxcc { get; set; } = "";
    public string Country { get; set; } = "";
    public string Grid { get; set; } = "";
    public string State { get; set; } = "";
    public string Iota { get; set; } = "";
    public bool LotwConfirmed { get; set; }
    public bool PaperConfirmed { get; set; }
    public bool EqslConfirmed { get; set; }
    public string Source { get; set; } = "";
    public DateTime? QsoDate { get; set; }

    public bool HasAnyConfirmation => LotwConfirmed || PaperConfirmed || EqslConfirmed;
}
