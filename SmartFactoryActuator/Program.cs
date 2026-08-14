using System.Net;
using System.Net.Sockets;
using SmartFactoryActuator.FasteningActuator.PneumaticPressure;
using SmartFactoryActuator.FasteningActuator.Vibration;
using SmartFactoryActuator.RejectCylinder;
using SmartFactoryActuator.Shared.Config;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;
using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

// 장비 5종을 상시 Modbus TCP Slave로 띄우는 호스트입니다.
// PLC(VirtualPLC.exe)가 반복해서 접속·폴링할 상대이므로, 한 번의 시나리오를 검증하고 끝나면 안 됩니다 — Ctrl+C 전까지 계속 떠 있습니다.

var host = IPAddress.Parse(EnvironmentConfig.Host);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

await using var infeed = new InfeedConveyorMain(host, EnvironmentConfig.InfeedConveyorPort);
await using var pneumatic = new PneumaticPressureMain(host, EnvironmentConfig.PneumaticPressurePort);
await using var vibration = new VibrationMain(host, EnvironmentConfig.VibrationPort);
await using var reject = new RejectCylinderMain(host, EnvironmentConfig.RejectCylinderPort);
var inspectionWork = new InspetionConveyorWork();
await using var inspection = new InspetionConveyorNetWork(inspectionWork, host, EnvironmentConfig.InspectionConveyorPort);

Console.WriteLine("장비 5종 기동 완료");
Console.WriteLine($"  Infeed Conveyor    : {EnvironmentConfig.InfeedConveyorPort}");
Console.WriteLine($"  Pneumatic Pressure : {EnvironmentConfig.PneumaticPressurePort}");
Console.WriteLine($"  Vibration          : {EnvironmentConfig.VibrationPort}");
Console.WriteLine($"  Reject Cylinder    : {EnvironmentConfig.RejectCylinderPort}");
Console.WriteLine($"  Inspection Conveyor: {EnvironmentConfig.InspectionConveyorPort}");
Console.WriteLine("Ctrl+C로 종료합니다.");

try
{
    await Task.WhenAll(
        infeed.RunAsync(cancellation.Token),
        pneumatic.RunAsync(cancellation.Token),
        vibration.RunAsync(cancellation.Token),
        reject.RunAsync(cancellation.Token),
        inspection.RunAsync(cancellation.Token));
}
catch (Exception exception) when (exception is OperationCanceledException or SocketException)
{
    // Ctrl+C 종료
}

Console.WriteLine("장비 5종 종료");
