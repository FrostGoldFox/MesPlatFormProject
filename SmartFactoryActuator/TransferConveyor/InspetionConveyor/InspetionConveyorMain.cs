using SmartFactoryActuator.TransferConveyor.InfeedConveyor;

namespace SmartFactoryActuator.TransferConveyor.InspetionConveyor;

/// <summary>
/// Inspection Conveyor의 구성·운영 진입점입니다.
/// Modbus Server는 이후 InpestionConveyorNetWork에서 Work를 주입받아 연결합니다.
/// </summary>
public sealed class InspetionConveyorMain
{
    public InspetionConveyorWork Work { get; } = new();

    /// <summary>
    /// Infeed Conveyor가 인계한 제품을 Inspection Conveyor의 첫 위치에 수신합니다.
    /// </summary>
    public bool TryAcceptProduct(ConveyorProduct product)
    {
        return Work.TryAcceptInput(product);
    }

    /// <summary>
    /// 다음 공정이 제품을 수신할 수 있을 때 호출합니다.
    /// </summary>
    public bool TryTakeProductForNextProcess(out ConveyorProduct? product)
    {
        return Work.TryTakeOutput(out product);
    }
}