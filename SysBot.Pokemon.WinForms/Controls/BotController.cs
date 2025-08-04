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
    private Panel _progressBarContainer;
    private Panel _progressFill;
    private Timer _progressAnimationTimer;
    private int _targetProgress = 0;
    private int _currentProgress = 0;
    private Color _glowColor = Color.Cyan;
    private readonly Color _startColor = Color.Cyan;
    private readonly Color _endColor = Color.FromArgb(255, 0, 255);
    private bool _holdAt100 = false;
    private Timer _holdTimer;


    public BotController()
    {
        InitializeComponent();
        InitializeContextMenu();

        this.Margin = new Padding(0);
        this.Padding = new Padding(0);
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        this.UpdateStyles();

        _glowTimer = new Timer { Interval = 30 };
        _glowTimer.Tick += (s, e) => AnimateStatusGlow();
        _glowTimer.Start();

        // Disable mouse highlight effects
        foreach (Control control in Controls)
        {
            control.MouseEnter += (_, _) => BackColor = BackColor;
            control.MouseLeave += (_, _) => BackColor = BackColor;
        }

        _progressBarContainer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 4,
            BackColor = Color.FromArgb(20, 19, 57),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _progressBarContainer.BorderStyle = BorderStyle.None;

        _progressFill = new Panel
        {
            Height = _progressBarContainer.Height,
            Width = 0,
            Location = new Point(0, 0),
            BackColor = _glowColor,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _progressFill.BorderStyle = BorderStyle.None;

        _progressBarContainer.Controls.Add(_progressFill);
        Controls.Add(_progressBarContainer);

        _progressAnimationTimer = new Timer { Interval = 15 };
        _progressAnimationTimer.Tick += (_, _) => AnimateProgress();
        _progressAnimationTimer.Start();
    }

    public void SetTradeProgress(int percent)
    {
        if (percent < 0 || percent > 100)
            return;

        _targetProgress = percent;
    }

    public void SetProgressValue(int percent)
    {
        Console.WriteLine($"SetProgressValue({percent})"); // Add this
        _targetProgress = Math.Clamp(percent, 0, 100);
    }

    public void ResetProgress()
    {
        _targetProgress = 0;
    }

    private void AnimateProgress()
    {
        if (_holdAt100)
            return; // Don't animate while in the 6-second hold

        if (_currentProgress == _targetProgress)
            return;

        int speed = 2;

        if (_currentProgress < _targetProgress)
            _currentProgress = Math.Min(_currentProgress + speed, _targetProgress);
        else
            _currentProgress = Math.Max(_currentProgress - speed, _targetProgress);

        int totalWidth = _progressBarContainer.Width;
        _progressFill.Width = (totalWidth * _currentProgress) / 100;

        // If we hit 100%, trigger the 6-second green hold
        if (_currentProgress == 100)
        {
            _holdAt100 = true;
            _progressFill.BackColor = Color.Lime; // or Color.FromArgb(0, 255, 0) for neon green

            _holdTimer = new Timer { Interval = 6000 }; // 6 seconds
            _holdTimer.Tick += (s, e) =>
            {
                _holdTimer.Stop();
                _holdAt100 = false;
                _targetProgress = 0; // Reset to 0 and restart animation
            };
            _holdTimer.Start();
            return;
        }

        // Otherwise: gradient & glow during normal progress
        float percentProgress = _currentProgress / 100f;
        Color interpolated = InterpolateColor(_startColor, _endColor, percentProgress);

        int brightnessPulse = (int)(10 + (Math.Sin(DateTime.Now.Millisecond / 200.0) * 20));
        _progressFill.BackColor = ControlPaint.Light(interpolated, brightnessPulse / 100f);
    }

    private Color InterpolateColor(Color start, Color end, float progress)
    {
        int r = (int)(start.R + (end.R - start.R) * progress);
        int g = (int)(start.G + (end.G - start.G) * progress);
        int b = (int)(start.B + (end.B - start.B) * progress);
        return Color.FromArgb(r, g, b);
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
