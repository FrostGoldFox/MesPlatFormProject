using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;

namespace SmartFactoryActuator.TransferConveyor.InspetionConveyor;

public sealed class InspetionConveyorNetWork : IAsyncDisposable
{
    public const byte UnitId = 0x11;
    public const ushort RunCommandCoil = 0;
    public const ushort StopCommandCoil = 1;
    public const ushort ResetCommandCoil = 2;
    public const ushort AcceptCommandCoil = 3;
    public const ushort RunningInput = 0;
    public const ushort PositionStartInput = 1;

    private readonly InspetionConveyorWork _work;
    private readonly TcpListener _listener;
    private readonly SlaveDataStore _dataStore = new();
    private readonly ModbusFactory _factory = new();

    public InspetionConveyorNetWork(InspetionConveyorWork work, IPAddress address, int port)
    {
        _work = work;
        _listener = new TcpListener(address, port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();

        var network = _factory.CreateSlaveNetwork(_listener);
        var slave = _factory.CreateSlave(UnitId, _dataStore);
        network.AddSlave(slave);

        try
        {
            await Task.WhenAll(
                network.ListenAsync(cancellationToken),
                DeviceLoopAsync(cancellationToken));
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task DeviceLoopAsync(CancellationToken cancellationToken)
    {
        var nextMoveAt = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            ProcessPlcCommands();

            if (_work.IsRunning && DateTimeOffset.UtcNow >= nextMoveAt)
            {
                _work.TryMoveOneStep();
                nextMoveAt = DateTimeOffset.UtcNow.AddMilliseconds(500);
            }

            PublishDeviceState();

            await Task.Delay(100, cancellationToken);
        }
    }

    private void ProcessPlcCommands()
    {
        bool[] commands = _dataStore.CoilDiscretes.ReadPoints(RunCommandCoil, 4);

        if (commands[0])
        {
            _work.Start();
            _dataStore.CoilDiscretes.WritePoints(RunCommandCoil, [false]);
        }

        if (commands[1])
        {
            _work.Stop();
            _dataStore.CoilDiscretes.WritePoints(StopCommandCoil, [false]);
        }

        if (commands[2])
        {
            _work.TryTakeOutput(out _);
            _dataStore.CoilDiscretes.WritePoints(ResetCommandCoil, [false]);
        }

        if (commands[3])
        {
            // 실제 Serial은 Modbus로 전달할 수 없어 자리 표시용 값만 싣는다 — 위치 점유 시뮬레이션이 목적이다.
            _work.TryAcceptInput(new ConveyorProduct("INSPECTION-MANUAL"));
            _dataStore.CoilDiscretes.WritePoints(AcceptCommandCoil, [false]);
        }
    }

    private void PublishDeviceState()
    {
        bool[] inputs = new bool[InspetionConveyorWork.PositionCount + 1];
        inputs[RunningInput] = _work.IsRunning;

        bool[] positions = _work.GetOccupancySnapshot();
        Array.Copy(positions, 0, inputs, PositionStartInput, positions.Length);

        _dataStore.CoilInputs.WritePoints(RunningInput, inputs);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
