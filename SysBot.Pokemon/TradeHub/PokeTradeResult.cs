namespace SysBot.Pokemon;

public enum PokeTradeResult
{
    Success,

    // Trade Partner Failures
    NoTrainerFound,

    TrainerTooSlow,

    TrainerLeft,

    TrainerOfferCanceledQuick,

    TrainerRequestBad,

    IllegalTrade,

    SuspiciousActivity,

    UserCanceled,

    // Recovery -- General Bot Failures
    // Anything below here should be retried once if possible.
    RoutineCancel,

    ExceptionConnection,

    ExceptionInternal,

    RecoverStart,

    RecoverPostLinkCode,

    RecoverOpenBox,

    RecoverReturnOverworld,

    RecoverEnterUnionRoom,

    TradeEvolveNotAllowed,
}

public static class PokeTradeResultExtensions
{
    public static bool ShouldAttemptRetry(this PokeTradeResult t) => t >= PokeTradeResult.RoutineCancel;
}
