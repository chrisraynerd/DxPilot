using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using JtdxAutoResume.V3.Models;
using MQTTnet;

namespace JtdxAutoResume.V3.Services;

public sealed class PskReporterClient : IAsyncDisposable
{
    private static readonly TimeSpan MinimumQueryInterval = TimeSpan.FromMinutes(5);
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    { Timeout = TimeSpan.FromSeconds(20) };
    private IMqttClient? _mqttClient;
    private DateTime _lastQueryAtUtc = DateTime.MinValue;
    private bool _intentionalStop;

    public PskReporterClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DXPilot-for-JTDX", "3.8"));
        _httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
    }

    public event Action<PskReporterSpot>? SpotReceived;
    public event Action<string>? StatusChanged;

    public bool IsLiveConnected => _mqttClient?.IsConnected == true;

    public async Task<bool> StartLiveAsync(string callsign, CancellationToken cancellationToken)
    {
        await StopLiveAsync();
        var normalizedCall = callsign.Trim().ToUpperInvariant();
        if (normalizedCall.Length == 0)
            return false;

        try
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();
            _intentionalStop = false;
            _mqttClient.ApplicationMessageReceivedAsync += args =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(args.ApplicationMessage.Payload.ToArray());
                    if (PskReporterParser.TryParseLiveJson(json, out var spot)
                        && spot.SenderCallsign.Equals(normalizedCall, StringComparison.OrdinalIgnoreCase))
                    {
                        SpotReceived?.Invoke(spot);
                    }
                }
                catch
                {
                }

                return Task.CompletedTask;
            };
            _mqttClient.DisconnectedAsync += args =>
            {
                if (!_intentionalStop)
                    StatusChanged?.Invoke("PSK Reporter live feed disconnected; the end-of-survey retrieval will still be attempted.");
                return Task.CompletedTask;
            };

            var clientId = $"DXPilot-{normalizedCall}-{Guid.NewGuid():N}";
            var options = new MqttClientOptionsBuilder()
                .WithClientId(clientId[..Math.Min(48, clientId.Length)])
                .WithTcpServer("mqtt.pskreporter.info", 1883)
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithTimeout(TimeSpan.FromSeconds(8))
                .Build();
            await _mqttClient.ConnectAsync(options, cancellationToken);
            var topic = $"pskr/filter/v2/+/FT8/{normalizedCall}/#";
            var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();
            await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken);
            StatusChanged?.Invoke($"PSK Reporter live feed connected for {normalizedCall}.");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"PSK Reporter live feed unavailable ({ex.GetBaseException().Message}); the survey can continue using one retrieval at the end.");
            await StopLiveAsync();
            return false;
        }
    }

    public async Task StopLiveAsync()
    {
        var client = _mqttClient;
        _mqttClient = null;
        if (client == null)
            return;
        _intentionalStop = true;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync();
        }
        catch
        {
        }
        client.Dispose();
    }

    public async Task<PskReporterQueryResult> QueryRecentAsync(
        string callsign,
        TimeSpan lookback,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var nextAllowed = _lastQueryAtUtc + MinimumQueryInterval;
        if (_lastQueryAtUtc != DateTime.MinValue && nowUtc < nextAllowed)
        {
            return new PskReporterQueryResult(
                [],
                false,
                $"Official PSK Reporter retrieval skipped to respect its five-minute request interval; live reports were retained. Next retrieval is available at {nextAllowed.ToLocalTime():HH:mm:ss}.");
        }

        _lastQueryAtUtc = nowUtc;
        try
        {
            var seconds = Math.Clamp((int)Math.Ceiling(lookback.TotalSeconds), 60, 86_400);
            var normalizedCall = callsign.Trim().ToUpperInvariant();
            var uri = "https://retrieve.pskreporter.info/query"
                + $"?senderCallsign={Uri.EscapeDataString(normalizedCall)}"
                + $"&flowStartSeconds=-{seconds}"
                + "&mode=FT8&rronly=1&noactive=1&nolocator=1&rptlimit=1000";
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var reports = PskReporterParser.ParseQueryXml(xml)
                .Where(report => report.SenderCallsign.Equals(normalizedCall, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new PskReporterQueryResult(reports, true, $"PSK Reporter retrieval returned {reports.Count} recent FT8 reports for matching against the survey probes.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PskReporterQueryResult([], false, $"PSK Reporter retrieval failed: {ex.GetBaseException().Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopLiveAsync();
        _httpClient.Dispose();
    }
}

public sealed record PskReporterQueryResult(
    IReadOnlyList<PskReporterSpot> Reports,
    bool Retrieved,
    string Status);
