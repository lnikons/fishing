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
        private readonly PointF rodTip = new PointF(80, 60);
        private PointF floatPos;
        private PointF floatTarget;

        // Стан закидання
        private bool isCharging;
        private bool isFlying;
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
        private const float BaseZoneHalfSize = 12.5f; // без прокачки зона займає 25% смуги

        // Половина зеленої зони: росте з покращенням «Краща вудка»
        private float ZoneHalfSize => BaseZoneHalfSize + (float)UpgradeValue(UpgradeType.CatchBar);

        // Риба, що зараз на гачку, і параметри її поведінки в міні-грі
        private Fish hookedFish;
        private float fishSpeed;       // скільки смуги риба проходить за 16 мс
        private float fishJumpScale;   // 1 — спокійна риба, менше — частіше міняє напрям
        private const float PressureGrowthMs = 6000f; // за скільки мс швидкість падіння шкали зростає на 100%

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
            rodPen.Dispose();
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
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0.27f, 0.27f, 0.31f, 0, 1 },
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
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
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
                var previous = g.InterpolationMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(sprite, new RectangleF(centerX - width / 2, centerY - height / 2, width, height));
                g.InterpolationMode = previous;
                return;
            }
            DrawFishShape(g, centerX, centerY, width / 120f,
                discovered ? RarityColor(fish.Rarity) : Color.FromArgb(70, 70, 80), discovered);
        }

        private void ShowPanel(Panel panel)
        {
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
            float distance = 150 + power * 4 + (float)UpgradeValue(UpgradeType.CastRange);
            floatPos = rodTip;
            floatTarget = new PointF(rodTip.X + distance, LogicalHeight - 100);
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
            fightProgress = 30f;
            fightElapsed = 0;
            fishPos = 50f;

            // Рибу обираємо одразу при підсіченні, щоб міні-гра знала, наскільки вона складна
            hookedFish = PickCaughtFish();
            float difficulty = (float)hookedFish.Difficulty; // 0.1 (легко) .. 1.0 (дуже важко)
            // Баланс підібрано моделюванням: кожен рівень «Кращої вудки» відкриває наступну рідкість
            fishSpeed = 0.7f + 0.7f * difficulty;           // карась ~0,8, легендарна ~1,4
            fishJumpScale = 1f - 0.5f * difficulty;          // складні частіше міняють напрям

            PickNewFishTarget();
            statusText = "Риба на гачку!";
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
                statusText = "Риба втекла";
            }
            pnlGame.Invalidate();
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
            statusText = "Риба втекла";
            sinceReactionTimeout.Restart();
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
            // через 6 с — удвічі, через 12 с — утричі. Риба зривається лише тоді,
            // коли шкала опустилася до нуля (без окремого таймера).
            float hardness = (float)hookedFish.Difficulty;
            float pressure = 1f + fightElapsed / PressureGrowthMs;
            fightProgress += (IsFishInZone() ? 0.5f - 0.15f * hardness : -(0.3f + 0.1f * hardness) * pressure) * k;
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

            // Вудка і волосінь
            e.Graphics.DrawLine(rodPen, rodTip, floatPos);

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

        private Fish PickCaughtFish()
        {
            // Риба з поточної локації каталогу; рідкісніші випадають рідше.
            // Шанси: звичайна 50, незвичайна 25, рідкісна 15, епічна 8, легендарна 2.
            // Коли бекенд зробить вибір риби (картка 21), цей метод замінимо його викликом.
            int[] rarityWeights = { 50, 25, 15, 8, 2 }; // Common .. Legendary
            List<Fish> pool = Catalog.GetLocation(player.CurrentLocationId).Fishes;
            int total = pool.Sum(f => rarityWeights[(int)f.Rarity]);
            int roll = rng.Next(total);
            foreach (Fish fish in pool)
            {
                roll -= rarityWeights[(int)fish.Rarity];
                if (roll < 0)
                {
                    return fish;
                }
            }
            return pool[0];
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
            // Проста риба-заглушка (справжні спрайти — картка 16)
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

            g.FillRectangle(discovered ? Brushes.White : Brushes.Gainsboro, card);
            g.DrawRectangle(Pens.DarkGray, card.X, card.Y, card.Width, card.Height);

            float centerX = card.X + card.Width / 2;
            float fishY = card.Y + 44;

            if (!discovered)
            {
                // Невідкритий вид: темний силует і знаки питання
                DrawFish(g, fish, centerX, fishY, 120, false);
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
                shopMessage = "Не вистачає монет";
                return;
            }
            player.Money -= location.Price;
            player.UnlockedLocationIds.Add(location.Id);
            player.CurrentLocationId = location.Id;
            shopMessage = $"Відкрито локацію «{location.Name}»";
            UpdateCoinsLabel();
        }

        private void SelectLocation(Location location)
        {
            player.CurrentLocationId = location.Id;
            shopMessage = $"Поточна локація: {location.Name}";
        }

        private void BuyUpgrade(Upgrade upgrade)
        {
            int level = UpgradeLevel(upgrade);
            int price = UpgradePrice(upgrade, level);
            if (level >= upgrade.MaxLevel || player.Money < price)
            {
                shopMessage = level >= upgrade.MaxLevel ? "Уже максимальний рівень" : "Не вистачає монет";
                return;
            }
            player.Money -= price;
            player.UpgradeLevels[upgrade.Id] = level + 1;
            shopMessage = $"Куплено: {upgrade.Name}, рівень {level + 1}";
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
                        shopMessage = "Не вистачає монет";
                    }
                    pnlShop.Invalidate();
                    return;
                }
            }
        }

        private static Color GameWaterColor(int locationId)
        {
            // Колір води на ігровому полі (фони з картки 16 підключимо пізніше)
            switch (locationId)
            {
                case 1: return Color.LightBlue;
                case 2: return Color.FromArgb(150, 205, 215);
                default: return Color.FromArgb(120, 170, 225);
            }
        }

        private static Color LocationColor(int locationId)
        {
            // Кольорові заглушки локацій (фони з картки 16 підключимо пізніше)
            switch (locationId)
            {
                case 1: return Color.FromArgb(90, 165, 120);
                case 2: return Color.FromArgb(60, 150, 180);
                default: return Color.FromArgb(40, 90, 170);
            }
        }

        private void PnlShop_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float width = pnlShop.ClientSize.Width / DpiScale;
            shopButtons.Clear();

            g.DrawString("Магазин", resultTitleFont, Brushes.Black, new RectangleF(0, 4, width, 34), centerFormat);
            if (!string.IsNullOrEmpty(shopMessage))
            {
                g.DrawString(shopMessage, shopTextFont, Brushes.DimGray, new RectangleF(0, 34, width, 18), centerFormat);
            }

            // --- Локації: 3 картки в ряд
            g.DrawString("Локації", shopNameFont, Brushes.Black, 25, 50);
            const float locWidth = 240, locHeight = 112, locGap = 15, locTop = 74;
            float locLeft = (width - (3 * locWidth + 2 * locGap)) / 2;
            for (int i = 0; i < Catalog.Locations.Count; i++)
            {
                DrawLocationCard(g, Catalog.Locations[i], new RectangleF(locLeft + i * (locWidth + locGap), locTop, locWidth, locHeight));
            }

            // --- Покращення: 5 карток у ряд
            g.DrawString("Покращення", shopNameFont, Brushes.Black, 25, 196);
            const float upWidth = 140, upHeight = 170, upGap = 12, upTop = 220;
            float upLeft = (width - (5 * upWidth + 4 * upGap)) / 2;
            for (int i = 0; i < TempUpgrades.Count; i++)
            {
                DrawUpgradeCard(g, TempUpgrades[i], new RectangleF(upLeft + i * (upWidth + upGap), upTop, upWidth, upHeight));
            }
        }

        private void DrawLocationCard(Graphics g, Location location, RectangleF card)
        {
            bool unlocked = player.UnlockedLocationIds.Contains(location.Id);
            bool current = player.CurrentLocationId == location.Id;

            g.FillRectangle(Brushes.White, card);
            var band = new RectangleF(card.X, card.Y, card.Width, 34);
            if (backgrounds.TryGetValue(location.Id, out Image thumbnail))
            {
                // Смужка фону біля лінії горизонту, затемнена, щоб назву було добре видно
                float sourceHeight = thumbnail.Width * band.Height / band.Width;
                var source = new RectangleF(0, thumbnail.Height * 0.36f - sourceHeight / 2, thumbnail.Width, sourceHeight);
                g.DrawImage(thumbnail, band, source, GraphicsUnit.Pixel);
                using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                {
                    g.FillRectangle(shade, band);
                }
            }
            else
            {
                using (var bandBrush = new SolidBrush(LocationColor(location.Id)))
                {
                    g.FillRectangle(bandBrush, band);
                }
            }
            g.DrawRectangle(current ? Pens.DarkOrange : Pens.DarkGray, card.X, card.Y, card.Width, card.Height);
            g.DrawString(location.Name, shopNameFont, Brushes.White, new RectangleF(card.X, card.Y + 4, card.Width, 26), centerFormat);
            g.DrawString($"Видів риби: {location.Fishes.Count}", shopTextFont, Brushes.DimGray,
                new RectangleF(card.X, card.Y + 38, card.Width, 20), centerFormat);

            var buttonRect = new RectangleF(card.X + 20, card.Y + card.Height - 42, card.Width - 40, 32);
            if (current)
            {
                DrawShopButton(g, buttonRect, "Поточна", Color.Gainsboro, Color.DimGray);
            }
            else if (unlocked)
            {
                DrawShopButton(g, buttonRect, "Обрати", Color.FromArgb(80, 160, 90), Color.White);
                shopButtons.Add((buttonRect, true, () => SelectLocation(location)));
            }
            else
            {
                bool canBuy = player.Money >= location.Price;
                DrawShopButton(g, buttonRect, $"Купити за {location.Price}",
                    canBuy ? Color.FromArgb(235, 150, 40) : Color.Gainsboro, canBuy ? Color.White : Color.Gray);
                shopButtons.Add((buttonRect, canBuy, () => BuyLocation(location)));
            }
        }

        private void DrawUpgradeCard(Graphics g, Upgrade upgrade, RectangleF card)
        {
            int level = UpgradeLevel(upgrade);
            bool maxed = level >= upgrade.MaxLevel;
            int price = UpgradePrice(upgrade, level);

            g.FillRectangle(Brushes.White, card);
            g.DrawRectangle(Pens.DarkGray, card.X, card.Y, card.Width, card.Height);
            g.DrawString(upgrade.Name, shopButtonFont, Brushes.Black, new RectangleF(card.X + 4, card.Y + 8, card.Width - 8, 40), centerFormat);
            g.DrawString(upgrade.Description, shopTextFont, Brushes.DimGray, new RectangleF(card.X + 4, card.Y + 48, card.Width - 8, 36), centerFormat);

            // Рівень крапками: заповнені — куплені
            float dotsLeft = card.X + (card.Width - upgrade.MaxLevel * 18) / 2;
            for (int i = 0; i < upgrade.MaxLevel; i++)
            {
                var dot = new RectangleF(dotsLeft + i * 18 + 3, card.Y + 92, 12, 12);
                if (i < level)
                {
                    g.FillEllipse(Brushes.DarkOrange, dot);
                }
                g.DrawEllipse(Pens.DarkOrange, dot);
            }

            var buttonRect = new RectangleF(card.X + 10, card.Y + card.Height - 42, card.Width - 20, 32);
            if (maxed)
            {
                DrawShopButton(g, buttonRect, "Макс.", Color.Gainsboro, Color.DimGray);
            }
            else
            {
                bool canBuy = player.Money >= price;
                DrawShopButton(g, buttonRect, $"Купити {price}",
                    canBuy ? Color.FromArgb(235, 150, 40) : Color.Gainsboro, canBuy ? Color.White : Color.Gray);
                shopButtons.Add((buttonRect, canBuy, () => BuyUpgrade(upgrade)));
            }
        }

        private void DrawShopButton(Graphics g, RectangleF rect, string text, Color back, Color fore)
        {
            using (var backBrush = new SolidBrush(back))
            using (var foreBrush = new SolidBrush(fore))
            {
                g.FillRectangle(backBrush, rect);
                g.DrawRectangle(Pens.Gray, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(text, shopButtonFont, foreBrush, rect, centerFormat);
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
