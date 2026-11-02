namespace Fishing.Models
{
    /// <summary>Вид риби.</summary>
    public class Fish
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Rarity Rarity { get; set; }

        /// <summary>Базова ціна продажу.</summary>
        public int BasePrice { get; set; }

        public double MinWeight { get; set; }
        public double MaxWeight { get; set; }

        /// <summary>Складність мінігри зі смугою: 0.0 (легко) .. 1.0 (дуже важко).</summary>
        public double Difficulty { get; set; }
    }
}
