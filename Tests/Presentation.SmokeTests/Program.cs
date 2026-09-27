using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using JtdxAutoResume.V3.Models;
using JtdxAutoResume.V3.Services;
using JtdxAutoResume.V3.ViewModels;
using JtdxAutoResume.V3.Views;

internal static class Program
{
 private static int _checks;
 private static string _root = "";
 private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); _checks++; }
 [STAThread]
 private static void Main()
 {
  _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
  var testRoot = Path.Combine(_root, "Tests", "Presentation.SmokeTests");
  foreach (var entry in JsonSerializer.Deserialize<ProtectedFile[]>(File.ReadAllText(Path.Combine(testRoot, "ProtectedLogicHashes.json")))!)
  {
   var bytes = File.ReadAllBytes(Path.Combine(_root, entry.Path));
   // Git checkouts can use LF or CRLF. Accept only this mechanical difference;
   // the original protected-content hashes stay unchanged.
   var source = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
   Check(new[] { bytes, System.Text.Encoding.UTF8.GetBytes(source), System.Text.Encoding.UTF8.GetBytes(source.Replace("\n", "\r\n")) }
    .Any(content => Convert.ToHexString(SHA256.HashData(content)) == entry.Hash), $"Presentation update changed protected logic: {entry.Path}");
  }
  var before = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(testRoot, "OriginalBindings.json")))!;
  var settingsSource = File.ReadAllText(Path.Combine(_root, "Views", "SettingsView.xaml"));
  var after = Regex.Matches(settingsSource, "(?:Command|Text|IsChecked|SelectedValue|ItemsSource)=\"(\\{Binding[^\"]*\\})\"").Select(m => m.Groups[1].Value).ToArray();
  foreach (var group in before.GroupBy(s => s))
   Check(after.Count(s => s == group.Key) == group.Count(), $"Existing settings binding must be preserved exactly: {group.Key}");
  XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
  var shell = XDocument.Load(Path.Combine(_root, "MainWindow.xaml"));
  Check(shell.Descendants().Count(e => e.Name.LocalName == "OperatingHeader") == 1, "Exactly one shared global header.");
  Check(File.ReadAllText(Path.Combine(_root, "Views", "WantedView.xaml")).Contains("Include current band"), "Operator requested keeping Include current band unchanged.");
  var app = new JtdxAutoResume.V3.App(); app.InitializeComponent(); // Never construct MainViewModel or start app services.
  var fake = new PreviewData();
  var header = new OperatingHeader { DataContext = fake };
  Render(header, "header", 1180, 170);
  var buttons = Descendants<Button>(header).ToArray();
  foreach (var command in new[] { fake.StartDxAssistCommand, fake.StartWantedSniperCommand, fake.StartLocationHuntCommand, fake.StartScavengerCommand, fake.StopAutoResumeCommand })
   Check(buttons.Count(b => ReferenceEquals(b.Command, command)) == 1, "Exactly one header control binds each original operating command.");
  Check(buttons.Single(b => ReferenceEquals(b.Command, fake.StartWantedSniperCommand)).Content.ToString() == "Wanted active", "Active mode wording is actually visible (not overridden by local Content).");
  fake.CurrentTargetStatus.SelectedTargetCall = "FR4OM";
  fake.CurrentTargetStatus.SelectedTargetDisplay = "FR4OM — Reunion";
  fake.CurrentTargetStatus.WantedReason = "New DXCC for G1CEC";
  Render(header, "header-target", 980, 200);
  Check(Descendants<TextBlock>(header).Any(t => t.Text == "FR4OM — Reunion"), "Shared target updates without page recreation.");
  Check(Descendants<TextBlock>(header).Any(t => t.Text == "New DXCC for G1CEC" && t.Visibility == Visibility.Visible), "Reason updates and becomes visible with a target.");
  fake.CurrentTargetStatus.SelectedTargetCall = "";
  Layout(header, 980, 200);
  Check(Descendants<TextBlock>(header).Single(t => t.Text == "New DXCC for G1CEC").Visibility == Visibility.Collapsed, "No stale wanted reason displayed without a selected target.");
  fake.CurrentTargetStatus.SelectedTargetCall = "FR4OM";
  var settings = new SettingsView { DataContext = fake };
  Render(settings, "settings", 1180, 780);
  var search = (TextBox)settings.FindName("SettingsSearchBox");
  var sections = ((StackPanel)settings.FindName("SettingsSections")).Children.OfType<Expander>().ToArray();
  Check(sections.Length == 13 && sections.Count(s => s.Visibility == Visibility.Visible) == 9, "Everyday settings first, advanced sections initially hidden.");
  var stateBefore = JsonSerializer.Serialize(fake.Settings.Settings);
  var confirmation = sections.Single(s => s.Header.ToString() == "Confirmation rules");
  confirmation.IsExpanded = false;
  search.Text = "tolerance"; Layout(settings, 1180, 780);
  Check(sections.Any(s => s.Header.ToString() == "Pixel detection" && s.Visibility == Visibility.Visible && s.IsExpanded), "Search reveals and opens relevant advanced controls.");
  search.Text = "no-such-setting-xyz";
  Check(((TextBlock)settings.FindName("SettingsNoMatches")).Visibility == Visibility.Visible, "Empty search results explained.");
  search.Text = "private-secret-value";
  Check(sections.All(s => s.Visibility == Visibility.Collapsed), "Search never indexes password values.");
  search.Clear();
  Check(!confirmation.IsExpanded && sections.Count(s => s.Visibility == Visibility.Visible) == 9, "Clearing search restores expanded state and advanced visibility.");
  Check(JsonSerializer.Serialize(fake.Settings.Settings) == stateBefore && fake.CommandExecutions == 0, "Layout and search never change settings or execute commands.");
  search.Text = "LoTW"; Render(settings, "settings-search", 1180, 780); search.Clear();

  var scavenger = new ScavengerView { DataContext = fake };
  RenderPage(scavenger, fake, "scavenger", 1500, 860);
  Check(!Descendants<Button>(scavenger).Any(b => ReferenceEquals(b.Command, fake.StartScavengerCommand) || ReferenceEquals(b.Command, fake.StopAutoResumeCommand)), "No duplicate Scavenger start/stop controls.");
  var scavengerSurface = new Border { Child = scavenger };
  Layout(scavengerSurface, 1472, 640);
  var chooser = Descendants<Expander>(scavenger).Single(e => e.Header.ToString() == "Choose bands");
  chooser.IsExpanded = true; Layout(scavengerSurface, 1472, 640);
  // Generate the expanded content explicitly in this offscreen, non-interactive renderer.
  ((FrameworkElement)chooser.Content).Measure(new Size(1400, 150));
  ((FrameworkElement)chooser.Content).Arrange(new Rect(0, 0, 1400, 150));
  ((FrameworkElement)chooser.Content).UpdateLayout();
  var bandCheckboxes = Descendants<CheckBox>(chooser).ToArray();
  Check(bandCheckboxes.Length == 12, $"All 12 band selectors still accessible (found {bandCheckboxes.Length}).");
  Check(bandCheckboxes.All(c => BindingOperations.GetBinding(c, CheckBox.IsCheckedProperty)?.Path.Path == "BandChoice.Enabled"), "Band choices retain original settings binding.");
  var blocked = new PreviewData { IsScavengerActive = true };
  blocked.CurrentTargetStatus.OperatingMode = "Scavenger active";
  blocked.Scavenger.Phase = "Calling wanted station"; blocked.Scavenger.Status = "Rotation paused · 40m";
  blocked.Scavenger.Bands[3].Status = "Calling FR4OM";
  scavenger.DataContext = blocked; Layout(scavengerSurface, 1472, 640);
  Check(Descendants<CheckBox>(chooser).All(c => !c.IsEnabled), "Band choices retain running-mode lock.");
  chooser.IsExpanded = false;
  var allBandCards = Descendants<Border>(scavenger).Where(b => b.DataContext is ScavengerBandRow && b.Width == 146).ToArray();
  Check(allBandCards.Count(b => b.Visibility == Visibility.Visible) == blocked.Scavenger.Bands.Count(b => b.BandChoice.Enabled), "Compact rotation shows only permitted bands.");
  scavengerSurface.Child = null;
  RenderPage(scavenger, blocked, "scavenger-active", 1180, 780);
  RenderPage(new WantedView { DataContext = fake }, fake, "wanted", 1500, 860);
  RenderPage(new LocationView { DataContext = fake }, fake, "location", 1500, 860);
  RenderPage(new DxAssistView { DataContext = fake }, fake, "dx-assist", 1500, 860);
  Console.WriteLine($"Presentation checks passed: {_checks}. No live settings or radio accessed.");
 }
 private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
 {
  for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
  { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) yield return found;
    foreach (var nested in Descendants<T>(child)) yield return nested; }
 }
 private static void Layout(FrameworkElement view, int width, int height)
 {
  view.Width = width; view.Height = height; view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height));
  view.UpdateLayout(); view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
 }
 private static void Render(FrameworkElement view, string name, int width, int height)
 {
  Layout(view, width, height);
  var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
  var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
  var folder = Path.Combine(_root, "build", "presentation-preview"); Directory.CreateDirectory(folder);
  using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
 }
 private static void RenderPage(UserControl content, PreviewData data, string name, int width, int height)
 {
  var panel = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(238,243,247)) };
  var header = new OperatingHeader { DataContext = data }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
  var title = new TextBlock { Text = "Live Monitor     DX Assist     Wanted     Location     Map     Scavenger     Session History     Settings     Achievements     Band Analysis", Margin = new Thickness(14,12,14,12) };
  DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
  var holder = new Border { Padding = new Thickness(14), Child = content }; panel.Children.Add(holder);
  Render(panel, name, width, height); holder.Child = null; panel.Children.Clear();
 }
 private sealed record ProtectedFile(string Path, string Hash);
 private sealed class PreviewData
 {
  public PreviewData()
  {
   BandAnalysis = new(Settings.Settings); Scavenger = new(() => Settings.Settings, BandAnalysis.Bands);
   foreach (var band in BandAnalysis.Bands) band.Enabled = new[] {"40m","30m","20m","17m","15m"}.Contains(band.Band);
   Scavenger.Phase = "Listening"; Scavenger.Status = "40m · 42 seconds remaining · round 3"; Scavenger.Round = 3;
   Scavenger.FilterSummary = "Hunting DXCC and grids · Overall + current band · New DXCC always has priority";
   Scavenger.Bands[3].Status = "Listening"; Scavenger.Bands[3].Activity = "23 stations"; Scavenger.Bands[3].Revisit = "Current band";
   Scavenger.Bands[7].Status = "Quiet"; Scavenger.Bands[7].Revisit = "Resting · 2 rounds";
   for (var p = 0; p < 3; p++) for (var i = 0; i < 6; i++)
   {
    var decode = new DecodeMessage { Callsign = $"TEST{i}", ContactableCall = $"TEST{i}", EntityName = "Layout sample", Grid = "JO01", Snr = -10, RawText = $"CQ TEST{i} JO01" };
    Scavenger.OpportunityPanels[p].Rows.Add(new(decode, p == 0 ? "New DXCC" : p == 1 ? "Unconfirmed grid" : "New state", "Eligible", false) { Category = p == 0 ? "DXCC" : p == 1 ? "grid" : "state", Need = NeedStatus.NeverWorked });
   }
   Wanted.AchievementProfiles.Add(new("G1CEC","G1CEC","G1CEC",9000,false,true,[]));
   Location.SelectAllAreasCommand.Execute(null);
   foreach (var area in Location.Areas) Location.Panels.Add(new(area.Key, area.Title));
   Settings.Settings.QrzPassword = "private-secret-value";
   StartDxAssistCommand = Command(); StartWantedSniperCommand = Command(); StartLocationHuntCommand = Command(); StartScavengerCommand = Command(); StopAutoResumeCommand = Command();
  }
  private RelayCommand Command() => new(() => CommandExecutions++);
  public int CommandExecutions { get; private set; }
  public SettingsViewModel Settings { get; } = new(); public WantedViewModel Wanted { get; } = new();
  public DxAssistViewModel DxAssist { get; } = new(); public LocationViewModel Location { get; } = new();
  public BandAnalysisViewModel BandAnalysis { get; } public ScavengerViewModel Scavenger { get; }
  public bool IsDxAssistActive => false; public bool IsWantedSniperActive => !IsScavengerActive; public bool IsLocationHuntActive => false;
  public bool IsScavengerActive { get; set; } public bool CanConfigureScavenger => !IsScavengerActive;
  public bool CanChangeAchievementProfile => false; public string SelectedAchievementProfileDisplayLabel => "G1CEC";
  public string SelectedAchievementProfileKey { get; set; } = "G1CEC"; public string AchievementProfileSummary => "Reviewing G1CEC";
  public string CurrentBand => "40m"; public string CurrentDigitalMode => "FT8";
  public bool SniperTargetsDxcc { get; set; } = true; public bool SniperTargetsGrids { get; set; } = true; public bool SniperTargetsStates { get; set; }
  public bool IncludeBandWanted { get; set; } = true; public bool IncludeModeWanted { get; set; } public bool IncludeBandModeWanted { get; set; }
  public bool KeepCallingNewDxccUntilStale { get; set; } public bool PrioritizeNewGridsInDxAssist { get; set; }
  public TargetStatusSummaryViewModel CurrentTargetStatus { get; } = new() { OperatingMode = "Wanted Sniper active", SelectedTargetCall = "FR4OM", SelectedTargetDisplay = "FR4OM — Reunion", WantedReason = "New DXCC for G1CEC", PlainStatusMessage = "Awaiting reply from FR4OM.", AttemptCounterLabel = "Call attempt 3/4", TxGateStatus = "TX allowed", ActualJtdxDxCall = "FR4OM" };
  public RelayCommand StartDxAssistCommand { get; } public RelayCommand StartWantedSniperCommand { get; } public RelayCommand StartLocationHuntCommand { get; } public RelayCommand StartScavengerCommand { get; } public RelayCommand StopAutoResumeCommand { get; }
 }
}
