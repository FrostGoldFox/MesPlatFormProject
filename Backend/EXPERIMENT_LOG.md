# 실험 로그 — Backend ↔ PLC Socket+JSON 통신 (2026-08-14)

> 목적: `Backend/NetWork.cs`(신규, 백엔드 측 JSON 소켓 클라이언트)가 `VirtualPLC`의 `JsonNetWork` 서버와
> 실제 프로세스 3개(장비 호스트 · PLC · 백엔드)로 분리된 상태에서 정상적으로 송수신하는지 검증한다.
> 스펙 정본은 `VirtualPLC/PLC_API_SPEC.md`.

## 1. 구성

```
SmartFactoryActuator.exe (장비 5종 호스트, 포트 1502~1506)
        ↑ Modbus TCP
VirtualPLC.exe (PLC, 장비 폴링 + JSON API 서버 포트 6000)
        ↑ Socket + JSON
Backend.exe (백엔드 클라이언트, Backend/NetWork.cs)
```

세 개 다 **별도 OS 프로세스**로 띄웠다 — in-process 테스트가 아니라 실제 소켓 연결로 검증했다.

## 2. 실행 명령 (실행 순서대로)

```bash
# 1) 장비 5종 호스트
cd SmartFactoryActuator/SmartFactoryActuator
dotnet run

# 2) PLC (장비가 뜬 뒤 실행)
cd SmartFactoryActuator/VirtualPLC
dotnet run

# 3) 백엔드 클라이언트 (PLC API 서버가 뜬 뒤 실행)
cd SmartFactoryActuator/Backend
dotnet run
```

## 3. 실행 결과 (실측, 편집 없음)

**장비 호스트**
```
장비 5종 기동 완료
  Infeed Conveyor    : 1502
  Pneumatic Pressure : 1503
  Vibration          : 1504
  Reject Cylinder    : 1505
  Inspection Conveyor: 1506
Ctrl+C로 종료합니다.
```

**PLC**
```
장비 5종 Modbus 연결 완료
PLC API 서버 시작 (포트 6000)
```

**백엔드**
```
[Backend] PLC 접속 시도: 127.0.0.1:6000
[Backend] 접속 완료

--- 1. 기동 직후 상태 조회 ---
요청 : GetStatus {}
응답 : { step: WaitingForInfeed, alarmType: null }

--- 2. 제품 등록 (EXP-0001) ---
요청 : RegisterProduct { serialNumber: "EXP-0001" }
응답 : { success: True }

--- 3. 같은 시각에 두 번째 제품 등록 시도 (EXP-0002, 실패해야 정상) ---
요청 : RegisterProduct { serialNumber: "EXP-0002" }
응답 : { success: False }

--- 4. 등록 직후 상태 재조회 (물리적으로 Infeed에 적재되지 않아 대기 상태 유지 예상) ---
요청 : GetStatus {}
응답 : { step: WaitingForInfeed, alarmType: null }

--- 5. 아직 검사 대기 단계가 아닌데 비전 판정 제출 시도 (실패해야 정상) ---
요청 : SubmitVisionResult { serialNumber: "EXP-0001", result: Failed }
응답 : { success: False }

--- 6. 알람이 없는 상태에서 알람 리셋 시도 (실패해야 정상) ---
요청 : ResetAlarm {}
응답 : { success: False }

[Backend] 실험 종료
```

## 4. 프레임 예시 (2번 요청, RegisterProduct 기준 실제 바이트 구조)

```
Body(JSON, 27바이트) : {"serialNumber":"EXP-0001"}
                        7B 22 73 65 72 69 61 6C 4E 75 6D 62 65 72 22 3A
                        22 45 58 50 2D 30 30 30 31 22 7D

Header(14바이트, 전부 리틀 엔디언):
  A7 5C        Magic  = 0x5CA7
  01           Version = 1
  01           Type    = 1 (Request)
  01 00        OpCode  = 1 (RegisterProduct)
  01 00 00 00  CorrelationId = 1
  1B 00 00 00  BodyLength = 27
```
실제로 `System.Text.Json`이 직렬화한 바이트 수를 세어 검증한 값이다(27바이트, 편집·어림값 아님).

## 5. 확인된 것

- 세 프로세스가 완전히 분리된 상태에서 Socket+JSON 프레임이 깨지지 않고 왕복했다
- `RegisterProduct` 중복 호출을 PLC가 정확히 거부했다(`IsBusy` 가드 동작 확인) — 두 번째 요청 이후에도 서버가 죽지 않고 계속 응답
- `SubmitVisionResult`/`ResetAlarm`을 잘못된 상태에서 호출했을 때 `success:false`로 안전하게 거부되는 것을 확인 — 예외로 연결이 끊기지 않는다

## 6. 확인하지 못한 것 (한계, 의도된 범위)

**공정이 `WaitingForInfeed`에서 더 진행되지 않는다.** 이건 버그가 아니라 설계 경계다 —
`공장계획.md` §2.2가 정의한 역할 분리("PLC가 만드는 것: 재실 여부... / MES가 만드는 것: Serial 채번...")상
**"재실 감지"는 물리 계층(장비 시뮬레이터)의 몫이고 백엔드가 원격으로 명령할 대상이 아니다.**
지금 3프로세스 분리 구조에서는 `SmartFactoryActuator.exe` 프로세스 내부에서만
`InfeedConveyorWork.TryLoadProduct(serial)`를 호출할 수 있어, 백엔드가 이 실험처럼 완전히 외부에서
붙는 상태로는 Vibrating 이후 단계를 재현할 수 없다.

**다음에 필요한 것**: 플랜트 시뮬레이터(2·3·5·6·7공정 이벤트 생성기)가 이 "재실" 트리거를 실제로 발행하는
지점을 아직 구현하지 않았다. 그 부분이 붙으면 이 실험을 확장해 전체 사이클(Vibrating→Fastening→
WaitingForVision→Completed)까지 백엔드 관점에서 재현할 수 있다.
