namespace SmartFactoryActuator.FasteningActuator.Vibration;

public enum VibrationState : ushort
{
    Unknown = 0,
    Working = 1,
    Stopped = 2,
    Error = 3
}

public sealed class VibrationWork
{
    private readonly Random _random = new();

    public ushort TargetVibration { get; set; } = 50;
    public ushort CurrentVibration { get; private set; } = 50;
    public bool IsRunning { get; private set; }
    public VibrationState SensorState { get; private set; } = VibrationState.Stopped;

    public void Start()
    {
        IsRunning = true;
        SensorState = VibrationState.Working;
    }

    public void Stop()
    {
        IsRunning = false;
        SensorState = VibrationState.Stopped;
    }

    public void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        int variation = _random.Next(-5, 6);
        CurrentVibration = (ushort)Math.Clamp(TargetVibration + variation, 0, ushort.MaxValue);
        SensorState = VibrationState.Working;
    }
}