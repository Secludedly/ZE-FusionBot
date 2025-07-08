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
    public Color ClickColor { get; set; } = Color.FromArgb(60, 30, 90);     // dark purple on click

    private bool isHovered = false;
    private bool isClicked = false;

    // Shake state variables
    private Timer shakeTimer;
    private int shakeCounter = 0;
    private Point originalLocation;

    public FancyButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9, FontStyle.Regular);

        DoubleBuffered = true;

        MouseEnter += FancyButton_MouseEnter;
        MouseLeave += FancyButton_MouseLeave;
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isClicked = true; Invalidate(); } };
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) { isClicked = false; Invalidate(); } };

        // Setup shake timer
        shakeTimer = new Timer();
        shakeTimer.Interval = 120; // milliseconds, controls shake speed
        shakeTimer.Tick += ShakeTimer_Tick;
    }

    private void FancyButton_MouseEnter(object sender, EventArgs e)
    {
        isHovered = true;
        Invalidate();

        originalLocation = Location;
        shakeCounter = 0;
        shakeTimer.Start();
    }

    private void FancyButton_MouseLeave(object sender, EventArgs e)
    {
        isHovered = false;
        isClicked = false;
        Invalidate();

        shakeTimer.Stop();
        Location = originalLocation; // reset position when hover ends
    }

    private void ShakeTimer_Tick(object? sender, EventArgs e)
    {
        const int shakeAmount = 2; // how many pixels it shakes side to side
        int offsetX = (shakeCounter % 2 == 0) ? shakeAmount : -shakeAmount;

        Location = new Point(originalLocation.X + offsetX, originalLocation.Y);
        shakeCounter++;

        // shakes forever while hovered
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int borderThickness = 4;
        int borderRadius = 15;

        // Draw thick white border first
        using (var whitePen = new Pen(Color.White, borderThickness))
        {
            var borderRect = new Rectangle(borderThickness / 2, borderThickness / 2, Width - borderThickness, Height - borderThickness);
            g.DrawRectangle(whitePen, borderRect);
        }

        // Calculate inner rectangle for button fill (inside the border)
        var fillRect = new Rectangle(borderThickness, borderThickness, Width - 2 * borderThickness, Height - 2 * borderThickness);

        // Choose gradient colors based on state
        Color start = StartColor;
        Color end = EndColor;

        if (isClicked)
        {
            start = ClickColor;
            end = ClickColor;
        }
        else if (isHovered)
        {
            start = HoverColor;
            end = HoverColor;
        }

        using (var brush = new LinearGradientBrush(fillRect, start, end, 45f))
        {
            g.FillRectangle(brush, fillRect);
        }

        // Draw text centered
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
