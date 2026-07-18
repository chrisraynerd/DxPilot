using JtdxAutoResume.V3.Controls.JtdxSelection;

namespace JtdxAutoResume.V3;

public partial class App : System.Windows.Application
{
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        JtdxBandActivityOverlay.CloseAll();
        base.OnExit(e);
    }
}
