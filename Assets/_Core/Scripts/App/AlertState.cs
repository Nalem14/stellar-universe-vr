using System;

namespace Core.App
{
    public enum AlertLevel
    {
        Normal,
        /// <summary>Hostiles in the system, a siege on one of our worlds here, an unscheduled wormhole.</summary>
        Amber,
        /// <summary>Our ship in a live fight.</summary>
        Red
    }

    /// <summary>The ship's alert condition, set by <see cref="AlertDirector"/>: lighting, sound and crew follow it in every room.</summary>
    public static class AlertState
    {
        public static AlertLevel Level { get; private set; }

        /// <summary>(previous, current).</summary>
        public static event Action<AlertLevel, AlertLevel> Changed;

        public static void Set(AlertLevel level)
        {
            if (level == Level)
                return;
            var was = Level;
            Level = level;
            Changed?.Invoke(was, level);
        }
    }
}
