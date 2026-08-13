using SmartFactoryActuator.TransferConveyor.InfeedConveyor;

namespace SmartFactoryActuator.TransferConveyor.InspetionConveyor;

/// <summary>
/// Infeed Conveyor에서 인계된 제품을 검사 위치와 다음 공정까지 이송합니다.
/// Modbus 계층은 이 Work의 상태를 읽고, PLC 명령만 전달합니다.
/// </summary>
public sealed class InspetionConveyorWork
{
    public const int PositionCount = 12;

    private readonly object _syncRoot = new();
    private readonly ConveyorProduct?[] _positions = new ConveyorProduct?[PositionCount];

    public bool IsRunning { get; private set; }

    /// <summary>1번 위치가 비어 있어 이전 컨베이어에서 제품을 받을 수 있는 상태입니다.</summary>
    public bool CanAcceptInput
    {
        get
        {
            lock (_syncRoot)
            {
                return _positions[0] is null;
            }
        }
    }

    /// <summary>마지막 위치의 제품을 다음 공정으로 넘길 수 있는 상태입니다.</summary>
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

    public void Start()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    /// <summary>Infeed Conveyor에서 인계된 제품을 1번 위치에 수신합니다.</summary>
    public bool TryAcceptInput(ConveyorProduct product)
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
    /// 제품을 한 위치만큼 이송합니다.
    /// 다음 위치가 점유되어 있으면 앞 제품을 추월하거나 덮어쓰지 않습니다.
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

    /// <summary>마지막 위치의 제품을 다음 공정으로 인계합니다.</summary>
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

    /// <summary>PLC Modbus Input에 반영할 위치별 재실 상태를 복사합니다.</summary>
    public bool[] GetOccupancySnapshot()
    {
        lock (_syncRoot)
        {
            return _positions.Select(product => product is not null).ToArray();
        }
    }
}