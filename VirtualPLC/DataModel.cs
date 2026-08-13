using System;

public enum State
{
	Unknown = 0,
	Working = 1,
	Stopped = 2,
	Error = 3
}
public class ConveyorPosition
{
	public int Position { get; init; }
	public bool IsObject { get; set; }
}
public class InfeedConveyorDataModel
{
	public string DeviceId { get; init; }
	public bool IsWorking { get; set; }
	public ConveyorPosition[] Positions { get; set; }
	public InfeedConveyorDataModel(string DeviceId)
	{
		this.DeviceId = DeviceId;
		const int PositionCount = 12;
		// 여러 위치를 담는 Positions 배열을 초기화한다.
		Positions = new ConveyorPosition[PositionCount];
		for (int i = 1; i <= PositionCount; i++)
		{
			Positions[i-1] = new ConveyorPosition
			{
				Position = i,
				IsObject = false
			}
		}
	}
}

public class InspectionConveyorDataModel
{
	public string DeviceId { get; init; }
	public bool IsWorking { get; set; }
	public ConveyorPosition[] Positions { get; set; }
	public InspectionConveyorDataModel(string DeviceId)
	{
		this.DeviceId = DeviceId;
		const int PositionCount = 12;
		Positions = new ConveyorPosition[PositionCount];
		for (int i = 1; i <= PositionCount; i++)
		{
			Positions[i-1] = new ConveyorPosition
			{
				Position = i,
				IsObject = false
			}
		}
	}
}

public class PneumaticPressureDataModel
{
	public string DeviceId { get; init; }
	public PneumaticPressureDataModel(string DeviceId)
	{
		this.DeviceId = DeviceId;
	}
	public ushort TargetPressure { get; set; } = 500;
	public ushort CurrentPressure { get; set; }
	public State SensorState { get; set; } = State.Unknown;
}

public class VibrationDataModel
{
	public string DeviceId { get; init; }
	public VibrationDataModel(string DeviceId)
	{
		this.DeviceId = DeviceId;
	}
	public bool IsWorking { get; set; }
	public ushort VibrationState { get; set; }
	public State SensorState { get; set; } = State.Unknown;
}

public class RejectCylinderDataModel
{
	public string DeviceId { get; init; }
	public RejectCylinderDataModel(string DeviceId)
	{
		this.DeviceId = DeviceId;
	}
	
	public bool IsWorking { get; set; }
	public State SensorState { get; set; } = State.Unknown;
	public ushort RejectCylinderState { get; set; }
}

