using System.Buffers.Binary;
using System.Text.Json;

namespace LanRemoteControl.Shared;

/// <summary>二进制协议序列化/反序列化工具类</summary>
public static class ProtocolSerializer
{
    /// <summary>FrameHeader 固定二进制布局大小（字节）</summary>
    public const int FrameHeaderSize = 24; // 4+4+4+4+8

    /// <summary>InputCommand 固定二进制布局大小（字节）</summary>
    public const int InputCommandSize = 18; // 1+4+4+1+1+4+2+1

    /// <summary>消息头大小：1 字节 MessageType + 4 字节 PayloadLength</summary>
    public const int MessageHeaderSize = 5;

    #region Message Frame Read/Write

    /// <summary>写入消息帧到流：1 字节 MessageType + 4 字节 PayloadLength (LE) + 变长 Payload</summary>
    public static void WriteMessage(Stream stream, MessageType type, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[MessageHeaderSize];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], payload.Length);
        stream.Write(header);
        stream.Write(payload);
    }

    /// <summary>异步写入消息帧到流</summary>
    public static async Task WriteMessageAsync(Stream stream, MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var header = new byte[MessageHeaderSize];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    /// <summary>从流中读取消息帧，返回 (MessageType, payload)</summary>
    public static (MessageType Type, byte[] Payload) ReadMessage(Stream stream)
    {
        Span<byte> header = stackalloc byte[MessageHeaderSize];
        ReadExact(stream, header);

        var type = (MessageType)header[0];
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[1..]);

        var payload = new byte[length];
        if (length > 0)
            ReadExact(stream, payload);

        return (type, payload);
    }

    /// <summary>异步从流中读取消息帧</summary>
    public static async Task<(MessageType Type, byte[] Payload)> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[MessageHeaderSize];
        await ReadExactAsync(stream, header, ct).ConfigureAwait(false);

        var type = (MessageType)header[0];
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));

        var payload = new byte[length];
        if (length > 0)
            await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);

        return (type, payload);
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading message.");
            offset += read;
        }
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading message.");
            offset += read;
        }
    }

    #endregion

    #region FrameHeader Binary Serialization

    /// <summary>将 FrameHeader 写入 24 字节缓冲区</summary>
    public static void WriteFrameHeader(Span<byte> buffer, FrameHeader header)
    {
        if (buffer.Length < FrameHeaderSize)
            throw new ArgumentException($"Buffer must be at least {FrameHeaderSize} bytes.", nameof(buffer));

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, header.SequenceNumber);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], header.Width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], header.Height);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], header.CompressedLength);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[16..], header.TimestampTicks);
    }

    /// <summary>从 24 字节缓冲区读取 FrameHeader</summary>
    public static FrameHeader ReadFrameHeader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < FrameHeaderSize)
            throw new ArgumentException($"Buffer must be at least {FrameHeaderSize} bytes.", nameof(buffer));

        return new FrameHeader(
            SequenceNumber: BinaryPrimitives.ReadUInt32LittleEndian(buffer),
            Width: BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]),
            Height: BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]),
            CompressedLength: BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]),
            TimestampTicks: BinaryPrimitives.ReadInt64LittleEndian(buffer[16..])
        );
    }

    #endregion

    #region InputCommand Binary Serialization

    /// <summary>将 InputCommand 写入 18 字节缓冲区</summary>
    public static void WriteInputCommand(Span<byte> buffer, InputCommand cmd)
    {
        if (buffer.Length < InputCommandSize)
            throw new ArgumentException($"Buffer must be at least {InputCommandSize} bytes.", nameof(buffer));

        buffer[0] = (byte)cmd.Type;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[1..], cmd.X);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[5..], cmd.Y);
        buffer[9] = (byte)cmd.Button;
        buffer[10] = (byte)cmd.ClickType;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[11..], cmd.ScrollDelta);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[15..], cmd.VirtualKeyCode);
        buffer[17] = cmd.IsKeyDown ? (byte)1 : (byte)0;
    }

    /// <summary>从 18 字节缓冲区读取 InputCommand</summary>
    public static InputCommand ReadInputCommand(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < InputCommandSize)
            throw new ArgumentException($"Buffer must be at least {InputCommandSize} bytes.", nameof(buffer));

        return new InputCommand
        {
            Type = (InputType)buffer[0],
            X = BinaryPrimitives.ReadInt32LittleEndian(buffer[1..]),
            Y = BinaryPrimitives.ReadInt32LittleEndian(buffer[5..]),
            Button = (MouseButton)buffer[9],
            ClickType = (ClickType)buffer[10],
            ScrollDelta = BinaryPrimitives.ReadInt32LittleEndian(buffer[11..]),
            VirtualKeyCode = BinaryPrimitives.ReadUInt16LittleEndian(buffer[15..]),
            IsKeyDown = buffer[17] != 0
        };
    }

    #endregion

    #region Session Management JSON Serialization

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static byte[] SerializeSessionRequest(SessionRequestPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, s_jsonOptions);

    public static SessionRequestPayload DeserializeSessionRequest(byte[] data) =>
        JsonSerializer.Deserialize<SessionRequestPayload>(data, s_jsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize SessionRequestPayload.");

    public static byte[] SerializeSessionResponse(SessionResponsePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, s_jsonOptions);

    public static SessionResponsePayload DeserializeSessionResponse(byte[] data) =>
        JsonSerializer.Deserialize<SessionResponsePayload>(data, s_jsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize SessionResponsePayload.");

    public static byte[] SerializeDiscoveryResponse(DiscoveryResponsePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, s_jsonOptions);

    public static DiscoveryResponsePayload DeserializeDiscoveryResponse(byte[] data) =>
        JsonSerializer.Deserialize<DiscoveryResponsePayload>(data, s_jsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize DiscoveryResponsePayload.");

    #endregion
}
