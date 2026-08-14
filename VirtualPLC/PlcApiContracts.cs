using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

namespace VirtualPLC;

/// <summary>PC(백엔드)가 PLC에 보내는 Request 프레임의 OpCode입니다. 상세 스펙은 PLC_API_SPEC.md 참조.</summary>
public enum PlcOpcode : ushort
{
    RegisterProduct = 1,
    SubmitVisionResult = 2,
    GetStatus = 3,
    ResetAlarm = 4
}

public sealed record RegisterProductRequest(string SerialNumber);
public sealed record RegisterProductResponse(bool Success);

public sealed record SubmitVisionResultRequest(string SerialNumber, InspectionResult Result);
public sealed record SubmitVisionResultResponse(bool Success);

public sealed record GetStatusResponse(string Step, string? AlarmType, string? AlarmMessage);

public sealed record ResetAlarmResponse(bool Success);
