#nullable enable
using System.Windows.Forms;
using System;
using System.Drawing;
using SysBot.Pokemon.WinForms.Controls;


namespace SysBot.Pokemon.WinForms;


partial class BotController
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _glowTimer?.Stop();
            _glowTimer?.Dispose();
            _progressAnimationTimer?.Stop();
            _progressAnimationTimer?.Dispose();
            _sparkleTimer?.Stop();
            _sparkleTimer?.Dispose();
            _holdTimer?.Stop();
            _holdTimer?.Dispose();
            _statusGlowTimer?.Stop();
            _statusGlowTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlStatus = new Panel();
        lblStatus = new Label();
        lblConnectionName = new Label();
        lblConnectionInfo = new Label();
        btnActions = new Button();
        RCMenu = new ContextMenuStrip(components);
        SuspendLayout();
        // 
        // pnlStatus
        // 
        pnlStatus.BackColor = Color.Red;
        pnlStatus.Location = new Point(8, 10);
        pnlStatus.Name = "pnlStatus";
        pnlStatus.Size = new Size(15, 15);
        pnlStatus.TabIndex = 0;
        _statusGlowTimer = new Timer
        {
            Interval = 30 // Lower is smoother
        };
        _statusGlowTimer.Tick += (_, _) => AnimateStatusGlow();
        _statusGlowTimer.Start();
        // 
        // lblStatus
        // 
        lblStatus.AutoSize = true;
        lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblStatus.ForeColor = Color.White;
        lblStatus.Location = new Point(29, 7);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(122, 20);
        lblStatus.TabIndex = 2;
        lblStatus.Text = "DISCONNECTED";
        // 
        // lblConnectionName
        // 
        lblConnectionName.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblConnectionName.ForeColor = Color.White;
        lblConnectionName.Location = new Point(28, 28);
        lblConnectionName.Name = "lblConnectionName";
        lblConnectionName.Size = new Size(520, 20);
        lblConnectionName.TabIndex = 3;
        lblConnectionName.Text = "???";
        // 
        // lblConnectionInfo
        // 
        lblConnectionInfo.Font = new Font("Segoe UI", 8.5F);
        lblConnectionInfo.ForeColor = Color.White;
        lblConnectionInfo.Location = new Point(28, 76);
        lblConnectionInfo.Name = "lblConnectionInfo";
        lblConnectionInfo.Size = new Size(661, 30);
        lblConnectionInfo.TabIndex = 4;
        BringToFront();
        //
        // lblRoutine
        //
        lblRoutine = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.White,
            Location = new Point(10, 60), // Adjust based on your layout
            Name = "lblRoutine",
            Size = new Size(150, 20),
        };
        Controls.Add(lblRoutine);
        //
        // rtbBotMeta
        //
        rtbBotMeta = new RichTextBox
        {
            Name = "rtbBotMeta",
            Location = new Point(32, 50), // adjust based on your layout
            Size = new Size(240, 30),
            BackColor = Color.FromArgb(20, 19, 57),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.None,
            TabStop = false,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.AntiqueWhite
        };
        Controls.Add(rtbBotMeta);
        // 
        // btnActions
        // 
        btnActions.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnActions.BackColor = Color.FromArgb(31, 30, 68);
        btnActions.FlatAppearance.BorderColor = Color.FromArgb(51, 50, 98);
        btnActions.FlatAppearance.MouseDownBackColor = Color.FromArgb(51, 50, 98);
        btnActions.FlatAppearance.MouseOverBackColor = Color.FromArgb(41, 40, 88);
        btnActions.FlatStyle = FlatStyle.Flat;
        btnActions.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        btnActions.ForeColor = Color.White;
        btnActions.Location = new Point(576, 6);
        btnActions.Name = "btnActions";
        btnActions.Size = new Size(116, 30);
        btnActions.TabIndex = 4;
        btnActions.Text = "▶ BOT MENU";
        btnActions.UseVisualStyleBackColor = false;
        btnActions.Click += BtnActions_Click;
        // 
        // RCMenu
        // 
        RCMenu.ImageScalingSize = new Size(20, 20);
        RCMenu.Name = "RCMenu";
        RCMenu.Size = new Size(61, 4);
        // 
        // BotController
        // 
        BackColor = Color.FromArgb(20, 19, 57);
        Controls.Add(pnlStatus);
        Controls.Add(lblStatus);
        Controls.Add(lblConnectionName);
        Controls.Add(lblConnectionInfo);
        Controls.Add(btnActions);
        Margin = new Padding(0);
        Name = "BotController";
        Size = new Size(700, 110);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Panel pnlStatus = null!;
    private Label lblStatus = null!;
    private Label lblConnectionInfo = null!;
#pragma warning disable CS0649 // Field is never assigned
    private Label? lblLastLogTime;
#pragma warning restore CS0649
    private Label lblConnectionName = new Label();
    private Label lblRoutine = null!;
    private RichTextBox rtbBotMeta = null!;
    private Button btnActions = null!;
    private ContextMenuStrip RCMenu = null!;
    private Timer _statusGlowTimer = null!;

}

