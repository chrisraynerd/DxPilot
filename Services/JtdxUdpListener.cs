using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class JtdxUdpListener : IDisposable
{
    private const uint Magic = 0xADBCCBDA;
    private UdpClient? _udpClient;
    private UdpClient? _forwardClient;
    private CancellationTokenSource? _cts;
    private IPEndPoint? _forwardEndpoint;
    private long _packetCount;
    private long _decodeCount;

    public bool IsRunning => _udpClient != null;
    public IPEndPoint? LastSenderEndpoint { get; private set; }
    public string LastAppId { get; private set; } = "";
    public JtdxStatusMessage? LastStatus { get; private set; }
    public event Action<DecodeMessage>? DecodeReceived;
    public event Action<JtdxStatusMessage>? StatusMessageReceived;
    public event Action<string>? StatusChanged;

    public Task StartAsync(int port, bool forwardEnabled = false, string forwardHost = "127.0.0.1", int forwardPort = 0)
    {
        Stop();

        try
        {
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            if (forwardEnabled && forwardPort > 0)
            {
                _forwardClient = new UdpClient();
                _forwardEndpoint = new IPEndPoint(IPAddress.Parse(forwardHost), forwardPort);
            }

            _packetCount = 0;
            _decodeCount = 0;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            var forwardText = _forwardEndpoint == null ? "" : $" Forwarding raw packets to {_forwardEndpoint}.";
            StatusChanged?.Invoke($"UDP listener running on port {port}.{forwardText}");
        }
        catch (Exception ex)
        {
            _udpClient = null;
            StatusChanged?.Invoke($"UDP listener could not start on port {port}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
        _forwardClient?.Dispose();
        _udpClient = null;
        _forwardClient = null;
        _forwardEndpoint = null;
        _cts = null;
        StatusChanged?.Invoke("UDP listener stopped.");
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                LastSenderEndpoint = result.RemoteEndPoint;
                _packetCount++;

                if (_forwardClient != null && _forwardEndpoint != null)
                    await _forwardClient.SendAsync(result.Buffer, _forwardEndpoint, cancellationToken);

                if (TryParseDecode(result.Buffer, out var decode, out var warning))
                {
                    _decodeCount++;
                    LastAppId = decode.SourceAppId;
                    DecodeReceived?.Invoke(decode);
                }
                else if (TryParseStatus(result.Buffer, out var status))
                {
                    LastAppId = status.SourceAppId;
                    LastStatus = status;
                    StatusMessageReceived?.Invoke(status);
                }
                else if (!string.IsNullOrWhiteSpace(warning))
                    StatusChanged?.Invoke(warning);
                else if (_packetCount == 1 || _packetCount % 25 == 0)
                    StatusChanged?.Invoke(
                        _decodeCount == 0
                            ? $"UDP packets received: {_packetCount}. No decode message parsed yet."
                            : $"UDP packets received: {_packetCount}; decode messages parsed: {_decodeCount}.");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"UDP receive error: {ex.Message}");
            }
        }
    }

    public static bool TryParseDecode(byte[] packet, out DecodeMessage decode, out string warning)
    {
        decode = new DecodeMessage();
        warning = "";

        try
        {
            var reader = new NetworkMessageReader(packet);
            if (reader.ReadUInt32() != Magic)
            {
                warning = "Ignored UDP packet with unknown WSJT-X/JTDX magic value.";
                return false;
            }

            _ = reader.ReadUInt32(); // schema
            var messageType = reader.ReadUInt32();
            var appId = reader.ReadString();

            if (messageType != 2)
                return false;

            _ = reader.ReadBool(); // new
            var milliseconds = reader.ReadUInt32();
            var snr = reader.ReadInt32();
            var dt = reader.ReadDouble();
            var deltaFrequency = reader.ReadUInt32();
            var protocolMode = reader.ReadString();
            var mode = AmateurBandMapper.NormalizeMode(protocolMode);
            var text = reader.ReadString();
            var lowConfidence = reader.TryReadBool(out var low) && low;

            decode = DecodeText(new DecodeMessage
            {
                // The UDP timestamp identifies the start of the FT8 slot. The packet normally
                // arrives near the end of that slot, so it must not be used as "last heard".
                ReceivedAt = DateTime.Now,
                DecodeTime = TimeSpan.FromMilliseconds(milliseconds),
                Snr = snr,
                Dt = dt,
                AudioOffset = (int)deltaFrequency,
                Mode = mode,
                ProtocolMode = protocolMode,
                RawText = text.Trim(),
                SourceAppId = appId,
                LowConfidence = lowConfidence
            });
            Debug.WriteLine(decode.ParseDebugLine);

            return true;
        }
        catch (Exception ex)
        {
            warning = $"Ignored malformed UDP packet: {ex.Message}";
            return false;
        }
    }

    public static bool TryParseStatus(byte[] packet, out JtdxStatusMessage status)
    {
        status = new JtdxStatusMessage();

        try
        {
            var reader = new NetworkMessageReader(packet);
            if (reader.ReadUInt32() != Magic)
                return false;

            _ = reader.ReadUInt32(); // schema
            var messageType = reader.ReadUInt32();
            var appId = reader.ReadString();

            if (messageType != 1)
                return false;

            var dialFrequencyHz = reader.ReadUInt64();
            var mode = AmateurBandMapper.NormalizeMode(reader.ReadString());
            var dxCall = reader.ReadString();
            _ = reader.ReadString(); // report
            var txMode = AmateurBandMapper.NormalizeMode(reader.ReadString());
            var txEnabled = reader.ReadBool();
            var transmitting = reader.ReadBool();
            var decoding = reader.ReadBool();
            _ = reader.ReadUInt32(); // RX DF
            _ = reader.ReadUInt32(); // TX DF
            _ = reader.ReadString(); // DE call
            _ = reader.ReadString(); // DE grid
            _ = reader.ReadString(); // DX grid
            _ = reader.TryReadBool(out _); // TX watchdog
            _ = reader.TryReadString(out _); // sub-mode
            _ = reader.TryReadBool(out _); // fast mode
            _ = reader.TryReadByte(out _); // special operation mode
            _ = reader.TryReadUInt32(out _); // frequency tolerance
            _ = reader.TryReadUInt32(out var trPeriodSeconds);
            _ = reader.TryReadString(out _); // configuration name
            _ = reader.TryReadString(out var txMessage);

            status = new JtdxStatusMessage
            {
                ReceivedAt = DateTime.Now,
                SourceAppId = appId,
                DialFrequencyHz = dialFrequencyHz,
                Band = AmateurBandMapper.FromDialFrequency(dialFrequencyHz),
                Mode = mode,
                TxMode = txMode,
                TrPeriodSeconds = trPeriodSeconds,
                DxCall = dxCall.Trim().ToUpperInvariant(),
                TxMessage = txMessage.Trim(),
                TxEnabled = txEnabled,
                Transmitting = transmitting,
                Decoding = decoding
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DecodeMessage DecodeText(DecodeMessage decode)
    {
        decode = new Ft8MessageParser().Parse(decode);
        var parts = decode.RawText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        decode.State = parts.Select(CleanToken).FirstOrDefault(IsUsState) ?? "";
        decode.Band = InferBand(decode.AudioOffset);
        return decode;
    }

    private static bool IsLikelyModifier(string value)
    {
        return value.Length <= 4 && (value.All(char.IsLetter) || value.All(char.IsDigit));
    }

    private static bool IsLikelyCallsign(string value)
    {
        value = CleanToken(value);
        return value.Any(char.IsDigit) && value.Any(char.IsLetter) && value.Length is >= 3 and <= 12 && !IsGrid(value);
    }

    private static bool IsGrid(string value)
    {
        value = CleanToken(value);
        return value.Length is 4 or 6
            && char.IsLetter(value[0])
            && char.IsLetter(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3]);
    }

    private static string FindMostLikelyStationCall(string[] parts)
    {
        var calls = parts
            .Select(CleanToken)
            .Where(IsLikelyCallsign)
            .ToList();

        if (calls.Count == 0)
            return "";

        // In normal directed FT8 messages, the transmitting/heard station is usually the first call.
        return calls[0].ToUpperInvariant();
    }

    private static string CleanToken(string value)
    {
        return value.Trim().Trim('<', '>', ':', ';', ',', '.', '!', '?').ToUpperInvariant();
    }

    private static bool IsUsState(string value)
    {
        return value is "AL" or "AK" or "AZ" or "AR" or "CA" or "CO" or "CT" or "DE" or "FL" or "GA"
            or "HI" or "ID" or "IL" or "IN" or "IA" or "KS" or "KY" or "LA" or "ME" or "MD"
            or "MA" or "MI" or "MN" or "MS" or "MO" or "MT" or "NE" or "NV" or "NH" or "NJ"
            or "NM" or "NY" or "NC" or "ND" or "OH" or "OK" or "OR" or "PA" or "RI" or "SC"
            or "SD" or "TN" or "TX" or "UT" or "VT" or "VA" or "WA" or "WV" or "WI" or "WY";
    }

    private static string InferBand(int? audioOffset)
    {
        // Decode packets do not include dial band; avoid placeholder band text in operator-facing reasons.
        return "";
    }

    public void Dispose()
    {
        Stop();
    }

    private ref struct NetworkMessageReader
    {
        private ReadOnlySpan<byte> _data;
        private int _offset;

        public NetworkMessageReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public uint ReadUInt32()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (_offset + 4 > _data.Length)
                return false;

            value = ReadUInt32();
            return true;
        }

        public ulong ReadUInt64()
        {
            Ensure(8);
            var value = BinaryPrimitives.ReadUInt64BigEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return value;
        }

        public int ReadInt32()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public bool ReadBool()
        {
            Ensure(1);
            return _data[_offset++] != 0;
        }

        public bool TryReadBool(out bool value)
        {
            value = false;
            if (_offset >= _data.Length)
                return false;

            value = ReadBool();
            return true;
        }

        public bool TryReadByte(out byte value)
        {
            value = 0;
            if (_offset >= _data.Length)
                return false;

            value = _data[_offset++];
            return true;
        }

        public double ReadDouble()
        {
            Ensure(8);
            var bits = BinaryPrimitives.ReadInt64BigEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public string ReadString()
        {
            var length = ReadUInt32();
            if (length == 0xFFFFFFFF)
                return "";
            if (length == 0)
                return "";
            if (length > _data.Length - _offset)
                throw new InvalidDataException("String length exceeds packet length.");

            var bytes = _data.Slice(_offset, (int)length).ToArray();
            _offset += (int)length;
            return DecodeNetworkString(bytes);
        }

        public bool TryReadString(out string value)
        {
            value = "";
            if (_offset + 4 > _data.Length)
                return false;

            value = ReadString();
            return true;
        }

        private static string DecodeNetworkString(byte[] bytes)
        {
            // WSJT-X/JTDX NetworkMessage strings are length-prefixed byte arrays.
            // Most live decode payloads are UTF-8; older/variant senders may still look like UTF-16.
            if (LooksLikeUtf16BigEndian(bytes))
                return Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0');

            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }

        private static bool LooksLikeUtf16BigEndian(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes.Length % 2 != 0)
                return false;

            var zeroHighBytes = 0;
            for (var i = 0; i < bytes.Length; i += 2)
            {
                if (bytes[i] == 0 && bytes[i + 1] is >= 0x20 and <= 0x7E)
                    zeroHighBytes++;
            }

            return zeroHighBytes >= bytes.Length / 4;
        }

        private void Ensure(int count)
        {
            if (_offset + count > _data.Length)
                throw new EndOfStreamException("Packet ended before all fields were available.");
        }
    }
}
