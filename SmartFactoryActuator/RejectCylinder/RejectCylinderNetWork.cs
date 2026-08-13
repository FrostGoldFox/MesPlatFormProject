using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;

namespace SmartFactoryActuator.RejectCylinder;

/// <summary>
/// Reject Cylinder의 Modbus TCP Slave(Server)입니다.
/// PLC는 Coil로 전진·후진을 명령하고, Discrete Input으로 현재 상태를 읽습니다.
/// </summary>
public sealed class RejectCylinderNetWork : IAsyncDisposable
{
    public const byte Id = 0x40;

    private const ushort ExtendCoil = 0;
    private const ushort RetractCoil = 1;
    private const ushort ExtendedInput = 0;
    private const ushort RetractedInput = 1;

    private readonly RejectCylinderWork _work;
    private readonly TcpListener _listener;
    private readonly SlaveDataStore _dataStore = new();

    public RejectCylinderNetWork(RejectCylinderWork work, IPAddress ipConfig, int port)
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
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadPlcCommands();
            PublishState();

            await Task.Delay(50, cancellationToken);
        }
    }

    private void ReadPlcCommands()
    {
        bool[] commands = _dataStore.CoilDiscretes.ReadPoints(ExtendCoil, 2);

        if (commands[0])
        {
            _work.Extend();
            _dataStore.CoilDiscretes.WritePoints(ExtendCoil, [false]);
        }

        if (commands[1])
        {
            _work.Retract();
            _dataStore.CoilDiscretes.WritePoints(RetractCoil, [false]);
        }
    }

    private void PublishState()
    {
        _dataStore.CoilInputs.WritePoints(
            ExtendedInput,
            [_work.IsExtended, !_work.IsExtended]);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}