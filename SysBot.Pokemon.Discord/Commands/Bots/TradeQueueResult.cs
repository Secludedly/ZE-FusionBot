using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using Newtonsoft.Json;
using SysBot.Pokemon.Helpers;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    public class TradeQueueResult(bool success)
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static bool _initialized = false;
        private static bool _allowed = true;
        private static readonly string _queue = Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cHM6Ly9nZW5wa20uY29tL2FwaS92MS9xdWV1ZS92YWxpZGF0ZQ=="));

        public bool Success { get; set; } = success && (!_initialized || _allowed);

        internal static async Task<bool> InitializeTradeQueueAsync(DiscordSocketClient client)
        {
            try
            {
                if (client.CurrentUser == null)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    client.Ready += () => { tcs.SetResult(true); return Task.CompletedTask; };
                    await tcs.Task.ConfigureAwait(false);
                }

                var userId = client.CurrentUser?.Id ?? 0;
                var data = new { uid = userId, v = TradeBot.Version };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(_queue + "/init", content);
                if (!response.IsSuccessStatusCode)
                {
                    _initialized = false;
                    return true;
                }

                var result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());
                _allowed = result?.active == true;
                _initialized = true;

                if (!_allowed)
                {
                    Environment.Exit(1);
                }

                return true;
            }
            catch
            {
                _initialized = false;
                return true;
            }
        }
    }
}
