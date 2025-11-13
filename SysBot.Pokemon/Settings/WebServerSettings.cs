using System.ComponentModel;

namespace SysBot.Pokemon;

/// <summary>
/// Settings for the Web Control Panel server
/// </summary>
public sealed class WebServerSettings
{
    private const string WebServer = nameof(WebServer);
    
    [Category(WebServer)]
    [Description("The port number for the Bot Control Panel web interface. Default is 8080.")]
    public int ControlPanelPort { get; set; } = 8080;
    
    [Category(WebServer)]
    [Description("Enable or disable the web control panel. When disabled, the web interface will not be accessible.")]
    public bool EnableWebServer { get; set; } = true;
    
    [Category(WebServer)]
    [Description("Allow external connections to the web control panel. When false, only localhost connections are allowed.")]
    public bool AllowExternalConnections { get; set; } = false;
}