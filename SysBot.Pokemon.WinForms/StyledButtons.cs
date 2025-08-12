using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public class FancyButton : Button
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StartColor { get; set; } = Color.FromArgb(56, 56, 131);  // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EndColor { get; set; } = Color.FromArgb(165, 137, 182);    // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Color.FromArgb(180, 210, 250);  // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverStartColor { get; set; } = Color.FromArgb(20, 30, 90); // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverEndColor { get; set; } = Color.FromArgb(160, 90, 200);  // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ClickColor { get; set; } = Color.FromArgb(60, 30, 90);     // not used but kept

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int GlowOpacity { get; set; } = 120; // 0-255 max opacity

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlowColor { get; set; } = Color.Cyan;

    private bool isHovered = false;
    private bool isClicked = false;

    // Shake state
    private Timer shakeTimer;
    private int shakeCounter = 0;
    private Point originalLocation;

    // Animation timer drives glow pulse (runs always) and animation offset (not used here)
    private Timer animationTimer;
    private int animationOffset = 0;
    private bool animationForward = true;

    private const int animationSpeed = 2;  // pixels per tick
    private const int animationRange = 100;

    // Glow alpha pulsing 0–180
    private int glowAlpha = 0;
    private bool glowIncreasing = true;

    public FancyButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Enter The Grid", 10F, FontStyle.Regular);

        DoubleBuffered = true;

        // Keep glow animation running all the time, start timer in constructor
        animationTimer = new Timer();
        animationTimer.Interval = 74;
        animationTimer.Tick += AnimationTimer_Tick;
        animationTimer.Start();

        shakeTimer = new Timer();
        shakeTimer.Interval = 90;
        shakeTimer.Tick += ShakeTimer_Tick;

        MouseEnter += FancyButton_MouseEnter;
        MouseLeave += FancyButton_MouseLeave;
        MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                isClicked = true;
                Invalidate();
            }
        };
        MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                isClicked = false;
                Invalidate();
            }
        };

        this.HandleCreated += (s, e) =>
        {
            if (FindForm() is SysBot.Pokemon.WinForms.LogsForm)
            {
                ForeColor = Color.FromArgb(255, 255, 255);
                StartColor = Color.FromArgb(10, 10, 40);
                EndColor = Color.FromArgb(50, 50, 90);
                HoverColor = Color.FromArgb(51, 255, 255);
                ClickColor = Color.FromArgb(10, 10, 40);
                HoverStartColor = Color.FromArgb(31, 225, 225);
                HoverEndColor = Color.FromArgb(50, 50, 90);
            }
            else
            {
                ForeColor = Color.White;
            }
        };
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        // Animate glow alpha pulsing 0-180 constantly (no gradient anim here)
        const int glowStep = 10;
        if (glowIncreasing)
        {
            glowAlpha += glowStep;
            if (glowAlpha >= 180)
            {
                glowAlpha = 180;
                glowIncreasing = false;
            }
        }
        else
        {
            glowAlpha -= glowStep;
            if (glowAlpha <= 0)
            {
                glowAlpha = 0;
                glowIncreasing = true;
            }
        }

        Invalidate();
    }

    private void FancyButton_MouseEnter(object sender, EventArgs e)
    {
        isHovered = true;

        originalLocation = Location;
        shakeCounter = 0;
        shakeTimer.Start();

        Invalidate();
    }

    private void FancyButton_MouseLeave(object sender, EventArgs e)
    {
        isHovered = false;
        isClicked = false;

        // Do NOT stop animationTimer here so glow continues forever

        shakeTimer.Stop();
        Location = originalLocation;

        // If you want glow to keep pulsing, comment out next line:
        // glowAlpha = 0;

        Invalidate();
    }

    private void ShakeTimer_Tick(object? sender, EventArgs e)
    {
        const int shakeAmount = 2;
        int offsetX = (shakeCounter % 2 == 0) ? shakeAmount : -shakeAmount;

        Location = new Point(originalLocation.X + offsetX, originalLocation.Y);
        shakeCounter++;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        g.Clear(Parent?.BackColor ?? SystemColors.Control);

        int borderThickness = 2;
        int glowThickness = 6;

        // 1) Draw the animated glow border *outside* the normal border (always, because animationTimer runs all the time)
        if (glowAlpha > 0)
        {
            for (int i = 0; i < glowThickness; i++)
            {
                int alpha = (int)((glowAlpha * (GlowOpacity / 255f)) * (1.0 - (float)i / glowThickness));
                using var glowPen = new Pen(Color.FromArgb(alpha, GlowColor), 1);
                var glowRect = new Rectangle(
                    i,
                    i,
                    Width - 1 - 2 * i,
                    Height - 1 - 2 * i);
                g.DrawRectangle(glowPen, glowRect);
            }
        }

        // 2) Draw solid near-black/dark gray fill inset by borderThickness
        Rectangle fillRect = new Rectangle(
            borderThickness,
            borderThickness,
            Width - (2 * borderThickness),
            Height - (2 * borderThickness)
        );

        using var brush = new SolidBrush(Color.FromArgb(20, 19, 57));
        g.FillRectangle(brush, fillRect);

        // 3) Draw the button text centered
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }
}
