using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VirtualPLC;

/// <summary>
/// WPF 테스트 화면의 8개 독립 명령을 받는 개발 단계 전용 TCP 서버입니다.
/// Command 1은 Infeed Position 1, Command 2~4는 공압 목표값을 갱신합니다.
/// </summary>
public sealed class TestNetWork : IAsyncDisposable
{
    private readonly InfeedConveyorDataModel _infeedModel;
    private readonly PneumaticPressureDataModel _pneumaticPressureModel;
    private readonly InspectionConveyorDataModel _inspectionModel;
    private readonly TcpListener _listener;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private int _command5Pending;
    private int _command6Pending;

    public bool command1;
    public bool command2;
    public bool command3;
    public bool command4;
    public bool command5;
    public bool command6;
    public bool command7;
    public bool command8;

    public TestNetWork(
        InfeedConveyorDataModel infeedModel,
        PneumaticPressureDataModel pneumaticPressureModel,
        InspectionConveyorDataModel inspectionModel,
        IPAddress address,
        int port)
    {
        _infeedModel = infeedModel;
        _pneumaticPressureModel = pneumaticPressureModel;
        _inspectionModel = inspectionModel;
        _listener = new TcpListener(address, port);
    }

    public sealed record CommandSignal(int Command, bool Value, ushort? TargetPressure = null);
    public sealed record CommandResponse(bool Accepted, int Command, bool Value, ushort? TargetPressure = null, string? Message = null);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) return;

                CommandSignal? signal;
                try
                {
                    signal = JsonSerializer.Deserialize<CommandSignal>(line, _jsonOptions);
                }
                catch (JsonException)
                {
                    await WriteResponseAsync(writer, false, 0, false, null, "JSON 형식이 올바르지 않습니다.");
                    continue;
                }

                if (signal is null || signal.Command is < 1 or > 8)
                {
                    await WriteResponseAsync(writer, false, signal?.Command ?? 0, signal?.Value ?? false, signal?.TargetPressure, "Command는 1~8이어야 합니다.");
                    continue;
                }

                if (!TrySetCommand(signal, out string? error))
                {
                    await WriteResponseAsync(writer, false, signal.Command, signal.Value, signal.TargetPressure, error);
                    continue;
                }

                ushort? appliedTargetPressure = signal.Command is 2 or 3 or 4
                    ? _pneumaticPressureModel.TargetPressure
                    : signal.TargetPressure;
                await WriteResponseAsync(writer, true, signal.Command, signal.Value, appliedTargetPressure);
            }
        }
    }

    private bool TrySetCommand(CommandSignal signal, out string? error)
    {
        error = null;
        switch (signal.Command)
        {
            case 1:
                command1 = signal.Value;
                _infeedModel.PositionOccupied[0] = signal.Value;
                return true;

            case 2:
            case 3:
            case 4:
                if (signal.TargetPressure is null)
                {
                    error = "TargetPressure 값이 필요합니다.";
                    return false;
                }

                if (signal.Command == 2) command2 = signal.Value;
                if (signal.Command == 3) command3 = signal.Value;
                if (signal.Command == 4) command4 = signal.Value;
                _pneumaticPressureModel.TargetPressure = signal.TargetPressure.Value;
                return true;

            case 5:
                command5 = signal.Value;
                _inspectionModel.PositionOccupied[11] = false;
                Interlocked.Exchange(ref _command5Pending, 1); // PLC 송신부가 한 번만 소비할 펄스
                return true;
            case 6:
                command6 = signal.Value;
                Interlocked.Exchange(ref _command6Pending, 1); // 체결설비→Inspection Position1 인계 신호, 한 번만 소비
                return true;
            case 7: command7 = signal.Value; return true;
            case 8: command8 = signal.Value; return true;
            default:
                error = "지원하지 않는 Command입니다.";
                return false;
        }
    }

    /// <summary>Command 5 수신 펄스를 한 번만 반환합니다.</summary>
    public bool TryConsumeCommand5() => Interlocked.Exchange(ref _command5Pending, 0) == 1;

    /// <summary>Command 6 수신 펄스를 한 번만 반환합니다.</summary>
    public bool TryConsumeCommand6() => Interlocked.Exchange(ref _command6Pending, 0) == 1;

    private Task WriteResponseAsync(StreamWriter writer, bool accepted, int command, bool value, ushort? targetPressure = null, string? message = null) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(new CommandResponse(accepted, command, value, targetPressure, message), _jsonOptions));

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}