namespace OPTISENS.Core.Session;

public enum RoundPhase
{
    Idle,
    RoundIntro,
    BlockTracking,
    BlockFlick,
    BlockDot,
    RoundScoring,
    Rest,
    SessionComplete,
    Aborted
}

public readonly record struct RoundPlan(int RoundNumber, double ProbeCm360, double RoundStartS);
