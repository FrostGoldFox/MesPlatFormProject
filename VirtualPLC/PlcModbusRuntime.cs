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
    ///
    /// <paramref name="manualPosition1Override"/>/<paramref name="manualPosition12Override"/>는 실물 센서가 없는
    /// 개발 단계에서 WPF 테스트 신호로 재실을 흉내내기 위한 값이다. 실측(Modbus)과 OR로 합쳐진다 —
    /// 둘 중 하나만 true여도 재실로 판단한다. 실제 배포 시에는 항상 false로 두면 이 인자가 없는 것과 같다.
    /// </summary>
    public void PollOnce(DateTimeOffset now, bool manualPosition1Override = false, bool manualPosition12Override = false)
    {
        try
        {
            InfeedConveyorDataModel infeed = _modbus.ReadInfeed();
            // PositionOccupied[0] = Position 1(재실), [11] = Position 12(체결설비 인계 지점)
            bool position1 = infeed.PositionOccupied[0] || manualPosition1Override;
            bool position12 = infeed.PositionOccupied[11] || manualPosition12Override;
            Process.Tick(now, position1, position12);

            if (Process.Step == ProcessStep.WaitingForFasteningReceipt && !position12)
            {
                Process.ConfirmFasteningReceipt(now);
            }

            // Inspection Conveyor: Position 1 재실 확인 → 체결설비 정지, Position 12 도착 확인 → 비전검사 트리거
            if (Process.Step is ProcessStep.WaitingForInspectionReceipt or ProcessStep.MovingToInspection)
            {
                InspectionConveyorDataModel inspection = _modbus.ReadInspection();

                if (Process.Step == ProcessStep.WaitingForInspectionReceipt && inspection.PositionOccupied[0])
                {
                    Process.ConfirmInspectionReceipt(now);
                }
                else if (Process.Step == ProcessStep.MovingToInspection && inspection.PositionOccupied[11])
                {
                    Process.ConfirmInspectionComplete(now);
                }
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
