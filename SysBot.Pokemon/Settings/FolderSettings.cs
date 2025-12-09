using System.ComponentModel;
using System.IO;

namespace SysBot.Pokemon;

public class FolderSettings : IDumper
{
    private const string FeatureToggle = nameof(FeatureToggle);

    private const string Files = nameof(Files);

    [Category(Files), Description("Source folder: where PKM files to distribute are selected from."), DisplayName("Distribute Folder")]
    public string DistributeFolder { get; set; } = string.Empty;

    [Category(FeatureToggle), Description("When enabled, dumps any received PKM files (trade results) to the DumpFolder."), DisplayName("Enable Dump")]
    public bool Dump { get; set; }

    [Category(Files), Description("Destination folder: where all received PKM files are dumped to."), DisplayName("Dump Folder")]
    public string DumpFolder { get; set; } = string.Empty;

    [Category(Files), Description("Directory where your Switch Remote For PC is located."), DisplayName("Switch Remote for PC Location")]
    public string SwitchRemoteForPC { get; set; } = string.Empty;

    [Category(Files), Description("Directory where your HOME Tracked Pokémon are located."), DisplayName("HOME-Ready Folder")]
    public string HOMEReadyPKMFolder { get; set; } = string.Empty;


    public void CreateDefaults(string path)
    {
        var dump = Path.Combine(path, "dump");
        Directory.CreateDirectory(dump);
        DumpFolder = dump;
        Dump = true;

        var distribute = Path.Combine(path, "distribute");
        Directory.CreateDirectory(distribute);
        DistributeFolder = distribute;
    }

    public override string ToString() => "Folder / Dumping Settings";
}
