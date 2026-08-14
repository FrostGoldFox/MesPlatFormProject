using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

/// <summary>WPF/MES와 VirtualPLC 사이에서 사용하는 JSON 패킷 종류입니다.</summary>
public enum JsonPacketType : byte
{
    Request = 1,
    Response = 2,
    Error = 3,
    Notification = 4
}

/// <summary>외부 클라이언트가 VirtualPLC로 보내는 요청입니다.</summary>
public sealed record JsonRequest(ushort OpCode, uint CorrelationId, JsonElement Payload, IPEndPoint? RemoteEndPoint);

/// <summary>오류 패킷의 JSON 본문입니다. 내부 예외의 stack trace는 외부에 보내지 않습니다.</summary>
public sealed record JsonError(string Code, string Message);

/// <summary>
/// WPF/MES ↔ VirtualPLC용 비동기 TCP JSON 서버입니다.
/// PLC ↔ Sensor/Actuator Modbus 통신과 분리된 외부 API 계층입니다.
/// 프레임: Magic(2) + Version(1) + Type(1) + OpCode(2) + CorrelationId(4) + BodyLength(4) + JSON UTF-8.
/// 모든 정수는 리틀 엔디언이며, TCP 부분 수신은 ReadExactAsync로 처리합니다.
/// </summary>
public sealed class JsonNetWork : IAsyncDisposable
{
    private const ushort Magic = 0x5CA7;
    private const byte Version = 1;
    private const int HeaderLength = 14;
    private const int MaxBodyLength = 1024 * 1024;

    private readonly TcpListener _listener;
    private readonly Func<JsonRequest, CancellationToken, ValueTask<object?>> _requestHandler;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public JsonNetWork(IPAddress address, int port, Func<JsonRequest, CancellationToken, ValueTask<object?>> requestHandler)
    {
        _listener = new TcpListener(address, port);
        _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        var sessions = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }

                sessions.Add(HandleClientAsync(client, cancellationToken));
                sessions.RemoveAll(task => task.IsCompleted);
            }
        }
        finally
        {
            _listener.Stop();
            await Task.WhenAll(sessions).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using NetworkStream stream = client.GetStream();
            IPEndPoint? remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await ReceiveFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (frame is null) return;

                    if (frame.Type != JsonPacketType.Request || frame.CorrelationId == 0)
                        throw new InvalidDataException("Request 패킷과 0이 아닌 CorrelationId가 필요합니다.");

                    var request = new JsonRequest(frame.OpCode, frame.CorrelationId, frame.Payload, remoteEndPoint);
                    try
                    {
                        object? response = await _requestHandler(request, cancellationToken).ConfigureAwait(false);
                        await SendFrameAsync(stream, JsonPacketType.Response, request.OpCode, request.CorrelationId, response, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        await SendFrameAsync(stream, JsonPacketType.Error, request.OpCode, request.CorrelationId,
                            new JsonError("HANDLER_ERROR", exception.Message), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (SocketException) { }
            catch (InvalidDataException) { }
        }
    }

    private async Task<JsonFrame?> ReceiveFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderLength];
        if (!await ReadExactAsync(stream, header, true, cancellationToken).ConfigureAwait(false)) return null;

        if (BinaryPrimitives.ReadUInt16LittleEndian(header) != Magic)
            throw new InvalidDataException("지원하지 않는 Magic 값입니다.");
        if (header[2] != Version)
            throw new InvalidDataException($"지원하지 않는 프로토콜 버전입니다: {header[2]}");

        int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        if (bodyLength < 0 || bodyLength > MaxBodyLength)
            throw new InvalidDataException($"본문 길이가 허용 범위를 벗어났습니다: {bodyLength}");

        byte[] body = new byte[bodyLength];
        await ReadExactAsync(stream, body, false, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        return new JsonFrame((JsonPacketType)header[3], BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(6)), document.RootElement.Clone());
    }

    private async Task SendFrameAsync(NetworkStream stream, JsonPacketType type, ushort opCode, uint correlationId, object? payload, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        if (body.Length > MaxBodyLength) throw new InvalidDataException($"응답 본문이 허용 범위를 벗어났습니다: {body.Length}");

        byte[] frame = new byte[HeaderLength + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, Magic);
        frame[2] = Version;
        frame[3] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), opCode);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), correlationId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), body.Length);
        body.CopyTo(frame.AsSpan(HeaderLength));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, bool allowEmpty, CancellationToken cancellationToken)
    {
        int received = 0;
        while (received < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[received..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (received == 0 && allowEmpty) return false;
                throw new IOException("프레임을 모두 받기 전에 원격 연결이 종료되었습니다.");
            }
            received += read;
        }
        return true;
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private sealed record JsonFrame(JsonPacketType Type, ushort OpCode, uint CorrelationId, JsonElement Payload);
}