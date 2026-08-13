namespace SmartFactoryActuator.FasteningActuator.PneumaticPressure;

public enum PneumaticPressureState : ushort
{
    Unknown = 0,
    Working = 1,
    Stopped = 2,
    Error = 3
}

public sealed class PneumaticPressureWork
{
    private readonly Random _random = new();

    public ushort TargetPressure { get; set; } = 500;
    public ushort CurrentPressure { get; private set; } = 500;
    public bool IsRunning { get; private set; }
    public PneumaticPressureState SensorState { get; private set; } = PneumaticPressureState.Stopped;

    public void Start()
    {
        IsRunning = true;
        SensorState = PneumaticPressureState.Working;
    }

    public void Stop()
    {
        IsRunning = false;
        SensorState = PneumaticPressureState.Stopped;
    }

    public void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        int variation = _random.Next(-10, 11);
        CurrentPressure = (ushort)Math.Clamp(TargetPressure + variation, 0, ushort.MaxValue);
        SensorState = PneumaticPressureState.Working;
    }
}