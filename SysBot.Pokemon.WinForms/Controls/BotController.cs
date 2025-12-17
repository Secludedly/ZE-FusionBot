using SysBot.Base;
using SysBot.Pokemon.WinForms.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private Timer _shimmerTimer;
    private Timer _sparkleTimer;
    private int _targetProgress = 0;
    private int _currentProgress = 0;
    private Color _glowColor = Color.Cyan;
    private int _shimmerX;
    private int _sparkleX = -50;
    private int _sparkleWidth = 50;
    private Color _startColor = Color.FromArgb(0, 122, 204);
    private Color _endColor = Color.FromArgb(153, 50, 204);
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

        // Sparkle animation timer
        _sparkleTimer = new Timer { Interval = 20 }; // ~50 FPS
        _sparkleTimer.Tick += (s, e) =>
        {
            _sparkleX += 8; // move sparkle speed
            if (_sparkleX > _progressFill.Width + _sparkleWidth)
                _sparkleX = -_sparkleWidth;
            _progressFill.Invalidate();
        };
        _sparkleTimer.Start();
    }

    private void _progressFill_Paint(object? sender, PaintEventArgs e)
    {
        if (_progressFill.Width <= 0)
            return;

        Rectangle rect = new Rectangle(_sparkleX, 0, _sparkleWidth, _progressFill.Height);

        using var shimmerBrush = new LinearGradientBrush(
            rect,
            Color.FromArgb(180, Color.White),
            Color.FromArgb(0, Color.White),
            LinearGradientMode.Horizontal
        );

        int filledWidth = _progressFill.Width;
        Rectangle clipRect = new Rectangle(0, 0, filledWidth, _progressFill.Height);

        var oldClip = e.Graphics.Clip;
        e.Graphics.SetClip(clipRect);
        e.Graphics.FillRectangle(shimmerBrush, rect);
        e.Graphics.SetClip(oldClip, System.Drawing.Drawing2D.CombineMode.Replace);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _progressFill.Paint += _progressFill_Paint;
    }

    public void SetTradeProgress(int percent)
    {
        if (percent < 0 || percent > 100)
            return;

        _targetProgress = percent;
    }

    public void SetProgressValue(int percent)
    {
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
            _progressFill.BackColor = Color.FromArgb(249, 88, 155); // 100% progress color

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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw background bar
        using (SolidBrush backBrush = new SolidBrush(Color.FromArgb(20, 19, 57)))
        {
            e.Graphics.FillRectangle(backBrush, 0, Height - 4, Width, 4);
        }

        // Progress fill
        int fillWidth = (Width * _currentProgress) / 100;
        if (fillWidth > 0)
        {
            using (SolidBrush fillBrush = new SolidBrush(InterpolateColor(_startColor, _endColor, _currentProgress / 100f)))
            {
                e.Graphics.FillRectangle(fillBrush, 0, Height - 4, fillWidth, 4);
            }

            // Shimmer effect
            int glowWidth = 70; // or whatever length you want
            using (LinearGradientBrush shimmerBrush = new LinearGradientBrush(
                new Rectangle(_shimmerX, Height - 4, glowWidth, 4),
                Color.FromArgb(180, Color.White),
                Color.FromArgb(0, Color.White),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(shimmerBrush, _shimmerX, Height - 4, glowWidth, 4);
            }
        }
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

        // Your color map for the menu text
        var colorMap = new Dictionary<string, Color>
    {
        { "▶️ Start Bot", Color.LimeGreen },
        { "⏹️ Stop Bot", Color.IndianRed },
        { "⏸️ Idle Bot", Color.White },
        { "🔼 Resume Bot", Color.White },
        { "🔁 Restart Bot", Color.White },
        { "🔄 Reboot + Stop", Color.White },
        { "💡 Turn Screen On", Color.White },
        { "🌑 Turn Screen Off", Color.White },
        { "⛔ Remove Bot", Color.IndianRed }
    };

        AddMenuItem("▶️ Start Bot", BotControlCommand.Start);
        AddMenuItem("⏹️ Stop Bot", BotControlCommand.Stop);
        AddMenuItem("⏸️ Idle Bot", BotControlCommand.Idle);
        AddMenuItem("🔼 Resume Bot", BotControlCommand.Resume);

        RCMenu.Items.Add(new ToolStripSeparator());

        AddMenuItem("🔁 Restart Bot", BotControlCommand.Restart);
        AddMenuItem("🔄 Reboot + Stop", BotControlCommand.RebootAndStop);

        RCMenu.Items.Add(new ToolStripSeparator());

        AddMenuItem("💡 Turn Screen On", BotControlCommand.ScreenOn);
        AddMenuItem("🌑 Turn Screen Off", BotControlCommand.ScreenOff);

        RCMenu.Items.Add(new ToolStripSeparator());

        var remove = new ToolStripMenuItem("⛔ Remove Bot");
        remove.Click += (_, __) => TryRemove();
        RCMenu.Items.Add(remove);

        RCMenu.Opening += RcMenuOnOpening;

        // Set the custom renderer here
        RCMenu.Renderer = new ColoredMenuRenderer(colorMap);
    }

    private void ColoredMenuItem_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (sender is not ToolStripMenuItem item)
            return;

        Color textColor = item.Tag is Color c ? c : SystemColors.MenuText;

        // Draw background (normal or selected)
        if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
        {
            e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);
            textColor = SystemColors.HighlightText; // invert text color on hover
        }
        else
        {
            e.Graphics.FillRectangle(SystemBrushes.Menu, e.Bounds);
        }

        // Draw text left aligned vertically centered
        TextRenderer.DrawText(
            e.Graphics,
            item.Text,
            e.Font,
            e.Bounds,
            textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        // Draw focus rectangle if needed
        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
        {
            e.DrawFocusRectangle();
        }
    }

    private void ColoredMenuItem_MeasureItem(object sender, MeasureItemEventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            Size textSize = TextRenderer.MeasureText(item.Text, item.Font);
            e.ItemWidth = textSize.Width;
            e.ItemHeight = textSize.Height;
        }
    }

    private class ColoredMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Dictionary<string, Color> _colorMap;
        private readonly Color _backgroundColor = Color.FromArgb(20, 19, 57);
        private readonly int _leftPadding = 22; // padding from left edge

        public ColoredMenuRenderer(Dictionary<string, Color> colorMap)
        {
            _colorMap = colorMap;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_backgroundColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Color bg = e.Item.Selected ? Color.Cyan : _backgroundColor;
            using var brush = new SolidBrush(bg);
            e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (_colorMap.TryGetValue(e.Item.Text, out Color color))
                e.TextColor = e.Item.Selected ? SystemColors.HighlightText : color;
            else
                e.TextColor = e.Item.Selected ? SystemColors.HighlightText : SystemColors.ControlText;

            // Adjust text rectangle to remove checkmark/image margin
            e.TextRectangle = new Rectangle(
                e.Item.ContentRectangle.Left + _leftPadding,
                e.Item.ContentRectangle.Top,
                e.Item.ContentRectangle.Width - _leftPadding,
                e.Item.ContentRectangle.Height
            );

            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Skip drawing the checkmark box entirely
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Skip the image margin area entirely
        }
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
        try
        {
            botSource ??= GetBotSafely();
            if (botSource == null) // Ensure botSource is not null
                return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR GETTING BOT]: {ex}");
            return;
        }

        var bot = botSource.Bot;
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

        // Use system font with fallback
        try
        {
            rtbBotMeta.SelectionFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        }
        catch
        {
            rtbBotMeta.SelectionFont = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Bold);
        }
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

    public BotSource<PokeBotState> GetBot()
    {
        if (Runner == null) throw new ArgumentNullException(nameof(Runner));
        var bot = Runner.GetBot(State) ?? throw new ArgumentNullException("bot");
        return bot;
    }

    private void ShowRecoveryStatus(object? sender, EventArgs e)
    {
        var bot = GetBot();
        if (bot is null)
        {
            MessageBox.Show("Bot not found.", "Recovery Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var recoveryState = bot.GetRecoveryState();
        if (recoveryState is null)
        {
            MessageBox.Show("Recovery service is not enabled for this bot.", "Recovery Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var status = $"Bot: {bot.Bot.Connection.Name}\n" +
                    $"Status: {(bot.IsRunning ? "Running" : "Stopped")}\n" +
                    $"Recovery Attempts: {recoveryState.ConsecutiveFailures}\n" +
                    $"Total Crashes: {recoveryState.CrashHistory.Count}\n" +
                    $"Is Recovering: {(recoveryState.IsRecovering ? "Yes" : "No")}\n";

        if (recoveryState.LastRecoveryAttempt is not null)
        {
            status += $"Last Recovery: {recoveryState.LastRecoveryAttempt.Value:yyyy-MM-dd HH:mm:ss}\n";
        }

        if (recoveryState.CrashHistory.Count > 0)
        {
            var lastCrash = recoveryState.CrashHistory.OrderByDescending(c => c).FirstOrDefault();
            if (lastCrash != default)
            {
                status += $"Last Crash: {lastCrash:yyyy-MM-dd HH:mm:ss}\n";
            }
        }

        MessageBox.Show(status, "Recovery Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public string ReadBotState()
    {
        try
        {
            var botSource = GetBot();
            if (botSource is null)
                return "ERROR";

            var bot = botSource.Bot;
            if (bot is null)
                return "ERROR";

            if (!botSource.IsRunning)
                return "STOPPED";

            if (botSource.IsStopping)
                return "STOPPING";

            if (botSource.IsPaused)
            {
                if (bot.Config?.CurrentRoutineType != PokeRoutineType.Idle)
                    return "IDLING";
                else
                    return "IDLE";
            }

            if (botSource.IsRunning && !bot.Connection.Connected)
                return "REBOOTING";

            var cfg = bot.Config;
            if (cfg == null)
                return "UNKNOWN";

            if (cfg.CurrentRoutineType == PokeRoutineType.Idle)
                return "IDLE";

            if (botSource.IsRunning && bot.Connection.Connected)
                return cfg.CurrentRoutineType.ToString();

            return "UNKNOWN";
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error reading bot state: {ex.Message}", "BotController");
            return "ERROR";
        }
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
