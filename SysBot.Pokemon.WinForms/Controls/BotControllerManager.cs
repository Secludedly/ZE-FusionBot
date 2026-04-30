using System.Collections.Concurrent;

namespace SysBot.Pokemon.WinForms.Controls
{
    public static class BotControllerManager
    {
        private static readonly ConcurrentDictionary<string, BotController> Controllers = new();

        public static void RegisterController(string botLabel, BotController controller)
        {
            Controllers[botLabel] = controller;
        }

        public static void UnregisterController(string botLabel)
        {
            Controllers.TryRemove(botLabel, out _);
        }

        public static BotController? GetController(string botLabel)
        {
            return Controllers.TryGetValue(botLabel, out var controller) ? controller : null;
        }

        public static void Clear()
        {
            Controllers.Clear();
        }
    }
}
