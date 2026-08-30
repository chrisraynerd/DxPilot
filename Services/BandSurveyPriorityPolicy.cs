namespace JtdxAutoResume.V3.Services;

public static class BandSurveyPriorityPolicy
{
    public static bool ShouldInterruptForNewDxcc(
        bool automaticSurvey,
        bool resumesAssistance) =>
        automaticSurvey || resumesAssistance;
}
