using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class LegalizerModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        [Command("convert"), Alias("showdown")]
        [Summary("Converts a Showdown Set to RegenTemplate format.")]
        [Priority(1)]
        public async Task ConvertShowdown([Summary("Generation/Format")] byte gen, [Remainder][Summary("Showdown Set")] string content)
        {
            var deleteMessageTask = DeleteCommandMessageAsync(Context.Message, 2000);
            var convertTask = Task.Run(async () =>
            {
                try
                {
                    await Context.Channel.ReplyWithLegalizedSetAsync(content, gen);
                }
                catch (Exception ex)
                {
                    LogUtil.LogSafe(ex, nameof(LegalizerModule<T>));
                }
            });

            await Task.WhenAll(deleteMessageTask, convertTask).ConfigureAwait(false);
        }

        [Command("convert"), Alias("showdown")]
        [Summary("Converts a Showdown Set to RegenTemplate format.")]
        [Priority(0)]
        public async Task ConvertShowdown([Remainder][Summary("Showdown Set")] string content)
        {
            var deleteMessageTask = DeleteCommandMessageAsync(Context.Message, 2000);
            var convertTask = Task.Run(() => Context.Channel.ReplyWithLegalizedSetAsync<T>(content));
            await Task.WhenAll(deleteMessageTask, convertTask).ConfigureAwait(false);
        }

        [Command("legalize"), Alias("alm")]
        [Summary("Tries to legalize the attached PKM data and outputs it as RegenTemplate.")]
        public async Task LegalizeAsync()
        {
            var deleteMessageTask = DeleteCommandMessageAsync(Context.Message, 2000);
            var legalizationTasks = Context.Message.Attachments.Select(att =>
                Task.Run(() => Context.Channel.ReplyWithLegalizedSetAsync(att))
            ).ToArray();

            await Task.WhenAll(deleteMessageTask, Task.WhenAll(legalizationTasks)).ConfigureAwait(false);
        }

        private async Task DeleteCommandMessageAsync(IUserMessage message, int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }
    }
}
