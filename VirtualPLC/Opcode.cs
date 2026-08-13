using System;

public enum ConveyorType : byte
{
    Start = 0x00,               // Conveyor 시작
    Stop = 0x01,                // Conveyor 정지
    MoveToInspection = 0x02,    // Conveyor에서 물건의 위치

    ResetFault = 0x10			// 오류 발생시에 초기화
}
public enum RejectCylinderType : byte
{
    Start = 0x00,
    Stop = 0x01


    ResetFault = 0x10           // 오류 발생시에 초기화
}
public enum VibrationType : byte
{
{
    Start = 0x00,
    Stop = 0x01


    ResetFault = 0x10           // 오류 발생시에 초기화
}
public enum PneumaticPressureSensorType : byte
{
    Start = 0x00,
    Stop = 0x01


    ResetFault = 0x10           // 오류 발생시에 초기화
}