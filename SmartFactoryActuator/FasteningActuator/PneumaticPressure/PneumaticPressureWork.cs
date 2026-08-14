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
    private static readonly TimeSpan StepInterval = TimeSpan.FromSeconds(1);

    private DateTimeOffset _lastStepAt = DateTimeOffset.MinValue;

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

    /// <summary>
    /// 목표값으로 즉시 점프하지 않고, 1초마다 현재값을 목표값 방향으로 1씩만 움직인다 —
    /// 실린더가 압력을 서서히 맞춰가는 것을 흉내낸다.
    /// </summary>
    public void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastStepAt < StepInterval)
        {
            return;
        }

        _lastStepAt = now;

        if (CurrentPressure < TargetPressure)
        {
            CurrentPressure++;
        }
        else if (CurrentPressure > TargetPressure)
        {
            CurrentPressure--;
        }

        SensorState = PneumaticPressureState.Working;
    }
}