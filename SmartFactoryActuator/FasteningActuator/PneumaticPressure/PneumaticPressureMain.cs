using System.Net;

namespace SmartFactoryActuator.FasteningActuator.PneumaticPressure;

/// <summary>
/// 공압 센서 구성·실행 진입점입니다.
/// </summary>
public sealed class PneumaticPressureMain : IAsyncDisposable
{
    public PneumaticPressureWork Work { get; }
    public PneumaticPressureNetWork NetWork { get; }

    public PneumaticPressureMain(IPAddress address, int port)
    {
        Work = new PneumaticPressureWork();
        NetWork = new PneumaticPressureNetWork(Work, address, port);
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