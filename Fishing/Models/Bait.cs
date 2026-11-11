namespace Fishing.Models
{
    /// <summary>Наживка. Чим більший RarityBonus, тим частіше клює рідкісна риба.</summary>
    public class Bait
    {
        public int Id { get; set; }
        public string Name { get; set; }

        /// <summary>0 = без впливу. Напр. 0.5 — помітно підвищує шанс рідкісних риб.</summary>
        public double RarityBonus { get; set; }
    }
}