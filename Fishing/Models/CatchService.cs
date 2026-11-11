using System;

namespace Fishing.Models
{
    /// <summary>
    /// Обробка результату ловлі (завдання 22):
    /// відкриття виду в журналі, розрахунок ціни, продаж і нарахування монет.
    /// </summary>
    public static class CatchService
    {
        private static readonly Random _rng = new Random();

        /// <summary>Випадкова вага риби в межах [MinWeight; MaxWeight], округлена до 0.01 кг.</summary>
        public static double RollWeight(Fish fish, Random rng = null)
        {
            if (fish == null) throw new ArgumentNullException(nameof(fish));
            rng = rng ?? _rng;
            double w = fish.MinWeight + rng.NextDouble() * (fish.MaxWeight - fish.MinWeight);
            return Math.Round(w, 2);
        }

        /// <summary>
        /// Ціна = BasePrice * множник ваги (0.75 для мінімальної .. 1.25 для максимальної) * (1 + sellBonus).
        /// sellBonus — частка, напр. 0.1 = +10% (покращення UpgradeType.SellBonus). Мінімум 1 монета.
        /// </summary>
        public static int CalculatePrice(Fish fish, double weight, double sellBonus = 0)
        {
            if (fish == null) throw new ArgumentNullException(nameof(fish));

            double range = fish.MaxWeight - fish.MinWeight;
            double t = range > 0 ? (weight - fish.MinWeight) / range : 0.5;
            t = Math.Max(0, Math.Min(1, t));

            double weightFactor = 0.75 + 0.5 * t;
            double price = fish.BasePrice * weightFactor * (1 + Math.Max(0, sellBonus));
            return Math.Max(1, (int)Math.Round(price));
        }

        /// <summary>
        /// Обробляє впійману рибу: рахує ціну і відкриває вид у журналі гравця.
        /// Монети НЕ нараховує — для цього викликається Sell (або autoSell = true).
        /// </summary>
        public static CatchResult ProcessCatch(Player player, Fish fish, double weight,
                                               double sellBonus = 0, bool autoSell = false)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (fish == null) throw new ArgumentNullException(nameof(fish));

            var result = new CatchResult
            {
                Fish = fish,
                Weight = weight,
                Price = CalculatePrice(fish, weight, sellBonus),
                IsNewSpecies = player.DiscoveredFishIds.Add(fish.Id) // true, якщо ще не було
            };

            if (autoSell)
                Sell(player, result);

            return result;
        }

        /// <summary>Продає рибу: нараховує монети. Повертає кількість нарахованих монет (0, якщо вже продано).</summary>
        public static int Sell(Player player, CatchResult result)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.IsSold) return 0;

            player.Money += result.Price;
            result.IsSold = true;
            return result.Price;
        }

        /// <summary>Чи відкритий вид у журналі (для кнопки «Журнал»).</summary>
        public static bool IsDiscovered(Player player, Fish fish)
        {
            return player.DiscoveredFishIds.Contains(fish.Id);
        }
    }
}
