using Discord.Commands;
using PKHeX.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class PKHeXModule<T> : SudoModule<T> where T : PKM, new()
{
    [Command("pkhex")]
    [Alias("pkh")]
    [Summary("Launch PKHeX on the bot host PC.")]
    [RequireOwner]
    public async Task LaunchPKHeXAsync()
    {
        try
        {
            var pkHeXDirectory = SysCord<T>.Runner.Config.Folder.PKHeXDirectory;

            if (string.IsNullOrWhiteSpace(pkHeXDirectory) || !Directory.Exists(pkHeXDirectory))
            {
                await ReplyAsync("PKHeX directory is not set or does not exist. Please configure it in the Hub settings.").ConfigureAwait(false);
                return;
            }

            var exePath = Directory
                .GetFiles(pkHeXDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).Contains("pkhex", StringComparison.OrdinalIgnoreCase));

            if (exePath == null)
            {
                await ReplyAsync("No PKHeX executable found in the configured folder.").ConfigureAwait(false);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = pkHeXDirectory,
                UseShellExecute = true
            });

            await ReplyAsync("🧬 PKHeX launched successfully.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReplyAsync($"Failed to launch PKHeX: {ex.Message}").ConfigureAwait(false);
        }
    }
}
