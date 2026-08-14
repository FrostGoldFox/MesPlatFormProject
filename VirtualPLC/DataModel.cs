namespace VirtualPLC;

/// <summary>PLC가 Modbus로 폴링해 들고 있는 Infeed Conveyor의 최근 상태입니다.</summary>
public sealed class InfeedConveyorDataModel
{
    public bool IsRunning { get; set; }

    /// <summary>index 0 = Position 1 ... index 11 = Position 12.</summary>
    public bool[] PositionOccupied { get; } = new bool[12];
}

/// <summary>PLC가 Modbus로 폴링해 들고 있는 Inspection Conveyor의 최근 상태입니다.</summary>
public sealed class InspectionConveyorDataModel
{
    public bool IsRunning { get; set; }

    /// <summary>index 0 = Position 1 ... index 11 = Position 12.</summary>
    public bool[] PositionOccupied { get; } = new bool[12];
}

/// <summary>PLC가 Modbus로 폴링해 들고 있는 공압 센서의 최근 상태입니다. State는 장비 쪽 State enum의 원시값입니다.</summary>
public sealed class PneumaticPressureDataModel
{
    public ushort State { get; set; }
    public ushort TargetPressure { get; set; }
    public ushort CurrentPressure { get; set; }
}

/// <summary>PLC가 Modbus로 폴링해 들고 있는 진동 센서의 최근 상태입니다.</summary>
public sealed class VibrationDataModel
{
    public bool IsRunning { get; set; }
    public ushort State { get; set; }
    public ushort TargetVibration { get; set; }
    public ushort CurrentVibration { get; set; }
}

/// <summary>PLC가 Modbus로 폴링해 들고 있는 Reject Cylinder의 최근 상태입니다.</summary>
public sealed class RejectCylinderDataModel
{
    public bool IsExtended { get; set; }
    public bool IsRetracted { get; set; }
}
