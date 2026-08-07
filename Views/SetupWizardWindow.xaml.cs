using System.Windows;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Controls.JtdxSelection;
using JtdxAutoResume.V3.Services;

namespace JtdxAutoResume.V3.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow(AppSettings settings, bool isFirstRun)
    {
        InitializeComponent();
        var viewModel = new SetupWizardViewModel(settings, isFirstRun);
        viewModel.CloseRequested += (_, completed) => DialogResult = completed;
        viewModel.CalibrationRequested += (_, _) => StartCalibration(viewModel);
        viewModel.EnableTxCaptureRequested += async (_, _) => await CaptureEnableTxAsync(viewModel);
        DataContext = viewModel;
    }

    private async Task CaptureEnableTxAsync(SetupWizardViewModel viewModel)
    {
        viewModel.BeginEnableTxCapture();
        WindowState = WindowState.Minimized;
        try
        {
            var point = await new ScreenClicker().PickPointAsync();
            if (point == null)
            {
                viewModel.CancelEnableTxCapture();
                return;
            }

            var rgb = new PixelDetector().GetScreenRgb(point.Value.x, point.Value.y);
            viewModel.AcceptEnableTxCapture(point.Value.x, point.Value.y, rgb);
        }
        catch (OperationCanceledException)
        {
            viewModel.CancelEnableTxCapture();
        }
        finally
        {
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void StartCalibration(SetupWizardViewModel viewModel)
    {
        if (!viewModel.TryPrepareCalibration(out var calibration, out var jtdxWindow) || jtdxWindow == null)
            return;

        var changed = false;
        var overlay = new JtdxBandActivityOverlay();
        overlay.CalibrationChanged += updated =>
        {
            changed = true;
            viewModel.AcceptCalibration(updated);
        };
        overlay.Closed += (_, _) =>
        {
            WindowState = WindowState.Normal;
            Activate();
            if (!changed)
                viewModel.CalibrationClosedWithoutChange();
        };

        WindowState = WindowState.Minimized;
        overlay.ShowCalibration(calibration, jtdxWindow.Left, jtdxWindow.Top);
        overlay.Activate();
        overlay.Focus();
    }
}
