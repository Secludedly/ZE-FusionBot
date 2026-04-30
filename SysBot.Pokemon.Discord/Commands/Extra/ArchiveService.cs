using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System.IO;
using System.Linq;
using System;

public static class ArchiveService
{
    public static void ExtractToDirectory(string inputPath, string outputPath)
    {
        Directory.CreateDirectory(outputPath);

        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        IArchive archive = ext switch
        {
            ".zip" => ZipArchive.Open(inputPath),
            ".rar" => RarArchive.Open(inputPath),
            ".7z" => SevenZipArchive.Open(inputPath),
            _ => throw new InvalidOperationException("Unsupported archive.")
        };

        foreach (var entry in archive.Entries.Where(x => !x.IsDirectory))
        {
            var outPath = Path.Combine(outputPath, entry.Key);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            entry.WriteToFile(outPath, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });
        }
    }
}
