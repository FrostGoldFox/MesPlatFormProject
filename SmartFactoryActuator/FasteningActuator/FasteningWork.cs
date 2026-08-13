using SmartFactoryActuator.FasteningActuator.PneumaticPressure;
using SmartFactoryActuator.FasteningActuator.Vibration;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;

namespace SmartFactoryActuator.FasteningActuator;

public enum FasteningState
{
    Idle,
    Working,
    Completed
}

/// <summary>
/// EQ-A01 체결 공정의 도메인 시퀀스입니다.
/// 통신 명령을 해석하지 않으며, 체결 중 센서 값을 갱신하고 12초 뒤 완료 상태만 만듭니다.
/// </summary>
public sealed class FasteningWork
{
    public static readonly TimeSpan StandardCycleTime = TimeSpan.FromSeconds(12);

    private DateTimeOffset? _startedAt;

    public FasteningWork(PneumaticPressureWork pneumaticPressure, VibrationWork vibration)
    {
        PneumaticPressure = pneumaticPressure ?? throw new ArgumentNullException(nameof(pneumaticPressure));
        Vibration = vibration ?? throw new ArgumentNullException(nameof(vibration));
    }

    public PneumaticPressureWork PneumaticPressure { get; }
    public VibrationWork Vibration { get; }
    public ConveyorProduct? Product { get; private set; }
    public FasteningState State { get; private set; } = FasteningState.Idle;
    public TimeSpan Elapsed { get; private set; }

    public bool TryStart(ConveyorProduct product, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (State == FasteningState.Working)
        {
            return false;
        }

        Product = product;
        _startedAt = startedAt;
        Elapsed = TimeSpan.Zero;
        State = FasteningState.Working;
        return true;
    }

    /// <summary>주기적으로 호출해 체결 중 센서값과 완료 상태를 갱신합니다.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (State != FasteningState.Working || _startedAt is null)
        {
            return;
        }

        Elapsed = now - _startedAt.Value;
        PneumaticPressure.Tick();
        Vibration.Tick();

        if (Elapsed >= StandardCycleTime)
        {
            Elapsed = StandardCycleTime;
            State = FasteningState.Completed;
        }
    }

    public bool TryTakeCompletedProduct(out ConveyorProduct? product)
    {
        product = null;

        if (State != FasteningState.Completed || Product is null)
        {
            return false;
        }

        product = Product;
        Product = null;
        _startedAt = null;
        Elapsed = TimeSpan.Zero;
        State = FasteningState.Idle;
        return true;
    }
}
