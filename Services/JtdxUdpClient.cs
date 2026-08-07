using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class JtdxUdpClient
{
    private const uint Magic = 0xADBCCBDA;
    private const uint Schema = 2;
    private const uint ReplyMessageType = 4;

    public async Task SendReplyAsync(DecodeMessage decode, string appId, int port, CancellationToken cancellationToken = default)
    {
        await SendReplyAsync(decode, appId, new IPEndPoint(IPAddress.Loopback, port), cancellationToken);
    }

    public async Task SendReplyAsync(DecodeMessage decode, string appId, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decode.RawText))
            throw new InvalidOperationException("No decode text is available to reply to.");

        var packet = BuildReplyPacket(decode, appId, Encoding.UTF8);
        using var client = new UdpClient();
        await client.SendAsync(packet, endpoint, cancellationToken);

        // Some JTDX/WSJT-X builds expect Qt QString-style UTF-16BE strings for commands,
        // while others emit live decode text as UTF-8. Sending both variants is harmless
        // for Reply because they describe the same decode and improves compatibility.
        var qtStringPacket = BuildReplyPacket(decode, appId, Encoding.BigEndianUnicode);
        await Task.Delay(40, cancellationToken);
        await client.SendAsync(qtStringPacket, endpoint, cancellationToken);
    }

    private static byte[] BuildReplyPacket(DecodeMessage decode, string appId, Encoding stringEncoding)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, Magic);
        WriteUInt32(stream, Schema);
        WriteUInt32(stream, ReplyMessageType);
        WriteString(stream, appId, stringEncoding);

        // WSJT-X/JTDX Reply mirrors a user double-clicking a decode. JTDX variants can differ,
        // so V3 keeps mouse Enable TX recovery as the fallback if UDP requests are not accepted.
        WriteUInt32(stream, (uint)(decode.DecodeTime?.TotalMilliseconds ?? 0));
        WriteInt32(stream, decode.Snr);
        WriteDouble(stream, decode.Dt);
        WriteUInt32(stream, (uint)Math.Max(0, decode.AudioOffset ?? 0));
        // Reply must echo the exact mode field from the Decode packet. JTDX uses
        // protocol markers such as "~" for FT8 and "+" for FT4; the normalized
        // FT8/FT4 value is retained separately for DX Pilot's own logic/UI.
        WriteString(
            stream,
            string.IsNullOrWhiteSpace(decode.ProtocolMode) ? decode.Mode : decode.ProtocolMode,
            stringEncoding);
        WriteString(stream, decode.RawText, stringEncoding);
        WriteBool(stream, decode.LowConfidence);
        WriteByte(stream, 0); // keyboard modifiers

        return stream.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        stream.Write(buffer);
    }

    private static void WriteBool(Stream stream, bool value)
    {
        stream.WriteByte(value ? (byte)1 : (byte)0);
    }

    private static void WriteByte(Stream stream, byte value)
    {
        stream.WriteByte(value);
    }

    private static void WriteString(Stream stream, string value, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value ?? "");
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }
}
