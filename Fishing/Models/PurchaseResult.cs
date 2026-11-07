namespace Fishing.Models
{
    public enum PurchaseStatus
    {
        Success,
        NotEnoughMoney,
        AlreadyOwned,   // локація вже відкрита
        MaxLevel,       // покращення вже на максимальному рівні
        NotFound        // невідомий id
    }

    /// <summary>Результат спроби купівлі в магазині.</summary>
    public class PurchaseResult
    {
        public PurchaseStatus Status { get; set; }
        public bool Success => Status == PurchaseStatus.Success;

        /// <summary>Повідомлення для гравця (можна показати в MessageBox / Label).</summary>
        public string Message { get; set; }

        /// <summary>Скільки монет списано (0, якщо покупка не відбулась).</summary>
        public int Spent { get; set; }
    }
}
