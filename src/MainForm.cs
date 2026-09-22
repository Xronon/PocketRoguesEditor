using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Главное окно: золото, таблица героев, кнопки записи и отмены.
    /// Собирается кодом без дизайнера. Само ничего не пишет — только через Engine.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly Engine _engine;
        private Snapshot _baseline;
        private bool _hasData;
        private bool _gameRunning;
        private bool _loading;
        private bool _dark;
        private bool _switchHover;

        private readonly Dictionary<string, NumericUpDown> _inputs = new Dictionary<string, NumericUpDown>();
        private readonly List<Button> _maxButtons = new List<Button>();
        private readonly ToolTip _tips = new ToolTip();

        // раздел «Навыки»: ряд кнопок-вкладок по героям и по панели на героя
        private readonly List<Button> _heroTabs = new List<Button>();
        private readonly List<Control> _skillPanels = new List<Control>();
        private readonly IntValue[] _guildLevels = new IntValue[6];
        private int _skillHero;
        private bool _heroChosen;
        private Label _guildInfo;
        private Button _maxSkills;

        private Label _banner;
        private Label _status;
        private Label _pending;
        private Button _save;
        private Button _reset;
        private Button _history;
        private Panel _themeSwitch;
        private Button _langSwitch;
        private Timer _timer;

        public MainForm()
        {
            _engine = new Engine(new PrefsStore(GameData.RegistryPath), Program.DataDir,
                                 new Engine.GameCheck(Engine.IsGameProcessRunning));

            Text = Program.Title + " " + Program.Version;
            Icon = AppIcon.Get();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            BuildThemeSwitch();
            BuildLayout();

            _dark = Settings.LoadDark();

            _timer = new Timer();
            _timer.Interval = 1500;
            _timer.Tick += delegate { CheckGame(false); };
        }

        /// <summary>Ключ /gear: сразу после запуска открыть окно «Снаряжение».</summary>
        public bool StartOnGear;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyTheme();
            _gameRunning = _engine.GameRunning();
            Reload();
            _timer.Start();
            if (StartOnGear) BeginInvoke(new EventHandler(OnEquipment), this, EventArgs.Empty);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && PendingCount() > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    L.T("Есть незаписанные правки. Закрыть без записи?", "There are unwritten edits. Close without writing?"),
                    Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
            }
            _timer.Stop();
            base.OnFormClosing(e);
        }

        // --- построение окна ------------------------------------------------

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.ColumnCount = 1;
            root.Dock = DockStyle.Fill;

            // Солнце-луна — отдельной строкой над полосой. Раньше значок был прибит к правому
            // краю окна, а окно подгоняет ширину под содержимое, включая этот значок: каждая
            // перекладка сдвигала его вправо и раздвигала окно ещё (в 1.0 окно уже было шире
            // нужного). Здесь строка растянута по колонке и на её ширину не влияет.
            TableLayoutPanel top = new TableLayoutPanel();
            top.ColumnCount = 3;
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.AutoSize = true;
            top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.Dock = DockStyle.Fill;
            top.Margin = new Padding(0);
            BuildLanguageSwitch();
            _langSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _langSwitch.Margin = new Padding(0, 0, 8, 0);
            top.Controls.Add(_langSwitch, 1, 0);
            _themeSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _themeSwitch.Margin = new Padding(0);
            top.Controls.Add(_themeSwitch, 2, 0);
            root.Controls.Add(top);

            _banner = new Label();
            _banner.AutoSize = false;
            _banner.Height = 44;
            _banner.Dock = DockStyle.Fill;
            _banner.TextAlign = ContentAlignment.MiddleLeft;
            _banner.Padding = new Padding(8, 0, 8, 0);
            _banner.Margin = new Padding(0, 2, 0, 10);
            root.Controls.Add(_banner);

            root.Controls.Add(BuildGoldRow());
            root.Controls.Add(SectionTitle(L.T("Характеристики", "Attributes")));
            root.Controls.Add(BuildHeroTable());
            root.Controls.Add(SectionTitle(L.T("Навыки", "Skills")));
            root.Controls.Add(BuildSkillsSection());

            Label hint = Muted(L.T(
                "Очки навыков — общий запас героя на характеристики и навыки. Характеристика в игре не выше 50, навык — не выше 25.\r\n"
                + "Здоровье записью не поднять: при загрузке игра срезает его до максимума — поднимайте выносливость.",
                "Skill points are the hero's shared pool for attributes and skills. In the game an attribute goes up to 50, a skill up to 25.\r\n"
                + "Health cannot be raised by writing it: on loading the game cuts it to the maximum — raise Endurance instead."));
            hint.Margin = new Padding(0, 6, 0, 10);
            root.Controls.Add(hint);

            _status = new Label();
            _status.AutoSize = true;
            _status.MaximumSize = new Size(760, 0);
            _status.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(_status);

            root.Controls.Add(BuildButtons());

            Label footer = Muted(L.T(
                "Кристаллы редактор не трогает. Сервер игры раз в час получает сумму золота, очков и характеристик —\r\n"
                + "разработчики видят эти числа. В кооперативе правками не пользоваться.",
                "The editor does not touch crystals. Once an hour the game server receives the totals of gold, points and attributes —\r\n"
                + "the developers see these numbers. Do not use edits in co-op."));
            footer.Margin = new Padding(0, 12, 0, 0);
            root.Controls.Add(footer);

            Controls.Add(root);
        }

        private Control BuildGoldRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Margin = new Padding(0, 0, 0, 10);

            Label title = new Label();
            title.Text = L.T("Золото", "Gold");
            title.AutoSize = true;
            title.Font = new Font(Font, FontStyle.Bold);
            title.Margin = new Padding(0, 6, 12, 0);
            row.Controls.Add(title);

            row.Controls.Add(MakeInput(GameData.Gold(), 140));
            return row;
        }

        private Control BuildHeroTable()
        {
            string[] headers = { L.T("Герой", "Hero"), L.T("Очки навыков", "Skill points"), GameData.Attributes[0].Title,
                                 GameData.Attributes[1].Title, GameData.Attributes[2].Title, GameData.Attributes[3].Title, "" };

            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = headers.Length;
            t.Margin = new Padding(0);

            for (int c = 0; c < headers.Length; c++)
            {
                Label h = new Label();
                h.Text = headers[c];
                h.AutoSize = true;
                h.Font = new Font(Font, FontStyle.Bold);
                h.Margin = new Padding(0, 0, 10, 4);
                t.Controls.Add(h, c, 0);
            }

            int r = 1;
            foreach (Hero hero in GameData.Heroes)
            {
                Label name = new Label();
                name.Text = hero.Title;
                name.AutoSize = true;
                name.Margin = new Padding(0, 6, 14, 0);
                t.Controls.Add(name, 0, r);

                t.Controls.Add(MakeInput(GameData.Points(hero), 100), 1, r);

                int c = 2;
                foreach (HeroAttribute a in GameData.Attributes)
                {
                    t.Controls.Add(MakeInput(GameData.AttributeOf(hero, a), 70), c, r);
                    c++;
                }

                Button max = new Button();
                max.Text = L.T("Все по 50", "All to 50");
                max.AutoSize = true;
                max.Margin = new Padding(4, 2, 0, 2);
                max.Tag = hero;
                max.Click += OnMaxAttributes;
                _tips.SetToolTip(max, L.T("Поставить герою все четыре характеристики на игровой предел — 50",
                                          "Set all four of the hero's attributes to the game's limit — 50"));
                _maxButtons.Add(max);
                t.Controls.Add(max, c, r);
                r++;
            }
            return t;
        }

        private NumericUpDown MakeInput(Field field, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Width = width;
            n.Minimum = field.Min;
            n.Maximum = field.Max;
            n.ThousandsSeparator = field.Max > 9999;
            n.TextAlign = HorizontalAlignment.Right;
            n.Margin = new Padding(0, 2, 10, 2);
            n.Tag = field;
            n.ValueChanged += delegate { if (!_loading) RefreshPending(); };
            _tips.SetToolTip(n, field.Title);
            _inputs[field.Key] = n;
            return n;
        }

        private Label SectionTitle(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
            l.Margin = new Padding(0, 10, 0, 6);
            return l;
        }

        /// <summary>
        /// Навыки: ряд кнопок с героями, под ним панель выбранного героя — три колонки по
        /// кнопкам атаки, в каждой по три навыка. Панели всех шести героев создаются сразу
        /// и только прячутся, поэтому незаписанные правки у других героев не теряются.
        /// </summary>
        private Control BuildSkillsSection()
        {
            TableLayoutPanel box = new TableLayoutPanel();
            box.AutoSize = true;
            box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            box.ColumnCount = 1;
            box.Margin = new Padding(0);

            FlowLayoutPanel tabs = new FlowLayoutPanel();
            tabs.AutoSize = true;
            tabs.WrapContents = false;
            tabs.Margin = new Padding(0, 0, 0, 6);
            foreach (Hero hero in GameData.Heroes)
            {
                Button b = new Button();
                b.Text = hero.Title;
                b.AutoSize = true;
                b.MinimumSize = new Size(96, 0);
                b.Margin = new Padding(0, 0, 4, 0);
                b.Tag = hero.Index;
                b.Click += delegate(object s, EventArgs e) { SelectSkillHero((int)((Button)s).Tag); };
                _heroTabs.Add(b);
                tabs.Controls.Add(b);
            }
            box.Controls.Add(tabs);

            // ширина колонки названий — по самому длинному названию у всех героев,
            // чтобы окно не прыгало при переключении
            int nameWidth = 0;
            foreach (Skill s in GameData.Skills)
                nameWidth = Math.Max(nameWidth, TextRenderer.MeasureText(s.Title, Font).Width);
            nameWidth += 8;

            Panel host = new Panel();
            host.AutoSize = true;
            host.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            host.Margin = new Padding(0);
            foreach (Hero hero in GameData.Heroes)
            {
                Control panel = BuildSkillPanel(hero, nameWidth);
                panel.Location = new Point(0, 0);
                panel.Visible = false;
                _skillPanels.Add(panel);
                host.Controls.Add(panel);
            }
            box.Controls.Add(host);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.AutoSize = true;
            bottom.WrapContents = false;
            bottom.Margin = new Padding(0, 4, 0, 0);

            _maxSkills = new Button();
            _maxSkills.Text = L.T("Все навыки героя на 25", "All hero skills to 25");
            _maxSkills.AutoSize = true;
            _maxSkills.Margin = new Padding(0, 0, 12, 0);
            _maxSkills.Click += OnMaxSkills;
            _tips.SetToolTip(_maxSkills, L.T("Поставить все девять навыков выбранного героя на игровой предел — 25",
                                             "Set all nine skills of the chosen hero to the game's limit — 25"));
            bottom.Controls.Add(_maxSkills);

            _guildInfo = Muted("");
            _guildInfo.Margin = new Padding(0, 6, 0, 0);
            bottom.Controls.Add(_guildInfo);
            box.Controls.Add(bottom);
            return box;
        }

        private Control BuildSkillPanel(Hero hero, int nameWidth)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 6;
            t.Margin = new Padding(0);

            for (int slot = 0; slot < 3; slot++)
            {
                Label h = new Label();
                h.Text = GameData.SlotTitle(slot);
                h.AutoSize = true;
                h.Font = new Font(Font, FontStyle.Bold);
                h.Margin = new Padding(0, 0, 10, 4);
                t.Controls.Add(h, slot * 2, 0);
                t.SetColumnSpan(h, 2);
            }

            int[] rowOfSlot = new int[] { 1, 1, 1 };
            foreach (Skill s in GameData.SkillsOf(hero))
            {
                Field f = GameData.SkillField(s);
                string tip = s.IsDefault
                    ? f.Title + L.T(": стартовый навык, игра не опускает его ниже 1", ": starting skill, the game never lets it drop below 1")
                    : f.Title;

                Label name = new Label();
                name.Text = s.Title;
                name.AutoSize = true;
                name.MinimumSize = new Size(nameWidth, 0);
                name.Margin = new Padding(0, 6, 4, 0);
                _tips.SetToolTip(name, tip);

                NumericUpDown n = MakeInput(f, 52);
                n.Margin = new Padding(0, 2, 22, 2);
                _tips.SetToolTip(n, tip);

                int row = rowOfSlot[s.Slot]++;
                t.Controls.Add(name, s.Slot * 2, row);
                t.Controls.Add(n, s.Slot * 2 + 1, row);
            }
            return t;
        }

        /// <summary>Показать навыки героя и подсветить его кнопку.</summary>
        private void SelectSkillHero(int index)
        {
            _skillHero = index;
            _heroChosen = true;
            for (int i = 0; i < _skillPanels.Count; i++) _skillPanels[i].Visible = i == index;
            PaintHeroTabs();
            UpdateGuildInfo();
        }

        /// <summary>
        /// Кнопки героев работают как вкладки: выбранная — жирная и с цветной рамкой.
        /// Красятся после темы, потому что тема перекрашивает все кнопки по-своему.
        /// Точка после имени — у героя есть незаписанные правки навыков.
        /// </summary>
        private void PaintHeroTabs()
        {
            for (int i = 0; i < _heroTabs.Count; i++)
            {
                Button b = _heroTabs[i];
                bool selected = i == _skillHero;
                b.Text = GameData.Heroes[i].Title + (HeroHasPendingSkills(i) ? " •" : "");
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = selected ? Theme.Entry : Theme.Btn;
                if (!Theme.Dark && !selected) b.BackColor = SystemColors.ControlLight;
                b.ForeColor = selected ? Theme.Fg : Theme.Muted;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = selected ? Theme.Accent : Theme.Border;
                b.FlatAppearance.MouseOverBackColor = Theme.Dark ? Theme.BtnHover : SystemColors.ControlLightLight;
                b.Font = selected ? new Font(Font, FontStyle.Bold) : Font;
            }
        }

        private bool HeroHasPendingSkills(int heroIndex)
        {
            if (_baseline == null) return false;
            foreach (Skill s in GameData.SkillsOf(GameData.Heroes[heroIndex]))
            {
                NumericUpDown n = _inputs[s.Key];
                Field f = GameData.SkillField(s);
                if (n.Enabled && (int)n.Value != f.GameValue(_baseline.Get(s.Key))) return true;
            }
            return false;
        }

        private void UpdateGuildInfo()
        {
            if (_guildInfo == null) return;
            IntValue g = _guildLevels[_skillHero];
            int lvl = g.State == ValueState.Ok ? g.Value : 0;
            if (lvl >= GameData.SkillMax)
                _guildInfo.Text = L.T("Гильдия героя: ур. " + lvl + " — в игре навыки качаются до 25.",
                                      "Hero's guild: level " + lvl + " — in the game skills go up to 25.");
            else
                _guildInfo.Text = L.T("Гильдия героя: ур. " + lvl + " — в самой игре навыки качаются только до "
                                      + "уровня гильдии; редактор даёт до 25 сразу.",
                                      "Hero's guild: level " + lvl + " — in the game itself skills go only up to "
                                      + "the guild level; the editor allows up to 25 right away.");
        }

        private Control BuildButtons()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Margin = new Padding(0);

            _save = new Button();
            _save.Text = L.T("Записать в игру", "Write to game");
            _save.AutoSize = true;
            _save.Padding = new Padding(8, 2, 8, 2);
            _save.Click += OnSave;
            row.Controls.Add(_save);

            _reset = new Button();
            _reset.Text = L.T("Сбросить правки", "Reset edits");
            _reset.AutoSize = true;
            _reset.Click += delegate
            {
                Reload();
                SetStatus(L.T("Правки сброшены: в окне то, что сейчас в игре.", "Edits reset: the window shows what is in the game now."), false);
            };
            _tips.SetToolTip(_reset, L.T("Вернуть в окно числа из игры, ничего не записывая", "Bring back the game's numbers without writing anything"));
            row.Controls.Add(_reset);

            Button gear = new Button();
            gear.Text = L.T("Снаряжение…", "Gear…");
            gear.AutoSize = true;
            gear.Click += OnEquipment;
            _tips.SetToolTip(gear, L.T("Вещи героя: качество, эффекты, проклятия; выдача, замена, удаление",
                                       "Hero's items: quality, effects, curses; adding, replacing, deleting"));
            row.Controls.Add(gear);

            _history = new Button();
            _history.Text = L.T("Прошлые правки…", "Past edits…");
            _history.AutoSize = true;
            _history.Click += OnHistory;
            _tips.SetToolTip(_history, L.T("Список сделанных правок; любую можно отменить", "The list of edits made; any of them can be undone"));
            row.Controls.Add(_history);

            _pending = Muted("");
            _pending.Margin = new Padding(12, 8, 0, 0);
            row.Controls.Add(_pending);
            return row;
        }

        private static Label Muted(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        /// <summary>Солнце-луна в правом верхнем углу — как в «Смотрителе стола».</summary>
        private void BuildThemeSwitch()
        {
            _themeSwitch = new Panel();
            _themeSwitch.Size = new Size(26, 22);
            _themeSwitch.Cursor = Cursors.Hand;
            _themeSwitch.BackColor = Color.Transparent;
            _tips.SetToolTip(_themeSwitch, L.T("Светлая или тёмная тема", "Light or dark theme"));

            _themeSwitch.Paint += delegate(object s, PaintEventArgs e)
            {
                Theme.DrawSwitch(e.Graphics, new Rectangle(0, 0, _themeSwitch.Width, _themeSwitch.Height),
                                 _dark, _switchHover);
            };
            _themeSwitch.MouseEnter += delegate { _switchHover = true; _themeSwitch.Invalidate(); };
            _themeSwitch.MouseLeave += delegate { _switchHover = false; _themeSwitch.Invalidate(); };
            _themeSwitch.Click += delegate
            {
                _dark = !_dark;
                Settings.SaveDark(_dark);
                ApplyTheme();
            };

        }

        /// <summary>
        /// Язык программы — «RU ▾» / «EN ▾» рядом с солнцем-луной: как в игре, English или Русский.
        /// Окна собираются кодом, поэтому новый язык — с перезапуском программы.
        /// </summary>
        private void BuildLanguageSwitch()
        {
            _langSwitch = new Button();
            _langSwitch.AutoSize = true;
            _langSwitch.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _langSwitch.Cursor = Cursors.Hand;
            _langSwitch.Padding = new Padding(2, 0, 2, 0);
            _langSwitch.Text = (L.Ru ? "RU" : "EN") + " ▾";
            _tips.SetToolTip(_langSwitch, L.T("Язык программы", "Program language"));
            _langSwitch.Click += delegate { ShowLanguageMenu(); };
        }

        private void ShowLanguageMenu()
        {
            string now = Settings.LoadLanguage();
            ContextMenuStrip menu = new ContextMenuStrip();
            if (Theme.Dark)
            {
                menu.BackColor = Theme.Btn;
                menu.ForeColor = Theme.Fg;
            }
            AddLanguage(menu, L.Auto, L.T("Как в игре", "As in the game"), now);
            AddLanguage(menu, L.English, "English", now);
            AddLanguage(menu, L.Russian, "Русский", now);
            menu.Closed += delegate { BeginInvoke(new MethodInvoker(menu.Dispose)); };
            menu.Show(_langSwitch, new Point(0, _langSwitch.Height));
        }

        private void AddLanguage(ContextMenuStrip menu, string value, string title, string now)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(title);
            item.Checked = value == now;
            item.Click += delegate { SwitchLanguage(value); };
            menu.Items.Add(item);
        }

        private void SwitchLanguage(string value)
        {
            if (value == Settings.LoadLanguage()) return;
            bool ru = L.Decide(value, Program.GameLanguage());
            if (ru != L.Ru && PendingCount() > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    L.T("Чтобы сменить язык, программа перезапустится, и незаписанные правки пропадут. Перезапустить?",
                        "To change the language the program restarts, and unwritten edits are lost. Restart?"),
                    Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            Settings.SaveLanguage(value);
            if (ru == L.Ru) return;   // язык тот же (например, «как в игре» и есть русский) — перезапуск не нужен
            _timer.Stop();
            Application.Restart();
        }

        private void ApplyTheme()
        {
            Theme.Dark = _dark;
            Theme.ApplyToForm(this);
            _themeSwitch.BackColor = Color.Transparent;
            _themeSwitch.Invalidate();
            // язык — плоской кнопкой без рамки, как надпись; подсветка при наведении
            _langSwitch.FlatStyle = FlatStyle.Flat;
            _langSwitch.UseVisualStyleBackColor = false;
            _langSwitch.FlatAppearance.BorderSize = 0;
            _langSwitch.BackColor = Theme.Bg;
            _langSwitch.ForeColor = Theme.Fg;
            _langSwitch.FlatAppearance.MouseOverBackColor = Theme.Dark ? Theme.BtnHover : SystemColors.ControlLight;
            UpdateBanner();
            RefreshPending();
            PaintHeroTabs();
            _status.ForeColor = _statusIsError ? ErrorColor : Theme.Fg;
        }

        // --- данные ---------------------------------------------------------

        /// <summary>Перечитать игру в окно. Незаписанные правки пропадают.</summary>
        private void Reload()
        {
            _hasData = _engine.HasGameData();
            _baseline = _engine.Load();

            _loading = true;
            try
            {
                foreach (Field f in GameData.AllFields())
                {
                    NumericUpDown n = _inputs[f.Key];
                    IntValue v = _baseline.Get(f.Key);
                    if (v.State == ValueState.Foreign)
                    {
                        n.Enabled = false;
                        _tips.SetToolTip(n, f.Title + L.T(": в игре здесь не целое число, редактор его не трогает",
                                                          ": the game holds something other than an integer here; the editor leaves it alone"));
                        continue;
                    }
                    // пределы мягкие: что уже стоит в игре, показываем как есть
                    int shown = f.GameValue(v);
                    n.Minimum = Math.Min(f.Min, shown);
                    n.Maximum = Math.Max(f.Max, shown);
                    n.Value = shown;
                    n.Enabled = _hasData;
                }
            }
            finally
            {
                _loading = false;
            }

            foreach (Hero h in GameData.Heroes) _guildLevels[h.Index] = _engine.Read(h.GuildKey);

            // первый раз открываем навыки того героя, что выбран в самой игре
            if (!_heroChosen)
            {
                IntValue cur = _engine.Read("curChar");
                int index = cur.State == ValueState.Ok ? cur.Value : 0;
                SelectSkillHero(index >= 0 && index < GameData.Heroes.Length ? index : 0);
            }

            foreach (Button b in _maxButtons) b.Enabled = _hasData;
            _maxSkills.Enabled = _hasData;
            UpdateGuildInfo();
            UpdateBanner();
            RefreshPending();
        }

        private Dictionary<string, int> Desired()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            foreach (KeyValuePair<string, NumericUpDown> pair in _inputs)
            {
                if (!pair.Value.Enabled) continue;
                d[pair.Key] = (int)pair.Value.Value;
            }
            return d;
        }

        private int PendingCount()
        {
            if (_baseline == null) return 0;
            return Engine.Diff(_baseline, Desired()).Count;
        }

        /// <summary>Подсветить изменённые поля и обновить счётчик и кнопки.</summary>
        private void RefreshPending()
        {
            if (_baseline == null) return;

            foreach (KeyValuePair<string, NumericUpDown> pair in _inputs)
            {
                NumericUpDown n = pair.Value;
                Field f = (Field)n.Tag;
                bool changed = n.Enabled && (int)n.Value != f.GameValue(_baseline.Get(pair.Key));
                n.BackColor = changed ? ChangedColor : Theme.Entry;
                n.ForeColor = Theme.Fg;
            }

            int count = PendingCount();
            _pending.Text = count == 0 ? "" : L.T("Изменено значений: ", "Values changed: ") + count;
            _save.Enabled = _hasData && !_gameRunning && count > 0;
            _reset.Enabled = count > 0;
            PaintHeroTabs();
        }

        private Color ChangedColor
        {
            get { return Theme.Dark ? Color.FromArgb(0x4a, 0x42, 0x22) : Color.FromArgb(0xff, 0xf1, 0xb8); }
        }

        private Color ErrorColor
        {
            get { return Theme.Dark ? Color.FromArgb(0xf0, 0x80, 0x78) : Color.FromArgb(0xb0, 0x20, 0x20); }
        }

        private bool _statusIsError;

        private void SetStatus(string text, bool error)
        {
            _statusIsError = error;
            _status.Text = text;
            _status.ForeColor = error ? ErrorColor : Theme.Fg;
        }

        // --- слежение за игрой ------------------------------------------------

        private void CheckGame(bool force)
        {
            bool running = _engine.GameRunning();
            if (running == _gameRunning && !force) return;

            bool closedNow = _gameRunning && !running;
            _gameRunning = running;

            // игра закрылась — числа в окне могли устареть; если правок нет, освежаем молча
            if (closedNow && PendingCount() == 0) Reload();
            else if (closedNow) SetStatus(L.T("Игра закрылась. Если успели поиграть, числа в окне могли устареть — "
                                          + "при записи программа это проверит.",
                                          "The game closed. If you played, the numbers in the window may be out of date — "
                                          + "the program checks this when writing."), false);
            UpdateBanner();
            RefreshPending();
        }

        private void UpdateBanner()
        {
            if (_banner == null) return;

            if (!_hasData)
            {
                _banner.Text = L.T("Не нашёл данных Pocket Rogues в реестре: игра на этом компьютере ещё не сохранялась.",
                                   "No Pocket Rogues data in the registry: the game has not saved on this computer yet.");
                PaintBanner(_banner, false);
            }
            else if (_gameRunning)
            {
                _banner.Text = L.T("Игра запущена. Смотреть можно, записывать — нет: при выходе игра "
                                   + "запишет свои значения поверх правки. Закройте игру.",
                                   "The game is running. You can look but not write: on exit the game "
                                   + "writes its own values over the edit. Close the game.");
                PaintBanner(_banner, false);
            }
            else
            {
                _banner.Text = L.T("Игра закрыта — можно править. Перед каждой записью делается резервная копия.",
                                   "The game is closed — you can edit. A backup is made before every write.");
                PaintBanner(_banner, true);
            }
        }

        private static void PaintBanner(Label banner, bool good)
        {
            if (Theme.Dark)
            {
                banner.BackColor = good ? Color.FromArgb(0x1f, 0x3a, 0x2a) : Color.FromArgb(0x4a, 0x24, 0x24);
                banner.ForeColor = Color.FromArgb(0xe8, 0xea, 0xee);
            }
            else
            {
                banner.BackColor = good ? Color.FromArgb(0xdc, 0xf2, 0xe2) : Color.FromArgb(0xfb, 0xdd, 0xdb);
                banner.ForeColor = Color.FromArgb(0x20, 0x24, 0x2a);
            }
        }

        // --- действия ---------------------------------------------------------

        private void OnMaxAttributes(object sender, EventArgs e)
        {
            Hero hero = (Hero)((Button)sender).Tag;
            foreach (HeroAttribute a in GameData.Attributes)
            {
                NumericUpDown n = _inputs[GameData.AttributeOf(hero, a).Key];
                if (n.Enabled && n.Value < GameData.AttributeMax) n.Value = GameData.AttributeMax;
            }
        }

        private void OnMaxSkills(object sender, EventArgs e)
        {
            foreach (Skill s in GameData.SkillsOf(GameData.Heroes[_skillHero]))
            {
                NumericUpDown n = _inputs[s.Key];
                if (n.Enabled && n.Value < GameData.SkillMax) n.Value = GameData.SkillMax;
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            ValidateChildren();   // дописать число, которое набирают прямо сейчас

            Dictionary<string, int> desired = Desired();
            List<Change> changes = Engine.Diff(_baseline, desired);
            if (changes.Count == 0) { SetStatus(L.T("Менять нечего.", "Nothing to change."), false); return; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("Записать в игру?", "Write to the game?"));
            sb.AppendLine();
            sb.Append(Engine.DescribeLines(changes, 20));
            sb.AppendLine();
            sb.Append(L.T("Перед записью будет сделана резервная копия; правку потом можно отменить.",
                          "A backup is made before writing; the edit can be undone later."));

            DialogResult r = MessageBox.Show(this, sb.ToString(), Program.Title,
                                             MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            Outcome o = _engine.Save(_baseline, desired);
            if (o.Ok)
            {
                Reload();
                SetStatus(o.Message, false);
                return;
            }

            if (o.Stale) Reload();
            SetStatus(o.Message, true);
            MessageBox.Show(this, o.Message, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnEquipment(object sender, EventArgs e)
        {
            Catalog catalog;
            try { catalog = Catalog.Embedded(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("Справочник снаряжения не читается: ", "The gear catalog cannot be read: ") + ex.Message, Program.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            using (EquipmentForm f = new EquipmentForm(_engine, catalog))
            {
                f.ShowDialog(this);
                if (f.ChangedGame)
                {
                    // запись снаряжения поменяла данные игры — снимок этого окна устарел
                    bool pending = PendingCount() > 0;
                    if (!pending) Reload();
                    else _baseline = _engine.Load();   // правки в окне не теряем, снимок — свежий
                    SetStatus(L.T("Снаряжение записано.", "Gear written."), false);
                }
            }
        }

        private void OnHistory(object sender, EventArgs e)
        {
            using (HistoryForm f = new HistoryForm(_engine))
            {
                f.ShowDialog(this);
                if (f.ChangedGame)
                {
                    Reload();
                    SetStatus(f.LastMessage, false);
                }
            }
        }
    }
}
