using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;

namespace SmartFactoryActuator.TransferConveyor.InfeedConveyor;

/// <summary>
/// Infeed Conveyor의 Modbus TCP Slave(Server)입니다.
/// PLC는 Coil로 명령을 쓰고, Discrete Input으로 장비 상태를 읽습니다.
/// </summary>
public sealed class InfeedConveyorNetWork : IAsyncDisposable
{
    public const byte Id = 0x10;

    private const ushort RunCoil = 0;
    private const ushort StopCoil = 1;
    private const ushort TransferCoil = 2;
    private const ushort RunningInput = 0;
    private const ushort PositionStartInput = 1;

    private readonly InfeedConveyorWork _work;
    private readonly TcpListener _listener;
    private readonly SlaveDataStore _dataStore = new();

    public InfeedConveyorNetWork(InfeedConveyorWork work, IPAddress ipConfig, int port)
    {
        _work = work;
        _listener = new TcpListener(ipConfig, port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();

        var factory = new ModbusFactory();
        var network = factory.CreateSlaveNetwork(_listener);
        var slave = factory.CreateSlave(Id, _dataStore);
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
            ReadPlcCommands();

            if (_work.IsRunning && DateTimeOffset.UtcNow >= nextMoveAt)
            {
                _work.TryMoveOneStep();
                nextMoveAt = DateTimeOffset.UtcNow.AddMilliseconds(500);
            }

            PublishState();
            await Task.Delay(100, cancellationToken);
        }
    }

    private void ReadPlcCommands()
    {
        bool[] commands = _dataStore.CoilDiscretes.ReadPoints(RunCoil, 3);

        if (commands[0])
        {
            _work.Start();
            _dataStore.CoilDiscretes.WritePoints(RunCoil, [false]);
        }

        if (commands[1])
        {
            _work.Stop();
            _dataStore.CoilDiscretes.WritePoints(StopCoil, [false]);
        }

        if (commands[2])
        {
            _work.TryTakeOutput(out _);
            _dataStore.CoilDiscretes.WritePoints(TransferCoil, [false]);
        }
    }

    private void PublishState()
    {
        bool[] inputs = new bool[InfeedConveyorWork.PositionCount + 1];
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