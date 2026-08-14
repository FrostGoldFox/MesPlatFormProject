using System.Net;
using System.Text.Json;
using VirtualPLC;

// TODO(D5, 접속 설정 외부화): 아래 주소·포트는 임시 기본값이다. EnvironmentConfig 구현 후 설정 파일로 옮길 것.
const string DeviceHost = "127.0.0.1";
const int InfeedPort = 1502;
const int PneumaticPort = 1503;
const int VibrationPort = 1504;
const int RejectPort = 1505;
const int InspectionPort = 1506;
const int PlcApiPort = 6000;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

await using var modbus = new ModbusNetWork(IPAddress.Parse(DeviceHost));
await modbus.ConnectAsync(InfeedPort, PneumaticPort, VibrationPort, RejectPort, InspectionPort, cancellation.Token);
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
            bool success = runtime.TrySubmitVisionResult(body.SerialNumber, body.Result);
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

await using var jsonServer = new JsonNetWork(IPAddress.Any, PlcApiPort, HandleRequestAsync);
Task apiServerTask = jsonServer.RunAsync(cancellation.Token);
Console.WriteLine($"PLC API 서버 시작 (포트 {PlcApiPort})");

using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
try
{
    while (await pollTimer.WaitForNextTickAsync(cancellation.Token))
    {
        runtime.PollOnce(DateTimeOffset.UtcNow);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C 종료
}

await apiServerTask;
