using System;
using System.Linq;

namespace Fishing.Models
{
    /// <summary>Магазин (завдання 24): купівля локацій і покращень із перевіркою балансу.</summary>
    public static class ShopService
    {
        // ---------- Локації ----------

        /// <summary>Локація доступна, якщо вона безкоштовна або вже куплена.</summary>
        public static bool IsLocationUnlocked(Player player, Location location)
        {
            return location.Price == 0 || player.UnlockedLocationIds.Contains(location.Id);
        }

        public static PurchaseResult BuyLocation(Player player, int locationId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var location = Catalog.Locations.FirstOrDefault(l => l.Id == locationId);
            if (location == null)
                return Fail(PurchaseStatus.NotFound, "Локацію не знайдено.");

            if (IsLocationUnlocked(player, location))
                return Fail(PurchaseStatus.AlreadyOwned, $"Локація «{location.Name}» уже відкрита.");

            if (player.Money < location.Price)
                return Fail(PurchaseStatus.NotEnoughMoney,
                    $"Недостатньо монет: потрібно {location.Price}, у вас {player.Money}.");

            player.Money -= location.Price;
            player.UnlockedLocationIds.Add(location.Id);

            return new PurchaseResult
            {
                Status = PurchaseStatus.Success,
                Spent = location.Price,
                Message = $"Локацію «{location.Name}» відкрито!"
            };
        }

        // ---------- Покращення ----------

        public static int GetUpgradeLevel(Player player, int upgradeId)
        {
            return player.UpgradeLevels.TryGetValue(upgradeId, out int lvl) ? lvl : 0;
        }

        /// <summary>Ціна наступного рівня = BasePrice * 2^(поточний рівень): 100, 200, 400. Якщо максимум — повертає -1.</summary>
        public static int GetUpgradePrice(Player player, Upgrade upgrade)
        {
            int level = GetUpgradeLevel(player, upgrade.Id);
            if (level >= upgrade.MaxLevel) return -1;
            return upgrade.BasePrice * (1 << level);
        }

        public static PurchaseResult BuyUpgrade(Player player, int upgradeId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var upgrade = UpgradeCatalog.Get(upgradeId);
            if (upgrade == null)
                return Fail(PurchaseStatus.NotFound, "Покращення не знайдено.");

            int level = GetUpgradeLevel(player, upgradeId);
            if (level >= upgrade.MaxLevel)
                return Fail(PurchaseStatus.MaxLevel, $"«{upgrade.Name}» уже на максимальному рівні ({upgrade.MaxLevel}).");

            int price = GetUpgradePrice(player, upgrade);
            if (player.Money < price)
                return Fail(PurchaseStatus.NotEnoughMoney,
                    $"Недостатньо монет: потрібно {price}, у вас {player.Money}.");

            player.Money -= price;
            player.UpgradeLevels[upgradeId] = level + 1;

            return new PurchaseResult
            {
                Status = PurchaseStatus.Success,
                Spent = price,
                Message = $"«{upgrade.Name}» покращено до рівня {level + 1}!"
            };
        }

        /// <summary>
        /// Сумарний ефект покращень певного типу (рівень * ValuePerLevel).
        /// Напр. GetUpgradeValue(player, UpgradeType.SellBonus) можна передати як sellBonus у CatchService.
        /// </summary>
        public static double GetUpgradeValue(Player player, UpgradeType type)
        {
            return UpgradeCatalog.All
                .Where(u => u.Type == type)
                .Sum(u => GetUpgradeLevel(player, u.Id) * u.ValuePerLevel);
        }

        private static PurchaseResult Fail(PurchaseStatus status, string message)
        {
            return new PurchaseResult { Status = status, Message = message, Spent = 0 };
        }
    }
}
