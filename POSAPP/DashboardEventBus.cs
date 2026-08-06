using System;

namespace POSAPP
{
    /// <summary>
    /// Static event bus — any form calls DashboardEventBus.Notify()
    /// and the Dashboard refreshes immediately.
    /// </summary>
    public static class DashboardEventBus
    {
        public static event EventHandler DataChanged;

        public static void Notify() =>
            DataChanged?.Invoke(null, EventArgs.Empty);
    }
}