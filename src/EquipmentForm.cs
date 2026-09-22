using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Окно «Снаряжение»: вещи героя (надетое и сумка), правка качества, эффектов и проклятий.
    /// Само ничего не пишет — только через Engine.SaveGear, с копией и отменой, как числа.
    /// Правила игры (сколько эффектов, какого вида) — в GearItem.
    /// </summary>
    internal sealed class EquipmentForm : Form
    {
        private readonly Engine _engine;
        private readonly Catalog _catalog;
        private Snapshot _baseline;
        private readonly Dictionary<int, HeroGear> _heroes = new Dictionary<int, HeroGear>();
        private int _hero = -1;
        private GearItem _selected;
        private bool _gameRunning;
        private bool _filling;
        private bool _closing;       // окно закрывается — подгонку столбцов не трогаем

        private readonly List<Button> _heroTabs = new List<Button>();
        private Label _heroState;
        private ListView _list;
        private Panel _editor;
        private Label _status;
        private Label _pending;
        private Button _save;
        private Button _resetAll;
        private Button _give;
        private Button _replace;
        private Button _delete;
        private Timer _timer;
        private readonly ToolTip _tips = new ToolTip();

        // Описание эффекта при наведении в раскрытом списке «Добавить» (с 1.7.1, как в моде: навёл
        // курсор на свойство — увидел описание). Строка под списком для этого не годится:
        // раскрытый список её закрывает.
        private readonly ToolTip _hoverTip = new ToolTip();
        private string _hoverText = "";
        private int _hoverIndex = -1;
        private const int HoverWidth = 380;

        /// <summary>Была ли запись в игру — тогда главное окно перечитает данные.</summary>
        public bool ChangedGame;

        public EquipmentForm(Engine engine, Catalog catalog)
        {
            _engine = engine;
            _catalog = catalog;

            Text = L.T("Снаряжение", "Gear");
            Icon = AppIcon.Get();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(1000, 640);
            MinimumSize = new Size(820, 480);
            Padding = new Padding(10);

            BuildLayout();
            _tips.AutoPopDelay = 30000;   // описание длинное — подсказка не должна гаснуть, пока читаешь
            SetUpHoverTip();

            _timer = new Timer();
            _timer.Interval = 1500;
            _timer.Tick += delegate { CheckGame(); };
        }

        // --- построение -----------------------------------------------------

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
                b.Click += delegate(object s, EventArgs e) { SelectHero((int)((Button)s).Tag); };
                _heroTabs.Add(b);
                tabs.Controls.Add(b);
            }
            root.Controls.Add(tabs, 0, 0);
            root.SetColumnSpan(tabs, 2);

            _heroState = Muted("");
            _heroState.AutoSize = true;
            _heroState.MaximumSize = new Size(940, 0);
            _heroState.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(_heroState, 0, 1);
            root.SetColumnSpan(_heroState, 2);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(L.T("Где", "Where"), 112);
            _list.Columns.Add(L.T("Вещь", "Item"), 168);
            _list.Columns.Add(L.T("Качество", "Quality"), 86);   // сумма первых трёх оставляет «Эффектам» место и при прокрутке
            _list.Columns.Add(L.T("Эффекты", "Effects"), 70);
            _list.SelectedIndexChanged += delegate { OnSelect(); };
            // Шапку столбцов Windows рисует сама и в тёмной теме оставляет белой — рисуем её сами,
            // строки же — штатно.
            _list.OwnerDraw = true;
            _list.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            _list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                // строки тоже свои: системная подсветка выбранной строки в тёмной теме — белое пятно
                bool sel = e.Item.Selected;
                using (SolidBrush b = new SolidBrush(sel ? Theme.Sel : Theme.Entry)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _list.Font, text, sel ? Theme.SelText : e.Item.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            // последний столбец — до правого края, чтобы за ним не белела пустая шапка
            _list.Resize += delegate { FitLastColumn(); };
            _list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                Color back = Theme.Dark ? Color.FromArgb(0x2d, 0x33, 0x3b) : SystemColors.Control;
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                Rectangle text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, _list.Font, text, Theme.Fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 2;
            left.Margin = new Padding(0);
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(_list, 0, 0);
            FlowLayoutPanel give = new FlowLayoutPanel();
            give.AutoSize = true;
            give.WrapContents = false;
            give.Margin = new Padding(0, 6, 0, 0);
            _give = new Button();
            _give.Text = L.T("Добавить вещь…", "Add item…");
            _give.AutoSize = true;
            _give.Click += delegate { OnGive(); };
            give.Controls.Add(_give);
            _replace = new Button();
            _replace.Text = ReplaceText;
            _replace.AutoSize = true;
            _replace.Click += delegate { OnReplace(); };
            give.Controls.Add(_replace);
            _delete = new Button();
            _delete.Text = L.T("Удалить", "Delete");
            _delete.AutoSize = true;
            _delete.Click += delegate { OnDelete(); };
            give.Controls.Add(_delete);
            left.Controls.Add(give, 0, 1);
            _tips.SetToolTip(_give, L.T("Новая вещь в сумку: снаряжение — легендарное, эффекты — как у выпавшей вещи.",
                                        "A new item into the bag: gear comes legendary, with effects rolled as on a dropped item."));
            _tips.SetToolTip(_replace, L.T("Надетую — на другую (старая уйдёт в сумку); вещь в сумке — на другую; в пустое место — надеть.",
                                           "A worn item for another (the old one goes to the bag); a bag item for another; an empty slot — put one on."));
            _tips.SetToolTip(_delete, L.T("Убрать вещь из сумки совсем. До записи в игру можно передумать: «Сбросить правки».",
                                          "Remove the item from the bag for good. Until it is written to the game you can change your mind: “Reset edits”."));
            root.Controls.Add(left, 0, 2);

            _editor = new Panel();
            _editor.Dock = DockStyle.Fill;
            _editor.AutoScroll = true;
            _editor.Padding = new Padding(14, 0, 4, 0);
            root.Controls.Add(_editor, 1, 2);

            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.AutoSize = true;
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnCount = 1;
            bottom.Margin = new Padding(0, 8, 0, 0);

            _status = new Label();
            _status.AutoSize = true;
            _status.MaximumSize = new Size(940, 0);
            _status.Margin = new Padding(0, 0, 0, 6);
            bottom.Controls.Add(_status);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.WrapContents = false;

            _save = new Button();
            _save.Text = L.T("Записать в игру", "Write to game");
            _save.AutoSize = true;
            _save.Padding = new Padding(8, 2, 8, 2);
            _save.Click += OnSave;
            buttons.Controls.Add(_save);

            _resetAll = new Button();
            _resetAll.Text = L.T("Сбросить правки", "Reset edits");
            _resetAll.AutoSize = true;
            _resetAll.Click += delegate
            {
                ReloadAll();
                SetStatus(L.T("Правки сброшены: в окне то, что сейчас в игре.", "Edits reset: the window shows what is in the game now."), false);
            };
            buttons.Controls.Add(_resetAll);

            Button close = new Button();
            close.Text = L.T("Закрыть", "Close");
            close.AutoSize = true;
            close.Click += delegate { Close(); };
            buttons.Controls.Add(close);

            _pending = Muted("");
            _pending.Margin = new Padding(12, 8, 0, 0);
            buttons.Controls.Add(_pending);
            bottom.Controls.Add(buttons);

            Label hint = Muted(L.T("Правится снаряжение героя с начатой вылазкой — вернувшегося в крепость или стоящего в "
                             + "подземелье. Сколько эффектов оставит игра: у вещи — качество + её врождённые "
                             + "эффекты; у кольца — очки, качество + 2. Артефакты только видны: их свойства зашиты в игре. "
                             + "Вещи можно выдавать, заменять и удалять: мест в сумке — сколько дают Склады.",
                             "You edit the gear of a hero with a run in progress — back in the fortress or standing in the "
                             + "dungeon. How many effects the game keeps: on an item — quality + its built-in effects; "
                             + "on a ring — points, quality + 2. Artifacts are view-only: their properties are built into the game. "
                             + "Items can be added, replaced and deleted: the bag holds as many as the Warehouse allows."));
            hint.AutoSize = true;
            hint.MaximumSize = new Size(940, 0);
            hint.Margin = new Padding(0, 8, 0, 0);
            bottom.Controls.Add(hint);

            root.Controls.Add(bottom, 0, 3);
            root.SetColumnSpan(bottom, 2);
            Controls.Add(root);
        }

        private static Label Muted(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.ApplyToForm(this);
            PaintList();
            _gameRunning = _engine.GameRunning();
            ReloadAll();

            // первым открываем героя, выбранного в игре, если у него есть вещи; иначе первого с вещами
            IntValue cur = _engine.Read("curChar");
            int start = cur.State == ValueState.Ok && cur.Value >= 0 && cur.Value < GameData.Heroes.Length ? cur.Value : 0;
            if (GearOf(start).State != GearState.Ok)
                for (int i = 0; i < GameData.Heroes.Length; i++)
                    if (GearOf(i).State == GearState.Ok) { start = i; break; }
            SelectHero(start);
            _timer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && AnyChanged())
            {
                DialogResult r = MessageBox.Show(this, L.T("Есть незаписанные правки снаряжения. Закрыть без записи?",
                                                           "There are unwritten gear edits. Close without writing?"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
            }
            _timer.Stop();
            base.OnFormClosing(e);
            if (!e.Cancel) _closing = true;
        }

        // --- данные ---------------------------------------------------------

        /// <summary>Перечитать игру: правки во всех героях пропадают.</summary>
        private void ReloadAll()
        {
            _baseline = _engine.Load();
            _heroes.Clear();
            _selected = null;
            if (_hero >= 0) SelectHero(_hero);
            RefreshControls();
        }

        private HeroGear GearOf(int hero)
        {
            HeroGear g;
            if (!_heroes.TryGetValue(hero, out g))
            {
                g = HeroGear.Load(hero,
                    new HeroGear.TextReader(delegate(string key)
                    {
                        // записи снаряжения — из снимка, на котором стоит окно; прочее — живьём
                        return GearRules.IsGearKey(key) ? _baseline.GetText(key) : _engine.ReadText(key);
                    }),
                    new HeroGear.IntReader(_engine.Read), _catalog);
                _heroes[hero] = g;
            }
            return g;
        }

        private bool AnyChanged()
        {
            foreach (HeroGear g in _heroes.Values) if (g.Changed) return true;
            return false;
        }

        // --- герои и список -------------------------------------------------------

        private void SelectHero(int index)
        {
            _hero = index;
            // сразу открыть первую вещь, которую можно править, — справа не пусто
            _selected = null;
            foreach (GearItem it in GearOf(index).Items)
                if (it.Editable) { _selected = it; break; }
            ShowHero();
        }

        private void ShowHero()
        {
            HeroGear g = GearOf(_hero);
            _heroState.Text = g.State == GearState.Ok
                ? (g.StateText.Length > 0 ? g.StateText + " " : "")
                  + L.T("В сумке " + g.BagCount + " из " + g.BagLimit + ".", "Bag: " + g.BagCount + " of " + g.BagLimit + ".")
                : g.StateText;
            FillList();
            PaintTabs();
            BuildEditor();
            RefreshControls();
        }

        private void FillList()
        {
            _filling = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                HeroGear g = GearOf(_hero);
                foreach (GearItem it in g.Items)
                {
                    if (it.Deleted) continue;
                    ListViewItem li = new ListViewItem(it.Place);
                    if (it.IsEmptySlot)
                    {
                        li.SubItems.Add(g.SlotLock(it.SlotKey) != null ? L.T("— закрыто —", "— locked —") : L.T("— пусто —", "— empty —"));
                        li.SubItems.Add("");
                        li.SubItems.Add("");
                        li.Tag = it;
                        li.ForeColor = Theme.Muted;
                        _list.Items.Add(li);
                        if (it == _selected) li.Selected = true;
                        continue;
                    }
                    li.SubItems.Add(it.Title + (it.IsNew ? L.T(" (новая)", " (new)") : "") + (it.Changed ? " •" : "") + (it.Count > 1 ? " ×" + it.Count : ""));
                    li.SubItems.Add(it.Editable || IsRing(it) ? GearRules.QualityTitle(it.Quality) : "");
                    // у кольца — очки из бюджета, у прочих — эффекты из нормы
                    li.SubItems.Add(!it.Editable ? (it.Effects.Count > 0 ? it.Effects.Count.ToString() : "")
                                    : it.IsRing ? it.RingPoints + L.T(" из ", " of ") + it.RingBudget + L.T(" оч.", " pts")
                                    : it.Effects.Count + L.T(" из ", " of ") + it.Capacity);
                    li.Tag = it;
                    li.ForeColor = it.Editable ? Theme.Fg : Theme.Muted;
                    _list.Items.Add(li);
                    if (it == _selected) li.Selected = true;
                }
                _list.EndUpdate();
                // вещей много — появилась вертикальная прокрутка: последний столбец пересчитать
                // по новой ширине, иначе вылезает лишняя горизонтальная прокрутка. Список узнаёт о
                // своей прокрутке не сразу, поэтому — после текущего события
                if (_list.IsHandleCreated) _list.BeginInvoke(new MethodInvoker(FitLastColumn));
            }
            finally
            {
                _filling = false;
            }
        }

        /// <summary>
        /// Последний столбец — ровно до правого края видимой части списка (без вертикальной
        /// прокрутки): иначе за ним белеет пустая шапка или вылезает горизонтальная прокрутка.
        /// Системное «растянуть до края» (-2) при вертикальной прокрутке промахивается.
        /// </summary>
        private void FitLastColumn()
        {
            // Окно закрывается: Windows ещё раз меняет размер списка, когда строк в нём уже нет, а
            // Items.Count об этом не знает — GetItemRect(0) бросал исключение (19.09.2026, «Журнал
            // работы»). Подгонять уже нечего.
            if (_closing || IsDisposed || Disposing || _list.IsDisposed || _list.Disposing || !_list.IsHandleCreated) return;
            int n = _list.Columns.Count;
            if (n == 0) return;
            int others = 0;
            for (int i = 0; i < n - 1; i++) others += _list.Columns[i].Width;
            // Вертикальную прокрутку список добавляет позже, чем мы сюда попадаем, — и столбцы
            // вылезают ровно на её ширину. Если вещей больше, чем влезает (плюс строка на шапку),
            // а прокрутки ещё нет, место под неё оставляем сразу.
            int client = _list.ClientSize.Width;
            bool hasV = (GetWindowLong(_list.Handle, -16) & 0x00200000) != 0;   // WS_VSCROLL
            int rowH = 0;
            if (_list.Items.Count > 0)
            {
                // список и его строки в Windows могут на миг разойтись — тогда просто не подгоняем
                try { rowH = _list.GetItemRect(0).Height; }
                catch (ArgumentException) { return; }
            }
            bool needV = rowH > 0 && (_list.Items.Count + 1) * rowH > _list.ClientSize.Height;
            if (needV && !hasV) client -= SystemInformation.VerticalScrollBarWidth;
            // и на пиксель уже края: при ширине ровно впритык список тоже показывает прокрутку
            int w = Math.Max(60, client - others - 1);
            if (_list.Columns[n - 1].Width != w) _list.Columns[n - 1].Width = w;
            // Сузив столбцы, список сам горизонтальную прокрутку не убирает (замерено: столбцы уже
            // видимой части, а прокрутка включена) — убираем, раз всё помещается.
            if (others + w <= client && (GetWindowLong(_list.Handle, -16) & 0x00100000) != 0)
                ShowScrollBar(_list.Handle, 0, false);   // SB_HORZ
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        private void PaintList()
        {
            _list.BackColor = Theme.Entry;
            _list.ForeColor = Theme.Fg;
        }

        private void PaintTabs()
        {
            for (int i = 0; i < _heroTabs.Count; i++)
            {
                Button b = _heroTabs[i];
                bool selected = i == _hero;
                HeroGear g;
                bool changed = _heroes.TryGetValue(i, out g) && g.Changed;
                b.Text = GameData.Heroes[i].Title + (changed ? " •" : "");
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = selected ? Theme.Entry : (Theme.Dark ? Theme.Btn : SystemColors.ControlLight);
                b.ForeColor = selected ? Theme.Fg : Theme.Muted;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = selected ? Theme.Accent : Theme.Border;
                b.Font = selected ? new Font(Font, FontStyle.Bold) : Font;
            }
        }

        private void OnSelect()
        {
            if (_filling) return;
            _selected = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as GearItem : null;
            BuildEditor();
            RefreshGiveButtons();
        }

        // --- выдача, замена, удаление --------------------------------------------------

        private void RefreshGiveButtons()
        {
            if (_give == null || _hero < 0) return;
            HeroGear g = GearOf(_hero);
            bool ok = g.State == GearState.Ok;
            GearItem it = _selected;
            _give.Enabled = ok;
            _replace.Enabled = ok && it != null && !it.Deleted
                               && (it.InBag || (it.SlotKey != null && !(it.IsEmptySlot && g.SlotLock(it.SlotKey) != null)));
            _replace.Text = it != null && it.IsEmptySlot ? L.T("Надеть…", "Put on…") : ReplaceText;
            _delete.Enabled = ok && it != null && it.InBag && !it.Deleted;
        }

        private static string ReplaceText { get { return L.T("Заменить…", "Replace…"); } }

        /// <summary>Хвост сообщений о выдаче: правка ещё не в игре.</summary>
        private static string AfterWrite
        {
            get { return L.T(" В игру попадёт после «Записать в игру».", " It reaches the game after “Write to game”."); }
        }

        private ItemInfo Pick(string slotKey, string purpose)
        {
            using (ItemPickerForm f = new ItemPickerForm(_catalog, _hero, slotKey, purpose))
            {
                return f.ShowDialog(this) == DialogResult.OK ? f.Chosen : null;
            }
        }

        private void OnGive()
        {
            HeroGear g = GearOf(_hero);
            if (g.State != GearState.Ok) { SetStatus(g.StateText, true); return; }
            if (g.BagCount >= g.BagLimit) { SetStatus(HeroGear.BagFull, true); return; }
            ItemInfo info = Pick(null, L.T("Добавить вещь в сумку — ", "Add an item to the bag — ") + GameData.Heroes[_hero].Title);
            if (info == null) return;
            string no = g.AddToBag(info);
            if (no == null) _selected = g.LastTouched;
            AfterChange(no);
            if (no == null) SetStatus(L.T("В сумку: ", "To the bag: ") + info.Title + "." + AfterWrite, false);
        }

        private void OnReplace()
        {
            HeroGear g = GearOf(_hero);
            GearItem target = _selected;
            if (target == null) { SetStatus(L.T("Выберите вещь слева.", "Choose an item on the left."), true); return; }
            string purpose;
            string slot = null;
            if (target.InBag) purpose = L.T("Заменить в сумке: ", "Replace in the bag: ") + target.Title;
            else
            {
                slot = target.SlotKey;
                purpose = (target.IsEmptySlot ? L.T("Надеть: ", "Put on: ") : L.T("Заменить: ", "Replace: "))
                        + (L.Ru ? target.Place.ToLowerInvariant() : target.Place);
            }
            ItemInfo info = Pick(slot, purpose + " — " + GameData.Heroes[_hero].Title);
            if (info == null) return;
            bool wornFromSave = !target.InBag && !target.IsEmptySlot && !target.IsNew;
            string no = g.Replace(target, info);
            if (no == null) _selected = g.LastTouched;
            AfterChange(no);
            if (no == null)
                SetStatus(wornFromSave
                    ? L.T("Надето: " + info.Title + ", «" + target.Title + "» — в сумку.",
                          "Worn: " + info.Title + ", “" + target.Title + "” goes to the bag.") + AfterWrite
                    : L.T("Готово: ", "Done: ") + info.Title + "." + AfterWrite, false);
        }

        private void OnDelete()
        {
            HeroGear g = GearOf(_hero);
            GearItem it = _selected;
            string no = g.Delete(it);
            if (no == null) _selected = null;
            AfterChange(no);
            if (no == null)
                SetStatus(L.T("Удалено из сумки: " + it.Title + ". В игре пропадёт после записи; передумали — «Сбросить правки».",
                              "Deleted from the bag: " + it.Title + ". It disappears from the game after writing; changed your mind — “Reset edits”."), false);
        }

        // --- правая часть: правка вещи ------------------------------------------------

        private void BuildEditor()
        {
            _editor.SuspendLayout();
            _editor.Controls.Clear();

            FlowLayoutPanel col = new FlowLayoutPanel();
            col.FlowDirection = FlowDirection.TopDown;
            col.WrapContents = false;
            col.AutoSize = true;
            col.Location = new Point(_editor.Padding.Left, 0);

            GearItem it = _selected;
            if (it == null)
            {
                col.Controls.Add(Muted(GearOf(_hero).State == GearState.Ok ? L.T("Выберите вещь слева.", "Choose an item on the left.") : ""));
            }
            else
            {
                Label title = new Label();
                title.Text = it.Title;
                title.AutoSize = true;
                title.Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold);
                title.Margin = new Padding(0, 0, 0, 2);
                if (it.IsEmptySlot) title.Text = it.Place;
                col.Controls.Add(title);
                if (it.IsEmptySlot)
                    col.Controls.Add(Muted(L.T("Пустое место на герое.", "An empty slot on the hero.")));
                else
                    col.Controls.Add(Muted(KindTitle(it) + " · "
                                           + (it.InBag ? L.T("в сумке", "in the bag")
                                                       : L.T("надето: " + it.Place.ToLowerInvariant(), "worn: " + it.Place))
                                           + (IsRing(it) ? L.T(" · качество: ", " · quality: ") + GearRules.QualityTitle(it.Quality) : "")
                                           + (it.IsNew ? L.T(" · новая, ещё не записана в игру", " · new, not yet written to the game") : "")
                                           + (it.MovedToBag ? L.T(" · снята при замене", " · taken off when replaced") : "")));

                if (it.IsEmptySlot)
                {
                    Label p = Muted(it.Problem + (GearOf(_hero).SlotLock(it.SlotKey) == null
                        ? L.T(" Нажмите «Надеть…» под списком.", " Press “Put on…” below the list.") : ""));
                    p.MaximumSize = new Size(DescriptionWidth, 0);
                    p.Margin = new Padding(0, 10, 0, 0);
                    col.Controls.Add(p);
                }
                else if (!it.Editable)
                {
                    Label p = Muted(it.Problem);
                    p.MaximumSize = new Size(DescriptionWidth, 0);
                    p.Margin = new Padding(0, 10, 0, 0);
                    col.Controls.Add(p);
                    if (it.Effects.Count > 0 || (it.Info != null && it.Info.Properties.Count > 0))
                        col.Controls.Add(ReadOnlyEffects(it));
                }
                else
                {
                    col.Controls.Add(QualityRow(it));
                    Label cap;
                    if (it.IsRing)
                    {
                        col.Controls.Add(Section(L.T("Эффекты — занято очков " + it.RingPoints + " из " + it.RingBudget,
                                                     "Effects — " + it.RingPoints + " of " + it.RingBudget + " points used")));
                        cap = Muted(L.T("У кольца очки: качество (" + it.Quality + ") + 2. Эффект стоит столько очков, "
                                        + "какая у него ступень. Двух эффектов одного рода кольцо не держит.",
                                        "A ring has points: quality (" + it.Quality + ") + 2. An effect costs as many points "
                                        + "as its tier. A ring cannot hold two effects of the same kind."));
                        cap.MaximumSize = new Size(DescriptionWidth, 0);
                    }
                    else
                    {
                        col.Controls.Add(Section(L.T("Эффекты — ", "Effects — ") + it.Effects.Count + L.T(" из ", " of ") + it.Capacity));
                        cap = Muted(L.T("Сколько оставит игра: качество (" + it.Quality + ") + врождённые (" + it.Info.Defaults.Count + ").",
                                        "What the game keeps: quality (" + it.Quality + ") + built-in (" + it.Info.Defaults.Count + ")."));
                    }
                    cap.Margin = new Padding(0, 0, 0, 4);
                    col.Controls.Add(cap);
                    foreach (GearEffect e in it.Effects) col.Controls.Add(EffectRow(it, e));
                    col.Controls.Add(AddRow(it));
                    col.Controls.Add(Section(L.T("Проклятия", "Curses")));
                    if (it.Curses.Count == 0) col.Controls.Add(Muted(L.T("Проклятий нет.", "No curses.")));
                    foreach (string c in it.Curses) col.Controls.Add(CurseRow(it, c));

                    if (it.Changed && !it.IsNew && !it.MovedToBag)
                    {
                        Button reset = new Button();
                        reset.Text = L.T("Вернуть вещь как в игре", "Restore the item as in the game");
                        reset.AutoSize = true;
                        reset.Margin = new Padding(0, 16, 0, 0);
                        reset.Click += delegate { it.Reset(); AfterChange(null); };
                        col.Controls.Add(reset);
                    }
                }
            }

            _editor.Controls.Add(col);
            Theme.Apply(_editor);
            _editor.ResumeLayout();
        }

        /// <summary>Кольцо: правкой не трогаем, но качество у него настоящее — показываем.</summary>
        private static bool IsRing(GearItem it)
        {
            return it.Info != null && it.Info.Class == "SO_ItemRing";
        }

        private static string KindTitle(GearItem it)
        {
            if (it.Info == null) return L.T("вещь", "item");
            switch (it.Info.Kind)
            {
                case GearKind.Head: return L.T("Шлем", "Helmet");
                case GearKind.Body: return L.T("Доспех", "Body armor");
                case GearKind.Shield: return L.T("Вторая рука (щит, гримуар…)", "Off-hand (shield, grimoire…)");
                case GearKind.Weapon: return L.T("Оружие", "Weapon");
                default:
                    if (it.Info.Class == "SO_ItemRing") return L.T("Кольцо", "Ring");
                    if (it.Info.Class == "SO_ItemTrash") return L.T("Артефакт", "Artifact");
                    if (it.Info.Class == "SO_ItemUsable") return L.T("Расходник", "Consumable");
                    return L.T("Вещь", "Item");
            }
        }

        /// <summary>
        /// Эффекты вещи, которую редактор не правит (кольца, артефакты) — только посмотреть.
        /// У артефакта свойства живут в самой игре, а не в сохранении, — берём их из справочника.
        /// </summary>
        private Control ReadOnlyEffects(GearItem it)
        {
            FlowLayoutPanel box = Column();
            if (it.Effects.Count > 0)
            {
                box.Controls.Add(Section(L.T("Эффекты", "Effects")));
                foreach (GearEffect e in it.Effects)
                {
                    Label l = new Label();
                    l.Text = "•  " + e.Title;
                    l.AutoSize = true;
                    l.Margin = new Padding(0, 4, 0, 0);
                    box.Controls.Add(l);
                    string d = e.Description;
                    if (d.Length > 0) box.Controls.Add(Description(d, 14));
                }
            }
            if (it.Info != null && it.Info.Properties.Count > 0)
            {
                box.Controls.Add(Section(L.T("Свойства", "Properties")));
                foreach (EffectInfo p in it.Info.Properties)
                {
                    string d = p.DescribeAt(p.DefaultLevel, "");
                    Label l = new Label();
                    l.Text = "•  " + (d.Length > 0 ? d : p.Title);
                    l.AutoSize = true;
                    l.MaximumSize = new Size(DescriptionWidth, 0);
                    l.Margin = new Padding(0, 4, 0, 0);
                    box.Controls.Add(l);
                }
            }
            return box;
        }

        /// <summary>Ширина серых строк-описаний: по правой части окна, длинное переносится.</summary>
        private const int DescriptionWidth = 420;

        /// <summary>Серая строка «что делает эффект» под его названием.</summary>
        private static Label Description(string text, int indent)
        {
            Label d = Muted(text);
            d.MaximumSize = new Size(DescriptionWidth - indent, 0);
            d.Margin = new Padding(indent, 0, 0, 6);
            return d;
        }

        private static FlowLayoutPanel Column()
        {
            FlowLayoutPanel box = new FlowLayoutPanel();
            box.FlowDirection = FlowDirection.TopDown;
            box.WrapContents = false;
            box.AutoSize = true;
            box.Margin = new Padding(0);
            return box;
        }

        private Label Section(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font(Font, FontStyle.Bold);
            l.Margin = new Padding(0, 14, 0, 2);
            return l;
        }

        private Control QualityRow(GearItem it)
        {
            FlowLayoutPanel row = Row();
            row.Margin = new Padding(0, 12, 0, 0);
            Label l = new Label();
            l.Text = L.T("Качество", "Quality");
            l.AutoSize = true;
            l.Font = new Font(Font, FontStyle.Bold);
            l.Margin = new Padding(0, 6, 10, 0);
            row.Controls.Add(l);

            ComboBox q = new ComboBox();
            q.DropDownStyle = ComboBoxStyle.DropDownList;
            q.Width = 170;
            List<int> values = new List<int>();
            for (int i = 0; i <= GearRules.MaxQuality; i++) values.Add(i);
            if (!values.Contains(it.Quality)) values.Add(it.Quality);   // что уже стоит — показываем как есть
            foreach (int v in values) q.Items.Add(new Choice(v, Capitalize(GearRules.QualityTitle(v))));
            q.SelectedIndex = values.IndexOf(it.Quality);
            q.SelectedIndexChanged += delegate
            {
                Choice c = (Choice)q.SelectedItem;
                if (c.Value == it.Quality) return;
                AfterChange(it.SetQuality(c.Value));
            };
            row.Controls.Add(q);
            return row;
        }

        private Control EffectRow(GearItem it, GearEffect e)
        {
            FlowLayoutPanel row = Row();
            Label name = new Label();
            name.Text = e.Title + (e.IsDefault ? L.T(" (врождённый)", " (built-in)") : "")
                      + (e.Info == null ? L.T(" (нет в справочнике)", " (not in the catalog)") : "");
            name.AutoSize = false;
            name.Width = 230;
            name.Height = 24;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.AutoEllipsis = true;
            // подсказка — полное название (длинное в строке обрезается) и что эффект делает
            string what = e.Description;
            _tips.SetToolTip(name, what.Length > 0 ? name.Text + "\n" + what : name.Text);
            row.Controls.Add(name);

            if (e.Info != null && e.Info.MaxTier > 0)
            {
                ComboBox tier = new ComboBox();
                tier.DropDownStyle = ComboBoxStyle.DropDownList;
                tier.Width = 110;
                for (int t = 0; t <= e.Info.MaxTier; t++)
                    tier.Items.Add(new Choice(t, L.T("ступень ", "tier ") + (t + 1) + ": " + e.Info.TierLabel(t)));
                tier.SelectedIndex = Math.Max(0, Math.Min(e.Tier, e.Info.MaxTier));
                tier.SelectedIndexChanged += delegate
                {
                    Choice c = (Choice)tier.SelectedItem;
                    if (c.Value == e.Tier) return;
                    AfterChange(it.SetTier(e, c.Value));
                };
                row.Controls.Add(tier);
            }
            else
            {
                Label none = Muted(e.Info == null ? "" : L.T("без ступеней", "no tiers"));
                none.AutoSize = false;
                none.Width = 110;
                none.Height = 24;
                none.TextAlign = ContentAlignment.MiddleLeft;
                row.Controls.Add(none);
            }

            if (!e.IsDefault)
            {
                Button remove = new Button();
                remove.Text = L.T("Убрать", "Remove");
                remove.AutoSize = true;
                remove.Margin = new Padding(6, 0, 0, 0);
                remove.Click += delegate { AfterChange(it.RemoveEffect(e)); };
                row.Controls.Add(remove);
            }
            if (what.Length == 0) return row;

            // под строкой — что эффект делает на выбранной ступени; после смены ступени окно
            // перестраивается, и число здесь обновляется само
            FlowLayoutPanel box = Column();
            box.Controls.Add(row);
            box.Controls.Add(Description(what, 0));
            return box;
        }

        private Control AddRow(GearItem it)
        {
            FlowLayoutPanel row = Row();
            row.Margin = new Padding(0, 6, 0, 0);
            List<EffectInfo> addable = it.Addable();
            bool full = it.IsRing ? it.RingPoints >= it.RingBudget : it.Effects.Count >= it.Capacity;

            ComboBox pick = new ComboBox();
            pick.DropDownStyle = ComboBoxStyle.DropDownList;
            pick.Width = 346;
            pick.MaxDropDownItems = 20;
            // строки рисуем сами: так видно, на какой эффект наведён курсор, — и рядом его описание
            pick.DrawMode = DrawMode.OwnerDrawFixed;
            pick.ItemHeight = Font.Height + 4;
            pick.DrawItem += delegate(object s, DrawItemEventArgs e) { DrawEffectChoice(pick, it, e); };
            pick.DropDownClosed += delegate { HideHover(); };
            foreach (EffectInfo e in addable)
            {
                string range = e.Range();
                pick.Items.Add(new EffectChoice(e, e.Title + (range.Length > 0 ? "  (" + range + ")" : "")));
            }
            if (pick.Items.Count > 0) pick.SelectedIndex = 0;
            pick.Enabled = !full && pick.Items.Count > 0;
            row.Controls.Add(pick);

            Button add = new Button();
            add.Text = L.T("Добавить", "Add");
            add.AutoSize = true;
            add.Margin = new Padding(6, 0, 0, 0);
            add.Enabled = pick.Enabled;
            add.Click += delegate
            {
                EffectChoice c = pick.SelectedItem as EffectChoice;
                if (c == null) return;
                // сразу высшая ступень (у кольца — высшая, что влезает в очки), её можно сменить
                AfterChange(it.AddEffect(c.Effect, it.TierForNew(c.Effect)));
            };
            row.Controls.Add(add);

            FlowLayoutPanel box = Column();
            box.Controls.Add(row);
            if (full)
            {
                string noRoom = it.IsRing ? L.T("Очков кольца не осталось", "No ring points left") : L.T("Свободных ячеек нет", "No free slots");
                Label why = Muted(it.Quality < GearRules.MaxQuality
                    ? noRoom + L.T(" — чтобы добавить эффект, поднимите качество", " — to add an effect, raise the quality")
                             + (it.IsRing ? L.T(" или понизьте ступени.", " or lower tiers.") : ".")
                    : noRoom + (it.IsRing ? L.T(": при легендарном качестве больше очков не будет — понизьте ступени.",
                                                ": legendary quality gives no more points — lower tiers.")
                                          : L.T(": при легендарном качестве больше эффектов игра не оставит.",
                                                ": at legendary quality the game keeps no more effects.")));
                why.MaximumSize = new Size(DescriptionWidth, 0);
                why.Margin = new Padding(0, 2, 0, 0);
                box.Controls.Add(why);
            }
            else if (pick.Items.Count > 0)
            {
                // что даст выбранный эффект: он добавится сразу на высшей ступени
                Label preview = Description("", 0);
                preview.Margin = new Padding(0, 2, 0, 0);
                MethodInvoker show = delegate
                {
                    EffectChoice c = pick.SelectedItem as EffectChoice;
                    string d = c != null ? c.Effect.DescribeAt(it.TierForNew(c.Effect), "") : "";
                    preview.Text = d.Length > 0 ? L.T("Даст: ", "Gives: ") + d : "";
                };
                pick.SelectedIndexChanged += delegate { show(); };
                show();
                box.Controls.Add(preview);
            }
            return box;
        }

        /// <summary>Строка списка «Добавить»; под курсором в раскрытом списке — описание рядом.</summary>
        private void DrawEffectChoice(ComboBox pick, GearItem it, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            EffectChoice c = pick.Items[e.Index] as EffectChoice;
            if (c == null) return;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool hot = (e.State & DrawItemState.Selected) != 0 && !edit;
            Color back = hot ? Theme.Sel : (Theme.Dark ? Theme.Entry : SystemColors.Window);
            Color fore = hot ? Theme.SelText : (Theme.Dark ? Theme.Fg : SystemColors.WindowText);
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            Rectangle text = new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, c.ToString(), pick.Font, text, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (hot && pick.DroppedDown && e.Index != _hoverIndex) ShowHover(pick, it, c, e.Index, e.Bounds);
        }

        private void ShowHover(ComboBox pick, GearItem it, EffectChoice c, int index, Rectangle row)
        {
            // с тем числом, с каким эффект добавится: высшая ступень (у кольца — какая влезет в очки)
            string d = c.Effect.DescribeAt(it.TierForNew(c.Effect), "");
            if (d.Length == 0) { HideHover(); return; }
            _hoverIndex = index;
            _hoverText = c.Effect.Title + " — " + d;
            // справа от раскрытого списка, напротив строки под курсором
            Point screen = pick.PointToScreen(new Point(pick.Width + 8, pick.Height + row.Y));
            Point at = PointToClient(screen);
            _hoverTip.Show(_hoverText, this, at.X, at.Y);
        }

        private void HideHover()
        {
            _hoverIndex = -1;
            if (IsHandleCreated) _hoverTip.Hide(this);
        }

        /// <summary>Подсказка в цветах окна: системная в тёмной теме — белое пятно.</summary>
        private void SetUpHoverTip()
        {
            _hoverTip.OwnerDraw = true;
            _hoverTip.UseAnimation = false;
            _hoverTip.UseFading = false;
            _hoverTip.Popup += delegate(object s, PopupEventArgs e)
            {
                Size sz = TextRenderer.MeasureText(_hoverText, Font, new Size(HoverWidth, 0), TextFormatFlags.WordBreak);
                e.ToolTipSize = new Size(Math.Min(sz.Width, HoverWidth) + 16, sz.Height + 12);
            };
            _hoverTip.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Dark ? Theme.Btn : SystemColors.Info)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                Rectangle text = new Rectangle(8, 6, e.Bounds.Width - 16, e.Bounds.Height - 12);
                TextRenderer.DrawText(e.Graphics, _hoverText, Font, text, Theme.Dark ? Theme.Fg : SystemColors.InfoText,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            };
        }

        private Control CurseRow(GearItem it, string curse)
        {
            FlowLayoutPanel row = Row();
            EffectInfo info = _catalog.EffectByPath(curse.Split('~')[0]);
            Label name = new Label();
            name.Text = info != null ? info.Title : curse;
            name.AutoSize = false;
            name.Width = 230;
            name.Height = 24;
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.AutoEllipsis = true;
            string what = info != null ? info.DescribeAt(0, "") : "";
            _tips.SetToolTip(name, what.Length > 0 ? name.Text + "\n" + what : name.Text);
            row.Controls.Add(name);

            Button remove = new Button();
            remove.Text = L.T("Снять проклятие", "Remove curse");
            remove.AutoSize = true;
            remove.Click += delegate { AfterChange(it.RemoveCurse(curse)); };
            row.Controls.Add(remove);
            if (what.Length == 0) return row;

            FlowLayoutPanel box = Column();
            box.Controls.Add(row);
            box.Controls.Add(Description(what, 0));
            return box;
        }

        private static FlowLayoutPanel Row()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Margin = new Padding(0, 1, 0, 1);
            return row;
        }

        private static string Capitalize(string s)
        {
            return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1);
        }

        /// <summary>После любой правки: сообщить об отказе, перерисовать строку, правку, кнопки.</summary>
        private void AfterChange(string refusal)
        {
            if (refusal != null) SetStatus(refusal, true);
            else SetStatus("", false);
            // Перестройка удаляет тот самый список или кнопку, из обработчика которых нас позвали.
            // Удалять элемент посреди его же события — путь к редким сбоям, поэтому откладываем
            // на момент сразу после события.
            BeginInvoke(new MethodInvoker(delegate
            {
                ShowHero();   // и строку героя: в ней число вещей в сумке
            }));
        }

        // --- запись и состояние ---------------------------------------------------

        private void RefreshControls()
        {
            int items = 0;
            foreach (HeroGear g in _heroes.Values)
                foreach (GearItem it in g.Items) if (it.Changed) items++;
            _pending.Text = items == 0 ? "" : L.T("Изменено вещей: ", "Items changed: ") + items;
            _save.Enabled = items > 0 && !_gameRunning;
            _resetAll.Enabled = items > 0;
            RefreshGiveButtons();
            if (_gameRunning && _status.Text.Length == 0)
                SetStatus(L.T("Игра запущена — записывать нельзя: при выходе она сохранит свои вещи поверх правки.",
                              "The game is running — writing is not possible: on exit it saves its own items over the edit."), true);
        }

        private bool _statusIsError;

        private void SetStatus(string text, bool error)
        {
            _statusIsError = error;
            _status.Text = text;
            _status.ForeColor = error
                ? (Theme.Dark ? Color.FromArgb(0xf0, 0x80, 0x78) : Color.FromArgb(0xb0, 0x20, 0x20))
                : Theme.Fg;
        }

        private void CheckGame()
        {
            bool running = _engine.GameRunning();
            if (running == _gameRunning) return;
            _gameRunning = running;
            if (!running && _statusIsError) SetStatus(L.T("Игра закрыта — можно записывать.", "The game is closed — you can write."), false);
            RefreshControls();
        }

        private void OnSave(object sender, EventArgs e)
        {
            Dictionary<string, string> texts = new Dictionary<string, string>();
            Dictionary<string, string> titles = new Dictionary<string, string>();
            List<string> lines = new List<string>();
            try
            {
                foreach (HeroGear g in _heroes.Values)
                {
                    if (!g.Changed) continue;
                    foreach (KeyValuePair<string, string> pair in g.BuildTexts())
                    {
                        texts[pair.Key] = pair.Value;
                        string what = g.DescribeChanges(pair.Key);
                        string where = KeyTitle(g, pair.Key);
                        titles[pair.Key] = where + " — " + what;
                        lines.Add(where + ": " + what);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus(L.T("Не смог собрать запись: ", "Could not build the record: ") + ex.Message, true);
                return;
            }
            if (texts.Count == 0) { SetStatus(L.T("Менять нечего.", "Nothing to change."), false); return; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("Записать в игру?", "Write to the game?"));
            sb.AppendLine();
            foreach (string l in lines) sb.Append("•  ").AppendLine(l);
            sb.AppendLine();
            sb.Append(L.T("Перед записью будет сделана резервная копия; правку потом можно отменить.",
                          "A backup is made before writing; the edit can be undone later."));
            if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Outcome o = _engine.SaveGear(_baseline, texts, titles);
            if (o.Ok)
            {
                ChangedGame = true;
                GearItem keep = _selected;
                string place = keep != null ? keep.Place + "|" + keep.Path : null;
                ReloadAll();
                ReselectByPlace(place);
                SetStatus(L.T("Записал. Правку можно отменить в «Прошлых правках» главного окна.",
                              "Written. The edit can be undone in “Past edits” in the main window."), false);
                return;
            }
            if (o.Stale) ReloadAll();
            SetStatus(o.Message, true);
            MessageBox.Show(this, o.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Подпись записи для вопроса и журнала: у записей этажа — с именем героя.</summary>
        private static string KeyTitle(HeroGear g, string key)
        {
            if (!GearRules.IsFloorKey(key)) return Engine.TitleOfGearKey(key);
            bool equip = key == GearRules.FloorEquipKey || key == GearRules.StaticEquipKey;
            return GameData.Heroes[g.HeroIndex].Title
                 + (g.InStatic ? L.T(" (в особой локации): ", " (in a special location): ") : L.T(" (в подземелье): ", " (in the dungeon): "))
                 + (equip ? L.T("снаряжение", "gear") : L.T("сумка", "bag"));
        }

        private void ReselectByPlace(string placeAndPath)
        {
            if (placeAndPath == null) return;
            foreach (GearItem it in GearOf(_hero).Items)
            {
                if (it.Place + "|" + it.Path != placeAndPath) continue;
                _selected = it;
                break;
            }
            FillList();
            BuildEditor();
        }

        // --- элементы выпадающих списков ---------------------------------------------

        private sealed class Choice
        {
            public readonly int Value;
            private readonly string _text;
            public Choice(int value, string text) { Value = value; _text = text; }
            public override string ToString() { return _text; }
        }

        private sealed class EffectChoice
        {
            public readonly EffectInfo Effect;
            private readonly string _text;
            public EffectChoice(EffectInfo effect, string text) { Effect = effect; _text = text; }
            public override string ToString() { return _text; }
        }
    }
}
