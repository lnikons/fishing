namespace Fishing.Models
{
    /// <summary>Один рядок журналу: вид риби та його стан («відкрита» / «закрита»).</summary>
    public class JournalEntry
    {
        public Fish Fish { get; set; }
        public Location Location { get; set; }

        /// <summary>true = вид відкритий (гравець уже ловив його).</summary>
        public bool IsDiscovered { get; set; }

        /// <summary>Назва для відображення: справжня, якщо відкрита, інакше "???".</summary>
        public string DisplayName => IsDiscovered ? Fish.Name : "???";

        /// <summary>Текстовий стан: «Відкрита» / «Закрита».</summary>
        public string StateText => IsDiscovered ? "Відкрита" : "Закрита";

        /// <summary>Готовий рядок для ListBox / Label.</summary>
        public override string ToString()
        {
            return $"{DisplayName} — {Location.Name} [{StateText}]";
        }
    }
}
