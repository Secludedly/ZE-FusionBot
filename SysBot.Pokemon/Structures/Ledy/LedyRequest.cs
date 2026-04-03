using PKHeX.Core;

namespace SysBot.Pokemon;

public class LedyRequest<T>(T RequestInfo, string Nickname, int? TradeCode = null)
    where T : PKM, new()
{
    public readonly string Nickname = Nickname;

    public readonly T RequestInfo = RequestInfo;

    public readonly int? TradeCode = TradeCode;
}
