using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Выбор вещи для выдачи или замены: слева виды вещей, логично
    /// сгруппированные (оружие по виду боя, вторая рука, броня, прочее), справа вещи этого вида от
    /// слабых сверху к сильным снизу. Оружие и вторая рука чужих классов — в свёрнутой ветке: не
    /// убраны, но и не мешают.
    /// </summary>
    internal sealed class ItemPickerForm : Form
    {
        private readonly Catalog _catalog;
        private readonly int _hero;
        private readonly string _slotKey;
        private TreeView _tree;
        private ListBox _items;
        private Label _about;
        private Button _ok;

        /// <summary>Что выбрали; null — ничего.</summary>
        public ItemInfo Chosen;

        // подвиды оружия — из перевода игры (Items/Types/weapon_…)
        private static readonly string[] SubtypesRu =
            { "Меч", "Кинжал", "Топор", "Молот", "Копьё", "Лук", "Арбалет", "Посох", "Жезл" };
        private static readonly string[] SubtypesEn =
            { "Sword", "Dagger", "Axe", "Hammer", "Spear", "Bow", "Crossbow", "Staff", "Wand" };

        /// <summary>Вторая рука по классам; у берсерка там второе оружие.</summary>
        private static readonly string[] OffhandsRu = { "Щиты", "Стрелы", "Книги", "Болты", "", "Гримуары" };
        private static readonly string[] OffhandsEn = { "Shields", "Arrows", "Books", "Bolts", "", "Grimoires" };

        /// <param name="slotKey">место на герое, куда выбираем (null — в сумку, подходит всё)</param>
        public ItemPickerForm(Catalog catalog, int hero, string slotKey, string purpose)
        {
            _catalog = catalog;
            _hero = hero;
            _slotKey = slotKey;

            Text = purpose;
            Icon = AppIcon.Get();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(820, 560);
            MinimumSize = new Size(640, 420);
            Padding = new Padding(10);
            BuildLayout();
            FillTree();
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _tree = new TreeView();
            _tree.Dock = DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.FullRowSelect = true;
            _tree.ShowLines = false;
            _tree.BorderStyle = BorderStyle.FixedSingle;
            _tree.AfterSelect += delegate { FillItems(); };
            root.Controls.Add(_tree, 0, 0);

            _items = new ListBox();
            _items.Dock = DockStyle.Fill;
            _items.IntegralHeight = false;
            _items.DrawMode = DrawMode.OwnerDrawFixed;
            _items.ItemHeight = Font.Height + 8;
            _items.DrawItem += DrawItem;
            _items.SelectedIndexChanged += delegate { ShowAbout(); };
            _items.DoubleClick += delegate { Choose(); };
            _items.Margin = new Padding(8, 3, 0, 3);
            root.Controls.Add(_items, 1, 0);

            _about = new Label();
            _about.AutoSize = true;
            _about.MaximumSize = new Size(780, 0);
            _about.Margin = new Padding(0, 8, 0, 4);
            root.Controls.Add(_about, 0, 1);
            root.SetColumnSpan(_about, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            _ok = new Button();
            _ok.Text = L.T("Выбрать", "Choose");
            _ok.AutoSize = true;
            _ok.Padding = new Padding(8, 2, 8, 2);
            _ok.Enabled = false;
            _ok.Click += delegate { Choose(); };
            buttons.Controls.Add(_ok);
            Button cancel = new Button();
            cancel.Text = L.T("Отмена", "Cancel");
            cancel.AutoSize = true;
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);
            Label note = new Label();
            note.AutoSize = true;
            note.ForeColor = SystemColors.GrayText;
            note.Margin = new Padding(12, 8, 0, 0);
            note.Text = L.T("Снаряжение выдаётся легендарным, эффекты — как у выпавшей вещи; их можно поправить потом.",
                            "Gear comes legendary, with effects rolled as on a dropped item; they can be adjusted later.");
            buttons.Controls.Add(note);
            root.Controls.Add(buttons, 0, 2);
            root.SetColumnSpan(buttons, 2);

            AcceptButton = _ok;
            CancelButton = cancel;
            Controls.Add(root);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.ApplyToForm(this);
            _tree.BackColor = Theme.Entry;
            _tree.ForeColor = Theme.Fg;
            _items.BackColor = Theme.Entry;
            _items.ForeColor = Theme.Fg;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        // --- виды вещей -----------------------------------------------------------------

        private sealed class Kind
        {
            public readonly List<ItemInfo> Items = new List<ItemInfo>();
        }

        /// <summary>Подходит ли вещь: можно выдать и, если выбираем в слот, надевается туда.</summary>
        private bool Suits(ItemInfo it)
        {
            if (!it.Givable) return false;
            return _slotKey == null || HeroGear.Fits(_slotKey, it);
        }

        private void FillTree()
        {
            // ключ вида → вещи; своё и общее отдельно от чужого
            Dictionary<string, Kind> kinds = new Dictionary<string, Kind>();
            foreach (ItemInfo it in _catalog.Items)
            {
                if (!Suits(it)) continue;
                if (it.Kind == GearKind.Weapon || it.Kind == GearKind.Shield)
                {
                    List<int> owners = it.Heroes.Count > 0 ? it.Heroes : new List<int>(new int[] { -1 });
                    foreach (int h in owners)
                        Put(kinds, (it.Kind == GearKind.Weapon ? "w" + h + ":" + it.WeaponSub : "o" + h), it);
                }
                else if (it.Kind == GearKind.Head) Put(kinds, "head", it);
                else if (it.Kind == GearKind.Body) Put(kinds, "body", it);
                else if (it.Kind == GearKind.Ring) Put(kinds, "ring", it);
                else if (it.IsArtifact) Put(kinds, "art", it);
                else if (it.IsUsable) Put(kinds, "use", it);
            }
            foreach (Kind k in kinds.Values) k.Items.Sort(Weaker);

            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            TreeNode first = null;
            TreeNode weapons = WeaponBranch(kinds, _hero, L.T("Оружие — ", "Weapons — ") + HeroTitle(_hero));
            if (weapons != null) { _tree.Nodes.Add(weapons); if (first == null) first = FirstLeaf(weapons); }
            TreeNode off = Leaf(kinds, "o" + _hero, OffhandTitle(_hero));
            if (off != null)
            {
                TreeNode hand = new TreeNode(L.T("Вторая рука", "Off-hand"));
                hand.Nodes.Add(off);
                _tree.Nodes.Add(hand);
                if (first == null) first = off;
            }
            TreeNode armor = new TreeNode(L.T("Броня", "Armor"));
            AddLeaf(armor, kinds, "head", L.T("Шлемы", "Helmets"));
            AddLeaf(armor, kinds, "body", L.T("Доспехи", "Body armor"));
            if (armor.Nodes.Count > 0) { _tree.Nodes.Add(armor); if (first == null) first = armor.Nodes[0]; }
            TreeNode other = new TreeNode(L.T("Прочее", "Other"));
            AddLeaf(other, kinds, "ring", L.T("Кольца", "Rings"));
            AddLeaf(other, kinds, "art", L.T("Артефакты", "Artifacts"));
            AddLeaf(other, kinds, "use", L.T("Расходники", "Consumables"));
            if (other.Nodes.Count > 0) { _tree.Nodes.Add(other); if (first == null) first = other.Nodes[0]; }

            // чужие классы — свёрнутой веткой: не убраны, но и не раскрыты
            TreeNode foreign = new TreeNode(L.T("Оружие других классов", "Other classes' weapons"));
            for (int h = 0; h < GameData.Heroes.Length; h++)
            {
                if (h == _hero) continue;
                TreeNode cls = WeaponBranch(kinds, h, HeroTitle(h));
                TreeNode theirs = Leaf(kinds, "o" + h, OffhandTitle(h));
                if (theirs != null)
                {
                    if (cls == null) cls = new TreeNode(HeroTitle(h));
                    cls.Nodes.Add(theirs);
                }
                if (cls != null) foreign.Nodes.Add(cls);
            }
            if (foreign.Nodes.Count > 0) _tree.Nodes.Add(foreign);

            foreach (TreeNode n in _tree.Nodes) if (n != foreign) n.ExpandAll();
            _tree.EndUpdate();
            if (first != null) _tree.SelectedNode = first;
            if (_tree.Nodes.Count == 0) _about.Text = NothingFits;
        }

        private static void Put(Dictionary<string, Kind> kinds, string key, ItemInfo it)
        {
            Kind k;
            if (!kinds.TryGetValue(key, out k)) { k = new Kind(); kinds[key] = k; }
            k.Items.Add(it);
        }

        /// <summary>Оружие класса по виду боя: ближний, дальний, магия — и внутри подвиды.</summary>
        private TreeNode WeaponBranch(Dictionary<string, Kind> kinds, int hero, string title)
        {
            TreeNode root = new TreeNode(title);
            string[] sections = { L.T("Ближний бой", "Melee"), L.T("Дальний бой", "Ranged"), L.T("Магия", "Magic") };
            int[][] subs = { new int[] { 0, 1, 2, 3, 4 }, new int[] { 5, 6 }, new int[] { 7, 8 } };
            for (int s = 0; s < sections.Length; s++)
            {
                TreeNode sec = new TreeNode(sections[s]);
                foreach (int sub in subs[s]) AddLeaf(sec, kinds, "w" + hero + ":" + sub, L.T(SubtypesRu[sub], SubtypesEn[sub]));
                if (sec.Nodes.Count > 0) root.Nodes.Add(sec);
            }
            return root.Nodes.Count > 0 ? root : null;
        }

        private static void AddLeaf(TreeNode parent, Dictionary<string, Kind> kinds, string key, string title)
        {
            TreeNode n = Leaf(kinds, key, title);
            if (n != null) parent.Nodes.Add(n);
        }

        private static TreeNode Leaf(Dictionary<string, Kind> kinds, string key, string title)
        {
            Kind k;
            if (!kinds.TryGetValue(key, out k) || k.Items.Count == 0 || title.Length == 0) return null;
            TreeNode n = new TreeNode(title + "  (" + k.Items.Count + ")");
            n.Tag = k;
            return n;
        }

        private static TreeNode FirstLeaf(TreeNode n)
        {
            if (n.Tag is Kind) return n;
            foreach (TreeNode c in n.Nodes)
            {
                TreeNode f = FirstLeaf(c);
                if (f != null) return f;
            }
            return null;
        }

        private static string HeroTitle(int hero)
        {
            return hero >= 0 && hero < GameData.Heroes.Length ? GameData.Heroes[hero].Title : L.T("Общее", "Shared");
        }

        private static string OffhandTitle(int hero)
        {
            return hero >= 0 && hero < OffhandsRu.Length ? L.T(OffhandsRu[hero], OffhandsEn[hero]) : L.T("Вторая рука", "Off-hand");
        }

        private static string NothingFits { get { return L.T("Сюда нечего надеть.", "Nothing can be put on here."); } }

        /// <summary>От слабых к сильным, при равной силе — от дешёвых к дорогим, потом по названию.</summary>
        private static int Weaker(ItemInfo a, ItemInfo b)
        {
            int c = a.Strength.CompareTo(b.Strength);
            if (c != 0) return c;
            c = a.Price.CompareTo(b.Price);
            if (c != 0) return c;
            return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
        }

        // --- вещи вида ------------------------------------------------------------------

        private void FillItems()
        {
            _items.BeginUpdate();
            _items.Items.Clear();
            Kind k = _tree.SelectedNode != null ? _tree.SelectedNode.Tag as Kind : null;
            if (k != null) foreach (ItemInfo it in k.Items) _items.Items.Add(it);
            _items.EndUpdate();
            if (_items.Items.Count > 0) _items.SelectedIndex = 0;
            ShowAbout();
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ItemInfo it = (ItemInfo)_items.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Theme.Sel : Theme.Entry)) e.Graphics.FillRectangle(b, e.Bounds);
            Color fg = sel ? Theme.SelText : Theme.Fg;
            string stat = it.StatText;
            Rectangle statBox = new Rectangle(e.Bounds.Right - 110, e.Bounds.Y, 104, e.Bounds.Height);
            Rectangle titleBox = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 122, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, it.Title + (it.Unique ? L.T("  · уникальная", "  · unique") : ""), _items.Font, titleBox, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (stat.Length > 0)
                TextRenderer.DrawText(e.Graphics, stat, _items.Font, statBox, sel ? Theme.SelText : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }

        private void ShowAbout()
        {
            ItemInfo it = _items.SelectedItem as ItemInfo;
            _ok.Enabled = it != null;
            if (it == null) { _about.Text = _tree.Nodes.Count == 0 ? NothingFits : L.T("Выберите вид вещи слева.", "Choose a kind of item on the left."); return; }
            string s = it.Title;
            if (it.StatText.Length > 0) s += ", " + it.StatText;
            if (it.Unique) s += L.T(". Уникальная: у героя может быть только одна", ". Unique: a hero can have only one");
            if (it.Kind != GearKind.Other)
                s += it.CanBeRare ? L.T(". Будет легендарной", ". Will be legendary") : L.T(". Качество у неё не меняется", ". Its quality does not change");
            if ((it.Kind == GearKind.Weapon || it.Kind == GearKind.Shield) && it.Heroes.Count > 0 && !it.Heroes.Contains(_hero))
                s += L.T(". Это вещь другого класса: игра такие этому герою не выдаёт, но надеть можно — маг с вещами "
                         + "некроманта атакует нормально (проверено 21.09.2026)",
                         ". This item belongs to another class: the game does not give such items to this hero, but it can be "
                         + "worn — a wizard with necromancer items attacks normally (tested 21.09.2026)");
            _about.Text = s + ".";
        }

        private void Choose()
        {
            ItemInfo it = _items.SelectedItem as ItemInfo;
            if (it == null) return;
            Chosen = it;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
