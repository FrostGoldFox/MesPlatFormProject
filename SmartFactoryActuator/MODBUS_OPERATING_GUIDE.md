# SmartFactoryActuator 운용·Modbus 통신 가이드

## 1. 실행 방법

프로젝트 루트에서 아래 명령을 실행합니다.

```powershell
dotnet run
```

실행 프로그램은 로컬 PC(`127.0.0.1`)에서 Infeed Conveyor, Pneumatic Pressure, Vibration, Reject Cylinder를 Modbus TCP Slave로 시작한 뒤, 내부 검증 Master가 명령을 보내고 상태를 읽습니다. 검증이 끝나면 모든 Server를 종료합니다.

## 2. 장비별 통신 주소

| 장비 | TCP 포트 | Unit ID | PLC가 쓰는 값 | PLC가 읽는 값 |
|---|---:|---:|---|---|
| Infeed Conveyor | 1502 | `0x10` | Coil 0: 운전, Coil 1: 정지 | Discrete Input 0: 운전 상태, 1~12: 위치 점유 |
| Pneumatic Pressure | 1503 | `0x20` | Holding Register 1: 목표 압력 | HR 0: 상태, HR 1: 목표 압력, HR 2: 현재 압력 |
| Vibration | 1504 | `0x30` | Holding Register 1: 목표 진동값 | HR 0: 상태, HR 1: 목표 진동값, HR 2: 현재 진동값 |
| Reject Cylinder | 1505 | `0x40` | Coil 0: 전진, Coil 1: 후진 | Discrete Input 0: 전진 상태, 1: 후진 상태 |

## 3. 데이터 의미

- 공압값은 `ushort` 원시값이며, 현재 기본 목표값은 `500`입니다.
- 공압 센서는 정상 상태에서 목표값의 ±10 범위를 유지합니다.
- 진동 센서는 정상 상태에서 목표값의 ±5 범위를 유지합니다.
- `State` 값은 `0=Unknown`, `1=Working`, `2=Stopped`, `3=Error`입니다.
- 컨베이어는 12개 위치를 갖고, `Discrete Input 1~12`가 각각 위치 1~12의 제품 점유 여부를 의미합니다.

## 4. 현재 검증 시나리오

1. Infeed Conveyor에 `DEMO-0001` 제품을 투입하고 운전 Coil을 기록합니다.
2. Master가 Discrete Input을 읽어 운전 상태와 제품 위치를 확인합니다.
3. 공압 목표값을 `500`에서 `650`으로 변경하고 Holding Register를 재조회합니다.
4. 진동 목표값을 `80`으로 변경하고 현재 진동값을 확인합니다.
5. Reject Cylinder에 전진·후진 Coil 명령을 보내고 각각의 상태 Input을 확인합니다.
6. Inspection Conveyor Work에 제품을 수신하고 2번 위치까지 이동시킵니다.

## 5. 구조 메모

현재 각 가상 장비는 테스트 편의를 위해 서로 다른 TCP 포트를 사용합니다. 따라서 실제 VirtualPLC가 장비별로 직접 연결한다면 장비별 TCP 연결을 관리해야 합니다. 추후 하나의 TCP 포트/여러 Unit ID 구조로 통합하면 VirtualPLC는 Master 연결 하나로 모든 장비를 폴링할 수 있습니다.