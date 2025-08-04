using System;
using SysBot.Pokemon.WinForms.Controls;
namespace SysBot.Pokemon.WinForms.Controls
{
    public static class ProgressHelper
    {
        private static BotController _controller;
        public static void Initialize(BotController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }
        public static void Set(int percent)
        {
            _controller?.SetTradeProgress(percent);
        }
        public static void Reset() => Set(0);
    }
}
