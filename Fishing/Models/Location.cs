using System.Collections.Generic;

namespace Fishing.Models
{
    /// <summary>Локація для рибалки.</summary>
    public class Location
    {
        public int Id { get; set; }
        public string Name { get; set; }

        /// <summary>Ціна відкриття. 0 = доступна з початку.</summary>
        public int Price { get; set; }

        /// <summary>Риба, що водиться на локації.</summary>
        public List<Fish> Fishes { get; } = new List<Fish>();
    }
}
