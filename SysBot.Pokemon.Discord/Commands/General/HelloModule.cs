using Discord;
using Discord.Commands;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon.Discord
{
    public class HelloModule : ModuleBase<SocketCommandContext>
    {
        [Command("hello")]
        [Alias("hi", "hey", "yo")]
        [Summary("Say hello to the bot and get a response.")]
        public async Task PingAsync()
        {
            var str = SysCordSettings.Settings.HelloResponse;
            var msg = string.Format(str, Context.User.Mention);

            string? imageUrl = null;
            // Regular expression to extract URL from the message
            var urlMatch = Regex.Match(msg, @"(http[s]?:\/\/.*\.(?:png|jpg|gif|jpeg))", RegexOptions.IgnoreCase);

            if (urlMatch.Success)
            {
                imageUrl = urlMatch.Value;
                // Remove the image URL from the message to avoid duplication
                msg = msg.Replace(imageUrl, "").Trim();
            }

            var embedBuilder = new EmbedBuilder()
                .WithTitle("Hello!")
                .WithDescription(msg)
                .WithColor(Color.Green);

            if (!string.IsNullOrEmpty(imageUrl))
            {
                embedBuilder.WithImageUrl(imageUrl);
            }

            var embed = embedBuilder.Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }
    }
}
