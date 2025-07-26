using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms.Controls
{
    public class FancyProgressBar : Control
    {
        private int _value = 0;
        private Color _progressColor = Color.FromArgb(180, 150, 0, 255);
        private readonly Timer _animationTimer;

        private int _shimmerX = 0;
        private int _shimmerDirection = 1; // 1 = right, -1 = left
        private const int ShimmerSpeed = 6;
        private const int ShimmerWidth = 70;

        public FancyProgressBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Size = new Size(200, 20);

            _animationTimer = new Timer { Interval = 30 };
            _animationTimer.Tick += (s, e) =>
            {
                AnimateShimmer();
                Invalidate();
            };
            _animationTimer.Start();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, 0, 100);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                Invalidate();
            }
        }

        private void AnimateShimmer()
        {
            int fillWidth = (int)(Width * (_value / 100.0));
            if (fillWidth <= 0) return;

            _shimmerX += _shimmerDirection * ShimmerSpeed;

            // Bounce shimmer within the filled area boundaries
            if (_shimmerX < 0)
                _shimmerDirection = 1;
            else if (_shimmerX + ShimmerWidth > fillWidth)
                _shimmerDirection = -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int fillWidth = (int)(Width * (_value / 100.0));
            if (fillWidth <= 0)
                return;

            int cornerRadius = Height / 2; // Fully rounded height

            // 1) Draw glow layers behind the filled bar
            int glowThickness = 25;
            for (int i = glowThickness; i > 0; i--)
            {
                int alpha = 30 - (i * 2); // fading alpha
                if (alpha < 0) alpha = 0;

                using (var glowBrush = new SolidBrush(Color.FromArgb(alpha, _progressColor)))
                {
                    var glowRect = new Rectangle(
                        0 - i,
                        0 - i,
                        fillWidth + i * 2,
                        Height + i * 2);
                    using var path = CreateRoundedRectanglePath(glowRect, cornerRadius + i);
                    g.FillPath(glowBrush, path);
                }
            }

            // 2) Draw filled progress bar with rounded corners
            var fillRect = new Rectangle(0, 0, fillWidth, Height);
            using var fillBrush = new SolidBrush(_progressColor);
            using var fillPath = CreateRoundedRectanglePath(fillRect, cornerRadius);
            g.FillPath(fillBrush, fillPath);

            // 3) Draw shimmer with bouncing effect, stronger highlight
            var shimmerRect = new Rectangle(_shimmerX, 0, ShimmerWidth, Height);
            using var shimmerPath = CreateRoundedRectanglePath(shimmerRect, cornerRadius);
            g.SetClip(shimmerPath);

            // Use a brighter, more vivid shimmer gradient
            Color shimmerStart = Color.FromArgb(255, 191, 64, 255); // Neon violet (full alpha)
            Color shimmerEnd = Color.FromArgb(150, 138, 43, 226);   // Medium purple with transparency

            using var shimmerBrush = new LinearGradientBrush(
                shimmerRect,
                shimmerStart,
                shimmerEnd,
                LinearGradientMode.ForwardDiagonal);

            g.FillRectangle(shimmerBrush, shimmerRect);

            g.ResetClip();

            // 4) Draw outer border rounded rectangle
            using var borderPen = new Pen(Color.FromArgb(180, _progressColor), 2);
            using var borderPath = CreateRoundedRectanglePath(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius);
            g.DrawPath(borderPen, borderPath);
        }

        // Helper method to create a rounded rectangle path
        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

    }
}

