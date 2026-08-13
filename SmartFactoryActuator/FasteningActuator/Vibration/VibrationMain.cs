using System.Net;

namespace SmartFactoryActuator.FasteningActuator.Vibration;

/// <summary>
/// 진동 센서 구성·실행 진입점입니다.
/// </summary>
public sealed class VibrationMain : IAsyncDisposable
{
    public VibrationWork Work { get; }
    public VibrationNetWork NetWork { get; }

    public VibrationMain(IPAddress address, int port)
    {
        Work = new VibrationWork();
        NetWork = new VibrationNetWork(Work, address, port);
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