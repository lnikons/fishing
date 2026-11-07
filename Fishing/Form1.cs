using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fishing.Models;

namespace Fishing
{
    public partial class Form1 : Form
    {
        // Точка кінчика вудки і поточна позиція поплавця
        private readonly PointF rodTip = new PointF(84, 112); // кінчик вудки, що тримається на містку зліва
        private PointF floatPos;
        private PointF floatTarget;

        // Стан закидання
        private bool isCharging;
        private bool isFlying;
        private bool isReeling;   // поплавець змотується назад до вудки
        private float castPower;
        private int castDirection = 1;

        // Стан поклювання
        private bool isBiting;
        private string statusText = "";
        private readonly Random rng = new Random();

        // Міні-гра на утримання
        private bool isFighting;
        private bool isHolding;
        private bool spaceDown;       // пробіл зараз утримується
        private bool mouseDown;       // ЛКМ зараз утримується на полі
        private float playerPos;      // центр зеленої зони на смузі, 0–100 знизу вгору
        private float fightProgress;  // прогрес вилову, 0–100
        private float fightElapsed;   // мс від початку міні-гри
        private float fishPos;        // позиція риби на смузі, 0–100
        private float fishTarget;     // куди зараз плаває риба
        private float fishRetargetMs; // скільки ще риба плаває до нової цілі, мс
        private const float BaseZoneHalfSize = 15f; // без прокачки зона займає 30% смуги

        // Половина зеленої зони: росте з покращенням «Краща вудка»
        private float ZoneHalfSize => BaseZoneHalfSize + (float)UpgradeValue(UpgradeType.CatchBar);

        // Риба, що зараз на гачку, і параметри її поведінки в міні-грі
        private Fish hookedFish;
        private float fishSpeed;       // скільки смуги риба проходить за 16 мс
        private float fishJumpScale;   // 1 — спокійна риба, менше — частіше міняє напрям
        private const float PressureGrowthMs = 9000f; // за скільки мс швидкість падіння шкали зростає на 100%

        // Екран результату вилову
        private bool isShowingResult;
        private Fish lastFish;
        private double lastWeight;
        private int lastPrice;
        private bool lastIsNewSpecies;
        private readonly HashSet<int> discoveredFishIds = new HashSet<int>();

        // Журнал риб: скільки разів спіймано кожен вид і найбільша вага
        private readonly Dictionary<int, int> catchCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, double> bestWeights = new Dictionary<int, double>();
        private int journalLocationId = 1; // відкрита вкладка журналу
        private readonly List<(RectangleF Rect, int LocationId)> journalTabs = new List<(RectangleF Rect, int LocationId)>();

        // Екран журналу (створюється в коді, а не в дизайнері)
        private readonly Panel pnlJournal = new Panel();

        // Стан гравця (модель бекенду Player): монети, поточна і відкриті локації, рівні покращень
        private readonly Player player = new Player { Money = 0, CurrentLocationId = 1 };

        // Магазин (створюється в коді, а не в дизайнері)
        private readonly Panel pnlShop = new Panel();
        private readonly Button btnShop = new Button();
        private string shopMessage = "";
        private RectangleF? hoveredShopRect;  // кнопка магазину під курсором
        private Image coinImage;              // монетка для цін у магазині

        // Кнопки, намальовані на панелі магазину: прямокутник, чи активна, що робити при кліку
        private readonly List<(RectangleF Rect, bool Enabled, Action OnClick)> shopButtons =
            new List<(RectangleF Rect, bool Enabled, Action OnClick)>();

        // ТИМЧАСОВО до бекенду (картки 20/24): покращення на моделі Upgrade.
        // ValuePerLevel — приріст ефекту за рівень: пікселі дальності, секунди, частка зони або ціни.
        private static readonly List<Upgrade> TempUpgrades = new List<Upgrade>
        {
            new Upgrade { Id = 4, Name = "Краща вудка",    Description = "Ширша зона лову",      Type = UpgradeType.CatchBar,       MaxLevel = 3, BasePrice = 150, ValuePerLevel = 2.5 },
            new Upgrade { Id = 1, Name = "Довга волосінь", Description = "Далі закидання",       Type = UpgradeType.CastRange,      MaxLevel = 3, BasePrice = 100, ValuePerLevel = 50 },
            new Upgrade { Id = 2, Name = "Смачна наживка", Description = "Швидше клює",          Type = UpgradeType.BiteSpeed,      MaxLevel = 3, BasePrice = 120, ValuePerLevel = 0.5 },
            new Upgrade { Id = 3, Name = "Швидка реакція", Description = "Довше вікно підсічки", Type = UpgradeType.ReactionWindow, MaxLevel = 3, BasePrice = 100, ValuePerLevel = 0.25 },
            new Upgrade { Id = 5, Name = "Торговець",      Description = "+10% до ціни риби",    Type = UpgradeType.SellBonus,      MaxLevel = 3, BasePrice = 200, ValuePerLevel = 0.10 },
        };

        // Масштаб екрана: 1.0 при 100%, 1.25 при 125% і т. д.
        // Гра малюється в логічних пікселях (як при 100%), а Graphics масштабує їх під екран.
        private float DpiScale => DeviceDpi / 96f;
        private float LogicalWidth => pnlGame.ClientSize.Width / DpiScale;
        private float LogicalHeight => pnlGame.ClientSize.Height / DpiScale;

        // Графіка з картки 16: вбудовані в збірку PNG (Fishing/Assets)
        private readonly Dictionary<int, Image> fishSprites = new Dictionary<int, Image>();      // Id риби → спрайт
        private readonly Dictionary<int, Image> fishSilhouettes = new Dictionary<int, Image>();  // Id риби → темний силует
        private readonly Dictionary<int, Image> backgrounds = new Dictionary<int, Image>();      // Id локації → фон
        private readonly List<Image> loadedImages = new List<Image>();                            // усе, що треба звільнити
        private Bitmap backgroundCache;   // фон, уже масштабований під розмір панелі (щоб не масштабувати щокадру)
        private int backgroundCacheLocationId = -1;
        private readonly PictureBox pbCoin = new PictureBox();

        // Ресурси для малювання: створюються один раз, а не в кожному кадрі.
        // Шрифти в пікселях, щоб масштаб не застосувався до них двічі.
        private readonly Pen rodPen = new Pen(Color.SaddleBrown, 3);
        private readonly Pen linePen = new Pen(Color.FromArgb(235, 245, 245, 245), 1.5f); // волосінь
        private readonly Font alertFont = new Font("Segoe UI", 27F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font statusFont = new Font("Segoe UI", 19F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font resultTitleFont = new Font("Segoe UI", 26F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font resultTextFont = new Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font resultHintFont = new Font("Segoe UI", 14F, FontStyle.Italic, GraphicsUnit.Pixel);
        private readonly Font shopNameFont = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font shopTextFont = new Font("Segoe UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font shopButtonFont = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly SolidBrush overlayBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        private readonly StringFormat centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly StringFormat rightFormat = new StringFormat { Alignment = StringAlignment.Far };

        private readonly System.Windows.Forms.Timer tmrCast = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly System.Windows.Forms.Timer tmrBite = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer tmrReaction = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly System.Windows.Forms.Timer tmrFight = new System.Windows.Forms.Timer { Interval = 16 };

        // Повідомлення («Риба втекла», «Не вистачає монет» тощо) самі зникають через 3 с
        private readonly System.Windows.Forms.Timer tmrMessage = new System.Windows.Forms.Timer { Interval = 3000 };

        // Реальний час між тіками: таймер WinForms може тікати рідше за 16 мс,
        // тому рух рахується від справжнього часу, а не від кількості тіків
        private readonly Stopwatch castClock = new Stopwatch();
        private readonly Stopwatch fightClock = new Stopwatch();

        // Відлік від моменту, коли минуло вікно реакції: запізнілий клік не повинен одразу закидати знову
        private readonly Stopwatch sinceReactionTimeout = new Stopwatch();
        private const int LatePressIgnoreMs = 300;

        // Кнопка повернення в меню на ігровому екрані (створюється в коді, а не в дизайнері)
        private readonly Button btnMenu = new Button();

        // Чи утримується зараз хоча б одна кнопка керування (ЛКМ або пробіл)
        private bool AnyInputDown => mouseDown || spaceDown;

        public Form1()
        {
            InitializeComponent();

            LoadGameImages();

            // Фіксований розмір вікна: не можна тягнути краї і розгортати
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Панелі займають всю область під написом монет
            pnlMenu.Size = new Size(ClientSize.Width, ClientSize.Height - pnlMenu.Top);
            pnlGame.Size = new Size(ClientSize.Width, ClientSize.Height - pnlGame.Top);

            // Клавіатура: пробіл дублює ЛКМ
            KeyPreview = true;
            KeyDown += Form1_KeyDown;
            KeyUp += Form1_KeyUp;
            Deactivate += (s, e) => ReleaseAllInput();

            btnPlay.Click += (s, e) => ShowPanel(pnlGame);
            btnJournal.Click += (s, e) => ShowPanel(pnlJournal);

            // Кнопка «Магазин» у меню між «Журналом» і «Виходом»
            player.UnlockedLocationIds.Add(1); // Ставок відкритий з початку
            btnShop.Text = "Магазин";
            btnShop.Size = btnJournal.Size;
            btnShop.Location = new Point(btnJournal.Left, btnExit.Top);
            btnShop.UseVisualStyleBackColor = true;

            // Іконки на кнопках меню і монета біля лічильника
            SetButtonIcon(btnJournal, "Fishing.Assets.Icons.icon_journal", 30);
            SetButtonIcon(btnShop, "Fishing.Assets.Icons.icon_shop", 30);
            pbCoin.Image = LoadEmbeddedImage("Fishing.Assets.Icons.icon_coin");
            coinImage = pbCoin.Image;
            if (pbCoin.Image != null)
            {
                loadedImages.Add(pbCoin.Image);
                pbCoin.SizeMode = PictureBoxSizeMode.Zoom;
                pbCoin.Size = new Size(28, 28);
                pbCoin.Location = new Point(lblScore.Left, lblScore.Top + 2);
                lblScore.Left += 34;
                Controls.Add(pbCoin);
            }
            btnShop.Click += (s, e) => { shopMessage = ""; ShowPanel(pnlShop); };
            pnlMenu.Controls.Add(btnShop);
            btnExit.Top = btnShop.Bottom + (btnJournal.Top - btnPlay.Bottom);

            // Панель магазину на місці ігрової
            pnlShop.Location = pnlGame.Location;
            pnlShop.Size = pnlGame.Size;
            pnlShop.BackColor = Color.FromArgb(240, 246, 250);
            pnlShop.Visible = false;
            EnableDoubleBuffering(pnlShop);
            pnlShop.Paint += PnlShop_Paint;
            pnlShop.MouseClick += PnlShop_MouseClick;
            pnlShop.MouseMove += PnlShop_MouseMove;
            pnlShop.MouseLeave += (s, e) => { hoveredShopRect = null; pnlShop.Cursor = Cursors.Default; pnlShop.Invalidate(); };
            Controls.Add(pnlShop);

            // Панель журналу на місці ігрової
            pnlJournal.Location = pnlGame.Location;
            pnlJournal.Size = pnlGame.Size;
            pnlJournal.BackColor = Color.FromArgb(250, 244, 228);
            pnlJournal.Visible = false;
            EnableDoubleBuffering(pnlJournal);
            pnlJournal.Paint += PnlJournal_Paint;
            pnlJournal.MouseClick += PnlJournal_MouseClick;
            Controls.Add(pnlJournal);

            // Кнопка «Меню» вгорі праворуч, видна лише на ігровому екрані
            btnMenu.Text = "Меню (Esc)";
            btnMenu.Size = new Size(110, 34);
            btnMenu.Location = new Point(ClientSize.Width - btnMenu.Width - 12, 8);
            btnMenu.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMenu.TabStop = false;
            btnMenu.Visible = false;
            btnMenu.Click += (s, e) => ReturnToMenu();
            SetButtonIcon(btnMenu, "Fishing.Assets.Icons.icon_menu", 20);
            Controls.Add(btnMenu);
            btnMenu.BringToFront(); // кнопка поверх гри і журналу
            btnExit.Click += (s, e) => Close();

            floatPos = rodTip;
            floatTarget = rodTip;

            tmrCast.Tick += TmrCast_Tick;
            tmrBite.Tick += TmrBite_Tick;
            tmrReaction.Tick += TmrReaction_Tick;
            tmrFight.Tick += TmrFight_Tick;
            tmrMessage.Tick += TmrMessage_Tick;

            // Подвійне буферизування прибирає мерехтіння при малюванні
            EnableDoubleBuffering(pnlGame);

            // Малювання підключено в коді, щоб дизайнер його не загубив
            // Спершу відписка: якщо дизайнер теж підключить Paint, кадр не малюватиметься двічі
            pnlGame.Paint -= pnlGame_Paint;
            pnlGame.Paint += pnlGame_Paint;
            pnlGame.MouseDown += PnlGame_MouseDown;
            pnlGame.MouseUp += PnlGame_MouseUp;

            // Звільняємо таймери і ресурси малювання при закритті вікна
            FormClosed += (s, e) => DisposeGameResources();
        }

        private void DisposeGameResources()
        {
            tmrCast.Dispose();
            tmrBite.Dispose();
            tmrReaction.Dispose();
            tmrFight.Dispose();
            tmrMessage.Dispose();
            rodPen.Dispose();
            linePen.Dispose();
            alertFont.Dispose();
            statusFont.Dispose();
            resultTitleFont.Dispose();
            resultTextFont.Dispose();
            resultHintFont.Dispose();
            shopNameFont.Dispose();
            shopTextFont.Dispose();
            shopButtonFont.Dispose();
            overlayBrush.Dispose();
            centerFormat.Dispose();
            rightFormat.Dispose();
            backgroundCache?.Dispose();
            foreach (Image image in loadedImages)
            {
                image.Dispose();
            }
        }

        private static void EnableDoubleBuffering(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(control, true, null);
        }

        private static Image LoadEmbeddedImage(string resourcePrefix)
        {
            // Шукаємо вбудований ресурс за початком імені, напр. «Fishing.Assets.Fish.fish_03_»
            Assembly assembly = Assembly.GetExecutingAssembly();
            string name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                return null; // графіки немає — гра намалює заглушку
            }
            using (Stream stream = assembly.GetManifestResourceStream(name))
            using (Image image = Image.FromStream(stream))
            {
                // Копія в пам'яті: Image.FromStream вимагає, щоб потік жив увесь час
                return new Bitmap(image);
            }
        }

        private static Image MakeSilhouette(Image source)
        {
            // Той самий спрайт, але суцільно темний: колір замінюємо, прозорість лишаємо
            var silhouette = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0.85f, 0 },
                new float[] { 0.45f, 0.49f, 0.56f, 0, 1 },
            });
            using (var attributes = new ImageAttributes())
            using (Graphics g = Graphics.FromImage(silhouette))
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return silhouette;
        }

        private void LoadGameImages()
        {
            foreach (Fish fish in Catalog.AllFish)
            {
                Image sprite = LoadEmbeddedImage($"Fishing.Assets.Fish.fish_{fish.Id:00}_");
                if (sprite != null)
                {
                    fishSprites[fish.Id] = sprite;
                    fishSilhouettes[fish.Id] = MakeSilhouette(sprite);
                    loadedImages.Add(sprite);
                    loadedImages.Add(fishSilhouettes[fish.Id]);
                }
            }

            string[] backgroundNames = { "lake", "river", "sea" }; // Ставок, Річка, Море — у порядку Id локацій
            for (int i = 0; i < backgroundNames.Length; i++)
            {
                Image background = LoadEmbeddedImage($"Fishing.Assets.Backgrounds.bg_{backgroundNames[i]}");
                if (background != null)
                {
                    backgrounds[i + 1] = background;
                    loadedImages.Add(background);
                }
            }
        }

        private void SetButtonIcon(Button button, string resourcePrefix, int logicalSize)
        {
            Image icon = LoadEmbeddedImage(resourcePrefix);
            if (icon == null)
            {
                return;
            }
            int size = (int)Math.Round(logicalSize * DeviceDpi / 96f);
            var scaled = new Bitmap(icon, new Size(size, size));
            icon.Dispose();
            loadedImages.Add(scaled);
            button.Image = scaled;
            CenterIconAndText(button);
            button.SizeChanged += (s, e) => CenterIconAndText(button); // після масштабування під DPI
        }

        private void CenterIconAndText(Button button)
        {
            // WinForms не центрує пару «іконка + текст», тому ставимо їх ліворуч і рахуємо відступ самі
            if (button.Image == null)
            {
                return;
            }
            int gap = (int)Math.Round(6 * DeviceDpi / 96f);
            Size text = TextRenderer.MeasureText(button.Text, button.Font);
            int content = button.Image.Width + gap + text.Width;

            // Якщо іконка з текстом не влазять (великий масштаб екрана), розширюємо кнопку,
            // інакше текст переноситься і обрізається
            int margin = (int)Math.Round(10 * DeviceDpi / 96f);
            int needed = content + 2 * margin + (button.Width - button.ClientSize.Width);
            if (button.Width < needed)
            {
                bool anchoredRight = (button.Anchor & AnchorStyles.Right) != 0 && (button.Anchor & AnchorStyles.Left) == 0;
                if (anchoredRight)
                {
                    button.Left -= needed - button.Width; // права межа лишається на місці
                }
                button.Width = needed; // SizeChanged викличе цей метод ще раз уже з новою шириною
                return;
            }
            int left = Math.Max(0, (button.ClientSize.Width - content) / 2);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(left, 0, 0, 0);
        }

        private Bitmap GetScaledBackground(Size size)
        {
            // Масштабуємо фон під розмір панелі лише один раз для кожної локації
            if (!backgrounds.TryGetValue(player.CurrentLocationId, out Image source) || size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }
            if (backgroundCache != null && backgroundCacheLocationId == player.CurrentLocationId && backgroundCache.Size == size)
            {
                return backgroundCache;
            }
            backgroundCache?.Dispose();
            backgroundCache = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(backgroundCache))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, new Rectangle(0, 0, size.Width, size.Height));
            }
            backgroundCacheLocationId = player.CurrentLocationId;
            return backgroundCache;
        }

        private void DrawFish(Graphics g, Fish fish, float centerX, float centerY, float width, bool discovered)
        {
            // Спрайт риби (або силует невідкритої); якщо графіки немає — проста заглушка
            Dictionary<int, Image> set = discovered ? fishSprites : fishSilhouettes;
            if (set.TryGetValue(fish.Id, out Image sprite))
            {
                float height = width * sprite.Height / sprite.Width;
                // Спрайти — піксель-арт: масштабуємо без згладжування, щоб пікселі лишались чіткими
                var previousInterpolation = g.InterpolationMode;
                var previousOffset = g.PixelOffsetMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(sprite, new RectangleF(centerX - width / 2, centerY - height / 2, width, height));
                g.InterpolationMode = previousInterpolation;
                g.PixelOffsetMode = previousOffset;
                return;
            }
            DrawFishShape(g, centerX, centerY, width / 120f,
                discovered ? RarityColor(fish.Rarity) : Color.FromArgb(70, 70, 80), discovered);
        }

        private void ShowStatus(string text)
        {
            statusText = text;
            tmrMessage.Stop();
            tmrMessage.Start(); // відлік 3 с починається заново з кожним новим повідомленням
        }

        private void ShowShopMessage(string text)
        {
            shopMessage = text;
            tmrMessage.Stop();
            tmrMessage.Start();
        }

        private void ClearMessages()
        {
            tmrMessage.Stop();
            statusText = "";
            shopMessage = "";
        }

        private void TmrMessage_Tick(object sender, EventArgs e)
        {
            ClearMessages();
            pnlGame.Invalidate();
            pnlShop.Invalidate();
        }

        private void ShowPanel(Panel panel)
        {
            // Повідомлення попереднього екрана не переносяться на новий
            ClearMessages();

            pnlMenu.Visible = false;
            pnlGame.Visible = false;
            pnlJournal.Visible = false;
            pnlShop.Visible = false;
            panel.Visible = true;
            btnMenu.Visible = panel != pnlMenu;
            if (panel == pnlJournal)
            {
                journalLocationId = player.CurrentLocationId; // журнал відкривається на поточній локації
            }
            if (panel == pnlGame)
            {
                pnlGame.BackColor = GameWaterColor(player.CurrentLocationId);
            }
            if (panel == pnlJournal || panel == pnlShop)
            {
                panel.Invalidate();
            }
            ActiveControl = null;
        }

        private void ReturnToMenu()
        {
            ResetGameState();
            ShowPanel(pnlMenu);
        }

        private void ResetGameState()
        {
            // Зупиняємо всі таймери, щоб гра не йшла далі у фоні, поки відкрите меню
            tmrCast.Stop();
            tmrBite.Stop();
            tmrReaction.Stop();
            tmrFight.Stop();

            isCharging = false;
            isFlying = false;
            isReeling = false;
            isBiting = false;
            isFighting = false;
            isHolding = false;
            isShowingResult = false;
            mouseDown = false;
            spaceDown = false;
            statusText = "";
            floatPos = rodTip;
            floatTarget = rodTip;
            pnlGame.Invalidate();
        }

        private void UpdateCoinsLabel()
        {
            lblScore.Text = $"Монети: {player.Money}";
        }

        private void PnlGame_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !mouseDown)
            {
                bool wasDown = AnyInputDown;
                mouseDown = true;
                if (!wasDown)
                {
                    HandlePress();
                }
            }
        }

        private void PnlGame_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                mouseDown = false;
                if (!AnyInputDown)
                {
                    HandleRelease();
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            // Лише для тестування в Debug-збірці: F9 додає 500 монет (у Release цього коду немає)
            if (e.KeyCode == Keys.F9)
            {
                e.Handled = true;
                player.Money += 500;
                UpdateCoinsLabel();
                pnlShop.Invalidate();
                return;
            }
#endif
            if (e.KeyCode == Keys.Escape && (pnlGame.Visible || pnlJournal.Visible || pnlShop.Visible))
            {
                e.Handled = true;
                ReturnToMenu();
            }
            else if (e.KeyCode == Keys.Space)
            {
                // Пробіл керує грою лише на ігровому екрані і без Alt/Ctrl/Shift.
                // У меню він, як звично, натискає кнопку, а Alt+Пробіл відкриває системне меню вікна.
                if (!pnlGame.Visible || e.Modifiers != Keys.None)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;

                // Автоповтор утриманої клавіші не повинен знову запускати дію
                if (!spaceDown)
                {
                    bool wasDown = AnyInputDown;
                    spaceDown = true;
                    if (!wasDown)
                    {
                        HandlePress();
                    }
                }
            }
            else if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.F10)
            {
                // Alt і F10 вмикають режим меню вікна, який з'їдає наступне введення
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && spaceDown)
            {
                e.Handled = true;
                spaceDown = false;
                if (!AnyInputDown)
                {
                    HandleRelease();
                }
            }
            else if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.F10)
            {
                e.Handled = true;
            }
        }

        private void ReleaseAllInput()
        {
            // Вікно втратило фокус: відпускання могло не дійти, тому скидаємо утримання
            spaceDown = false;
            mouseDown = false;
            isHolding = false;
            if (isCharging)
            {
                isCharging = false;
                tmrCast.Stop();
                pnlGame.Invalidate();
            }
        }

        private void HandlePress()
        {
            // Екран результату закривається будь-яким натисканням
            if (isShowingResult)
            {
                isShowingResult = false;
                statusText = "";
                pnlGame.Invalidate();
                return;
            }

            // Запізнілий клік одразу після «Риба втекла» не запускає новий закид
            if (sinceReactionTimeout.IsRunning && sinceReactionTimeout.ElapsedMilliseconds < LatePressIgnoreMs)
            {
                return;
            }

            // Під час міні-гри утримання тримає зону вгорі
            if (isFighting)
            {
                isHolding = true;
                return;
            }

            // Клік у вікні реакції: підсічення і старт міні-гри
            if (isBiting)
            {
                tmrReaction.Stop();
                isBiting = false;
                StartFight();
                pnlGame.Invalidate();
                return;
            }

            // Поки поплавець летить або чекає поклювання, закидати не можна
            if (isFlying || tmrBite.Enabled)
            {
                return;
            }

            // Новий закид під час змотування: поплавець одразу на вудці
            if (isReeling)
            {
                isReeling = false;
                floatPos = rodTip;
            }

            isCharging = true;
            castPower = 0;
            castDirection = 1;
            statusText = "";
            castClock.Restart();
            tmrCast.Start();
        }

        private void HandleRelease()
        {
            if (isFighting)
            {
                isHolding = false;
                return;
            }

            if (!isCharging)
            {
                return;
            }

            isCharging = false;
            StartCast(castPower);
        }

        private void StartCast(float power)
        {
            // Чим сильніше заряд, тим далі летить поплавець
            float distance = CastDistanceFor(power);
            lastCastDistance = distance;
            floatPos = rodTip;
            floatTarget = new PointF(Math.Min(rodTip.X + distance, LogicalWidth - 16), LogicalHeight - 100);
            isFlying = true;
            castClock.Restart();
            tmrCast.Start();
        }

        private void StartBiteWait()
        {
            // Випадковий час очікування від 2 до 5 секунд, наживка скорочує його на 0,5 с за рівень
            int baitBonusMs = (int)(UpgradeValue(UpgradeType.BiteSpeed) * 1000);
            tmrBite.Interval = Math.Max(500, rng.Next(2000, 5001) - baitBonusMs);
            tmrBite.Start();
        }

        private void StartFight()
        {
            isFighting = true;
            // Кнопка, якою підсікли, може бути ще затиснута: тоді вона одразу керує зоною
            isHolding = AnyInputDown;
            playerPos = 50f;
            fightProgress = 20f;
            fightElapsed = 0;
            fishPos = 50f;

            // Рибу обираємо одразу при підсіченні, щоб міні-гра знала, наскільки вона складна
            hookedFish = PickCaughtFish();
            float difficulty = (float)hookedFish.Difficulty; // 0.1 (легко) .. 1.0 (дуже важко)
            // Що складніша риба, то швидше вона плаває по смузі
            fishSpeed = 0.6f + 0.6f * difficulty;           // карась ~0,7, легендарна ~1,2
            fishJumpScale = 1f - 0.5f * difficulty;          // складні частіше міняють напрям

            PickNewFishTarget();
            ShowStatus("Риба на гачку!");
            fightClock.Restart();
            tmrFight.Start();
        }

        private void PickNewFishTarget()
        {
            // Риба обирає нову точку на смузі і плаває до неї.
            // Складна риба робить різкі ривки: нова ціль далеко від поточної позиції.
            float difficulty = hookedFish == null ? 0f : (float)hookedFish.Difficulty;
            float minJump = 35f * difficulty;
            fishTarget = rng.Next(5, 96);
            for (int attempt = 0; attempt < 5 && Math.Abs(fishTarget - fishPos) < minJump; attempt++)
            {
                fishTarget = rng.Next(5, 96);
            }
            fishRetargetMs = rng.Next(800, 2001) * fishJumpScale;
        }

        private void EndFight(bool caught)
        {
            tmrFight.Stop();
            isFighting = false;
            isHolding = false;
            if (caught)
            {
                // За спійману рибу нараховується її ціна
                ShowCatchResult(hookedFish);
                player.Money += lastPrice;
                UpdateCoinsLabel();
            }
            else
            {
                ShowStatus("Риба втекла");
            }
            StartReelIn();
            pnlGame.Invalidate();
        }

        private void StartReelIn()
        {
            // Після вилову чи втечі риби змотуємо волосінь: поплавець повертається до кінчика вудки
            isReeling = true;
            floatTarget = rodTip;
            castClock.Restart();
            tmrCast.Start();
        }

        private bool IsFishInZone()
        {
            return fishPos >= playerPos - ZoneHalfSize && fishPos <= playerPos + ZoneHalfSize;
        }

        private void TmrBite_Tick(object sender, EventArgs e)
        {
            tmrBite.Stop();
            isBiting = true;
            // Базове вікно 1,5 с, «Швидка реакція» додає 0,25 с за рівень
            tmrReaction.Interval = 1500 + (int)(UpgradeValue(UpgradeType.ReactionWindow) * 1000);
            tmrReaction.Start();
            pnlGame.Invalidate();
        }

        private void TmrReaction_Tick(object sender, EventArgs e)
        {
            // Вікно реакції минуло без кліку
            tmrReaction.Stop();
            isBiting = false;
            ShowStatus("Риба втекла");
            sinceReactionTimeout.Restart();
            StartReelIn();
            pnlGame.Invalidate();
        }

        private void TmrFight_Tick(object sender, EventArgs e)
        {
            // k = 1, якщо з минулого тіку пройшло 16 мс; обмежено, щоб після підвисання не було стрибка
            float dt = (float)fightClock.Elapsed.TotalMilliseconds;
            fightClock.Restart();
            float k = Math.Min(dt / 16f, 4f);

            // Зелена зона піднімається під час утримання і опускається, коли кнопку відпущено
            playerPos += (isHolding ? 1.2f : -0.9f) * k;
            playerPos = Math.Max(ZoneHalfSize, Math.Min(100f - ZoneHalfSize, playerPos));

            // Риба плаває випадково: доходить до цілі і обирає нову
            if (fishRetargetMs <= 0 || Math.Abs(fishTarget - fishPos) < 1f)
            {
                PickNewFishTarget();
            }
            fishRetargetMs -= dt;
            fishPos += Math.Sign(fishTarget - fishPos) * fishSpeed * k;
            fishPos = Math.Max(0f, Math.Min(100f, fishPos));

            // Прогрес росте, поки риба в зеленій зоні, і падає поза нею
            // Складну рибу довше виводити: прогрес росте повільніше і падає швидше.
            // Чим довше триває виважування, тим швидше падає шкала поза зоною:
            // через 9 с — удвічі, через 18 с — утричі. Риба зривається лише тоді,
            // коли шкала опустилася до нуля (без окремого таймера).
            float hardness = (float)hookedFish.Difficulty;
            float pressure = 1f + fightElapsed / PressureGrowthMs;
            fightProgress += (IsFishInZone() ? 0.42f - 0.12f * hardness : -(0.25f + 0.1f * hardness) * pressure) * k;
            fightProgress = Math.Max(0f, Math.Min(100f, fightProgress));

            fightElapsed += dt;

            if (fightProgress >= 100f)
            {
                EndFight(true);
            }
            else if (fightProgress <= 0f)
            {
                EndFight(false);
            }
            else
            {
                pnlGame.Invalidate();
            }
        }

        private void TmrCast_Tick(object sender, EventArgs e)
        {
            float dt = (float)castClock.Elapsed.TotalMilliseconds;
            castClock.Restart();
            float k = Math.Min(dt / 16f, 4f);

            if (isCharging)
            {
                // Заряд ходить туди-сюди від 0 до 100
                castPower += castDirection * 3 * k;
                if (castPower >= 100)
                {
                    castPower = 100;
                    castDirection = -1;
                }
                else if (castPower <= 0)
                {
                    castPower = 0;
                    castDirection = 1;
                }
            }
            else if (isFlying)
            {
                // Поплавець летить до цілі
                float dx = floatTarget.X - floatPos.X;
                float dy = floatTarget.Y - floatPos.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);

                float step = 8 * k;
                if (length <= step)
                {
                    floatPos = floatTarget;
                    isFlying = false;
                    tmrCast.Stop();
                    StartBiteWait();
                }
                else
                {
                    floatPos = new PointF(floatPos.X + dx / length * step, floatPos.Y + dy / length * step);
                }
            }
            else if (isReeling)
            {
                // Змотування: поплавець пливе назад до вудки трохи швидше, ніж летів
                float dx = rodTip.X - floatPos.X;
                float dy = rodTip.Y - floatPos.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                float step = 12 * k;
                if (length <= step)
                {
                    floatPos = rodTip;
                    isReeling = false;
                    tmrCast.Stop();
                }
                else
                {
                    floatPos = new PointF(floatPos.X + dx / length * step, floatPos.Y + dy / length * step);
                }
            }
            else
            {
                tmrCast.Stop();
            }

            pnlGame.Invalidate();
        }

        private void pnlGame_Paint(object sender, PaintEventArgs e)
        {
            // Фон локації: уже масштабований під панель, тому малюється швидко, без перерахунку
            Bitmap background = GetScaledBackground(pnlGame.ClientSize);
            if (background != null)
            {
                e.Graphics.DrawImageUnscaled(background, 0, 0);
            }

            // Масштаб під DPI і згладжування ліній та тексту
            e.Graphics.ScaleTransform(DpiScale, DpiScale);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Буйки дальності і точка падіння під час заряду
            DrawDepthMarkers(e.Graphics);

            // Місток, вудка і волосінь
            DrawPierAndRod(e.Graphics);
            e.Graphics.DrawLine(linePen, rodTip, floatPos);

            // Поплавець
            e.Graphics.FillEllipse(Brushes.Red, floatPos.X - 6, floatPos.Y - 6, 12, 12);
            e.Graphics.FillEllipse(Brushes.White, floatPos.X - 3, floatPos.Y - 3, 6, 6);

            // Знак поклювання над поплавцем
            if (isBiting)
            {
                e.Graphics.DrawString("!", alertFont, Brushes.Red, floatPos.X - 8, floatPos.Y - 55);
            }

            // Шкала сили закидання
            if (isCharging)
            {
                e.Graphics.DrawRectangle(Pens.Black, 20, LogicalHeight - 40, 200, 16);
                e.Graphics.FillRectangle(Brushes.OrangeRed, 21, LogicalHeight - 39, castPower * 2 - 1 < 0 ? 0 : castPower * 2 - 1, 14);
            }

            // Міні-гра: смуга з зеленою зоною, рибою і шкала прогресу поруч
            if (isFighting)
            {
                DrawFightBar(e.Graphics);
            }

            // Повідомлення про результат
            if (!string.IsNullOrEmpty(statusText))
            {
                e.Graphics.DrawString(statusText, statusFont, Brushes.Black, 20, 20);
            }

            // Назва поточної локації у правому верхньому куті поля
            e.Graphics.DrawString(Catalog.GetLocation(player.CurrentLocationId).Name, shopNameFont, Brushes.DarkSlateGray,
                new RectangleF(LogicalWidth - 200, 8, 190, 24), rightFormat);

            // Екран результату поверх гри
            if (isShowingResult && lastFish != null)
            {
                DrawResultCard(e.Graphics);
            }
        }

        // Наживка для вибору риби (модель бекенду Bait). Поки в магазині наживок немає, тому null —
        // FishPicker тоді бере базові шанси рідкостей.
        private Bait currentBait = null;

        // Дальність останнього закиду в логічних пікселях від кінчика вудки
        private float lastCastDistance;

        private static float RequiredCastDistance(Rarity rarity)
        {
            // Далі — глибше, а глибше водиться рідкісніша риба.
            // Максимальний закид без прокачки — 550, тож легендарна потребує майже повного заряду,
            // а «Довга волосінь» (+50 за рівень) робить її досяжнішою.
            switch (rarity)
            {
                case Rarity.Common: return 0;
                case Rarity.Uncommon: return 250;
                case Rarity.Rare: return 350;
                case Rarity.Epic: return 450;
                default: return 540;
            }
        }

        private static float MaxCastDistance(Rarity rarity)
        {
            // Найдешевша риба тримається біля берега: далі 400 звичайна вже не клює
            return rarity == Rarity.Common ? 400 : float.MaxValue;
        }

        private static bool IsReachable(Fish fish, float distance)
        {
            return distance >= RequiredCastDistance(fish.Rarity) && distance < MaxCastDistance(fish.Rarity);
        }

        private Fish PickCaughtFish()
        {
            // Вибір риби — логіка бекенду (картка 21): поточна локація гравця, рідкість і наживка.
            // Дальність закиду лише відкриває рибу: досяжна риба клює з тим самим базовим шансом,
            // що й з самого початку, без жодного бонусу за відстань.
            Location location = Catalog.GetLocation(player.CurrentLocationId);
            var reachable = new Location { Id = location.Id, Name = location.Name, Price = location.Price };
            reachable.Fishes.AddRange(location.Fishes.Where(f => IsReachable(f, lastCastDistance)));
            if (reachable.Fishes.Count == 0)
            {
                reachable.Fishes.Add(location.Fishes[0]);
            }
            return FishPicker.Pick(reachable, currentBait, rng) ?? location.Fishes[0];
        }

        private float CastDistanceFor(float power)
        {
            // Чим сильніше заряд, тим далі летить поплавець; «Довга волосінь» додає дальність
            return 150 + power * 4 + (float)UpgradeValue(UpgradeType.CastRange);
        }

        private void DrawPierAndRod(Graphics g)
        {
            float height = LogicalHeight;
            float deckY = height - 138;

            // Дерев'яний місток: дві палі у воді і настил з дощок
            using (var post = new SolidBrush(Color.FromArgb(105, 70, 40)))
            using (var deck = new SolidBrush(Color.FromArgb(160, 110, 60)))
            using (var plankPen = new Pen(Color.FromArgb(120, 80, 45), 1))
            {
                g.FillRectangle(post, 8, deckY + 8, 7, height - deckY);
                g.FillRectangle(post, 50, deckY + 8, 7, height - deckY);
                g.FillRectangle(deck, 0, deckY, 70, 11);
                for (int x = 14; x < 70; x += 14)
                {
                    g.DrawLine(plankPen, x, deckY, x, deckY + 11);
                }
                g.DrawRectangle(plankPen, 0, deckY, 70, 11);
            }

            // Вудлище: від ручки на містку до кінчика, тоншає до кінця
            var handle = new PointF(30, deckY - 4);
            float dx = rodTip.X - handle.X, dy = rodTip.Y - handle.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / length, ny = dx / length; // перпендикуляр до вудлища
            var rod = new[]
            {
                new PointF(handle.X + nx * 3f, handle.Y + ny * 3f),
                new PointF(rodTip.X + nx * 0.8f, rodTip.Y + ny * 0.8f),
                new PointF(rodTip.X - nx * 0.8f, rodTip.Y - ny * 0.8f),
                new PointF(handle.X - nx * 3f, handle.Y - ny * 3f),
            };
            using (var rodBrush = new SolidBrush(Color.FromArgb(120, 75, 35)))
            {
                g.FillPolygon(rodBrush, rod);
            }

            // Коркова ручка і котушка
            float ux = dx / length, uy = dy / length;
            using (var cork = new Pen(Color.FromArgb(205, 165, 110), 6.5f))
            {
                g.DrawLine(cork, handle, new PointF(handle.X + ux * 22, handle.Y + uy * 22));
            }
            var reel = new PointF(handle.X + ux * 30 + nx * 6, handle.Y + uy * 30 + ny * 6);
            g.FillEllipse(Brushes.DimGray, reel.X - 5, reel.Y - 5, 10, 10);
            g.FillEllipse(Brushes.Silver, reel.X - 2, reel.Y - 2, 4, 4);
        }

        private void DrawDepthMarkers(Graphics g)
        {
            // Буйки на лінії падіння поплавця: з цієї відстані починає траплятися риба такої рідкості
            float waterY = LogicalHeight - 100;
            Location location = Catalog.GetLocation(player.CurrentLocationId);
            foreach (Rarity rarity in location.Fishes.Select(f => f.Rarity).Distinct())
            {
                float required = RequiredCastDistance(rarity);
                if (required <= 0)
                {
                    continue;
                }
                float x = rodTip.X + required;
                using (var buoy = new SolidBrush(Color.FromArgb(200, RarityColor(rarity))))
                {
                    g.FillEllipse(buoy, x - 5, waterY - 5, 10, 10);
                }
                g.DrawEllipse(Pens.White, x - 5, waterY - 5, 10, 10);
                g.DrawLine(Pens.White, x, waterY - 5, x, waterY - 14);
            }

            // Сірий буйок: далі цієї межі найдешевша (звичайна) риба вже не клює
            if (location.Fishes.Any(f => f.Rarity == Rarity.Common))
            {
                float x = rodTip.X + MaxCastDistance(Rarity.Common);
                g.FillEllipse(Brushes.Gray, x - 5, waterY - 5, 10, 10);
                g.DrawEllipse(Pens.White, x - 5, waterY - 5, 10, 10);
                g.DrawLine(Pens.White, x - 4, waterY - 4, x + 4, waterY + 4);
                g.DrawLine(Pens.White, x - 4, waterY + 4, x + 4, waterY - 4);
            }

            // Під час заряду: куди впаде поплавець
            if (isCharging)
            {
                float landX = rodTip.X + CastDistanceFor(castPower);
                g.FillPolygon(Brushes.OrangeRed, new[]
                {
                    new PointF(landX, waterY + 8),
                    new PointF(landX - 7, waterY + 20),
                    new PointF(landX + 7, waterY + 20),
                });
            }
        }

        private static string FormatWeight(double kg)
        {
            // Хамса важить грами, мегалодон — тонни: точність залежить від ваги
            if (kg < 1) return $"{kg:0.000} кг";
            if (kg < 100) return $"{kg:0.0} кг";
            return $"{kg:0} кг";
        }

        private void ShowCatchResult(Fish fish)
        {
            lastFish = fish;
            lastWeight = fish.MinWeight + rng.NextDouble() * (fish.MaxWeight - fish.MinWeight);
            // Ціна залежить від ваги: найменша риба ×0,75, рекордна ×1,25, у середньому базова ціна
            double weightShare = fish.MaxWeight > fish.MinWeight
                ? (lastWeight - fish.MinWeight) / (fish.MaxWeight - fish.MinWeight)
                : 0.5;
            double sellBonus = 1 + UpgradeValue(UpgradeType.SellBonus); // «Торговець»: +10% за рівень
            lastPrice = (int)Math.Round(fish.BasePrice * (0.75 + 0.5 * weightShare) * sellBonus);
            lastIsNewSpecies = discoveredFishIds.Add(fish.Id); // true, якщо цей вид ще не ловили

            // Облік для журналу
            catchCounts[fish.Id] = (catchCounts.TryGetValue(fish.Id, out int count) ? count : 0) + 1;
            if (!bestWeights.TryGetValue(fish.Id, out double best) || lastWeight > best)
            {
                bestWeights[fish.Id] = lastWeight;
            }
            statusText = "";
            isShowingResult = true;
        }

        private static string RarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return "Звичайна";
                case Rarity.Uncommon: return "Незвичайна";
                case Rarity.Rare: return "Рідкісна";
                case Rarity.Epic: return "Епічна";
                default: return "Легендарна";
            }
        }

        private static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return Color.DimGray;
                case Rarity.Uncommon: return Color.ForestGreen;
                case Rarity.Rare: return Color.RoyalBlue;
                case Rarity.Epic: return Color.MediumOrchid;
                default: return Color.DarkOrange;
            }
        }

        private static string CoinsWord(int amount)
        {
            // 1 монета, 2–4 монети, 5+ монет (з урахуванням 11–14)
            int lastTwo = amount % 100;
            int last = amount % 10;
            if (lastTwo >= 11 && lastTwo <= 14) return "монет";
            if (last == 1) return "монета";
            if (last >= 2 && last <= 4) return "монети";
            return "монет";
        }

        private void DrawResultCard(Graphics g)
        {
            float width = LogicalWidth;
            float height = LogicalHeight;

            // Затемнення ігрового поля
            g.FillRectangle(overlayBrush, 0, 0, width, height);

            // Картка по центру
            const float cardWidth = 380;
            const float cardHeight = 250;
            float cardLeft = (width - cardWidth) / 2;
            float cardTop = (height - cardHeight) / 2;
            g.FillRectangle(Brushes.White, cardLeft, cardTop, cardWidth, cardHeight);
            g.DrawRectangle(Pens.Black, cardLeft, cardTop, cardWidth, cardHeight);

            Color rarityColor = RarityColor(lastFish.Rarity);

            // Заголовок
            g.DrawString("Зловлено!", resultTitleFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 10, cardWidth, 36), centerFormat);

            // Спрайт спійманої риби
            float fishX = cardLeft + cardWidth / 2;
            float fishY = cardTop + 70;
            DrawFish(g, lastFish, fishX, fishY, 116, true);

            // Назва, рідкість, вага, ціна
            using (var rarityBrush = new SolidBrush(rarityColor))
            {
                g.DrawString(lastFish.Name, resultTitleFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 98, cardWidth, 34), centerFormat);
                g.DrawString(RarityName(lastFish.Rarity), resultTextFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 130, cardWidth, 24), centerFormat);
            }
            g.DrawString($"Вага: {FormatWeight(lastWeight)}", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 156, cardWidth, 24), centerFormat);
            g.DrawString($"Ціна: {lastPrice} {CoinsWord(lastPrice)}", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 182, cardWidth, 24), centerFormat);

            // Позначка нового виду в правому верхньому куті картки
            if (lastIsNewSpecies)
            {
                var badge = new RectangleF(cardLeft + cardWidth - 128, cardTop - 14, 140, 30);
                g.FillRectangle(Brushes.Gold, badge);
                g.DrawRectangle(Pens.DarkGoldenrod, badge.X, badge.Y, badge.Width, badge.Height);
                g.DrawString("Новий вид!", statusFont, Brushes.Black, badge, centerFormat);
            }

            // Підказка
            g.DrawString("ЛКМ або пробіл — продовжити", resultHintFont, Brushes.DimGray,
                new RectangleF(cardLeft, cardTop + cardHeight - 30, cardWidth, 22), centerFormat);
        }

        private static void DrawFishShape(Graphics g, float centerX, float centerY, float scale, Color color, bool withEye)
        {
            // Запасна риба-заглушка, якщо спрайт не завантажився
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, centerX - 35 * scale, centerY - 15 * scale, 60 * scale, 30 * scale);
                g.FillPolygon(brush, new[]
                {
                    new PointF(centerX + 20 * scale, centerY),
                    new PointF(centerX + 42 * scale, centerY - 14 * scale),
                    new PointF(centerX + 42 * scale, centerY + 14 * scale),
                });
            }
            if (withEye)
            {
                g.FillEllipse(Brushes.White, centerX - 25 * scale, centerY - 6 * scale, 7 * scale, 7 * scale);
            }
        }

        private void PnlJournal_MouseClick(object sender, MouseEventArgs e)
        {
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            foreach (var tab in journalTabs)
            {
                if (tab.Rect.Contains(point))
                {
                    journalLocationId = tab.LocationId;
                    pnlJournal.Invalidate();
                    return;
                }
            }
        }

        private void PnlJournal_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float width = pnlJournal.ClientSize.Width / DpiScale;

            // Заголовок і лічильник відкритих видів з усього каталогу
            g.DrawString("Журнал риб", resultTitleFont, Brushes.Black,
                new RectangleF(0, 6, width, 34), centerFormat);
            g.DrawString($"Відкрито видів: {discoveredFishIds.Count} з {Catalog.AllFish.Count()}", resultTextFont, Brushes.DimGray,
                new RectangleF(0, 36, width, 24), centerFormat);

            // Вкладки локацій
            journalTabs.Clear();
            const float tabWidth = 170, tabHeight = 32, tabGap = 10, tabTop = 66;
            float tabsLeft = (width - (Catalog.Locations.Count * tabWidth + (Catalog.Locations.Count - 1) * tabGap)) / 2;
            for (int i = 0; i < Catalog.Locations.Count; i++)
            {
                Location location = Catalog.Locations[i];
                var rect = new RectangleF(tabsLeft + i * (tabWidth + tabGap), tabTop, tabWidth, tabHeight);
                bool active = location.Id == journalLocationId;
                bool unlocked = player.UnlockedLocationIds.Contains(location.Id);
                int found = location.Fishes.Count(f => discoveredFishIds.Contains(f.Id));

                using (var tabBrush = new SolidBrush(active ? LocationColor(location.Id) : Color.FromArgb(225, 225, 225)))
                {
                    g.FillRectangle(tabBrush, rect);
                }
                g.DrawRectangle(Pens.DarkGray, rect.X, rect.Y, rect.Width, rect.Height);
                string caption = unlocked ? $"{location.Name}  {found}/{location.Fishes.Count}" : $"{location.Name}  (закрито)";
                g.DrawString(caption, shopButtonFont, active ? Brushes.White : Brushes.DimGray, rect, centerFormat);
                journalTabs.Add((rect, location.Id));
            }

            // 5 карток видів обраної локації
            Location shown = Catalog.GetLocation(journalLocationId);
            const float cardWidth = 140, cardHeight = 196, gap = 12, cardsTop = 112;
            float cardsLeft = (width - (shown.Fishes.Count * cardWidth + (shown.Fishes.Count - 1) * gap)) / 2;
            for (int i = 0; i < shown.Fishes.Count; i++)
            {
                DrawJournalCard(g, shown.Fishes[i], new RectangleF(cardsLeft + i * (cardWidth + gap), cardsTop, cardWidth, cardHeight));
            }

            // Підказка для закритої локації
            if (!player.UnlockedLocationIds.Contains(shown.Id))
            {
                g.DrawString($"Локацію «{shown.Name}» можна відкрити в магазині за {shown.Price} {CoinsWord(shown.Price)}",
                    resultHintFont, Brushes.DimGray, new RectangleF(0, cardsTop + cardHeight + 10, width, 22), centerFormat);
            }
        }

        private void DrawJournalCard(Graphics g, Fish fish, RectangleF card)
        {
            bool discovered = discoveredFishIds.Contains(fish.Id);

            using (var cardBrush = new SolidBrush(discovered ? Color.White : Color.FromArgb(232, 235, 240)))
            using (var path = RoundedRect(card, 10))
            {
                g.FillPath(cardBrush, path);
                g.DrawPath(Pens.Gainsboro, path);
            }

            float centerX = card.X + card.Width / 2;
            float fishY = card.Y + 44;

            if (!discovered)
            {
                // Невідкритий вид: темний силует і знаки питання
                DrawFish(g, fish, centerX, fishY, 120, false);
                // Значок «?» на силуеті
                var badge = new RectangleF(centerX + 30, fishY - 30, 22, 22);
                using (var badgeBrush = new SolidBrush(Color.FromArgb(120, 130, 145)))
                {
                    g.FillEllipse(badgeBrush, badge);
                }
                g.DrawString("?", shopButtonFont, Brushes.White, badge, centerFormat);
                g.DrawString("???", resultTitleFont, Brushes.DimGray,
                    new RectangleF(card.X, card.Y + 78, card.Width, 34), centerFormat);
                g.DrawString("Ще не спіймано", resultHintFont, Brushes.Gray,
                    new RectangleF(card.X, card.Y + 116, card.Width, 22), centerFormat);
                return;
            }

            Color rarityColor = RarityColor(fish.Rarity);
            DrawFish(g, fish, centerX, fishY, 120, true);

            using (var rarityBrush = new SolidBrush(rarityColor))
            {
                g.DrawString(fish.Name, shopNameFont, rarityBrush,
                    new RectangleF(card.X + 2, card.Y + 78, card.Width - 4, 26), centerFormat);
                g.DrawString(RarityName(fish.Rarity), resultHintFont, rarityBrush,
                    new RectangleF(card.X, card.Y + 104, card.Width, 20), centerFormat);
            }

            int count = catchCounts.TryGetValue(fish.Id, out int c) ? c : 0;
            double best = bestWeights.TryGetValue(fish.Id, out double w) ? w : 0;
            g.DrawString($"Спіймано: {count}", shopTextFont, Brushes.Black,
                new RectangleF(card.X, card.Y + 132, card.Width, 20), centerFormat);
            g.DrawString($"Рекорд: {FormatWeight(best)}", shopTextFont, Brushes.Black,
                new RectangleF(card.X, card.Y + 152, card.Width, 20), centerFormat);
            g.DrawString($"Ціна: від {fish.BasePrice}", shopTextFont, Brushes.DimGray,
                new RectangleF(card.X, card.Y + 172, card.Width, 20), centerFormat);
        }

        private static int UpgradePrice(Upgrade upgrade, int currentLevel)
        {
            // Кожен наступний рівень удвічі дорожчий: 100, 200, 400
            return upgrade.BasePrice * (1 << currentLevel);
        }

        private int UpgradeLevel(Upgrade upgrade)
        {
            return player.UpgradeLevels.TryGetValue(upgrade.Id, out int level) ? level : 0;
        }

        private double UpgradeValue(UpgradeType type)
        {
            // Ефект = куплений рівень × приріст за рівень; 0, якщо покращення не куплено
            Upgrade upgrade = TempUpgrades.FirstOrDefault(u => u.Type == type);
            return upgrade == null ? 0 : UpgradeLevel(upgrade) * upgrade.ValuePerLevel;
        }

        private void BuyLocation(Location location)
        {
            if (player.Money < location.Price)
            {
                ShowShopMessage("Не вистачає монет");
                return;
            }
            player.Money -= location.Price;
            player.UnlockedLocationIds.Add(location.Id);
            player.CurrentLocationId = location.Id;
            ShowShopMessage($"Відкрито локацію «{location.Name}»");
            UpdateCoinsLabel();
        }

        private void SelectLocation(Location location)
        {
            player.CurrentLocationId = location.Id;
            ShowShopMessage($"Поточна локація: {location.Name}");
        }

        private void BuyUpgrade(Upgrade upgrade)
        {
            int level = UpgradeLevel(upgrade);
            int price = UpgradePrice(upgrade, level);
            if (level >= upgrade.MaxLevel || player.Money < price)
            {
                ShowShopMessage(level >= upgrade.MaxLevel ? "Уже максимальний рівень" : "Не вистачає монет");
                return;
            }
            player.Money -= price;
            player.UpgradeLevels[upgrade.Id] = level + 1;
            ShowShopMessage($"Куплено: {upgrade.Name}, рівень {level + 1}");
            UpdateCoinsLabel();
        }

        private void PnlShop_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // Клік у пікселях екрана переводимо в логічні пікселі, в яких намальовано кнопки
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            foreach (var button in shopButtons)
            {
                if (button.Rect.Contains(point))
                {
                    if (button.Enabled)
                    {
                        button.OnClick();
                    }
                    else
                    {
                        ShowShopMessage("Не вистачає монет");
                    }
                    pnlShop.Invalidate();
                    return;
                }
            }
        }

        private static Color GameWaterColor(int locationId)
        {
            // Запасний колір води, якщо фон локації не завантажився
            switch (locationId)
            {
                case 1: return Color.LightBlue;
                case 2: return Color.FromArgb(150, 205, 215);
                default: return Color.FromArgb(120, 170, 225);
            }
        }

        private static Color LocationColor(int locationId)
        {
            // Кольори локацій: вкладки журналу і запасна смужка в магазині
            switch (locationId)
            {
                case 1: return Color.FromArgb(90, 165, 120);
                case 2: return Color.FromArgb(60, 150, 180);
                default: return Color.FromArgb(40, 90, 170);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawCard(Graphics g, RectangleF card, Color border)
        {
            // Картка: м'яка тінь, біле тло, заокруглені кути
            using (var shadowPath = RoundedRect(new RectangleF(card.X + 2, card.Y + 3, card.Width, card.Height), 12))
            using (var shadow = new SolidBrush(Color.FromArgb(28, 20, 40, 70)))
            {
                g.FillPath(shadow, shadowPath);
            }
            using (var path = RoundedRect(card, 12))
            using (var borderPen = new Pen(border, 1.5f))
            {
                g.FillPath(Brushes.White, path);
                g.DrawPath(borderPen, path);
            }
        }

        private enum ShopButtonStyle { Buy, Select, Disabled, Current, Maxed }

        private void DrawShopButton(Graphics g, RectangleF rect, string text, ShopButtonStyle style, bool withCoin)
        {
            Color back, fore;
            switch (style)
            {
                case ShopButtonStyle.Buy: back = Color.FromArgb(240, 150, 40); fore = Color.White; break;
                case ShopButtonStyle.Select: back = Color.FromArgb(70, 160, 110); fore = Color.White; break;
                case ShopButtonStyle.Current: back = Color.FromArgb(225, 244, 232); fore = Color.FromArgb(40, 130, 80); break;
                case ShopButtonStyle.Maxed: back = Color.FromArgb(255, 241, 200); fore = Color.FromArgb(170, 120, 10); break;
                default: back = Color.FromArgb(236, 238, 242); fore = Color.FromArgb(150, 155, 165); break;
            }
            // Підсвітка при наведенні на активну кнопку
            bool active = style == ShopButtonStyle.Buy || style == ShopButtonStyle.Select;
            if (active && hoveredShopRect.HasValue && hoveredShopRect.Value == rect)
            {
                back = ControlPaint.Light(back, 0.25f);
            }

            using (var path = RoundedRect(rect, 8))
            using (var backBrush = new SolidBrush(back))
            {
                g.FillPath(backBrush, path);
            }

            // Монетка перед ціною або галочка для поточної локації
            float iconSize = 16;
            SizeF textSize = g.MeasureString(text, shopButtonFont);
            bool hasIcon = (withCoin && coinImage != null) || style == ShopButtonStyle.Current;
            float contentWidth = textSize.Width + (hasIcon ? iconSize + 4 : 0);
            float x = rect.X + (rect.Width - contentWidth) / 2;
            float midY = rect.Y + rect.Height / 2;
            if (withCoin && coinImage != null)
            {
                g.DrawImage(coinImage, x, midY - iconSize / 2, iconSize, iconSize);
                x += iconSize + 4;
            }
            else if (style == ShopButtonStyle.Current)
            {
                using (var check = new Pen(fore, 2.5f))
                {
                    g.DrawLines(check, new[] { new PointF(x + 2, midY), new PointF(x + 6, midY + 4), new PointF(x + 14, midY - 5) });
                }
                x += iconSize + 4;
            }
            using (var foreBrush = new SolidBrush(fore))
            {
                g.DrawString(text, shopButtonFont, foreBrush, x, midY - textSize.Height / 2);
            }
        }

        private void PnlShop_MouseMove(object sender, MouseEventArgs e)
        {
            // Підсвічуємо кнопку під курсором
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            RectangleF? hovered = null;
            foreach (var button in shopButtons)
            {
                if (button.Enabled && button.Rect.Contains(point))
                {
                    hovered = button.Rect;
                    break;
                }
            }
            if (hovered != hoveredShopRect)
            {
                hoveredShopRect = hovered;
                pnlShop.Cursor = hovered.HasValue ? Cursors.Hand : Cursors.Default;
                pnlShop.Invalidate();
            }
        }

        private void PnlShop_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float width = pnlShop.ClientSize.Width / DpiScale;
            float height = pnlShop.ClientSize.Height / DpiScale;
            shopButtons.Clear();

            // М'який градієнт: від блакитного вгорі до світлого внизу
            using (var background = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(0, 0, width, height), Color.FromArgb(214, 234, 248), Color.FromArgb(246, 250, 253), 90f))
            {
                g.FillRectangle(background, 0, 0, width, height);
            }

            g.DrawString("Магазин", resultTitleFont, Brushes.Black, new RectangleF(0, 2, width, 34), centerFormat);
            if (!string.IsNullOrEmpty(shopMessage))
            {
                g.DrawString(shopMessage, shopTextFont, Brushes.DimGray, new RectangleF(0, 32, width, 18), centerFormat);
            }

            // --- Локації: 3 картки в ряд
            DrawSectionTitle(g, "Локації", 26, 46);
            const float locWidth = 240, locHeight = 122, locGap = 15, locTop = 68;
            float locLeft = (width - (3 * locWidth + 2 * locGap)) / 2;
            for (int i = 0; i < Catalog.Locations.Count; i++)
            {
                DrawLocationCard(g, Catalog.Locations[i], new RectangleF(locLeft + i * (locWidth + locGap), locTop, locWidth, locHeight));
            }

            // --- Покращення: 5 карток у ряд
            DrawSectionTitle(g, "Покращення", 26, 198);
            const float upWidth = 140, upHeight = 172, upGap = 12, upTop = 220;
            float upLeft = (width - (5 * upWidth + 4 * upGap)) / 2;
            for (int i = 0; i < TempUpgrades.Count; i++)
            {
                DrawUpgradeCard(g, TempUpgrades[i], new RectangleF(upLeft + i * (upWidth + upGap), upTop, upWidth, upHeight));
            }
        }

        private void DrawSectionTitle(Graphics g, string text, float x, float y)
        {
            using (var accent = new SolidBrush(Color.FromArgb(240, 150, 40)))
            {
                g.FillRectangle(accent, x, y + 3, 4, 16);
            }
            g.DrawString(text, shopNameFont, Brushes.Black, x + 9, y);
        }

        private void DrawLocationCard(Graphics g, Location location, RectangleF card)
        {
            bool unlocked = player.UnlockedLocationIds.Contains(location.Id);
            bool current = player.CurrentLocationId == location.Id;

            DrawCard(g, card, current ? Color.FromArgb(240, 150, 40) : Color.FromArgb(214, 220, 228));

            // Верх картки — фон локації (обрізаний за заокругленими кутами)
            var band = new RectangleF(card.X, card.Y, card.Width, 54);
            using (var clip = RoundedRect(card, 12))
            {
                var previousClip = g.Clip;
                g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                if (backgrounds.TryGetValue(location.Id, out Image thumbnail))
                {
                    float sourceHeight = thumbnail.Width * band.Height / band.Width;
                    var source = new RectangleF(0, thumbnail.Height * 0.36f - sourceHeight / 2, thumbnail.Width, sourceHeight);
                    g.DrawImage(thumbnail, band, source, GraphicsUnit.Pixel);
                }
                else
                {
                    using (var bandBrush = new SolidBrush(LocationColor(location.Id)))
                    {
                        g.FillRectangle(bandBrush, band);
                    }
                }
                // Затемнення знизу смужки, щоб назва читалась на будь-якому фоні
                using (var shade = new System.Drawing.Drawing2D.LinearGradientBrush(
                    band, Color.FromArgb(10, 0, 0, 0), Color.FromArgb(130, 0, 0, 0), 90f))
                {
                    g.FillRectangle(shade, band);
                }
                g.Clip = previousClip;
            }
            g.DrawString(location.Name, shopNameFont, Brushes.White, new RectangleF(card.X, card.Y + 26, card.Width, 26), centerFormat);

            string info = unlocked ? $"Видів риби: {location.Fishes.Count}" : $"Закрито · видів риби: {location.Fishes.Count}";
            g.DrawString(info, shopTextFont, Brushes.DimGray, new RectangleF(card.X, card.Y + 58, card.Width, 20), centerFormat);

            var buttonRect = new RectangleF(card.X + 18, card.Bottom - 40, card.Width - 36, 30);
            if (current)
            {
                DrawShopButton(g, buttonRect, "Поточна", ShopButtonStyle.Current, false);
            }
            else if (unlocked)
            {
                DrawShopButton(g, buttonRect, "Обрати", ShopButtonStyle.Select, false);
                shopButtons.Add((buttonRect, true, () => SelectLocation(location)));
            }
            else if (player.Money >= location.Price)
            {
                DrawShopButton(g, buttonRect, $"Купити за {location.Price}", ShopButtonStyle.Buy, true);
                shopButtons.Add((buttonRect, true, () => BuyLocation(location)));
            }
            else
            {
                DrawShopButton(g, buttonRect, $"Бракує {location.Price - player.Money}", ShopButtonStyle.Disabled, true);
                shopButtons.Add((buttonRect, false, () => BuyLocation(location)));
            }
        }

        private static Color UpgradeColor(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.CatchBar: return Color.FromArgb(70, 160, 110);
                case UpgradeType.CastRange: return Color.FromArgb(70, 130, 200);
                case UpgradeType.BiteSpeed: return Color.FromArgb(200, 110, 140);
                case UpgradeType.ReactionWindow: return Color.FromArgb(140, 110, 200);
                default: return Color.FromArgb(225, 160, 40);
            }
        }

        private static void DrawUpgradeIcon(Graphics g, UpgradeType type, PointF c)
        {
            // Прості білі значки на кольоровому кружку
            using (var pen = new Pen(Color.White, 2.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                switch (type)
                {
                    case UpgradeType.CatchBar: // вудка з котушкою
                        g.DrawLine(pen, c.X - 9, c.Y + 9, c.X + 9, c.Y - 9);
                        g.DrawEllipse(pen, c.X - 7, c.Y + 1, 6, 6);
                        g.DrawLine(pen, c.X + 9, c.Y - 9, c.X + 9, c.Y + 3);
                        break;
                    case UpgradeType.CastRange: // котушка волосіні і стрілка вдаль
                        g.DrawEllipse(pen, c.X - 10, c.Y - 6, 12, 12);
                        g.DrawLine(pen, c.X + 2, c.Y, c.X + 10, c.Y);
                        g.DrawLines(pen, new[] { new PointF(c.X + 6, c.Y - 4), new PointF(c.X + 10, c.Y), new PointF(c.X + 6, c.Y + 4) });
                        break;
                    case UpgradeType.BiteSpeed: // хробак
                        g.DrawBezier(pen, c.X - 10, c.Y + 2, c.X - 5, c.Y - 8, c.X, c.Y + 10, c.X + 5, c.Y);
                        g.DrawLine(pen, c.X + 5, c.Y, c.X + 9, c.Y - 4);
                        break;
                    case UpgradeType.ReactionWindow: // секундомір
                        g.DrawEllipse(pen, c.X - 8, c.Y - 6, 16, 16);
                        g.DrawLine(pen, c.X, c.Y + 2, c.X, c.Y - 3);
                        g.DrawLine(pen, c.X, c.Y + 2, c.X + 4, c.Y + 4);
                        g.DrawLine(pen, c.X - 2, c.Y - 10, c.X + 2, c.Y - 10);
                        break;
                    default: // монета з плюсом
                        g.DrawEllipse(pen, c.X - 9, c.Y - 9, 18, 18);
                        g.DrawLine(pen, c.X - 4, c.Y, c.X + 4, c.Y);
                        g.DrawLine(pen, c.X, c.Y - 4, c.X, c.Y + 4);
                        break;
                }
            }
        }

        private void DrawUpgradeCard(Graphics g, Upgrade upgrade, RectangleF card)
        {
            int level = UpgradeLevel(upgrade);
            bool maxed = level >= upgrade.MaxLevel;
            int price = UpgradePrice(upgrade, level);
            Color color = UpgradeColor(upgrade.Type);

            DrawCard(g, card, Color.FromArgb(214, 220, 228));

            // Значок покращення
            var iconCenter = new PointF(card.X + card.Width / 2, card.Y + 30);
            using (var badge = new SolidBrush(color))
            {
                g.FillEllipse(badge, iconCenter.X - 20, iconCenter.Y - 20, 40, 40);
            }
            DrawUpgradeIcon(g, upgrade.Type, iconCenter);

            g.DrawString(upgrade.Name, shopButtonFont, Brushes.Black, new RectangleF(card.X + 4, card.Y + 54, card.Width - 8, 24), centerFormat);
            g.DrawString(upgrade.Description, shopTextFont, Brushes.DimGray, new RectangleF(card.X + 6, card.Y + 78, card.Width - 12, 34), centerFormat);

            // Рівень сегментами: заповнені — куплені
            const float segmentWidth = 30, segmentGap = 5;
            float segmentsLeft = card.X + (card.Width - (upgrade.MaxLevel * segmentWidth + (upgrade.MaxLevel - 1) * segmentGap)) / 2;
            using (var filled = new SolidBrush(color))
            using (var empty = new SolidBrush(Color.FromArgb(226, 230, 236)))
            {
                for (int i = 0; i < upgrade.MaxLevel; i++)
                {
                    var segment = new RectangleF(segmentsLeft + i * (segmentWidth + segmentGap), card.Y + 118, segmentWidth, 6);
                    using (var path = RoundedRect(segment, 3))
                    {
                        g.FillPath(i < level ? filled : empty, path);
                    }
                }
            }

            var buttonRect = new RectangleF(card.X + 10, card.Bottom - 40, card.Width - 20, 30);
            if (maxed)
            {
                DrawShopButton(g, buttonRect, "Максимум", ShopButtonStyle.Maxed, false);
            }
            else if (player.Money >= price)
            {
                DrawShopButton(g, buttonRect, price.ToString(), ShopButtonStyle.Buy, true);
                shopButtons.Add((buttonRect, true, () => BuyUpgrade(upgrade)));
            }
            else
            {
                DrawShopButton(g, buttonRect, price.ToString(), ShopButtonStyle.Disabled, true);
                shopButtons.Add((buttonRect, false, () => BuyUpgrade(upgrade)));
            }
        }

        private void DrawFightBar(Graphics g)
        {
            const int barWidth = 30;
            const int barHeight = 300;
            const int barTop = 60;
            float barLeft = LogicalWidth - 80;

            // Фон смуги
            g.FillRectangle(Brushes.White, barLeft, barTop, barWidth, barHeight);

            // Зелена зона навколо центру playerPos: значення 100 — верх смуги, 0 — низ
            float zoneHighValue = Math.Min(100f, playerPos + ZoneHalfSize);
            float zoneLowValue = Math.Max(0f, playerPos - ZoneHalfSize);
            float zoneTop = barTop + (100f - zoneHighValue) / 100f * barHeight;
            float zoneBottom = barTop + (100f - zoneLowValue) / 100f * barHeight;
            g.FillRectangle(Brushes.LightGreen, barLeft, zoneTop, barWidth, zoneBottom - zoneTop);

            // Рамка смуги
            g.DrawRectangle(Pens.Black, barLeft, barTop, barWidth, barHeight);

            // Риба: темно-червона точка, що плаває по смузі
            float fishY = barTop + (100f - fishPos) / 100f * barHeight;
            Color fishColor = hookedFish == null ? Color.DarkRed : RarityColor(hookedFish.Rarity);
            using (var fishBrush = new SolidBrush(fishColor))
            {
                g.FillEllipse(fishBrush, barLeft + barWidth / 2f - 7, fishY - 6, 14, 12);
            }
            g.DrawEllipse(Pens.Black, barLeft + barWidth / 2f - 7, fishY - 6, 14, 12);

            // Шкала прогресу зліва від смуги, заповнюється знизу вгору
            float progressLeft = barLeft - 30;
            g.DrawRectangle(Pens.Black, progressLeft, barTop, 16, barHeight);
            float fillHeight = fightProgress / 100f * barHeight;
            g.FillRectangle(Brushes.Gold, progressLeft + 1, barTop + barHeight - fillHeight, 14, fillHeight);
        }
    }
}
