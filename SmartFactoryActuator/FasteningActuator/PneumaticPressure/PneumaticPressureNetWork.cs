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
    private const ushort StartCoil = 0;
    private const ushort StopCoil = 1;
    private const ushort RunningInput = 0;
    private readonly PneumaticPressureWork _work;
    private readonly TcpListener _listener;
    private readonly SlaveDataStore _dataStore = new();

    public PneumaticPressureNetWork(PneumaticPressureWork work, IPAddress ipConfig, int port)
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
        network.AddSlave(factory.CreateSlave(Id, _dataStore));
        try { await Task.WhenAll(network.ListenAsync(cancellationToken), DeviceLoopAsync(cancellationToken)); }
        finally { _listener.Stop(); }
    }

    private async Task DeviceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool[] commands = _dataStore.CoilDiscretes.ReadPoints(StartCoil, 2);
            if (commands[0]) { _work.Start(); _dataStore.CoilDiscretes.WritePoints(StartCoil, [false]); }
            if (commands[1]) { _work.Stop(); _dataStore.CoilDiscretes.WritePoints(StopCoil, [false]); }
            _work.TargetPressure = _dataStore.HoldingRegisters.ReadPoints(TargetPressureRegister, 1)[0];
            _work.Tick();
            _dataStore.CoilInputs.WritePoints(RunningInput, [_work.IsRunning]);
            _dataStore.HoldingRegisters.WritePoints(StateRegister, [(ushort)_work.SensorState]);
            _dataStore.HoldingRegisters.WritePoints(CurrentPressureRegister, [_work.CurrentPressure]);
            await Task.Delay(100, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() { _listener.Stop(); return ValueTask.CompletedTask; }
}