using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Views;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var settings = new AppSettings
{
    MyCallsign = "",
    HomeGrid = "",
    FullAdifPath = "",
    LiveJtdxAdifPath = "",
    JtdxAllTxtPath = ""
};
var wizard = new SetupWizardViewModel(settings, isFirstRun: true);

Require(wizard.UdpListenPort == 2237, "JTDX Primary UDP Server default must be 2237.");
Require(wizard.UdpForwardPort == 2238, "GridTracker Receive UDP default must be 2238.");
Require(wizard.LoggingProgramPort == 2236, "Downstream logging-program default must be 2236.");
Require(wizard.JtdxPortInstructions.Contains("UDP Server port number to 2237"), "JTDX instructions must use the JTDX field name.");
Require(wizard.GridTrackerPortInstructions.Contains("Receive UDP Messages Received from JTDX"), "GridTracker instructions must use the GridTracker field name.");
Require(wizard.LoggingProgramInstructions.Contains("JT_MESSAGE") && wizard.LoggingProgramInstructions.Contains("2236"), "Logging instructions must include the generic port and Log4OM example.");

wizard.ConnectionSetup = SetupWizardViewModel.LoggerOnly;
Require(!wizard.UsesGridTracker && wizard.UsesLoggingProgram, "Logger-only setup must hide GridTracker guidance.");
Require(wizard.ConnectionSummary.Contains("logging program 2236") && !wizard.ConnectionSummary.Contains("GridTracker"), "Logger-only summary must describe direct forwarding.");
wizard.ConnectionSetup = SetupWizardViewModel.JtdxOnly;
Require(!wizard.UsesForwarding, "JTDX-only setup must disable companion forwarding.");
wizard.ConnectionSetup = SetupWizardViewModel.GridTrackerAndLogger;

wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 1, "Welcome should continue to station setup.");
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 1 && wizard.HasError, "Invalid station details must block progress.");

wizard.Callsign = "G1CEC";
wizard.HomeGrid = "IO83";
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 2, "Valid station details should continue to UDP setup.");
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 3, "Valid UDP chain should continue to log setup.");

wizard.WatchLiveJtdxAdif = false;
wizard.WatchJtdxAllTxt = false;
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 4, "Optional log watchers may be disabled.");
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 4 && wizard.HasError, "Enable TX calibration must be completed before setup can finish.");

wizard.AcceptEnableTxCapture(1360, 781, 0xDCDCDC);
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 5, "Completed Enable TX calibration should continue to grid calibration.");
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 5 && wizard.HasError, "Grid calibration must be completed before setup can finish.");

wizard.AcceptCalibration(new JtdxBandActivityGridCalibration
{
    BandActivityLeftRelative = 10,
    BandActivityTopRelative = 70,
    BandActivityWidth = 700,
    BandActivityHeight = 800,
    FirstFullRowCentreYRelative = 86,
    RowHeight = 16,
    MessageClickXRelative = 500,
    SafeVisibleFullRowCount = 52,
    CalibrationDate = DateTime.Now
});
wizard.NextCommand.Execute(null);
Require(wizard.CurrentStep == 6, "Completed calibration should continue to review.");

var completed = false;
wizard.CloseRequested += (_, result) => completed = result;
wizard.NextCommand.Execute(null);
Require(completed, "Finish must close the wizard as completed.");
Require(settings.SetupWizardCompleted, "Finish must persist first-run completion.");
Require(settings.MyCallsign == "G1CEC" && settings.HomeGrid == "IO83", "Finish must apply station identity.");
Require(settings.DownstreamLoggerPort == 2236, "Finish must preserve the logging-program reminder port.");
Require(settings.EnableTxCalibrationDate != DateTime.MinValue && settings.EnableTxX == 1360 && settings.EnableTxY == 781, "Finish must persist Enable TX safety calibration.");
Require(settings.JtdxBandCalibrationDate != DateTime.MinValue, "Finish must persist grid calibration.");

Exception? windowFailure = null;
var windowThread = new Thread(() =>
{
    try
    {
        var application = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };
        var window = new SetupWizardWindow(new AppSettings(), isFirstRun: false);
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(window.Close);
        window.ShowDialog();
        application.Shutdown();
    }
    catch (Exception ex)
    {
        windowFailure = ex;
    }
});
windowThread.SetApartmentState(ApartmentState.STA);
windowThread.Start();
windowThread.Join();
Require(windowFailure == null, $"Wizard window must open without runtime XAML/binding errors: {windowFailure}");

Console.WriteLine("PASS: setup wizard station validation, adaptive port guidance, log paths, mandatory Enable TX and grid calibration, and settings application.");
