using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using NModbus.Extensions.Enron;
using NModbus.Serial;
using NModbus.Utility;

class ModbusNetWork : IAsyncDisposable
{
    readonly ModbusFactory factory = new();

    IPAddress Ipconfig;
    int port;
    TcpClient tcpclient;
    IModbusMaster master;

    public ModbusNetWork(IPAddress ip, int port)
    {
        this.Ipconfig = ip;
        this.port = port;
    }

    public async Task CreateMaster(CancellationToken cancellationToken = default)
    {
        tcpclient = new TcpClient(Ipconfig, port);              // tcp 연결 생성
        master = factory.CreateMaster(tcpclient);      // Connect dict에 deviceName 이라는 key에 IModbusMaster 타입의  Value 생성
    }

    public async Task<bool> RequestInfeedConveyorAsync(InfeedConveyorDataModel Model)
    {
        bool[] coils = await master.ReadCoilsAsync(slaveAddress: 0x10, startAddress: 0, numberOfPoints: 13) // Conveyor는 0x1_번, Infeed는 0번
                                                                                                            // coil 설정 값으로 0번 IsWorking, 1~12번 Position 값
        if (coils == null)
            return false;
        for (int i = 0; i < 13; i++)
        {
            if (i == 0)
                Model.IsWorking = coils[0]
            else
                Model.Positions[i - 1].IsObject = coils[i];
        }
    }
    public async Task<bool> RequestInspectionConveyorAsync(InspectionConveyorDataModel model)
    {
        bool[] coils = await master.ReadCoilsAsync(slaveAddress: 0x11, startAddress: 0, numberOfPoints: 13) // Conveyor는 0x1_번, Infeed는 1번
                                                                                                            // coil 설정 값으로 0번 IsWorking, 1~12번 Position 값
        if (coils == null)
            return false;
        for (int i = 0; i < 13; i++)
        {
            if (i == 0)
                model.IsWorking = coils[0]
            else
                model.Positions[i] = coils[i]
        }
    }

    public class PneumaticValueType
    {
        public ushort DeviceID { get; set; }
        public ushort TargetPressure { get; set; }
        public ushort CurrentPressure { get; set; }
        public State State { get; set; }
        public async Task<ushort> RequestPneumaticPressureDataModel(PneumaticPressureDataModel model)
        {
            ushort[] value = await master.ReadHoldingRegistersAsync(slaveAddress: 0x20, 0, 4) // PneumaticPressure는 0x2_번

        State state = (State)value[0];
            model.SensorState = state;
            model.TargetPressure = value[1];
            model.CurrentPressure = value[2];
        }

        
        public async Task RequestWritePneumaticPressure(ushort edit_PneumaticPressure, InfeedConveyorDataModel model)
        {
            model.TargetPressure = edit_PneumaticPressure;
            return master.WriteSingleRegisterAsync(slaveAddress: 0x20, registerAddress: 0, value: edit_PneumaticPressure);
        }

        public async Task<ushort> RequestVibrationDataModel(VibrationDataModel model)
        {
            ushort[] value = await master.ReadHoldingRegistersAsync(slaveAddress: 0x30, 0, 4);   // Vibration 는 0x3_번
            bool iswork = (bool)value[0][0];
            State state = (State)value[2];
            model.IsWorking = iswork;
            model.VibrationState = value[1];
            model.SensorState = state;
        }
    }
    public class RejectCylinderType
    {
        public ushrot DeviceId { get; set; }
        public ushort 
    }
    public ValueTask DisposeAsync()
    {
        tcpclient.Stop();
        return ValueTask.CompletedTask;
    }
}
