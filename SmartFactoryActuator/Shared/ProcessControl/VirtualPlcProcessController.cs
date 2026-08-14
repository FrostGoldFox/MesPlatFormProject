using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

namespace SmartFactoryActuator.Shared.ProcessControl;

public enum ProcessStep
{
    WaitingForInfeed,
    MovingInfeed,
    WaitingForFasteningReceipt,
    Vibrating,
    Fastening,
    WaitingForInspectionReceipt,
    MovingToInspection,
    WaitingForVision,
    Completed,
    Faulted
}

public enum ProcessAlarmType
{
    InfeedTimeout,
    InspectionTransferTimeout,
    PressureOutOfRange,
    ModbusCommunication
}

public sealed record ProcessAlarm(ProcessAlarmType Type, string Message);

public enum ProcessCommandType
{
    StartInfeed,
    StopInfeed,
    TransferInfeedToFastening,
    StartVibration,
    StopVibration,
    StartFastening,
    StopFastening,
    StartInspectionConveyor,
    TriggerVision,
    ExtendRejectCylinder,
    RetractRejectCylinder
}

public sealed record ProcessCommand(ProcessCommandType Type, string SerialNumber);
public sealed record ProcessCompletion(string SerialNumber, InspectionResult InspectionResult, bool RequiresReject);

/// <summary>
/// VirtualPLC의 제품 1개 공정 시퀀스입니다. Modbus 통신은 호출자가 담당하고,
/// 이 클래스는 설비 상태와 시간을 바탕으로 명령 또는 알람을 결정합니다.
/// </summary>
public sealed class VirtualPlcProcessController
{
    public static readonly TimeSpan InfeedArrivalPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan InfeedTransferPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan InfeedTransferTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan VibrationDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FasteningDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan InspectionTransferTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan VisionTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RejectDwellDuration = TimeSpan.FromSeconds(2);

    private readonly Queue<ProcessCommand> _commands = new();
    private DateTimeOffset _nextInfeedPollAt;
    private DateTimeOffset _nextTransferPollAt;
    private DateTimeOffset? _stepStartedAt;
    private DateTimeOffset? _rejectExtendedAt;
    private string? _serialNumber;
    private InspectionResult? _completionResult;

    public ProcessStep Step { get; private set; } = ProcessStep.WaitingForInfeed;
    public ProcessAlarm? ActiveAlarm { get; private set; }

    // Serial이 등록된 대기 상태도 새 제품을 받을 수 없으므로 _serialNumber까지 확인합니다.
    public bool IsBusy => _serialNumber is not null || Step is not ProcessStep.WaitingForInfeed and not ProcessStep.Completed;

    public bool TryRegisterInfeedProduct(string serialNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(serialNumber) || IsBusy || Step == ProcessStep.Faulted)
        {
            return false;
        }

        _serialNumber = serialNumber;
        _nextInfeedPollAt = now;
        return true;
    }

    public void Tick(DateTimeOffset now, bool infeedPosition1HasProduct, bool infeedPosition12HasProduct)
    {
        // Reject 실린더는 어느 Step에 있든(이미 다음 제품을 받고 있어도) 독립적으로 원위치한다.
        if (_rejectExtendedAt is not null && now - _rejectExtendedAt.Value >= RejectDwellDuration)
        {
            _commands.Enqueue(new ProcessCommand(ProcessCommandType.RetractRejectCylinder, string.Empty));
            _rejectExtendedAt = null;
        }

        switch (Step)
        {
            case ProcessStep.WaitingForInfeed:
                StartInfeedWhenReady(now, infeedPosition1HasProduct, infeedPosition12HasProduct);
                break;

            case ProcessStep.MovingInfeed:
                if (HasElapsed(now, InfeedTransferTimeout))
                {
                    Enqueue(ProcessCommandType.StopInfeed);
                    RaiseAlarm(ProcessAlarmType.InfeedTimeout, "Infeed 제품이 제한 시간 안에 Position 12에 도착하지 않았습니다.");
                    break;
                }

                StopInfeedWhenTransferReady(now, infeedPosition12HasProduct);
                break;

            case ProcessStep.Vibrating when HasElapsed(now, VibrationDuration):
                Enqueue(ProcessCommandType.StopVibration);
                Enqueue(ProcessCommandType.StartFastening);
                Step = ProcessStep.Fastening;
                _stepStartedAt = now;
                break;

            case ProcessStep.Fastening when HasElapsed(now, FasteningDuration):
                // 체결설비는 아직 끄지 않는다 — Inspection Position 1 도착이 확인된 뒤에 끈다(ConfirmInspectionReceipt).
                Enqueue(ProcessCommandType.StartInspectionConveyor);
                Step = ProcessStep.WaitingForInspectionReceipt;
                _stepStartedAt = now;
                break;

            case ProcessStep.MovingToInspection when HasElapsed(now, InspectionTransferTimeout):
                RaiseAlarm(ProcessAlarmType.InspectionTransferTimeout, "제품이 제한 시간 안에 Inspection Position 12에 도착하지 않았습니다.");
                break;

            case ProcessStep.WaitingForVision when HasElapsed(now, VisionTimeout):
                Complete(InspectionResult.Unjudged, now);
                break;
        }
    }

    public bool ConfirmFasteningReceipt(DateTimeOffset now)
    {
        if (Step != ProcessStep.WaitingForFasteningReceipt)
        {
            return false;
        }

        Enqueue(ProcessCommandType.StartVibration);
        Step = ProcessStep.Vibrating;
        _stepStartedAt = now;
        return true;
    }

    /// <summary>Inspection Conveyor Position 1의 재실이 확인된 시점. 여기서 비로소 체결설비를 끈다.</summary>
    public bool ConfirmInspectionReceipt(DateTimeOffset now)
    {
        if (Step != ProcessStep.WaitingForInspectionReceipt)
        {
            return false;
        }

        Enqueue(ProcessCommandType.StopFastening);
        Step = ProcessStep.MovingToInspection;
        _stepStartedAt = now;
        return true;
    }

    /// <summary>Inspection Conveyor Position 12까지 이동이 확인된 시점. 여기서 비전검사를 트리거한다.</summary>
    public bool ConfirmInspectionComplete(DateTimeOffset now)
    {
        if (Step != ProcessStep.MovingToInspection)
        {
            return false;
        }

        Enqueue(ProcessCommandType.TriggerVision);
        Step = ProcessStep.WaitingForVision;
        _stepStartedAt = now;
        return true;
    }

    public bool TrySubmitVisionResult(string serialNumber, InspectionResult result, DateTimeOffset now)
    {
        if (Step != ProcessStep.WaitingForVision || !string.Equals(_serialNumber, serialNumber, StringComparison.Ordinal))
        {
            return false;
        }

        Complete(result, now);
        return true;
    }

    public void RaiseCommunicationAlarm(Exception exception)
    {
        RaiseAlarm(ProcessAlarmType.ModbusCommunication, $"Modbus 통신 오류: {exception.Message}");
    }

    /// <summary>공압 실측값이 정상 범위를 벗어났을 때 호출한다 — 계획서 AL-P1-03(공압 저하) 대응.</summary>
    public void RaisePressureOutOfRangeAlarm(ushort currentPressure, ushort lowerBound, ushort upperBound)
    {
        RaiseAlarm(ProcessAlarmType.PressureOutOfRange,
            $"공압이 허용 범위를 벗어났습니다: 현재값={currentPressure} (허용 {lowerBound}~{upperBound})");
    }

    /// <summary>원인 확인 후 호출합니다. 고장 제품은 자동 재시작하지 않고 폐기/재투입 판단을 외부에서 합니다.</summary>
    public bool ResetAlarm()
    {
        if (Step != ProcessStep.Faulted)
        {
            return false;
        }

        ActiveAlarm = null;
        _serialNumber = null;
        _stepStartedAt = null;
        _rejectExtendedAt = null;
        _completionResult = null;
        _commands.Clear();
        Step = ProcessStep.WaitingForInfeed;
        return true;
    }

    public bool TryTakeCompletion(out ProcessCompletion? completion)
    {
        completion = null;
        if (Step != ProcessStep.Completed || _serialNumber is null || _completionResult is null)
        {
            return false;
        }

        completion = new ProcessCompletion(_serialNumber, _completionResult.Value, _completionResult == InspectionResult.Failed);
        _serialNumber = null;
        _completionResult = null;
        _stepStartedAt = null;
        Step = ProcessStep.WaitingForInfeed;
        return true;
    }

    public IReadOnlyList<ProcessCommand> DequeueCommands()
    {
        ProcessCommand[] commands = _commands.ToArray();
        _commands.Clear();
        return commands;
    }

    private void StartInfeedWhenReady(DateTimeOffset now, bool position1HasProduct, bool position12HasProduct)
    {
        if (_serialNumber is null || now < _nextInfeedPollAt)
        {
            return;
        }

        _nextInfeedPollAt = now + InfeedArrivalPollInterval;
        if (!position1HasProduct || position12HasProduct)
        {
            return;
        }

        Enqueue(ProcessCommandType.StartInfeed);
        Step = ProcessStep.MovingInfeed;
        _stepStartedAt = now;
        _nextTransferPollAt = now;
    }

    private void StopInfeedWhenTransferReady(DateTimeOffset now, bool position12HasProduct)
    {
        if (now < _nextTransferPollAt)
        {
            return;
        }

        _nextTransferPollAt = now + InfeedTransferPollInterval;
        if (!position12HasProduct)
        {
            return;
        }

        Enqueue(ProcessCommandType.StopInfeed);
        Enqueue(ProcessCommandType.TransferInfeedToFastening);
        Step = ProcessStep.WaitingForFasteningReceipt;
    }

    private bool HasElapsed(DateTimeOffset now, TimeSpan duration) =>
        _stepStartedAt is not null && now - _stepStartedAt.Value >= duration;

    private void Enqueue(ProcessCommandType type)
    {
        if (_serialNumber is not null)
        {
            _commands.Enqueue(new ProcessCommand(type, _serialNumber));
        }
    }

    private void Complete(InspectionResult result, DateTimeOffset now)
    {
        if (result == InspectionResult.Failed)
        {
            Enqueue(ProcessCommandType.ExtendRejectCylinder);
            _rejectExtendedAt = now;
        }

        _completionResult = result;
        Step = ProcessStep.Completed;
    }

    private void RaiseAlarm(ProcessAlarmType type, string message)
    {
        ActiveAlarm = new ProcessAlarm(type, message);
        Step = ProcessStep.Faulted;
    }
}