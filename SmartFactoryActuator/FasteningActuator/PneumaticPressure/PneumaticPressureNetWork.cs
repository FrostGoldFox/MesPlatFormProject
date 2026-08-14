using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;

namespace SmartFactoryActuator.FasteningActuator.PneumaticPressure;

public sealed class PneumaticPressureNetWork : IAsyncDisposable
{
    public const byte Id = 0x20;

    private const ushort StateRegister = 0;
    private const ushort TargetPressureRegister = 1;
    private const ushort CurrentPressureRegister = 2;

    private readonly PneumaticPressureWork _work;
    private readonly TcpListener _listener;
    private readonly SlaveDataStore _dataStore = new();

    public PneumaticPressureNetWork(
        PneumaticPressureWork work,
        IPAddress ipConfig,
        int port)
    {
        _work = work;
        _listener = new TcpListener(ipConfig, port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();

        _dataStore.HoldingRegisters.WritePoints(TargetPressureRegister, [_work.TargetPressure]);

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
            ReadTargetPressureCommand();
            _work.Tick();
            PublishState();

            await Task.Delay(100, cancellationToken);
        }
    }

    private void ReadTargetPressureCommand()
    {
        _work.TargetPressure = _dataStore.HoldingRegisters.ReadPoints(TargetPressureRegister, 1)[0];
    }
    private void PublishState()
    {
        _dataStore.HoldingRegisters.WritePoints(
            StateRegister,
            [(ushort)_work.SensorState]);

        _dataStore.HoldingRegisters.WritePoints(
            CurrentPressureRegister,
            [_work.CurrentPressure]);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}