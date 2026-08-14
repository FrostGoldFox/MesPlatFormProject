using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VirtualPLC;

namespace Backend;

/// <summary>
/// 백엔드(PC)가 PLC와 주고받는 Socket+JSON 통신 클라이언트입니다.
/// 프레임 포맷은 VirtualPLC/PLC_API_SPEC.md를 따릅니다:
/// Magic(2) + Version(1) + Type(1) + OpCode(2) + CorrelationId(4) + BodyLength(4) + JSON(UTF-8), 전부 리틀 엔디언.
/// 이 클래스는 장비 Modbus에 관여하지 않는다 — 백엔드가 아는 통신 상대는 PLC 하나뿐이다.
/// </summary>
public sealed class NetWork : IAsyncDisposable
{
    private const ushort Magic = 0x5CA7;
    private const byte Version = 1;
    private const int HeaderLength = 14;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;
    private uint _nextCorrelationId = 1;

    public async Task ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();
    }

    public Task<RegisterProductResponse> RegisterProductAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        SendAsync<RegisterProductRequest, RegisterProductResponse>(
            PlcOpcode.RegisterProduct, new RegisterProductRequest(serialNumber), cancellationToken);

    public Task<SubmitVisionResultResponse> SubmitVisionResultAsync(
        string serialNumber,
        SmartFactoryActuator.TransferConveyor.InspetionConveyor.InspectionResult result,
        CancellationToken cancellationToken = default) =>
        SendAsync<SubmitVisionResultRequest, SubmitVisionResultResponse>(
            PlcOpcode.SubmitVisionResult, new SubmitVisionResultRequest(serialNumber, result), cancellationToken);

    public Task<GetStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<object, GetStatusResponse>(PlcOpcode.GetStatus, new { }, cancellationToken);

    public Task<ResetAlarmResponse> ResetAlarmAsync(CancellationToken cancellationToken = default) =>
        SendAsync<object, ResetAlarmResponse>(PlcOpcode.ResetAlarm, new { }, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        PlcOpcode opcode, TRequest payload, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("ConnectAsync를 먼저 호출해야 합니다.");
        }

        uint correlationId = _nextCorrelationId++;
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        byte[] frame = new byte[HeaderLength + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, Magic);
        frame[2] = Version;
        frame[3] = (byte)JsonPacketType.Request;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), correlationId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), body.Length);
        body.CopyTo(frame.AsSpan(HeaderLength));

        await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

        byte[] header = new byte[HeaderLength];
        await ReadExactAsync(header, cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt16LittleEndian(header) != Magic)
        {
            throw new InvalidOperationException("응답 프레임의 Magic 값이 올바르지 않습니다 — 연결을 다시 맺어야 합니다.");
        }

        var type = (JsonPacketType)header[3];
        int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        byte[] responseBody = new byte[bodyLength];
        await ReadExactAsync(responseBody, cancellationToken).ConfigureAwait(false);

        if (type == JsonPacketType.Error)
        {
            JsonError? error = JsonSerializer.Deserialize<JsonError>(responseBody, _jsonOptions);
            throw new InvalidOperationException($"PLC 오류 응답: {error?.Code} - {error?.Message}");
        }

        return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions)
            ?? throw new InvalidOperationException("응답 본문을 역직렬화하지 못했습니다.");
    }

    /// <summary>TCP는 메시지 경계가 없으므로 요청한 바이트 수를 채울 때까지 반복해서 읽는다.</summary>
    private async Task ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int received = 0;
        while (received < buffer.Length)
        {
            int read = await _stream!.ReadAsync(buffer.AsMemory(received), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException($"연결이 끊겼습니다 — {buffer.Length}바이트 중 {received}바이트만 수신됨");
            }

            received += read;
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
