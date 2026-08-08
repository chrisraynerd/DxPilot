namespace JtdxAutoResume.V3.Services;

public sealed class CallNowSessionState
{
    public bool IsOneShot { get; private set; }

    public bool Begin(bool assistanceRunning)
    {
        if (!IsOneShot)
            IsOneShot = !assistanceRunning;

        return IsOneShot;
    }

    public void PromoteToAutomation()
    {
        IsOneShot = false;
    }

    public bool EndTarget()
    {
        var shouldStopAssistance = IsOneShot;
        IsOneShot = false;
        return shouldStopAssistance;
    }

    public void Reset()
    {
        IsOneShot = false;
    }
}
