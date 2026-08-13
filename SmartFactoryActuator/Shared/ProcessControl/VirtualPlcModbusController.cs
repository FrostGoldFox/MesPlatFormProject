using NModbus;
using SmartFactoryActuator.FasteningActuator.PneumaticPressure;
using SmartFactoryActuator.FasteningActuator.Vibration;
using SmartFactoryActuator.RejectCylinder;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;
using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

namespace SmartFactoryActuator.Shared.ProcessControl;

/// <summary>VirtualPLC의 Modbus Master 실행부입니다.</summary>
public sealed class VirtualPlcModbusController
{
    private readonly IModbusMaster _infeedMaster;
    private readonly IModbusMaster _pneumaticMaster;
    private readonly IModbusMaster _vibrationMaster;
    private readonly IModbusMaster _inspectionMaster;
    private readonly IModbusMaster _rejectMaster;

    public VirtualPlcModbusController(
        IModbusMaster infeedMaster,
        IModbusMaster pneumaticMaster,
        IModbusMaster vibrationMaster,
        IModbusMaster inspectionMaster,
        IModbusMaster rejectMaster,
        VirtualPlcProcessController? process = null)
    {
        _infeedMaster = infeedMaster;
        _pneumaticMaster = pneumaticMaster;
        _vibrationMaster = vibrationMaster;
        _inspectionMaster = inspectionMaster;
        _rejectMaster = rejectMaster;
        Process = process ?? new VirtualPlcProcessController();
    }

    public VirtualPlcProcessController Process { get; }
    public event Action<string>? VisionRequested;
    public event Action<ProcessCompletion>? ProcessCompleted;
    public event Action<ProcessAlarm>? AlarmRaised;

    public bool TryRegisterProduct(string serialNumber, DateTimeOffset now) =>
        Process.TryRegisterInfeedProduct(serialNumber, now);

    public bool TrySubmitVisionResult(string serialNumber, InspectionResult result) =>
        Process.TrySubmitVisionResult(serialNumber, result);

    /// <summary>
    /// Modbus 읽기와 쓰기는 모두 여기에서 예외를 잡습니다.
    /// 실패하면 다음 설비 단계로 진행하지 않고 Process를 Faulted 상태로 전환합니다.
    /// </summary>
    public void PollOnce(DateTimeOffset now)
    {
        try
        {
            // Input 0 = 운전 상태, Input 1 = Position 1, Input 12 = Position 12
            bool[] infeedInputs = _infeedMaster.ReadInputs(InfeedConveyorNetWork.Id, 0, 13);
            Process.Tick(now, infeedInputs[1], infeedInputs[12]);

            // Infeed 인계 명령 후 Position 12가 비어진 것을 확인해야 진동 공정으로 진행합니다.
            if (Process.Step == ProcessStep.WaitingForFasteningReceipt && !infeedInputs[12])
            {
                Process.ConfirmFasteningReceipt(now);
            }

            // Vision 콜백 중 NG 배출처럼 새 명령이 만들어질 수 있으므로 큐가 빌 때까지 처리합니다.
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
                _infeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 0, true);
                break;
            case ProcessCommandType.StopInfeed:
                _infeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 1, true);
                break;
            case ProcessCommandType.TransferInfeedToFastening:
                _infeedMaster.WriteSingleCoil(InfeedConveyorNetWork.Id, 2, true);
                break;
            case ProcessCommandType.StartVibration:
                _vibrationMaster.WriteSingleCoil(VibrationNetWork.Id, 0, true);
                break;
            case ProcessCommandType.StopVibration:
                _vibrationMaster.WriteSingleCoil(VibrationNetWork.Id, 1, true);
                break;
            case ProcessCommandType.StartFastening:
                _pneumaticMaster.WriteSingleCoil(PneumaticPressureNetWork.Id, 0, true);
                break;
            case ProcessCommandType.StopFastening:
                _pneumaticMaster.WriteSingleCoil(PneumaticPressureNetWork.Id, 1, true);
                break;
            case ProcessCommandType.StartInspectionConveyor:
                _inspectionMaster.WriteSingleCoil(InspetionConveyorNetWork.UnitId, InspetionConveyorNetWork.RunCommandCoil, true);
                break;
            case ProcessCommandType.TriggerVision:
                VisionRequested?.Invoke(command.SerialNumber);
                break;
            case ProcessCommandType.ExtendRejectCylinder:
                _rejectMaster.WriteSingleCoil(RejectCylinderNetWork.Id, 0, true);
                break;
        }
    }
}