using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Views;

internal static class Program
{
    private static int _checks;
    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        _checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        AdifQso Q(string band, bool lotw = false, string state = "CA", string dxcc = "291", string profile = "G1CEC") => new()
        {
            Call = "TEST", Dxcc = dxcc, State = state, Band = band, LotwConfirmed = lotw,
            StationCallsign = profile, Mode = "FT8", QsoDate = new DateTime(2026, 9, 20), TimeOn = "120000"
        };
        AdifQso[] qsos = [Q("20m", true), Q("20m", true), Q("20m"), Q(" 15M "),
            Q("40m", true, profile: "2E0CCD"), Q("6m", true, "AK", "6"), Q("30m", false, "HI", "110"),
            Q("80m", true, "CA", "1"), Q("17m", true, ""), Q("10m", true, "DC"), Q("2m")];
        qsos[3].PaperConfirmed = qsos[3].EqslConfirmed = true;
        var states = StateAchievementCollator.Build(qsos);
        Check(states.Count == 50 && states.Select(r => r.StateCode).Distinct().Count() == 50, "All 50 states exactly once.");
        var ca = states.Single(r => r.StateCode == "CA");
        Check(ca.EntityName == "California" && ca.QsoCount == 6 && ca.LotwConfirmedQsoCount == 3, "State totals exclude a foreign CA, include off-matrix bands.");
        Check(ca.BandCells.Count == 11 && string.Join(",", ca.BandCells.Select(c => c.Band)) == "160m,80m,60m,40m,30m,20m,17m,15m,12m,10m,6m", "Requested bands and order.");
        var twenty = ca.BandCells.Single(c => c.Band == "20m");
        Check(twenty.DisplayCount == "2" && twenty.WorkedCount == 3 && twenty.StatusKey == "LotwConfirmed", "Mixed worked/confirmed cell displays confirmed count, not total.");
        Check(twenty.ToolTip.Contains("3 worked") && twenty.ToolTip.Contains("2 LoTW confirmed") && twenty.ToolTip.Contains("1 awaiting"), "Tooltip explains both counts.");
        var fifteen = ca.BandCells.Single(c => c.Band == "15m");
        Check(fifteen.DisplayCount == "1" && fifteen.StatusKey == "WorkedUnconfirmed", "Paper/eQSL do not turn a cell green; case and whitespace normalized.");
        Check(ca.BandCells[0].DisplayCount == "" && ca.BandCells[0].StatusKey == "Needed", "Unworked band is blank despite another band being confirmed.");
        Check(states.Single(r => r.StateCode == "AK").QsoCount == 1 && states.Single(r => r.StateCode == "HI").QsoCount == 1, "Alaska and Hawaii included.");
        Check(states.Single(r => r.StateCode == "TX").QsoCount == 0 && states.All(r => r.StateCode != "DC"), "Missing states visible; DC not a 51st state.");
        Check(StateAchievementCollator.Build([]).All(r => r.QsoCount == 0 && r.BandCells.All(b => b.StatusKey == "Needed")), "Empty ADIF shows 50 unworked states.");
        foreach (var band in AchievementBandCell.BandOrder)
        {
            var cell = AchievementBandCell.Build([Q(band), Q(band, true)]).Single(c => c.Band == band);
            Check(cell.WorkedCount == 2 && cell.LotwConfirmedCount == 1, $"Counts for {band}.");
        }
        var vm = new AchievementsViewModel();
        CallsignLogProfile[] profiles = [new("ALL", "", "All callsigns", qsos.Length, true, false, []), new("G1CEC", "G1CEC", "G1CEC", qsos.Length - 1, false, true, [])];
        DxccEntityDefinition[] entities = [new("291", "United States"), new("6", "Alaska"), new("110", "Hawaii"), new("1", "Canada"), new("209", "Belgium")];
        var resolver = new DxccResolver(Path.Combine(AppContext.BaseDirectory, "Data", "cty.csv"));
        var rarity = new DxccRarityService();
        vm.UpdateData(qsos, [], profiles, entities, resolver, rarity, "G1CEC");
        Check(vm.DxccRows.Single(r => r.DxccNumber == "291").BandCells[3].WorkedCount == 0 && vm.StateRows.Single(r => r.StateCode == "CA").BandCells[3].WorkedCount == 0, "Selected profile excludes old identity in both matrices.");
        Check(vm.StateDataSummary.Contains("2 US QSOs"), "Invalid/missing state records disclosed.");
        Check(vm.BuildQsoDetails(vm.StateRows.Single(r => r.StateCode == "CA")).Qsos.Count == 5, "State drill-down follows profile and eligibility.");
        Check(vm.BuildQsoDetails(vm.StateRows.Single(r => r.StateCode == "CA")).Title.Contains("California — CA"), "State history title is not DXCC.");
        vm.SelectedAreaIndex = 1;
        Check(vm.TotalEntities == 50 && vm.LotwConfirmedEntities == 2 && vm.WorkedUnconfirmedEntities == 1 && vm.NeededEntities == 47, "USA summary reflects states not countries.");
        vm.SearchText = "California";
        Check(vm.StateRows.Count == 1, "State name search.");
        vm.SelectedStatusFilter = "Needed";
        Check(vm.StateRows.Count == 0, "Status filter combines with search.");
        vm.SearchText = "";
        Check(vm.StateRows.Count == 47, "Needed state filter.");
        vm.SelectedStatusFilter = "All entities";
        vm.SelectedProfileKey = "ALL";
        Check(vm.StateRows.Single(r => r.StateCode == "CA").BandCells[3].LotwConfirmedCount == 1, "All profiles restores historic confirmation.");
        vm.SelectedAreaIndex = 0;
        Check(vm.TotalEntities == 5 && vm.DxccRows.Single(r => r.DxccNumber == "209").BandCells.All(c => c.WorkedCount == 0), "DXCC summary restored and never-worked country shown.");

        var app = new JtdxAutoResume.V3.App();
        app.InitializeComponent(); // resources only: no MainViewModel, settings, timers or radio
        var importCount = 0;
        var command = new RelayCommand(() => importCount++);
        var view = new AchievementsView { DataContext = new { Achievements = vm, LoadAdifCommand = command,
            RefreshAchievementsCommand = new RelayCommand(() => { }), SelectedAchievementProfileDisplayLabel = "All callsigns",
            LogbookStatus = "Example ADIF records for layout verification" } };
        var grid = (DataGrid)view.FindName("AchievementsGrid");
        var stateGrid = (DataGrid)view.FindName("StatesGrid");
        Check(grid.Columns.Skip(6).Take(11).Select(c => c.Header.ToString()).SequenceEqual(AchievementBandCell.BandOrder.Select(b => b.TrimEnd('m'))), "DXCC band headers inserted after totals.");
        Check(stateGrid.Columns.Skip(5).Take(11).Count() == 11, "State band columns present.");
        var output = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "preview");
        Render(view, output + "-dxcc.png", 1550, 820);
        var import = Descendants<Button>(view).Single(b => b.Content?.ToString() == "Import ADIF…");
        Check(ReferenceEquals(import.Command, command), "Import button binds the exact Settings import command.");
        import.Command.Execute(null);
        Check(importCount == 1, "Import button invokes command once, no duplicate import path.");
        var template = (DataTemplate)view.FindResource("AchievementBandTemplate");
        foreach (var (cell, color) in new[] { (twenty, "#FFDCFCE7"), (fifteen, "#FFFFF7D6"), (ca.BandCells[0], "#FFFFFFFF") })
        {
            var border = (Border)template.LoadContent(); border.DataContext = cell;
            border.Measure(new Size(43, 30)); border.Arrange(new Rect(0, 0, 43, 30)); border.UpdateLayout();
            Check(border.Background.ToString() == color, $"Actual template colour for {cell.StatusKey}.");
        }
        vm.SelectedAreaIndex = 1;
        Render(view, output + "-states.png", 1550, 820);
        Check(stateGrid.RowStyle != null && vm.TotalEntities == 50, "State tab inherits row styles and updates summary.");
        Render(view, output + "-states-compact.png", 1200, 720);
        Console.WriteLine($"Achievements smoke tests passed: {_checks} checks. No live settings or radio accessed.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var match2 in Descendants<T>(child)) yield return match2;
        }
    }

    private static void Render(AchievementsView view, string path, int width, int height)
    {
        view.Width = width; view.Height = height;
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); encoder.Save(file);
    }
}
