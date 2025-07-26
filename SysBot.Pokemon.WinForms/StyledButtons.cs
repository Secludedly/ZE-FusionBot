using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public class FancyButton : Button
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StartColor { get; set; } = Color.FromArgb(56, 56, 131);  // light blue with pink undertones
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EndColor { get; set; } = Color.FromArgb(165, 137, 182);    // pinkish tone
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Color.FromArgb(180, 210, 250);  // lighter blue on hover
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverStartColor { get; set; } = Color.FromArgb(20, 30, 90);
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverEndColor { get; set; } = Color.FromArgb(160, 90, 200);
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ClickColor { get; set; } = Color.FromArgb(60, 30, 90);     // dark purple on click

    private bool isHovered = false;
    private bool isClicked = false;

    // Shake state variables
    private Timer shakeTimer;
    private int shakeCounter = 0;
    private Point originalLocation;

    // Animation fields for hover gradient
    private Timer animationTimer;
    private int animationOffset = 0;
    private bool animationForward = true; // Direction flag for ping-pong
    private const int animationSpeed = 2; // pixels per tick
    private const int animationRange = 100; // width of animated gradient sweep

    public FancyButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Verdana", 9, FontStyle.Regular);

        DoubleBuffered = true;

        // Check for LogsForm container and override colors
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
                // fallback default text color
                ForeColor = Color.White;
            }
        };

        // Shake timer setup
        shakeTimer = new Timer();
        shakeTimer.Interval = 120;
        shakeTimer.Tick += ShakeTimer_Tick;

        // Animation timer setup
        animationTimer = new Timer();
        animationTimer.Interval = 30; // ~33 FPS
        animationTimer.Tick += AnimationTimer_Tick;

        MouseEnter += FancyButton_MouseEnter;
        MouseLeave += FancyButton_MouseLeave;
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isClicked = true; Invalidate(); } };
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) { isClicked = false; Invalidate(); } };
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (animationForward)
        {
            animationOffset += animationSpeed;
            if (animationOffset >= animationRange)
            {
                animationOffset = animationRange;
                animationForward = false;
            }
        }
        else
        {
            animationOffset -= animationSpeed;
            if (animationOffset <= 0)
            {
                animationOffset = 0;
                animationForward = true;
            }
        }
        Invalidate();
    }

    private void FancyButton_MouseEnter(object sender, EventArgs e)
    {
        isHovered = true;
        animationOffset = 0;
        animationForward = true;
        animationTimer.Start();

        originalLocation = Location;
        shakeCounter = 0;
        shakeTimer.Start();

        Invalidate();
    }

    private void FancyButton_MouseLeave(object sender, EventArgs e)
    {
        isHovered = false;
        isClicked = false;
        animationTimer.Stop();
        animationOffset = 0;
        animationForward = true;

        shakeTimer.Stop();
        Location = originalLocation;

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
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;

        g.Clear(Parent?.BackColor ?? SystemColors.Control);

        int borderThickness = 2;

        // Fill rectangle inset to avoid overlap with borders
        Rectangle fillRect = new Rectangle(
            borderThickness,
            borderThickness,
            Width - (2 * borderThickness),
            Height - (2 * borderThickness)
        );

        if (isHovered && !isClicked)
        {
            // Animated gradient fill on hover: midnight blue to purple moving horizontally ping-pong style
            Rectangle animatedRect = new Rectangle(
                fillRect.X - animationOffset,
                fillRect.Y,
                fillRect.Width + animationRange,
                fillRect.Height
            );

            using (var brush = new LinearGradientBrush(
                animatedRect,
                HoverStartColor,
                HoverEndColor,
                LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, fillRect);
            }
        }
        else
        {
            // Static gradient fill
            Color start = isClicked ? ClickColor : StartColor;
            Color end = isClicked ? ClickColor : EndColor;

            using (var brush = new LinearGradientBrush(fillRect, start, end, 45f))
            {
                g.FillRectangle(brush, fillRect);
            }
        }

        // Border colors
        Color topLeft = Color.FromArgb(60, 80, 150);          // Soft indigo-blue shadow
        Color bottomRight = Color.FromArgb(190, 200, 255);    // Light icy purple

        // Draw borders with thickness 2, avoiding corner overlap
        using (var pen = new Pen(topLeft, borderThickness))
        {
            g.DrawLine(pen, 0, 0, Width - 1, 0);           // Top border
            g.DrawLine(pen, 0, 0, 0, Height - 1);          // Left border
        }

        using (var pen = new Pen(bottomRight, borderThickness))
        {
            g.DrawLine(pen, 0, Height - borderThickness, Width - borderThickness, Height - borderThickness);  // Bottom border
            g.DrawLine(pen, Width - borderThickness, 0, Width - borderThickness, Height - borderThickness);   // Right border
        }

        // Draw text centered
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
