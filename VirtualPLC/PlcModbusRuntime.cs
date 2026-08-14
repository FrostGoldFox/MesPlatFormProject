using SmartFactoryActuator.Shared.ProcessControl;

namespace VirtualPLC;

/// <summary>
/// <see cref="ModbusNetWork"/>(장비 5종 폴링)와 <see cref="VirtualPlcProcessController"/>(상태 머신)를 묶는 실행부입니다.
/// SmartFactoryActuator.Shared.ProcessControl.VirtualPlcModbusController와 같은 역할이지만,
/// NModbus의 IModbusMaster를 직접 쓰지 않고 이 프로젝트가 소유한 <see cref="ModbusNetWork"/>를 통해서만 장비와 통신한다.
/// </summary>
public sealed class PlcModbusRuntime
{
    private readonly ModbusNetWork _modbus;

    public PlcModbusRuntime(ModbusNetWork modbus, VirtualPlcProcessController? process = null)
    {
        _modbus = modbus;
        Process = process ?? new VirtualPlcProcessController();
    }

    public VirtualPlcProcessController Process { get; }

    public event Action<string>? VisionRequested;
    public event Action<ProcessCompletion>? ProcessCompleted;
    public event Action<ProcessAlarm>? AlarmRaised;

    public bool TryRegisterProduct(string serialNumber, DateTimeOffset now) =>
        Process.TryRegisterInfeedProduct(serialNumber, now);

    public bool TrySubmitVisionResult(string serialNumber, SmartFactoryActuator.TransferConveyor.InspetionConveyor.InspectionResult result) =>
        Process.TrySubmitVisionResult(serialNumber, result);

    /// <summary>
    /// Modbus 읽기·쓰기는 모두 여기서 예외를 잡는다. 실패하면 다음 단계로 진행하지 않고
    /// Process를 Faulted 상태로 전환한다 (SmartFactoryActuator.Shared.ProcessControl.VirtualPlcModbusController와 동일한 정책).
    /// </summary>
    public void PollOnce(DateTimeOffset now)
    {
        try
        {
            InfeedConveyorDataModel infeed = _modbus.ReadInfeed();
            // PositionOccupied[0] = Position 1(재실), [11] = Position 12(체결설비 인계 지점)
            Process.Tick(now, infeed.PositionOccupied[0], infeed.PositionOccupied[11]);

            if (Process.Step == ProcessStep.WaitingForFasteningReceipt && !infeed.PositionOccupied[11])
            {
                Process.ConfirmFasteningReceipt(now);
            }

            // Vision 콜백처럼 명령 처리 중 새 명령이 생길 수 있으므로 큐가 빌 때까지 반복한다.
            while (true)
            {
                IReadOnlyList<ProcessCommand> commands = Process.DequeueCommands();
                if (commands.Count == 0)
                {
                    break;
                }

                foreach (ProcessCommand command in commands)
                {
                    Execute(command);
                }
            }

            if (Process.TryTakeCompletion(out ProcessCompletion? completion) && completion is not null)
            {
                ProcessCompleted?.Invoke(completion);
            }
        }
        catch (Exception exception)
        {
            Process.RaiseCommunicationAlarm(exception);
            if (Process.ActiveAlarm is not null)
            {
                AlarmRaised?.Invoke(Process.ActiveAlarm);
            }
        }
    }

    private void Execute(ProcessCommand command)
    {
        switch (command.Type)
        {
            case ProcessCommandType.StartInfeed:
                _modbus.StartInfeed();
                break;
            case ProcessCommandType.StopInfeed:
                _modbus.StopInfeed();
                break;
            case ProcessCommandType.TransferInfeedToFastening:
                _modbus.TransferInfeedToFastening();
                break;
            case ProcessCommandType.StartVibration:
                _modbus.StartVibration();
                break;
            case ProcessCommandType.StopVibration:
                _modbus.StopVibration();
                break;
            case ProcessCommandType.StartFastening:
                _modbus.StartFastening();
                break;
            case ProcessCommandType.StopFastening:
                _modbus.StopFastening();
                break;
            case ProcessCommandType.StartInspectionConveyor:
                _modbus.StartInspectionConveyor();
                break;
            case ProcessCommandType.TriggerVision:
                VisionRequested?.Invoke(command.SerialNumber);
                break;
            case ProcessCommandType.ExtendRejectCylinder:
                _modbus.ExtendRejectCylinder();
                break;
        }
    }
}
