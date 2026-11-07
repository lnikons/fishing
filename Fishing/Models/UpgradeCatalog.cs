using System.Collections.Generic;
using System.Linq;

namespace Fishing.Models
{
    /// <summary>Статичний каталог покращень, які можна купити в магазині.</summary>
    public static class UpgradeCatalog
    {
        public static IReadOnlyList<Upgrade> All { get; } = new List<Upgrade>
        {
            // ValuePerLevel — приріст ефекту за рівень: пікселі дальності, секунди, частка зони або ціни.
            new Upgrade { Id = 4, Name = "Краща вудка",    Description = "Ширша зона лову",      Type = UpgradeType.CatchBar,       MaxLevel = 3, BasePrice = 150, ValuePerLevel = 2.5 },
            new Upgrade { Id = 1, Name = "Довга волосінь", Description = "Далі закидання",       Type = UpgradeType.CastRange,      MaxLevel = 3, BasePrice = 100, ValuePerLevel = 50 },
            new Upgrade { Id = 2, Name = "Смачна наживка", Description = "Швидше клює",          Type = UpgradeType.BiteSpeed,      MaxLevel = 3, BasePrice = 120, ValuePerLevel = 0.5 },
            new Upgrade { Id = 3, Name = "Швидка реакція", Description = "Довше вікно підсічки", Type = UpgradeType.ReactionWindow, MaxLevel = 3, BasePrice = 100, ValuePerLevel = 0.25 },
            new Upgrade { Id = 5, Name = "Торговець",      Description = "+10% до ціни риби",    Type = UpgradeType.SellBonus,      MaxLevel = 3, BasePrice = 200, ValuePerLevel = 0.10 },
        };

        public static Upgrade Get(int id) => All.FirstOrDefault(u => u.Id == id);
    }
}
