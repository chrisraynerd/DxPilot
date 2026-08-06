using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class JtdxAllTxtMonitor : IDisposable
{
    private static readonly Regex MonthlyFileName = new(
        @"^\d{6}_ALL\.TXT$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TransmissionLine = new(
        @"^(?<stamp>\d{8}_\d{6}(?:\.\d{3})?)\(\d+\)\s+(?<kind>Transmitting|Retransmitting)\s+.+?\s+(?<mode>FT8|FT4):\s+(?<message>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SemaphoreSlim _readGate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private string _configuredPath = "";
    private string _activePath = "";
    private long _position;
    private string _partialLine = "";
    private bool _disposed;

    public string ConfiguredPath => _configuredPath;
    public string ActivePath => _activePath;
    public bool IsRunning => _watcher != null;

    public event Action<JtdxOutgoingTransmission>? TransmissionObserved;
    public event Action<string>? StatusChanged;

    public void Start(string configuredPath)
    {
        Stop();
        _configuredPath = configuredPath?.Trim() ?? "";
        _activePath = ResolveCurrentPath(_configuredPath);
        _partialLine = "";

        var directory = Path.GetDirectoryName(_activePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusChanged?.Invoke($"JTDX ALL.TXT monitor not started; folder missing: {directory}");
            return;
        }

        _position = File.Exists(_activePath) ? new FileInfo(_activePath).Length : 0;
        var fileName = Path.GetFileName(_activePath);
        var filter = MonthlyFileName.IsMatch(fileName) ? "*_ALL.TXT" : fileName;
        _watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
                | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        StatusChanged?.Invoke(
            File.Exists(_activePath)
                ? $"Watching JTDX outgoing messages: {_activePath}"
                : $"Watching for JTDX outgoing-message file: {_activePath}");
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _position = 0;
        _partialLine = "";
    }

    public static string DefaultCurrentPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JTDX",
            $"{DateTime.UtcNow:yyyyMM}_ALL.TXT");
    }

    public static string ResolveCurrentPath(string? configuredPath)
    {
        var configured = configuredPath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultCurrentPath();
        if (Directory.Exists(configured))
            return Path.Combine(configured, $"{DateTime.UtcNow:yyyyMM}_ALL.TXT");

        var fileName = Path.GetFileName(configured);
        if (!MonthlyFileName.IsMatch(fileName))
            return configured;

        var directory = Path.GetDirectoryName(configured);
        return string.IsNullOrWhiteSpace(directory)
            ? DefaultCurrentPath()
            : Path.Combine(directory, $"{DateTime.UtcNow:yyyyMM}_ALL.TXT");
    }

    public static bool TryParseTransmission(string line, out JtdxOutgoingTransmission transmission)
    {
        transmission = new JtdxOutgoingTransmission();
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var match = TransmissionLine.Match(line);
        if (!match.Success)
            return false;

        var stampText = match.Groups["stamp"].Value;
        var formats = stampText.Contains('.', StringComparison.Ordinal)
            ? new[] { "yyyyMMdd_HHmmss.fff" }
            : new[] { "yyyyMMdd_HHmmss" };
        if (!DateTime.TryParseExact(
                stampText,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var loggedAtUtc))
        {
            return false;
        }

        transmission = new JtdxOutgoingTransmission
        {
            LoggedAtUtc = loggedAtUtc,
            ObservedAt = DateTime.Now,
            IsRetransmitting = match.Groups["kind"].Value.Equals("Retransmitting", StringComparison.OrdinalIgnoreCase),
            Mode = match.Groups["mode"].Value.Trim().ToUpperInvariant(),
            Message = Regex.Replace(match.Groups["message"].Value.Trim(), @"\s+", " "),
            RawLine = line
        };
        return !string.IsNullOrWhiteSpace(transmission.Message);
    }

    public static JtdxOutgoingMessageAnalysis AnalyseMessage(
        string message,
        string myCallsign,
        string expectedTargetCall)
    {
        var tokens = message
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanCallToken)
            .Where(token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
            return new JtdxOutgoingMessageAnalysis();

        if (tokens[0].Equals("CQ", StringComparison.OrdinalIgnoreCase))
        {
            return new JtdxOutgoingMessageAnalysis
            {
                Disposition = JtdxOutgoingMessageDisposition.Cq
            };
        }

        var expected = CleanCallToken(expectedTargetCall);
        if (!string.IsNullOrWhiteSpace(expected)
            && tokens.Any(token => token.Equals(expected, StringComparison.OrdinalIgnoreCase)))
        {
            return new JtdxOutgoingMessageAnalysis
            {
                Disposition = JtdxOutgoingMessageDisposition.ExpectedTarget,
                ObservedTargetCall = expected
            };
        }

        var mine = CleanCallToken(myCallsign);
        var observed = tokens
            .Where(token => !token.Equals(mine, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(LooksLikeCallsign) ?? "";
        return string.IsNullOrWhiteSpace(observed)
            ? new JtdxOutgoingMessageAnalysis()
            : new JtdxOutgoingMessageAnalysis
            {
                Disposition = JtdxOutgoingMessageDisposition.WrongTarget,
                ObservedTargetCall = observed
            };
    }

    public static IReadOnlyList<string> RunSelfTest()
    {
        var failures = new List<string>();
        const string targetLine = "20260726_003030.107(0)  Transmitting 18.1 MHz + 589Hz  FT8:  K4AAX G1CEC IO83";
        const string cqLine = "20260726_003100.072(0)  Transmitting 18.1 MHz + 589Hz  FT8:  CQ G1CEC IO83";
        const string retransmitLine = "20260726_000404.214(0)  Retransmitting 18.1 MHz +589Hz  FT8:  NP3DM G1CEC R-21";

        if (!TryParseTransmission(targetLine, out var target)
            || AnalyseMessage(target.Message, "G1CEC", "K4AAX").Disposition != JtdxOutgoingMessageDisposition.ExpectedTarget)
        {
            failures.Add("Expected-target transmission was not recognised.");
        }

        if (!TryParseTransmission(cqLine, out var cq)
            || AnalyseMessage(cq.Message, "G1CEC", "K4AAX").Disposition != JtdxOutgoingMessageDisposition.Cq)
        {
            failures.Add("CQ transmission was not recognised.");
        }

        if (!TryParseTransmission(retransmitLine, out var retransmit)
            || !retransmit.IsRetransmitting
            || AnalyseMessage(retransmit.Message, "G1CEC", "K4AAX").Disposition != JtdxOutgoingMessageDisposition.WrongTarget)
        {
            failures.Add("Wrong-target retransmission was not recognised.");
        }

        return failures;
    }

    private static string CleanCallToken(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('<', '>', '[', ']', '(', ')', ',', ':', ';')
            .ToUpperInvariant();
    }

    private static bool LooksLikeCallsign(string token)
    {
        return token.Length is >= 3 and <= 20
            && token.Any(char.IsLetter)
            && token.Any(char.IsDigit)
            && token.All(ch => char.IsLetterOrDigit(ch) || ch == '/');
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        _ = ReadNewLinesAsync();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs args)
    {
        _ = ReadNewLinesAsync();
    }

    private async Task ReadNewLinesAsync()
    {
        await _readGate.WaitAsync();
        try
        {
            var resolved = ResolveCurrentPath(_configuredPath);
            if (!resolved.Equals(_activePath, StringComparison.OrdinalIgnoreCase))
            {
                _activePath = resolved;
                _position = 0;
                _partialLine = "";
                StatusChanged?.Invoke($"JTDX ALL.TXT month rollover detected; now watching {_activePath}");
            }

            if (!File.Exists(_activePath))
                return;

            await using var stream = new FileStream(
                _activePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _position)
            {
                _position = 0;
                _partialLine = "";
            }
            if (stream.Length == _position)
                return;

            stream.Seek(_position, SeekOrigin.Begin);
            var length = checked((int)(stream.Length - _position));
            var buffer = new byte[length];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
                if (count <= 0)
                    break;
                read += count;
            }
            _position += read;

            var appended = _partialLine + Encoding.UTF8.GetString(buffer, 0, read);
            var lines = appended.Split('\n');
            _partialLine = appended.EndsWith('\n') ? "" : lines[^1];
            var completeCount = appended.EndsWith('\n') ? lines.Length : lines.Length - 1;
            for (var index = 0; index < completeCount; index++)
            {
                var line = lines[index].TrimEnd('\r');
                if (TryParseTransmission(line, out var transmission))
                    TransmissionObserved?.Invoke(transmission);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"JTDX ALL.TXT monitor error: {ex.GetBaseException().Message}");
        }
        finally
        {
            _readGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _readGate.Dispose();
    }
}
