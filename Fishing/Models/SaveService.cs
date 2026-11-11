using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Fishing.Models
{
    /// <summary>
    /// Збереження та завантаження прогресу гравця у текстовий файл (завдання 25).
    /// Формат — прості рядки "ключ=значення", без зовнішніх бібліотек (працює на .NET Framework).
    /// Load змінює переданий об'єкт Player «на місці», тому працює і з readonly-полями форми.
    /// Якщо файл відсутній або пошкоджений — гравець не змінюється, а метод повертає false і текст помилки.
    /// </summary>
    public static class SaveService
    {
        public const int CurrentVersion = 1;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Шлях за замовчуванням: %AppData%\Fishing\save.txt.</summary>
        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fishing");
                return Path.Combine(dir, "save.txt");
            }
        }

        public static bool HasSave(string path = null)
        {
            return File.Exists(path ?? DefaultPath);
        }

        public static bool DeleteSave(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // ---------- Збереження ----------

        /// <param name="catchCounts">Id риби → скільки спіймано (необов'язково).</param>
        /// <param name="bestWeights">Id риби → рекордна вага (необов'язково).</param>
        public static bool Save(Player player, IDictionary<int, int> catchCounts,
                                IDictionary<int, double> bestWeights, out string error, string path = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            error = null;
            path = path ?? DefaultPath;

            var data = new SaveData
            {
                Money = player.Money,
                Score = player.Score,
                CurrentLocationId = player.CurrentLocationId,
                UnlockedLocationIds = player.UnlockedLocationIds.ToList(),
                UpgradeLevels = new Dictionary<int, int>(player.UpgradeLevels),
                DiscoveredFishIds = player.DiscoveredFishIds.ToList(),
                CatchCounts = catchCounts != null ? new Dictionary<int, int>(catchCounts) : new Dictionary<int, int>(),
                BestWeights = bestWeights != null ? new Dictionary<int, double>(bestWeights) : new Dictionary<int, double>()
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                // Спочатку пишемо у тимчасовий файл, щоб збій посеред запису не зіпсував старе збереження.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialize(data), Encoding.UTF8);

                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = "Не вдалося зберегти прогрес: " + ex.Message;
                return false;
            }
        }

        /// <summary>Скорочений варіант: лише Player.</summary>
        public static bool Save(Player player, out string error, string path = null)
        {
            return Save(player, null, null, out error, path);
        }

        // ---------- Завантаження ----------

        /// <summary>
        /// Читає файл, перевіряє дані та підставляє їх у player (і в словники, якщо передані).
        /// Некоректні значення (невідомі id, рівень вище максимуму, від'ємні монети) відкидаються або виправляються.
        /// </summary>
        public static bool Load(Player player, IDictionary<int, int> catchCounts,
                                IDictionary<int, double> bestWeights, out string error, string path = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            error = null;
            path = path ?? DefaultPath;

            if (!File.Exists(path))
            {
                error = "Файл збереження не знайдено.";
                return false;
            }

            SaveData data;
            try
            {
                data = Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = "Файл збереження пошкоджено: " + ex.Message;
                return false;
            }

            if (data.Version > CurrentVersion)
            {
                error = "Збереження створене новішою версією гри.";
                return false;
            }

            Apply(data, player, catchCounts, bestWeights);
            return true;
        }

        /// <summary>Скорочений варіант: лише Player.</summary>
        public static bool Load(Player player, out string error, string path = null)
        {
            return Load(player, null, null, out error, path);
        }

        // ---------- Текстовий формат ----------

        private static string Serialize(SaveData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("version=" + d.Version.ToString(Inv));
            sb.AppendLine("money=" + d.Money.ToString(Inv));
            sb.AppendLine("score=" + d.Score.ToString(Inv));
            sb.AppendLine("location=" + d.CurrentLocationId.ToString(Inv));
            sb.AppendLine("unlocked=" + string.Join(",", d.UnlockedLocationIds.Select(i => i.ToString(Inv))));
            sb.AppendLine("upgrades=" + string.Join(",", d.UpgradeLevels.Select(p => p.Key.ToString(Inv) + ":" + p.Value.ToString(Inv))));
            sb.AppendLine("discovered=" + string.Join(",", d.DiscoveredFishIds.Select(i => i.ToString(Inv))));
            sb.AppendLine("counts=" + string.Join(",", d.CatchCounts.Select(p => p.Key.ToString(Inv) + ":" + p.Value.ToString(Inv))));
            sb.AppendLine("weights=" + string.Join(",", d.BestWeights.Select(p => p.Key.ToString(Inv) + ":" + p.Value.ToString("R", Inv))));
            return sb.ToString();
        }

        /// <summary>Розбирає текст. Невідомі ключі ігноруються, при помилці формату кидає FormatException.</summary>
        private static SaveData Parse(string text)
        {
            var d = new SaveData { Version = 0 };
            bool hasVersion = false;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) throw new FormatException("некоректний рядок: " + line);

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "version": d.Version = ParseInt(value); hasVersion = true; break;
                    case "money": d.Money = ParseInt(value); break;
                    case "score": d.Score = ParseInt(value); break;
                    case "location": d.CurrentLocationId = ParseInt(value); break;
                    case "unlocked": d.UnlockedLocationIds = ParseIntList(value); break;
                    case "upgrades": d.UpgradeLevels = ParseIntPairs(value); break;
                    case "discovered": d.DiscoveredFishIds = ParseIntList(value); break;
                    case "counts": d.CatchCounts = ParseIntPairs(value); break;
                    case "weights": d.BestWeights = ParseDoublePairs(value); break;
                    // інші ключі ігноруємо
                }
            }

            if (!hasVersion) throw new FormatException("відсутня версія файлу");
            return d;
        }

        private static int ParseInt(string s)
        {
            if (!int.TryParse(s, NumberStyles.Integer, Inv, out int v))
                throw new FormatException("очікувалось ціле число, а не \"" + s + "\"");
            return v;
        }

        private static double ParseDouble(string s)
        {
            if (!double.TryParse(s, NumberStyles.Float, Inv, out double v))
                throw new FormatException("очікувалось число, а не \"" + s + "\"");
            return v;
        }

        private static List<int> ParseIntList(string s)
        {
            var list = new List<int>();
            if (s.Length == 0) return list;
            foreach (string part in s.Split(','))
                list.Add(ParseInt(part.Trim()));
            return list;
        }

        private static Dictionary<int, int> ParseIntPairs(string s)
        {
            var dict = new Dictionary<int, int>();
            if (s.Length == 0) return dict;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) throw new FormatException("очікувалась пара id:значення, а не \"" + part + "\"");
                dict[ParseInt(kv[0].Trim())] = ParseInt(kv[1].Trim());
            }
            return dict;
        }

        private static Dictionary<int, double> ParseDoublePairs(string s)
        {
            var dict = new Dictionary<int, double>();
            if (s.Length == 0) return dict;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) throw new FormatException("очікувалась пара id:значення, а не \"" + part + "\"");
                dict[ParseInt(kv[0].Trim())] = ParseDouble(kv[1].Trim());
            }
            return dict;
        }

        // ---------- Перевірка та застосування ----------

        // Усі дані спочатку перевіряються, і лише потім записуються в об'єкти гри.
        private static void Apply(SaveData data, Player player,
                                  IDictionary<int, int> catchCounts, IDictionary<int, double> bestWeights)
        {
            var validLocations = new HashSet<int>(Catalog.Locations.Select(l => l.Id));
            var validFish = new HashSet<int>(Catalog.AllFish.Select(f => f.Id));

            // Безкоштовні локації відкриті завжди
            var unlocked = new HashSet<int>(Catalog.Locations.Where(l => l.Price == 0).Select(l => l.Id));
            foreach (int id in data.UnlockedLocationIds)
                if (validLocations.Contains(id)) unlocked.Add(id);

            int currentLocation = unlocked.Contains(data.CurrentLocationId)
                ? data.CurrentLocationId
                : Catalog.Locations.First(l => l.Price == 0).Id;

            var upgrades = new Dictionary<int, int>();
            foreach (var pair in data.UpgradeLevels)
            {
                Upgrade upgrade = UpgradeCatalog.Get(pair.Key);
                if (upgrade == null) continue;
                int level = Math.Max(0, Math.Min(upgrade.MaxLevel, pair.Value));
                if (level > 0) upgrades[upgrade.Id] = level;
            }

            var discovered = new HashSet<int>(data.DiscoveredFishIds.Where(validFish.Contains));

            var counts = new Dictionary<int, int>();
            foreach (var pair in data.CatchCounts)
                if (validFish.Contains(pair.Key) && pair.Value > 0) counts[pair.Key] = pair.Value;

            var weights = new Dictionary<int, double>();
            foreach (var pair in data.BestWeights)
                if (validFish.Contains(pair.Key) && pair.Value > 0 && !double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
                    weights[pair.Key] = pair.Value;

            // Спійманий вид завжди вважається відкритим
            foreach (int id in counts.Keys) discovered.Add(id);

            // --- запис у об'єкти гри ---
            player.Money = Math.Max(0, data.Money);
            player.Score = Math.Max(0, data.Score);
            player.CurrentLocationId = currentLocation;

            player.UnlockedLocationIds.Clear();
            foreach (int id in unlocked) player.UnlockedLocationIds.Add(id);

            player.UpgradeLevels.Clear();
            foreach (var pair in upgrades) player.UpgradeLevels[pair.Key] = pair.Value;

            player.DiscoveredFishIds.Clear();
            foreach (int id in discovered) player.DiscoveredFishIds.Add(id);

            if (catchCounts != null)
            {
                catchCounts.Clear();
                foreach (var pair in counts) catchCounts[pair.Key] = pair.Value;
            }
            if (bestWeights != null)
            {
                bestWeights.Clear();
                foreach (var pair in weights) bestWeights[pair.Key] = pair.Value;
            }
        }
    }
}
