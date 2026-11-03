using System.Threading;

namespace PreySense.Helpers
{
    /// <summary>
    /// Last real key-down. Mouse movement and clicks do not update it.
    /// </summary>
    public static class KeyboardActivity
    {
        private static int _lastKeyTick = Environment.TickCount;

        public static uint LastKeyTick => unchecked((uint)Volatile.Read(ref _lastKeyTick));
        public static event Action? KeyDown;

        public static void NoteKeyDown()
        {
            Volatile.Write(ref _lastKeyTick, Environment.TickCount);
            KeyDown?.Invoke();
        }
    }
}
