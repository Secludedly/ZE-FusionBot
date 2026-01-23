using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Twitch;

public interface IChatBot : IDisposable
{
    bool IsConnected { get; }

    Task StartAsync(CancellationToken token);
    Task StopAsync();
    void StartingDistribution(string message);
    void SendMessage(string message);
}
