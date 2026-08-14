using System.Net;
using System.Net.Sockets;
using NModbus;
using SmartFactoryActuator.FasteningActuator.PneumaticPressure;
using SmartFactoryActuator.FasteningActuator.Vibration;
using SmartFactoryActuator.RejectCylinder;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;
using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

namespace VirtualPLC;

/// <summary>
/// PLC의 Modbus Master 계층입니다. 장비 5종은 테스트 편의상 서로 다른 TCP 포트를 쓰므로
/// (MODBUS_OPERATING_GUIDE.md §5), 장비별로 별도 TCP 연결을 맺습니다. 하나의 포트/여러 Unit ID
/// 구조로 통합되면 이 클래스의 연결 관리 부분만 바뀌고 Read/Execute 메서드는 그대로 유지된다.
/// </summary>
public sealed class ModbusNetWork : IAsyncDisposable
{
    private readonly IPAddress _address;
    private readonly ModbusFactory _factory = new();

    private TcpClient? _infeedClient;
    private TcpClient? _pneumaticClient;
    private TcpClient? _vibrationClient;
    private TcpClient? _rejectClient;
    private TcpClient? _inspectionClient;

    private IModbusMaster? _infeedMaster;
    private IModbusMaster? _pneumaticMaster;
    private IModbusMaster? _vibrationMaster;
    private IModbusMaster? _rejectMaster;
    private IModbusMaster? _inspectionMaster;

    public ModbusNetWork(IPAddress address)
    {
        _address = address;
    }

    private IModbusMaster InfeedMaster => _infeedMaster ?? throw NotConnected();
    private IModbusMaster PneumaticMaster => _pneumaticMaster ?? throw NotConnected();
    private IModbusMaster VibrationMaster => _vibrationMaster ?? throw NotConnected();
    private IModbusMaster RejectMaster => _rejectMaster ?? throw NotConnected();
    private IModbusMaster InspectionMaster => _inspectionMaster ?? throw NotConnected();

    public async Task ConnectAsync(
        int infeedPort,
        int pneumaticPort,
        int vibrationPort,
        int rejectPort,
        int inspectionPort,
        CancellationToken cancellationToken = default)
    {
        _infeedClient = new TcpClient();
        _pneumaticClient = new TcpClient();
        _vibrationClient = new TcpClient();
        _rejectClient = new TcpClient();
        _inspectionClient = new TcpClient();

        await Task.WhenAll(
            _infeedClient.ConnectAsync(_address, infeedPort, cancellationToken).AsTask(),
            _pneumaticClient.ConnectAsync(_address, pneumaticPort, cancellationToken).AsTask(),
            _vibrationClient.ConnectAsync(_address, vibrationPort, cancellationToken).AsTask(),
            _rejectClient.ConnectAsync(_address, rejectPort, cancellationToken).AsTask(),
            _inspectionClient.ConnectAsync(_address, inspectionPort, cancellationToken).AsTask()).ConfigureAwait(false);

        _infeedMaster = _factory.CreateMaster(_infeedClient);
        _pneumaticMaster = _factory.CreateMaster(_pneumaticClient);
        _vibrationMaster = _factory.CreateMaster(_vibrationClient);
        _rejectMaster = _factory.CreateMaster(_rejectClient);
        _inspectionMaster = _factory.CreateMaster(_inspectionClient);
    }

    // ---- 읽기: Discrete Input 0 = 운전중, 1~12 = Position 1~12 점유 여부 ----

    public InfeedConveyorDataModel ReadInfeed()
    {
        bool[] inputs = InfeedMaster.ReadInputs(InfeedConveyorNetWork.Id, 0, 13);
        var model = new InfeedConveyorDataModel { IsRunning = inputs[0] };
        Array.Copy(inputs, 1, model.PositionOccupied, 0, 12);
        return model;
    }

    public InspectionConveyorDataModel ReadInspection()
    {
        bool[] inputs = InspectionMaster.ReadInputs(InspetionConveyorNetWork.UnitId, 0, 13);
        var model = new InspectionConveyorDataModel { IsRunning = inputs[0] };
        Array.Copy(inputs, 1, model.PositionOccupied, 0, 12);
        return model;
    }

    public PneumaticPressureDataModel ReadPneumaticPressure()
    {
        ushort[] registers = PneumaticMaster.ReadHoldingRegisters(PneumaticPressureNetWork.Id, 0, 3);
        return new PneumaticPressureDataModel
        {
            State = registers[0],
            TargetPressure = registers[1],
            CurrentPressure = registers[2]
        };
    }

    public VibrationDataModel ReadVibration()
    {
        bool[] inputs = VibrationMaster.ReadInputs(VibrationNetWork.Id, 0, 1);
        ushort[] registers = VibrationMaster.ReadHoldingRegisters(VibrationNetWork.Id, 0, 3);
        return new VibrationDataModel
        {
            IsRunning = inputs[0],
            State = registers[0],
            TargetVibration = registers[1],
            CurrentVibration = registers[2]
        };
    }

    public RejectCylinderDataModel ReadRejectCylinder()
    {
        bool[] inputs = RejectMaster.ReadInputs(RejectCylinderNetWork.Id, 0, 2);
        return new RejectCylinderDataModel { IsExtended = inputs[0], IsRetracted = inputs[1] };
    }

    // ---- 쓰기: Coil 명령. 값 자체는 장비 쪽에서 처리 후 자동으로 false로 되돌린다 ----

    public void StartInfeed() => InfeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 0, true);
    public void StopInfeed() => InfeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 1, true);
    public void TransferInfeedToFastening() => InfeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 2, true);

    public void StartVibration() => VibrationMaster.WriteSingleCoil(VibrationNetWork.Id, 0, true);
    public void StopVibration() => VibrationMaster.WriteSingleCoil(VibrationNetWork.Id, 1, true);
    public void SetTargetVibration(ushort target) => VibrationMaster.WriteSingleRegister(VibrationNetWork.Id, 1, target);

    // 주의: PneumaticPressureNetWork는 현재 Coil을 읽지 않고 HoldingRegister만 폴링하므로,
    // 아래 두 Coil 쓰기는 프로토콜상 성공하지만 장비 상태에는 아직 영향을 주지 않는다(기존 동작과 동일하게 유지).
    public void StartFastening() => PneumaticMaster.WriteSingleCoil(PneumaticPressureNetWork.Id, 0, true);
    public void StopFastening() => PneumaticMaster.WriteSingleCoil(PneumaticPressureNetWork.Id, 1, true);
    public void SetTargetPressure(ushort target) => PneumaticMaster.WriteSingleRegister(PneumaticPressureNetWork.Id, 1, target);

    public void StartInspectionConveyor() =>
        InspectionMaster.WriteSingleCoil(InspetionConveyorNetWork.UnitId, InspetionConveyorNetWork.RunCommandCoil, true);
    public void StopInspectionConveyor() =>
        InspectionMaster.WriteSingleCoil(InspetionConveyorNetWork.UnitId, InspetionConveyorNetWork.StopCommandCoil, true);

    // Command 5: Inspection Conveyor의 Position 12 Clear 요청을 장비 Coil로 전달한다.
    public void ClearInspectionPosition12() =>
        InspectionMaster.WriteSingleCoil(InspetionConveyorNetWork.UnitId, InspetionConveyorNetWork.ResetCommandCoil, true);

    // Command 6: 체결설비에서 나온 제품이 Inspection Position 1에 도착했다는 신호(사람이 옮기는 구간).
    public void AcceptInspectionInput() =>
        InspectionMaster.WriteSingleCoil(InspetionConveyorNetWork.UnitId, InspetionConveyorNetWork.AcceptCommandCoil, true);

    public void ExtendRejectCylinder() => RejectMaster.WriteSingleCoil(RejectCylinderNetWork.Id, 0, true);
    public void RetractRejectCylinder() => RejectMaster.WriteSingleCoil(RejectCylinderNetWork.Id, 1, true);

    private static InvalidOperationException NotConnected() =>
        new("ConnectAsync를 먼저 호출해야 합니다.");

    public ValueTask DisposeAsync()
    {
        _infeedClient?.Dispose();
        _pneumaticClient?.Dispose();
        _vibrationClient?.Dispose();
        _rejectClient?.Dispose();
        _inspectionClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}
