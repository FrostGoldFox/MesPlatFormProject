using System.Net;
using System.Net.Sockets;
using NModbus;
using SmartFactoryActuator.FasteningActuator.PneumaticPressure;
using SmartFactoryActuator.FasteningActuator.Vibration;
using SmartFactoryActuator.RejectCylinder;
using SmartFactoryActuator.Shared.ProcessControl;
using SmartFactoryActuator.TransferConveyor.InfeedConveyor;
using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

const int InfeedPort = 1502;
const int PneumaticPort = 1503;
const int VibrationPort = 1504;
const int RejectPort = 1505;
const int InspectionPort = 1506;

using var cancellation = new CancellationTokenSource();
await using var infeed = new InfeedConveyorMain(IPAddress.Loopback, InfeedPort);
await using var pneumatic = new PneumaticPressureMain(IPAddress.Loopback, PneumaticPort);
await using var vibration = new VibrationMain(IPAddress.Loopback, VibrationPort);
await using var reject = new RejectCylinderMain(IPAddress.Loopback, RejectPort);
var inspectionWork = new InspetionConveyorWork();
await using var inspection = new InspetionConveyorNetWork(inspectionWork, IPAddress.Loopback, InspectionPort);

Task[] devices =
[
    infeed.RunAsync(cancellation.Token),
    pneumatic.RunAsync(cancellation.Token),
    vibration.RunAsync(cancellation.Token),
    reject.RunAsync(cancellation.Token),
    inspection.RunAsync(cancellation.Token)
];

await Task.Delay(250);

using var infeedClient = new TcpClient();
using var pneumaticClient = new TcpClient();
using var vibrationClient = new TcpClient();
using var rejectClient = new TcpClient();
using var inspectionClient = new TcpClient();
await Task.WhenAll(
    infeedClient.ConnectAsync(IPAddress.Loopback, InfeedPort),
    pneumaticClient.ConnectAsync(IPAddress.Loopback, PneumaticPort),
    vibrationClient.ConnectAsync(IPAddress.Loopback, VibrationPort),
    rejectClient.ConnectAsync(IPAddress.Loopback, RejectPort),
    inspectionClient.ConnectAsync(IPAddress.Loopback, InspectionPort));

var factory = new ModbusFactory();
var infeedMaster = factory.CreateMaster(infeedClient);
var pneumaticMaster = factory.CreateMaster(pneumaticClient);
var vibrationMaster = factory.CreateMaster(vibrationClient);
var inspectionMaster = factory.CreateMaster(inspectionClient);
var rejectMaster = factory.CreateMaster(rejectClient);
var plc = new VirtualPlcModbusController(
    infeedMaster, pneumaticMaster, vibrationMaster, inspectionMaster, rejectMaster);

var visionTriggered = false;
plc.VisionRequested += _ => visionTriggered = true;

Console.WriteLine("=== PLC 순차 공정 테스트 ===");
const string serialNumber = "PROCESS-0001";
if (!infeed.TryLoadProduct(serialNumber) || !plc.TryRegisterProduct(serialNumber, DateTimeOffset.UtcNow))
{
    throw new InvalidOperationException("제품 등록 실패");
}

var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(12);
while (plc.Process.Step != ProcessStep.Vibrating && DateTimeOffset.UtcNow < timeoutAt)
{
    plc.PollOnce(DateTimeOffset.UtcNow);
    await Task.Delay(100);
}

if (plc.Process.Step != ProcessStep.Vibrating)
{
    throw new TimeoutException("Infeed 인계 및 진동 시작을 확인하지 못했습니다.");
}

await Task.Delay(150);
bool vibrationRunning = vibrationMaster.ReadInputs(VibrationNetWork.Id, 0, 1)[0];
DateTimeOffset vibrationFinishedAt = DateTimeOffset.UtcNow.AddSeconds(31);
plc.PollOnce(vibrationFinishedAt);
await Task.Delay(150);
bool vibrationStopped = !vibrationMaster.ReadInputs(VibrationNetWork.Id, 0, 1)[0];
bool fasteningRunning = pneumaticMaster.ReadInputs(PneumaticPressureNetWork.Id, 0, 1)[0];

DateTimeOffset fasteningFinishedAt = vibrationFinishedAt.AddSeconds(31);
plc.PollOnce(fasteningFinishedAt);
await Task.Delay(150);
bool inspectionRunning = inspectionMaster.ReadInputs(InspetionConveyorNetWork.UnitId, 0, 1)[0];

Console.WriteLine($"Vibration: Started={vibrationRunning}, Stopped={vibrationStopped}");
Console.WriteLine($"Fastening: RunningAfterVibration={fasteningRunning}");
Console.WriteLine($"Inspection: Running={inspectionRunning}, VisionTriggered={visionTriggered}");

if (!vibrationRunning || !vibrationStopped || !fasteningRunning || !inspectionRunning ||
    !visionTriggered || plc.Process.Step != ProcessStep.WaitingForVision)
{
    throw new InvalidOperationException("순차 공정 테스트 검증 실패");
}

Console.WriteLine("순차 공정 테스트 성공");

// 설비 통신과 무관한 PLC 안전 규칙 검증: 중복 투입과 Infeed 이송 시간 초과
var safetyProcess = new VirtualPlcProcessController();
DateTimeOffset safetyStartedAt = DateTimeOffset.UtcNow;
bool firstRegistration = safetyProcess.TryRegisterInfeedProduct("SAFETY-0001", safetyStartedAt);
bool duplicateRegistrationBlocked = !safetyProcess.TryRegisterInfeedProduct("SAFETY-0002", safetyStartedAt);
safetyProcess.Tick(safetyStartedAt, infeedPosition1HasProduct: true, infeedPosition12HasProduct: false);
safetyProcess.Tick(safetyStartedAt.Add(VirtualPlcProcessController.InfeedTransferTimeout).AddMilliseconds(1), false, false);
bool infeedTimeoutDetected = safetyProcess.Step == ProcessStep.Faulted &&
    safetyProcess.ActiveAlarm?.Type == ProcessAlarmType.InfeedTimeout;
bool stopCommandIssued = safetyProcess.DequeueCommands().Any(command => command.Type == ProcessCommandType.StopInfeed);
bool alarmReset = safetyProcess.ResetAlarm();

Console.WriteLine($"Safety: DuplicateBlocked={duplicateRegistrationBlocked}, InfeedTimeout={infeedTimeoutDetected}, StopCommand={stopCommandIssued}, Reset={alarmReset}");
if (!firstRegistration || !duplicateRegistrationBlocked || !infeedTimeoutDetected || !stopCommandIssued || !alarmReset)
{
    throw new InvalidOperationException("안전 규칙 테스트 검증 실패");
}
cancellation.Cancel();
try { await Task.WhenAll(devices); }
catch (Exception exception) when (exception is OperationCanceledException or SocketException) { }