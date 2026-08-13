using System.Net;

namespace SmartFactoryActuator.RejectCylinder;

/// <summary>
/// Reject Cylinder 구성·실행 진입점입니다.
/// </summary>
public sealed class RejectCylinderMain : IAsyncDisposable
{
    public RejectCylinderWork Work { get; }
    public RejectCylinderNetWork NetWork { get; }

    public RejectCylinderMain(IPAddress address, int port)
    {
        Work = new RejectCylinderWork();
        NetWork = new RejectCylinderNetWork(Work, address, port);
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