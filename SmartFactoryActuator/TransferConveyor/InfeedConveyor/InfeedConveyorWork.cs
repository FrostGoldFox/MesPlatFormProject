namespace SmartFactoryActuator.TransferConveyor.InfeedConveyor;

/// <summary>
/// 컨베이어 위를 이동하는 제품의 최소 식별 정보입니다.
/// MES가 Serial 번호를 만들면 이 객체에 담아 다음 공정까지 전달합니다.
/// </summary>
public sealed record ConveyorProduct(string SerialNumber);

/// <summary>
/// 12개 위치를 갖는 투입 컨베이어의 물리 동작을 시뮬레이션합니다.
/// PLC/Modbus 계층은 이 클래스의 상태를 읽고 제어 명령만 전달합니다.
/// </summary>
public sealed class InfeedConveyorWork
{
    public const int PositionCount = 12;

    private readonly object _syncRoot = new();
    private readonly ConveyorProduct?[] _positions = new ConveyorProduct?[PositionCount];

    public bool IsRunning { get; private set; }

    /// <summary>마지막 위치에 제품이 있어 다음 설비로 넘길 수 있는 상태입니다.</summary>
    public bool IsTransferReady
    {
        get
        {
            lock (_syncRoot)
            {
                return _positions[^1] is not null;
            }
        }
    }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>1번 위치가 비어 있을 때만 제품을 투입합니다.</summary>
    public bool TryLoad(ConveyorProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        lock (_syncRoot)
        {
            if (_positions[0] is not null)
            {
                return false;
            }

            _positions[0] = product;
            return true;
        }
    }

    /// <summary>
    /// 컨베이어를 한 위치만큼 이동합니다.
    /// 다음 위치가 비어 있는 제품만 이동하며, 마지막 위치는 다음 설비가 인계할 때까지 유지됩니다.
    /// </summary>
    public bool TryMoveOneStep()
    {
        lock (_syncRoot)
        {
            if (!IsRunning)
            {
                return false;
            }

            var moved = false;

            for (var index = PositionCount - 2; index >= 0; index--)
            {
                if (_positions[index] is null || _positions[index + 1] is not null)
                {
                    continue;
                }

                _positions[index + 1] = _positions[index];
                _positions[index] = null;
                moved = true;
            }

            return moved;
        }
    }

    /// <summary>마지막 위치의 제품을 다음 설비로 인계합니다.</summary>
    public bool TryTakeOutput(out ConveyorProduct? product)
    {
        lock (_syncRoot)
        {
            product = _positions[^1];

            if (product is null)
            {
                return false;
            }

            _positions[^1] = null;
            return true;
        }
    }

    /// <summary>Modbus Discrete Input 반영에 사용할 위치별 재실 상태를 복사해 반환합니다.</summary>
    public bool[] GetOccupancySnapshot()
    {
        lock (_syncRoot)
        {
            return _positions.Select(product => product is not null).ToArray();
        }
    }
}