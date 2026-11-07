using System.Collections.Generic;
using System.Linq;

namespace Fishing.Models
{
    /// <summary>Журнал (завдання 23): список усіх видів зі станом «відкрита / закрита».</summary>
    public static class JournalService
    {
        /// <summary>Усі види з усіх локацій (порядок як у каталозі).</summary>
        public static List<JournalEntry> GetEntries(Player player)
        {
            return Catalog.Locations
                .SelectMany(loc => loc.Fishes.Select(fish => new JournalEntry
                {
                    Fish = fish,
                    Location = loc,
                    IsDiscovered = player.DiscoveredFishIds.Contains(fish.Id)
                }))
                .ToList();
        }

        /// <summary>Види лише однієї локації.</summary>
        public static List<JournalEntry> GetEntries(Player player, int locationId)
        {
            return GetEntries(player).Where(e => e.Location.Id == locationId).ToList();
        }

        /// <summary>Скільки видів відкрито і скільки всього (напр. 3 з 15).</summary>
        public static void GetProgress(Player player, out int discovered, out int total)
        {
            var entries = GetEntries(player);
            total = entries.Count;
            discovered = entries.Count(e => e.IsDiscovered);
        }

        /// <summary>Рядок прогресу для заголовка журналу, напр. "Відкрито: 3 / 15".</summary>
        public static string GetProgressText(Player player)
        {
            GetProgress(player, out int discovered, out int total);
            return $"Відкрито: {discovered} / {total}";
        }
    }
}
