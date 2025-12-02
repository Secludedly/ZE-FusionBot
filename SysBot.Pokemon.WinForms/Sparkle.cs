using System;
using System.Drawing;

namespace SysBot.Pokemon.WinForms
{
    public class Sparkle
    {
        public PointF Position;
        public float Opacity;
        public float Size;
        public float Life;
        public float MaxLife;
        public float DX, DY; // tiny random movement
        public Color Color;   // sparkle color

        private static readonly Random rng = new Random();

        public Sparkle(PointF pos)
        {
            Position = pos;

            Size = rng.Next(2, 6); // 2–5 px radius
            MaxLife = Life = rng.Next(30, 80); // lifespan in ticks
            Opacity = 0; // start invisible

            DX = (float)(rng.NextDouble() - 0.5) * 0.5f; // tiny horizontal drift
            DY = (float)(rng.NextDouble() - 0.5) * 0.5f; // tiny vertical drift

            // Random color: white or light yellow
            Color = rng.NextDouble() < 0.5
                ? Color.FromArgb(255, 255, 255)          // pure white
                : Color.FromArgb(255, 255, 240, 180);   // soft light yellow
        }

        public bool Tick()
        {
            Life--;

            // Smooth fade in/out using sine wave
            float progress = 1f - Life / MaxLife;
            Opacity = (float)Math.Sin(progress * Math.PI * 0.65); 

            // Tiny random jitter movement
            Position.X += DX + (float)(rng.NextDouble() - 0.5) * 0.2f;
            Position.Y += DY + (float)(rng.NextDouble() - 0.5) * 0.2f;

            return Life <= 0;
        }

        public void Draw(Graphics g)
        {
            int alpha = (int)(Opacity * 255);
            alpha = Math.Clamp(alpha, 0, 255);

            using (Brush b = new SolidBrush(Color.FromArgb(alpha, Color)))
            {
                g.FillEllipse(b, Position.X, Position.Y, Size, Size);
            }
        }
    }
}
