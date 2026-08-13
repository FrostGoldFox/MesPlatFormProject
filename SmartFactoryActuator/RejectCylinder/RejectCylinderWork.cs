namespace SmartFactoryActuator.RejectCylinder;

public enum RejectCylinderState : ushort
{
    Retracted = 0,
    Extended = 1
}

/// <summary>
/// NG 제품을 라인 밖으로 밀어내는 가상 배출 실린더입니다.
/// </summary>
public sealed class RejectCylinderWork
{
    public RejectCylinderState State { get; private set; } = RejectCylinderState.Retracted;

    public bool IsExtended => State == RejectCylinderState.Extended;

    public void Extend()
    {
        State = RejectCylinderState.Extended;
    }

    public void Retract()
    {
        State = RejectCylinderState.Retracted;
    }
}