using System.Collections.Generic;

namespace Fishing.Models
{
    /// <summary>Дані, які записуються у файл збереження (текстовий формат ключ=значення).</summary>
    public class SaveData
    {
        /// <summary>Версія формату — на випадок, якщо структура зміниться.</summary>
        public int Version { get; set; } = SaveService.CurrentVersion;

        public int Money { get; set; }
        public int Score { get; set; }
        public int CurrentLocationId { get; set; }

        public List<int> UnlockedLocationIds { get; set; } = new List<int>();

        /// <summary>Id покращення → рівень.</summary>
        public Dictionary<int, int> UpgradeLevels { get; set; } = new Dictionary<int, int>();

        /// <summary>Id відкритих видів риб (журнал).</summary>
        public List<int> DiscoveredFishIds { get; set; } = new List<int>();

        /// <summary>Id риби → скільки разів спіймано.</summary>
        public Dictionary<int, int> CatchCounts { get; set; } = new Dictionary<int, int>();

        /// <summary>Id риби → рекордна вага, кг.</summary>
        public Dictionary<int, double> BestWeights { get; set; } = new Dictionary<int, double>();
    }
}
