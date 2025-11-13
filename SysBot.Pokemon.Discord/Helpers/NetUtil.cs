using Discord;
using PKHeX.Core;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class NetUtil
{
    public static async Task<byte[]> DownloadFromUrlAsync(string url)
    {
        using var client = new HttpClient();
        return await client.GetByteArrayAsync(url).ConfigureAwait(false);
    }

    // Existing Discord attachment method
    public static async Task<Download<PKM>> DownloadPKMAsync(IAttachment att, SimpleTrainerInfo? defTrainer = null)
    {
        var result = new Download<PKM> { SanitizedFileName = Format.Sanitize(att.Filename) };
        var extension = System.IO.Path.GetExtension(result.SanitizedFileName);
        var isMyg = MysteryGift.IsMysteryGift(att.Size) && extension != ".pb7";
        if (!EntityDetection.IsSizePlausible(att.Size) && !isMyg)
        {
            result.ErrorMessage = $"{result.SanitizedFileName}: Invalid size.";
            return result;
        }
        string url = att.Url;
        // Download the resource and load the bytes into a buffer.
        var buffer = await DownloadFromUrlAsync(url).ConfigureAwait(false);
        PKM? pkm = null;
        try
        {
            if (isMyg)
            {
                pkm = MysteryGift.GetMysteryGift(buffer, extension)?.ConvertToPKM(defTrainer ?? new SimpleTrainerInfo());
            }
            else
            {
                pkm = EntityFormat.GetFromBytes(buffer, EntityFileExtension.GetContextFromExtension(result.SanitizedFileName, EntityContext.None));
            }
        }
        catch (ArgumentException)
        {
            //Item wondercard
        }
        if (pkm is null)
        {
            result.ErrorMessage = $"{result.SanitizedFileName}: Invalid pkm attachment.";
            return result;
        }
        result.Data = pkm;
        result.Success = true;
        return result;
    }

    // New overload for URL-based downloads (for web trades)
    public static async Task<Download<PKM>> DownloadPKMAsync(string url, SimpleTrainerInfo? defTrainer = null)
    {
        // Extract filename from URL
        var uri = new Uri(url);
        var filename = System.IO.Path.GetFileName(uri.LocalPath);
        var result = new Download<PKM> { SanitizedFileName = Format.Sanitize(filename) };

        try
        {
            // Download the resource
            var buffer = await DownloadFromUrlAsync(url).ConfigureAwait(false);

            var extension = System.IO.Path.GetExtension(result.SanitizedFileName);
            var isMyg = MysteryGift.IsMysteryGift(buffer.Length) && extension != ".pb7";

            if (!EntityDetection.IsSizePlausible(buffer.Length) && !isMyg)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid size.";
                return result;
            }

            PKM? pkm = null;
            try
            {
                if (isMyg)
                {
                    pkm = MysteryGift.GetMysteryGift(buffer, extension)?.ConvertToPKM(defTrainer ?? new SimpleTrainerInfo());
                }
                else
                {
                    pkm = EntityFormat.GetFromBytes(buffer, EntityFileExtension.GetContextFromExtension(result.SanitizedFileName, EntityContext.None));
                }
            }
            catch (ArgumentException)
            {
                //Item wondercard
            }

            if (pkm is null)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid pkm file.";
                return result;
            }

            result.Data = pkm;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to download {result.SanitizedFileName}: {ex.Message}";
        }

        return result;
    }
}

public sealed class Download<T> where T : class
{
    public T? Data;
    public string? ErrorMessage;
    public string? SanitizedFileName;
    public bool Success;
}
