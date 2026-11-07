using System;
using System.Linq;

namespace Fishing.Models
{
    /// <summary>Вибір риби, яка клюнула: за локацією, рідкістю та наживкою.</summary>
    public static class FishPicker
    {
        private static readonly Random _rng = new Random();

        /// <summary>Базова вага (шанс) за рідкістю.</summary>
        private static double BaseWeight(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return 50;
                case Rarity.Uncommon: return 25;
                case Rarity.Rare: return 15;
                case Rarity.Epic: return 8;
                case Rarity.Legendary: return 2;
                default: return 1;
            }
        }

        /// <summary>
        /// Повертає рибу з локації. Наживка (може бути null) збільшує вагу рідкісних риб:
        /// вага = базова * (1 + RarityBonus * номер рідкості).
        /// </summary>
        public static Fish Pick(Location location, Bait bait = null, Random rng = null)
        {
            if (location == null || location.Fishes.Count == 0)
                return null;

            rng = rng ?? _rng;
            double bonus = bait?.RarityBonus ?? 0;

            double[] weights = location.Fishes
                .Select(f => BaseWeight(f.Rarity) * (1 + bonus * (int)f.Rarity))
                .ToArray();

            double roll = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                    return location.Fishes[i];
            }
            return location.Fishes[location.Fishes.Count - 1];
        }
    }
}