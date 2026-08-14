using System.Net;
using System.Text.Json;
using SmartFactoryActuator.Shared.Config;
using VirtualPLC;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

await using var modbus = new ModbusNetWork(IPAddress.Parse(EnvironmentConfig.Host));
await modbus.ConnectAsync(
    EnvironmentConfig.InfeedConveyorPort,
    EnvironmentConfig.PneumaticPressurePort,
    EnvironmentConfig.VibrationPort,
    EnvironmentConfig.RejectCylinderPort,
    EnvironmentConfig.InspectionConveyorPort,
    cancellation.Token);
Console.WriteLine("장비 5종 Modbus 연결 완료");

var runtime = new PlcModbusRuntime(modbus);
runtime.VisionRequested += serial => Console.WriteLine($"[Vision 요청] {serial}");
runtime.ProcessCompleted += completion => Console.WriteLine($"[공정 완료] {completion.SerialNumber} -> {completion.InspectionResult}");
runtime.AlarmRaised += alarm => Console.WriteLine($"[알람] {alarm.Type}: {alarm.Message}");

var requestJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

async ValueTask<object?> HandleRequestAsync(JsonRequest request, CancellationToken _)
{
    switch ((PlcOpcode)request.OpCode)
    {
        case PlcOpcode.RegisterProduct:
        {
            RegisterProductRequest body = request.Payload.Deserialize<RegisterProductRequest>(requestJsonOptions)
                ?? throw new InvalidDataException("RegisterProduct payload가 비어 있습니다.");
            bool success = runtime.TryRegisterProduct(body.SerialNumber, DateTimeOffset.UtcNow);
            return new RegisterProductResponse(success);
        }

        case PlcOpcode.SubmitVisionResult:
        {
            SubmitVisionResultRequest body = request.Payload.Deserialize<SubmitVisionResultRequest>(requestJsonOptions)
                ?? throw new InvalidDataException("SubmitVisionResult payload가 비어 있습니다.");
            bool success = runtime.TrySubmitVisionResult(body.SerialNumber, body.Result, DateTimeOffset.UtcNow);
            return new SubmitVisionResultResponse(success);
        }

        case PlcOpcode.GetStatus:
            return new GetStatusResponse(
                runtime.Process.Step.ToString(),
                runtime.Process.ActiveAlarm?.Type.ToString(),
                runtime.Process.ActiveAlarm?.Message);

        case PlcOpcode.ResetAlarm:
            return new ResetAlarmResponse(runtime.Process.ResetAlarm());

        default:
            throw new InvalidDataException($"알 수 없는 OpCode입니다: {request.OpCode}");
    }
}

await using var jsonServer = new JsonNetWork(IPAddress.Any, EnvironmentConfig.PlcApiPort, HandleRequestAsync);
Task apiServerTask = jsonServer.RunAsync(cancellation.Token);
Console.WriteLine($"PLC API 서버 시작 (포트 {EnvironmentConfig.PlcApiPort})");

// VirtualPLCTestWPF가 "물건 올려두기" 등 수동 신호를 보내는 통신. 실물 센서가 없는 개발 단계 전용이다.
var manualInfeedSignal = new InfeedConveyorDataModel();
var manualPneumaticPressureSignal = new PneumaticPressureDataModel();
var manualInspectionSignal = new InspectionConveyorDataModel();
await using var testNetwork = new TestNetWork(manualInfeedSignal, manualPneumaticPressureSignal, manualInspectionSignal, IPAddress.Any, EnvironmentConfig.ReservedPort1);
Task testNetworkTask = testNetwork.RunAsync(cancellation.Token);
Console.WriteLine($"테스트 신호 수신 시작 (포트 {EnvironmentConfig.ReservedPort1})");

using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
try
{
    while (await pollTimer.WaitForNextTickAsync(cancellation.Token))
    {
        // Command 5는 수신 1건당 한 번만 Inspection 장비의 Clear Coil로 전달한다.
        if (testNetwork.TryConsumeCommand5())
        {
            modbus.ClearInspectionPosition12();
        }

        // Command 6: 체결설비→Inspection Position1 인계(사람이 옮기는 구간)를 한 번만 전달한다.
        if (testNetwork.TryConsumeCommand6())
        {
            modbus.AcceptInspectionInput();
        }

        // WPF 테스트 신호로 바뀐 목표압력을 실제 장비에 매 주기 반영한다.
        modbus.SetTargetPressure(manualPneumaticPressureSignal.TargetPressure);

        runtime.PollOnce(
            DateTimeOffset.UtcNow,
            manualPosition1Override: manualInfeedSignal.PositionOccupied[0],
            manualPosition12Override: manualInfeedSignal.PositionOccupied[11]);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C 종료
}

await Task.WhenAll(apiServerTask, testNetworkTask);
