using SysBot.Base;
using SysBot.Pokemon.WinForms.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

public partial class BotController : UserControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PokeBotState State { get; private set; } = new();
    private IPokeBotRunner? Runner;
    public EventHandler? Remove;
    public List<BotController> BotControls { get; } = new();
    private string _status = "DISCONNECTED";
    private Timer _glowTimer;
    private float _glowPhase = 60f;
    private bool _glowIncreasing = true;
    private Color _glowBaseColor = Color.Red;


    public BotController()
    {
        InitializeComponent();
        InitializeContextMenu();

        this.Margin = new Padding(0);
        this.Padding = new Padding(0);
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        this.UpdateStyles();
        _glowTimer = new Timer { Interval = 30 }; // Adjust speed as needed
        _glowTimer.Tick += (s, e) => AnimateStatusGlow();
        _glowTimer.Start();


        // Disable mouse highlight effects
        foreach (Control control in Controls)
        {
            control.MouseEnter += (_, _) => BackColor = BackColor;
            control.MouseLeave += (_, _) => BackColor = BackColor;
        }
    }

    private void InitializeContextMenu()
    {
        RCMenu.Items.Clear();

        // Primary control commands
        AddMenuItem("Start Bot", BotControlCommand.Start);
        AddMenuItem("Stop Bot", BotControlCommand.Stop);
        AddMenuItem("Idle Bot", BotControlCommand.Idle);
        AddMenuItem("Resume Bot", BotControlCommand.Resume);

        RCMenu.Items.Add(new ToolStripSeparator());

        // Maintenance / remote control
        AddMenuItem("Restart Bot", BotControlCommand.Restart);
        AddMenuItem("Reboot + Stop", BotControlCommand.RebootAndStop);

        RCMenu.Items.Add(new ToolStripSeparator());

        // Screen commands
        AddMenuItem("Turn Screen On", BotControlCommand.ScreenOn);
        AddMenuItem("Turn Screen Off", BotControlCommand.ScreenOff);

        RCMenu.Items.Add(new ToolStripSeparator());

        // Final command
        var remove = new ToolStripMenuItem("Remove Bot");
        remove.Click += (_, __) => TryRemove();
        RCMenu.Items.Add(remove);

        RCMenu.Opening += RcMenuOnOpening;
    }

    private void AddMenuItem(string label, BotControlCommand cmd)
    {
        var bot = GetBotSafely();
        var item = new ToolStripMenuItem(label)
        {
            Tag = cmd,
            Enabled = cmd.IsUsable(bot?.IsRunning == true, bot?.IsPaused == true)
        };
        item.Click += (_, __) => SendCommand(cmd);
        RCMenu.Items.Add(item);
    }


    public void Initialize(IPokeBotRunner runner, PokeBotState cfg)
    {
        Runner = runner;
        State = cfg;
        ReloadStatus();
    }

    public bool IsRunning()
    {
        return lblStatus.Text.Equals("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateLastLogTime(DateTime time)
    {
        // Example output: "LAST LOG: 6:30:00 PM"
        string formatted = time.ToString("h:mm:ss tt"); // 12-hour, no leading zero on hour, AM/PM
        lblLastLogTime.Text = $"{formatted}";
    }

    public void ReloadStatus(BotSource<PokeBotState>? botSource = null)
    {
        try { botSource ??= GetBot(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR GETTING BOT]: {ex}");
            return;
        }

        var bot = botSource?.Bot;
        if (bot == null) return;

        string status = bot.Connection == null ? "DISCONNECTED"
                      : botSource.IsPaused ? "PAUSED"
                      : botSource.IsRunning ? "RUNNING"
                      : "STOPPED";

        _status = status;
        UpdateStatusUI(status);

        lblConnectionName.Text = bot.Connection?.Label ?? "Unknown Connection";
        lblConnectionInfo.Text = $"↪ {bot.LastLogged}";
        SetBotMetaDisplay(State.InitialRoutine.ToString(), bot.LastTime);
    }
    private void SetBotMetaDisplay(string routine, DateTime lastTime)
    {
        rtbBotMeta.Clear();

        // Format top line
        string timeString = lastTime.ToString("h:mm.ss tt");
        string topLine = $"{routine} @ {timeString}";

        rtbBotMeta.SelectionFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        rtbBotMeta.SelectionColor = Color.White;
        rtbBotMeta.AppendText(topLine);
    }

    private void UpdateStatusUI(string status)
    {
        Color statusColor = status.ToUpperInvariant() switch
        {
            "RUNNING" => Color.LimeGreen,
            "PAUSED" => Color.Goldenrod,
            "STOPPED" => Color.OrangeRed,
            "DISCONNECTED" => Color.Red,
            _ => Color.DimGray
        };

        lblStatus.Text = status.ToUpperInvariant();
        lblStatus.ForeColor = statusColor;
        _glowBaseColor = statusColor;
    }

    private void AnimateStatusGlow()
    {
        float min = 60f;
        float max = 255f;
        float speed = 5f;

        _glowPhase += (_glowIncreasing ? speed : -speed);

        if (_glowPhase >= max)
        {
            _glowPhase = max;
            _glowIncreasing = false;
        }
        else if (_glowPhase <= min)
        {
            _glowPhase = min;
            _glowIncreasing = true;
        }

        // Fade between BACKGROUND COLOR and _glowBaseColor
        float t = (_glowPhase - min) / (max - min);

        Color background = Color.FromArgb(20, 19, 57);
        int r = (int)(background.R + (_glowBaseColor.R - background.R) * t);
        int g = (int)(background.G + (_glowBaseColor.G - background.G) * t);
        int b = (int)(background.B + (_glowBaseColor.B - background.B) * t);

        pnlStatus.BackColor = Color.FromArgb(r, g, b);
    }

    public void TryRemove()
    {
        GetBot().Stop();
        Remove?.Invoke(this, EventArgs.Empty);
    }

    public void SendCommand(BotControlCommand cmd)
    {
        if (Runner?.Config.SkipConsoleBotCreation != false)
        {
            LogUtil.LogError("No bots were created because SkipConsoleBotCreation is on!", "Hub");
            return;
        }

        var bot = GetBot();
        switch (cmd)
        {
            case BotControlCommand.Idle: bot.Pause(); break;
            case BotControlCommand.Start: Runner.InitializeStart(); bot.Start(); break;
            case BotControlCommand.Stop: bot.Stop(); break;
            case BotControlCommand.Resume: bot.Resume(); break;
            case BotControlCommand.Restart:
                if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Restart the connection?") != DialogResult.Yes)
                    return;
                Runner.InitializeStart(); bot.Restart(); break;
            case BotControlCommand.RebootAndStop: bot.RebootAndStop(); break;

            case BotControlCommand.ScreenOn:
                _ = Task.Run(() => BotControlCommandExtensions.SendScreenState(State.Connection.IP, true));
                break;
            case BotControlCommand.ScreenOff:
                _ = Task.Run(() => BotControlCommandExtensions.SendScreenState(State.Connection.IP, false));
                break;

            default:
                WinFormsUtil.Alert("Unsupported command.");
                break;
        }
    }

    private void BtnActions_Click(object? sender, EventArgs e)
    {
        if (RCMenu.Items.Count > 0)
            RCMenu.Show(btnActions, new Point(0, btnActions.Height));
    }

    private void RcMenuOnOpening(object? sender, CancelEventArgs e)
    {
        var bot = GetBotSafely();

        foreach (ToolStripItem item in RCMenu.Items)
        {
            if (item is ToolStripMenuItem mi && mi.Tag is BotControlCommand cmd)
            {
                mi.Enabled = cmd.IsUsable(bot?.IsRunning == true, bot?.IsPaused == true);
            }
        }
    }

    private BotSource<PokeBotState> GetBot()
    {
        if (Runner == null) throw new ArgumentNullException(nameof(Runner));
        var bot = Runner.GetBot(State) ?? throw new ArgumentNullException("bot");
        return bot;
    }

    public void ReadAllBotStates()
    {
        foreach (var bot in BotControls)
            bot.ReloadStatus();
    }

    private BotSource<PokeBotState>? GetBotSafely()
    {
        try
        {
            return Runner != null ? Runner.GetBot(State) : null;
        }
        catch
        {
            return null;
        }
    }

    public enum BotControlCommand
    {
        None,
        Start,
        Stop,
        Idle,
        Resume,
        Restart,
        RebootAndStop,
        ScreenOn,
        ScreenOff
    }

}
