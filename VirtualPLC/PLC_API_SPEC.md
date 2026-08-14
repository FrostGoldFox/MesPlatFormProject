# PLC ↔ 백엔드(PC) API 명세서

> 대상: `VirtualPLC/JsonNetWork.cs` 구현 기준. PLC가 TCP 서버, 백엔드가 클라이언트로 접속한다.
> 장비 4~5종 ↔ PLC 구간(Modbus TCP)과는 별개 계층이다 — 이 문서는 PLC ↔ 백엔드 구간만 다룬다.

## 1. 전송 계층

- 프로토콜: TCP 소켓, 기본 포트 `6000`
- PLC(`VirtualPLC.exe`)가 `TcpListener`로 리슨하고, 백엔드가 클라이언트로 접속한다
- 프레이밍: 파일 교환이 아니라 **소켓 위에 JSON 직렬화 메시지**를 얹는 방식 (2026-08-13 결정, MQTT·Modbus 미사용)

## 2. 프레임 포맷

```
| Magic (2B) | Version (1B) | Type (1B) | OpCode (2B) | CorrelationId (4B) | BodyLength (4B) | Body (JSON, UTF-8) |
```

- 전체 헤더 길이: 14바이트, 정수는 전부 **리틀 엔디언**
  (장비↔PLC Modbus 구간의 "상위 워드 우선(빅엔디안)" 규칙과는 무관한 별도 계층이다)
- `Magic` = `0x5CA7` 고정. 다른 값이 오면 프레임 동기가 깨진 것으로 보고 연결을 끊는다
- `Version` = `1` 고정
- `BodyLength` 상한 1MB. 벗어나면 프레임이 손상된 것으로 간주해 예외 발생 후 연결 종료

## 3. 패킷 타입 (`Type` 필드)

| 값 | 이름 | 방향 | 설명 |
|---|---|---|---|
| 1 | Request | 백엔드 → PLC | `CorrelationId`는 0이 아니어야 한다 |
| 2 | Response | PLC → 백엔드 | 같은 `CorrelationId`로 매칭 |
| 3 | Error | PLC → 백엔드 | Body는 `{ "code": string, "message": string }` |
| 4 | Notification | (미구현) | PLC가 능동적으로 푸시하는 이벤트용으로 예약된 값. 현재는 백엔드가 `GetStatus`를 주기적으로 폴링하는 방식만 지원한다 |

## 4. OpCode 및 Request/Response 본문

Body는 모두 camelCase JSON(`JsonSerializerDefaults.Web` 기준)이다.

### 4.1 OpCode 1 — RegisterProduct
Serial이 부여된 제품을 Infeed 등록 대기 상태로 PLC에 알린다. MES가 Serial을 채번한 **직후**, 실제 컨베이어 투입 전에 호출한다.

**Request**
```json
{ "serialNumber": "LOT-0001-0001" }
```

**Response**
```json
{ "success": true }
```
`success=false`인 경우: 이미 다른 제품이 처리 중이거나(`IsBusy`), PLC가 알람(Faulted) 상태다. 후자는 `ResetAlarm` 먼저 필요.

### 4.2 OpCode 2 — SubmitVisionResult
모듈3(AI 비전검사)의 판정 결과를 PLC에 전달한다. PLC는 `WaitingForVision` 상태이고 Serial이 일치할 때만 반영한다.

**Request**
```json
{ "serialNumber": "LOT-0001-0001", "result": 1 }
```
`result`: `0`=Passed, `1`=Failed, `2`=Unjudged (`InspectionResult` enum 원시값)

**Response**
```json
{ "success": true }
```

### 4.3 OpCode 3 — GetStatus
PLC의 현재 공정 단계와 알람 상태를 조회한다. Request Body는 빈 객체 `{}`.

**Response**
```json
{
  "step": "WaitingForVision",
  "alarmType": null,
  "alarmMessage": null
}
```
`step`은 `ProcessStep` enum 이름 문자열: `WaitingForInfeed`, `MovingInfeed`, `WaitingForFasteningReceipt`, `Vibrating`, `Fastening`, `WaitingForVision`, `Completed`, `Faulted`.
알람이 없으면 `alarmType`/`alarmMessage`는 `null`, 있으면 `alarmType`은 `InfeedTimeout` 또는 `ModbusCommunication`.

### 4.4 OpCode 4 — ResetAlarm
`Faulted` 상태를 해제하고 `WaitingForInfeed`로 되돌린다. Request Body는 빈 객체 `{}`.

**Response**
```json
{ "success": true }
```
`Faulted` 상태가 아니면 `success=false`.

## 5. 오류 처리

요청 처리 중 예외가 발생하면 PLC는 `Type=Error` 프레임으로 응답한다:
```json
{ "code": "HANDLER_ERROR", "message": "..." }
```
내부 예외의 스택 트레이스는 외부로 전달하지 않는다.

## 6. 흐름 예시 (정상 케이스)

```
백엔드                              PLC
  │──RegisterProduct(SMOKE-0001)──▶│
  │◀──────success=true─────────────│
  │                                 │  (Infeed→진동→체결 자동 진행, 폴링 루프가 처리)
  │──GetStatus──────────────────────▶│
  │◀───step=WaitingForVision───────│
  │──SubmitVisionResult(Failed)────▶│
  │◀──────success=true─────────────│
                                     │  (Reject 실린더 전진, PLC 내부 이벤트로 로그 기록)
```

## 7. 미구현 / 알려진 제약

- **Notification 푸시 없음.** 공정 완료·알람 발생을 PLC가 먼저 알리는 기능은 아직 없다. 지금은 백엔드가 `GetStatus`를 주기적으로 폴링해야 한다. 실시간 뷰(WPF)가 보류 상태라 당장 급하지 않지만, 필요해지면 `JsonPacketType.Notification`을 실제로 활용하는 푸시 경로를 추가해야 한다
- **동시 접속 처리 순서 미보장.** `JsonNetWork`는 클라이언트별로 별도 세션을 두지만, PLC 쪽 상태(`VirtualPlcProcessController`)는 제품 1개만 동시 처리하므로 여러 백엔드 인스턴스가 동시에 `RegisterProduct`를 보내면 하나만 성공한다(의도된 동작)
- **접속 주소 하드코딩.** `PLCMain.cs`의 장비 호스트·포트, API 포트(6000)는 임시 기본값이다. 접속 설정 외부화(D5) 완료 전까지는 코드 상수로 남아 있다
