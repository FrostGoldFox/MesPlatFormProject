using System.Net;

namespace SmartFactoryActuator.TransferConveyor.InfeedConveyor;

/// <summary>
/// Infeed Conveyor 구성 요소의 시작 지점입니다.
/// PLC 테스트 시 이 객체 하나를 만들고 RunAsync를 실행합니다.
/// </summary>
public sealed class InfeedConveyorMain : IAsyncDisposable
{
    public InfeedConveyorWork Work { get; }
    public InfeedConveyorNetWork NetWork { get; }

    public InfeedConveyorMain(IPAddress address, int port)
    {
        Work = new InfeedConveyorWork();
        NetWork = new InfeedConveyorNetWork(Work, address, port);
    }

    /// <summary>
    /// MES 또는 투입 공정이 만든 Serial 번호로 제품을 1번 위치에 투입합니다.
    /// </summary>
    public bool TryLoadProduct(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        return Work.TryLoad(new ConveyorProduct(serialNumber));
    }

    /// <summary>
    /// Inspection Conveyor가 수신 준비 상태일 때 호출할 제품 인계 진입점입니다.
    /// 수신 측이 실제로 제품을 받을 수 있는지는 호출 전에 확인해야 합니다.
    /// </summary>
    public bool TryTakeProductForInspection(out ConveyorProduct? product)
    {
        return Work.TryTakeOutput(out product);
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        return NetWork.RunAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return NetWork.DisposeAsync();
    }
}