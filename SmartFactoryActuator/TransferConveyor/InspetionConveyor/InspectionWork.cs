using SmartFactoryActuator.TransferConveyor.InfeedConveyor;

namespace SmartFactoryActuator.TransferConveyor.InspetionConveyor;

public enum InspectionResult
{
    Passed,
    Failed,
    Unjudged
}

/// <summary>
/// 비전 검사의 결과를 공정 규칙으로 변환합니다.
/// AI 판정과 MES 전송은 외부 계층의 책임이며, 이 클래스는 통과·배출·Bypass 여부만 결정합니다.
/// </summary>
public sealed class InspectionWork
{
    public InspectionDecision Decide(ConveyorProduct product, bool sensorDetected, InspectionResult? aiResult)
    {
        ArgumentNullException.ThrowIfNull(product);

        // 트리거 미감지 또는 제한 시간 내 AI 결과 미도착은 설비 정지가 아닌 Bypass 대상입니다.
        if (!sensorDetected || aiResult is null)
        {
            return new InspectionDecision(product, InspectionResult.Unjudged, false);
        }

        return new InspectionDecision(product, aiResult.Value, aiResult == InspectionResult.Failed);
    }
}

public sealed record InspectionDecision(
    ConveyorProduct Product,
    InspectionResult Result,
    bool RequiresReject);
