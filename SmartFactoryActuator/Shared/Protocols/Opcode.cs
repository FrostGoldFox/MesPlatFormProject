namespace SmartFactoryActuator.Shared.Protocols;

/// <summary>
/// PLC-PC JSON 통신에서 사용할 장비별 opcode입니다.
/// 0x00~0x0F: 동작, 0x10~0x1F: 오류 초기화, 0x20~0x2F: 상태 신호 예약.
/// </summary>
public enum ConveyorType : byte
{
    Start = 0x00,
    Stop = 0x01,
    MoveToInspection = 0x02,
    ResetFault = 0x10
}

public enum RejectCylinderType : byte
{
    Start = 0x00,
    Stop = 0x01,
    ResetFault = 0x10
}

public enum VibrationType : byte
{
    Start = 0x00,
    Stop = 0x01,
    ResetFault = 0x10
}

public enum PneumaticPressureSensorType : byte
{
    Start = 0x00,
    Stop = 0x01,
    ResetFault = 0x10
}

internal class Opcode
{
}