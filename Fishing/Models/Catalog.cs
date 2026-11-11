using System.Collections.Generic;
using System.Linq;

namespace Fishing.Models
{
    /// <summary>Статичний каталог: усі локації та риби гри.</summary>
    public static class Catalog
    {
        public static IReadOnlyList<Location> Locations { get; } = BuildLocations();

        /// <summary>Усі риби з усіх локацій.</summary>
        public static IEnumerable<Fish> AllFish => Locations.SelectMany(l => l.Fishes);

        public static Location GetLocation(int id) => Locations.First(l => l.Id == id);

        public static Fish GetFish(int id) => AllFish.First(f => f.Id == id);

        private static List<Location> BuildLocations()
        {
            // --- Локація 1: Ставок (безкоштовна) ---
            var pond = new Location { Id = 1, Name = "Ставок", Price = 0 };
            pond.Fishes.AddRange(new[]
            {
                new Fish { Id = 1,  Name = "Карась",       Rarity = Rarity.Common,    BasePrice = 10,  MinWeight = 0.2, MaxWeight = 1.0,  Difficulty = 0.10 },
                new Fish { Id = 2,  Name = "Плотва",       Rarity = Rarity.Common,    BasePrice = 12,  MinWeight = 0.1, MaxWeight = 0.8,  Difficulty = 0.15 },
                new Fish { Id = 3,  Name = "Окунь",        Rarity = Rarity.Uncommon,  BasePrice = 25,  MinWeight = 0.2, MaxWeight = 1.5,  Difficulty = 0.30 },
                new Fish { Id = 4,  Name = "Короп",        Rarity = Rarity.Rare,      BasePrice = 60,  MinWeight = 1.0, MaxWeight = 8.0,  Difficulty = 0.50 },
                new Fish { Id = 5,  Name = "Золотий короп", Rarity = Rarity.Legendary, BasePrice = 300, MinWeight = 2.0, MaxWeight = 6.0,  Difficulty = 0.85 },
            });

            // --- Локація 2: Річка ---
            var river = new Location { Id = 2, Name = "Річка", Price = 500 };
            river.Fishes.AddRange(new[]
            {
                new Fish { Id = 6,  Name = "Головань",     Rarity = Rarity.Common,    BasePrice = 30,  MinWeight = 0.3, MaxWeight = 2.0,  Difficulty = 0.20 },
                new Fish { Id = 7,  Name = "Лящ",          Rarity = Rarity.Uncommon,  BasePrice = 55,  MinWeight = 0.5, MaxWeight = 3.0,  Difficulty = 0.35 },
                new Fish { Id = 8,  Name = "Щука",         Rarity = Rarity.Rare,      BasePrice = 120, MinWeight = 1.0, MaxWeight = 12.0, Difficulty = 0.55 },
                new Fish { Id = 9,  Name = "Сом",          Rarity = Rarity.Epic,      BasePrice = 250, MinWeight = 5.0, MaxWeight = 40.0, Difficulty = 0.75 },
                new Fish { Id = 10, Name = "Білуга",       Rarity = Rarity.Legendary, BasePrice = 700, MinWeight = 30.0, MaxWeight = 150.0, Difficulty = 0.95 },
            });

            // --- Локація 3: Море ---
            var sea = new Location { Id = 3, Name = "Море", Price = 2000 };
            sea.Fishes.AddRange(new[]
            {
                new Fish { Id = 11, Name = "Хамса",        Rarity = Rarity.Common,    BasePrice = 60,  MinWeight = 0.01, MaxWeight = 0.05, Difficulty = 0.25 },
                new Fish { Id = 12, Name = "Камбала",      Rarity = Rarity.Uncommon,  BasePrice = 110, MinWeight = 0.5, MaxWeight = 3.0,  Difficulty = 0.40 },
                new Fish { Id = 13, Name = "Тунець",       Rarity = Rarity.Rare,      BasePrice = 260, MinWeight = 10.0, MaxWeight = 80.0, Difficulty = 0.65 },
                new Fish { Id = 14, Name = "Риба-меч",     Rarity = Rarity.Epic,      BasePrice = 550, MinWeight = 20.0, MaxWeight = 120.0, Difficulty = 0.80 },
                new Fish { Id = 15, Name = "Мегалодон",    Rarity = Rarity.Legendary, BasePrice = 2000, MinWeight = 500.0, MaxWeight = 1500.0, Difficulty = 1.00 },
            });

            return new List<Location> { pond, river, sea };
        }
    }
}
