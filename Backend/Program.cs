using System.Net;
using Backend;
using SmartFactoryActuator.TransferConveyor.InspetionConveyor;

const string PlcHost = "127.0.0.1";
const int PlcApiPort = 6000;

Console.WriteLine($"[Backend] PLC 접속 시도: {PlcHost}:{PlcApiPort}");
await using var network = new NetWork();
await network.ConnectAsync(IPAddress.Parse(PlcHost), PlcApiPort);
Console.WriteLine("[Backend] 접속 완료");
Console.WriteLine();

Console.WriteLine("--- 1. 기동 직후 상태 조회 ---");
var status1 = await network.GetStatusAsync();
Console.WriteLine($"요청 : GetStatus {{}}");
Console.WriteLine($"응답 : {{ step: {status1.Step}, alarmType: {status1.AlarmType ?? "null"} }}");
Console.WriteLine();

Console.WriteLine("--- 2. 제품 등록 (EXP-0001) ---");
var register1 = await network.RegisterProductAsync("EXP-0001");
Console.WriteLine($"요청 : RegisterProduct {{ serialNumber: \"EXP-0001\" }}");
Console.WriteLine($"응답 : {{ success: {register1.Success} }}");
Console.WriteLine();

Console.WriteLine("--- 3. 같은 시각에 두 번째 제품 등록 시도 (EXP-0002, 실패해야 정상) ---");
var register2 = await network.RegisterProductAsync("EXP-0002");
Console.WriteLine($"요청 : RegisterProduct {{ serialNumber: \"EXP-0002\" }}");
Console.WriteLine($"응답 : {{ success: {register2.Success} }}");
Console.WriteLine();

Console.WriteLine("--- 4. 등록 직후 상태 재조회 (물리적으로 Infeed에 적재되지 않아 대기 상태 유지 예상) ---");
var status2 = await network.GetStatusAsync();
Console.WriteLine($"요청 : GetStatus {{}}");
Console.WriteLine($"응답 : {{ step: {status2.Step}, alarmType: {status2.AlarmType ?? "null"} }}");
Console.WriteLine();

Console.WriteLine("--- 5. 아직 검사 대기 단계가 아닌데 비전 판정 제출 시도 (실패해야 정상) ---");
var vision = await network.SubmitVisionResultAsync("EXP-0001", InspectionResult.Failed);
Console.WriteLine($"요청 : SubmitVisionResult {{ serialNumber: \"EXP-0001\", result: Failed }}");
Console.WriteLine($"응답 : {{ success: {vision.Success} }}");
Console.WriteLine();

Console.WriteLine("--- 6. 알람이 없는 상태에서 알람 리셋 시도 (실패해야 정상) ---");
var reset = await network.ResetAlarmAsync();
Console.WriteLine($"요청 : ResetAlarm {{}}");
Console.WriteLine($"응답 : {{ success: {reset.Success} }}");
Console.WriteLine();

Console.WriteLine("[Backend] 실험 종료");
