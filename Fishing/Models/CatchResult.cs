namespace Fishing.Models
{
    /// <summary>Результат однієї успішної ловлі.</summary>
    public class CatchResult
    {
        public Fish Fish { get; set; }

        /// <summary>Вага впійманої риби, кг.</summary>
        public double Weight { get; set; }

        /// <summary>Ціна продажу з урахуванням ваги та бонусу.</summary>
        public int Price { get; set; }

        /// <summary>true, якщо цей вид риби гравець упіймав уперше (відкрито запис у журналі).</summary>
        public bool IsNewSpecies { get; set; }

        /// <summary>true, якщо рибу вже продано (захист від подвійного продажу).</summary>
        public bool IsSold { get; set; }
    }
}
