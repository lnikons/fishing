using System.Collections.Generic;

namespace Fishing.Models
{
    /// <summary>Стан гравця.</summary>
    public class Player
    {
        public int Money { get; set; }
        public int Score { get; set; }

        public int CurrentLocationId { get; set; }

        /// <summary>Id відкритих локацій.</summary>
        public HashSet<int> UnlockedLocationIds { get; } = new HashSet<int>();

        /// <summary>Id покращення → куплений рівень.</summary>
        public Dictionary<int, int> UpgradeLevels { get; } = new Dictionary<int, int>();
    }
}
