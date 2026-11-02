namespace Fishing.Models
{
    public enum UpgradeType
    {
        CastRange,       // дальність закидання
        BiteSpeed,       // швидкість поклювання
        ReactionWindow,  // вікно підсічення
        CatchBar,        // розмір смуги в мінігрі
        SellBonus        // бонус до ціни продажу
    }

    /// <summary>Покращення, яке можна купити за гроші.</summary>
    public class Upgrade
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public UpgradeType Type { get; set; }

        public int MaxLevel { get; set; }

        /// <summary>Ціна першого рівня.</summary>
        public int BasePrice { get; set; }

        /// <summary>Приріст ефекту за один рівень.</summary>
        public double ValuePerLevel { get; set; }
    }
}
