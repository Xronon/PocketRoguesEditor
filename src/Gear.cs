using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>Эффект на вещи: что за эффект, какая ступень, врождённый ли.</summary>
    internal sealed class GearEffect
    {
        public string Raw;           // исходная строка из сохранения; null — эффект добавлен в редакторе
        public string Path;          // часть до первой «~», как записана в сохранении
        public int Tier;
        public string Tail = "";     // всё после ступени (у брони и оружия пусто)
        public EffectInfo Info;      // null — эффекта нет в справочнике
        public bool IsDefault;       // врождённый эффект вещи — не убирается

        public static GearEffect FromSave(string raw, Catalog cat)
        {
            GearEffect e = new GearEffect();
            e.Raw = raw;
            string[] parts = raw.Split('~');
            e.Path = parts[0];
            int tier;
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out tier))
                e.Tier = tier;
            for (int i = 2; i < parts.Length; i++) e.Tail += "~" + parts[i];
            e.Info = cat.EffectByPath(e.Path);
            return e;
        }

        public static GearEffect New(EffectInfo info, int tier)
        {
            GearEffect e = new GearEffect();
            e.Path = info.Path;
            e.Info = info;
            e.Tier = tier;
            return e;
        }

        private int _origTier = -1;

        /// <summary>Запомнить ступень, с которой эффект пришёл из сохранения.</summary>
        public void RememberOriginal() { _origTier = Tier; }

        public bool TierChanged { get { return Raw != null && Tier != _origTier; } }

        public void ResetTier() { if (Raw != null) Tier = _origTier; }

        /// <summary>Строка для сохранения. Нетронутый эффект пишется ровно как был, байт в байт.</summary>
        public string ToSave()
        {
            if (Raw != null && !TierChanged) return Raw;
            return Path + "~" + Tier.ToString(CultureInfo.InvariantCulture) + Tail;
        }

        public string Title { get { return Info != null ? Info.Title : Path; } }

        /// <summary>Что эффект делает, с числом текущей ступени, — как в игре. Пусто — описания нет.</summary>
        public string Description { get { return Info != null ? Info.DescribeAt(Tier, Tail) : ""; } }

        /// <summary>«Искра Жизни 18» — название и значение на текущей ступени.</summary>
        public string Describe()
        {
            if (Info == null) return Path;
            string v = Info.TierLabel(Tier);
            return v.Length > 0 ? Info.Title + " " + v : Info.Title;
        }
    }

    /// <summary>
    /// Одна вещь героя: где лежит, что это, и её правленое состояние. Исходный JSON-объект
    /// вещи остаётся в тексте сохранения; при записи в нём меняются только поля качества,
    /// эффектов, проклятий и пустых ячеек.
    /// </summary>
    internal sealed partial class GearItem
    {
        public string SourceKey;
        public string Place;
        public JsonNode Node;
        public ItemInfo Info;
        public string Path;
        public int Count;
        public string Problem;       // почему вещь нельзя править (пусто — можно)

        public int OrigQuality;
        public int OrigEmptySlots;
        public List<string> OrigEffects = new List<string>();
        public List<string> OrigCurses = new List<string>();

        public int Quality;
        public List<GearEffect> Effects = new List<GearEffect>();
        public List<GearEffect> OrigEffectObjects = new List<GearEffect>();   // для подписи «что убрано»
        public List<string> Curses = new List<string>();

        public bool Editable { get { return Problem == null; } }

        public string Title
        {
            get
            {
                if (Info != null) return Info.Title;
                int slash = Path.LastIndexOf('/');
                return slash >= 0 ? Path.Substring(slash + 1) : Path;
            }
        }

        /// <summary>
        /// Сколько эффектов игра оставит на вещи: качество + врождённые
        /// (SO_ItemEquip.NormalEffectsCount). Лишнее она срезает при загрузке.
        /// </summary>
        public int Capacity
        {
            get
            {
                if (Info == null) return 0;
                if (IsRing) return RingBudget;
                return Math.Max(0, Quality) + Info.Defaults.Count;
            }
        }

        /// <summary>У кольца пустых ячеек игра не считает (её ветка нормализации их не трогает) — оставляем как было.</summary>
        public int EmptySlots { get { return IsRing ? OrigEmptySlots : Math.Max(0, Capacity - Effects.Count); } }

        // --- кольцо: бюджет очков ---------------------------------------------------------
        // SO_ItemEquip.NormalizeEffects, ветка SO_ItemRing: бюджет = качество + 1; эффекты идут по
        // порядку, каждый «стоит» ступень + 1; пока потрачено не больше бюджета, эффект остаётся,
        // а его ступень зажимается в остаток. Итог без срезов и понижений — ровно когда сумма
        // (ступень + 1) по всем эффектам не больше качество + 2. Это и держит редактор.
        // Ещё: два эффекта одного рода (ArtifactEffects) кольцо не держит — оставит первый
        // (SO_ItemRing.NormalizeEffects), а несовместимые игра на кольцо не кладёт (AddRandomEffect).

        public bool IsRing { get { return Info != null && Info.Kind == GearKind.Ring; } }

        public static int RingBudgetFor(int quality) { return Math.Max(0, quality) + 2; }

        public int RingBudget { get { return RingBudgetFor(Quality); } }

        public int RingPoints
        {
            get
            {
                int sum = 0;
                foreach (GearEffect e in Effects) sum += e.Tier + 1;
                return sum;
            }
        }

        /// <summary>Какую ступень получит новый эффект: высшую — у кольца высшую, что влезает в бюджет.</summary>
        public int TierForNew(EffectInfo info)
        {
            if (!IsRing) return info.MaxTier;
            return Math.Min(info.MaxTier, RingBudget - RingPoints - 1);
        }

        /// <summary>Чем эффект мешает кольцу: тот же род или несовместимость. null — ничем.</summary>
        private string RingClash(EffectInfo info, GearEffect except)
        {
            foreach (GearEffect g in Effects)
            {
                if (g == except || g.Info == null) continue;
                if (info.Kind >= 0 && g.Info.Kind == info.Kind)
                    return L.T("На кольце уже есть эффект того же рода («" + g.Title + "») — игра оставит только один.",
                               "The ring already has an effect of the same kind (“" + g.Title + "”) — the game keeps only one.");
                if (info.ClashesWith(g.Info))
                    return L.T("Этот эффект игра не сочетает с «" + g.Title + "».",
                               "The game does not combine this effect with “" + g.Title + "”.");
            }
            return null;
        }

        public List<string> EffectStrings()
        {
            List<string> list = new List<string>();
            foreach (GearEffect e in Effects) list.Add(e.ToSave());
            return list;
        }

        /// <summary>Поменялись поля вещи: качество, эффекты, проклятия.</summary>
        public bool FieldsChanged
        {
            get
            {
                return Quality != OrigQuality || !Same(EffectStrings(), OrigEffects) || !Same(Curses, OrigCurses);
            }
        }

        /// <summary>Есть что записать: правка полей, новая вещь, удалённая или переложенная в сумку.</summary>
        public bool Changed { get { return !IsEmptySlot && (IsNew || Deleted || MovedToBag || FieldsChanged); } }

        private static bool Same(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // --- правка по правилам игры ---------------------------------------------

        /// <summary>Поменять качество. null — получилось, иначе причина отказа.</summary>
        public string SetQuality(int quality)
        {
            if (!Editable) return Problem;
            if (quality < 0) return L.T("Качество не бывает отрицательным.", "Quality cannot be negative.");
            if (IsRing)
            {
                if (RingPoints > RingBudgetFor(quality))
                    return L.T("При таком качестве у кольца " + RingBudgetFor(quality) + " очков, а эффекты сейчас занимают "
                               + RingPoints + ". Сначала понизьте ступени или уберите эффект — иначе игра срежет сама.",
                               "At this quality the ring has " + RingBudgetFor(quality) + " points, but its effects now take "
                               + RingPoints + ". Lower tiers or remove an effect first — otherwise the game cuts them itself.");
                Quality = quality;
                return null;
            }
            int cap = quality + Info.Defaults.Count;
            if (Effects.Count > cap)
                return L.T("При таком качестве эффектов может быть не больше " + cap + ", а сейчас их " + Effects.Count
                           + ". Сначала уберите лишние — иначе игра срежет их сама.",
                           "At this quality an item can have at most " + cap + " effects, and it has " + Effects.Count
                           + ". Remove the extra ones first — otherwise the game cuts them itself.");
            Quality = quality;
            return null;
        }

        public string SetTier(GearEffect e, int tier)
        {
            if (!Editable) return Problem;
            if (e.Info == null) return L.T("Этого эффекта нет в справочнике — ступень не меняю.",
                                           "This effect is not in the catalog — its tier is left as is.");
            if (tier < 0 || tier > e.Info.MaxTier) return L.T("У эффекта ступени от 1 до ", "The effect has tiers 1 to ") + (e.Info.MaxTier + 1) + ".";
            if (IsRing && RingPoints - (e.Tier + 1) + (tier + 1) > RingBudget)
                return L.T("Не хватает очков кольца: их " + RingBudget + ", а с этой ступенью нужно "
                           + (RingPoints - e.Tier + tier) + ". Поднимите качество или понизьте другой эффект.",
                           "Not enough ring points: it has " + RingBudget + ", and this tier needs "
                           + (RingPoints - e.Tier + tier) + ". Raise the quality or lower another effect.");
            e.Tier = tier;
            return null;
        }

        public string RemoveEffect(GearEffect e)
        {
            if (!Editable) return Problem;
            if (e.IsDefault) return L.T("Это врождённый эффект вещи — игра вернёт его сама, убирать бессмысленно.",
                                        "This is the item's built-in effect — the game restores it, so removing it is pointless.");
            Effects.Remove(e);
            return null;
        }

        /// <summary>Эффекты, которые можно добавить: из пула вещи, своего вида, не из чёрного списка, без повторов.</summary>
        public List<EffectInfo> Addable()
        {
            List<EffectInfo> list = new List<EffectInfo>();
            if (!Editable) return list;
            foreach (EffectInfo e in Info.Pool)
            {
                if (e.Blacklisted || e.Class != Info.EffectClass) continue;
                if (Has(e) || list.Contains(e)) continue;
                if (IsRing && RingClash(e, null) != null) continue;
                list.Add(e);
            }
            list.Sort(delegate(EffectInfo a, EffectInfo b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture); });
            return list;
        }

        public bool Has(EffectInfo info)
        {
            foreach (GearEffect g in Effects) if (g.Info == info) return true;
            return false;
        }

        public string AddEffect(EffectInfo info, int tier)
        {
            if (!Editable) return Problem;
            if (info.Blacklisted) return L.T("Этот эффект игра вычищает при загрузке.", "The game removes this effect on loading.");
            if (info.Class != Info.EffectClass) return L.T("Эффект другого вида — игра его на такой вещи не загрузит.",
                                                           "This effect is for another kind of item — the game will not load it here.");
            if (Has(info)) return L.T("Такой эффект на вещи уже есть — игра оставит только один.",
                                      "The item already has this effect — the game keeps only one.");
            if (IsRing)
            {
                if (!Info.Pool.Contains(info)) return L.T("Такой эффект игра на это кольцо не выкидывает.",
                                                          "The game never rolls this effect on this ring.");
                string clash = RingClash(info, null);
                if (clash != null) return clash;
                int room = RingBudget - RingPoints - 1;
                if (room < 0)
                    return L.T("Очков кольца не осталось: их " + RingBudget + ". Поднимите качество или понизьте ступени.",
                               "No ring points left: it has " + RingBudget + ". Raise the quality or lower tiers.");
                if (tier < 0 || tier > info.MaxTier) tier = info.MaxTier;
                Effects.Add(GearEffect.New(info, Math.Min(tier, room)));
                return null;
            }
            if (Effects.Count >= Capacity)
                return L.T("Свободных ячеек нет: при этом качестве эффектов не больше " + Capacity + ". Поднимите качество.",
                           "No free slots: at this quality an item holds at most " + Capacity + " effects. Raise the quality.");
            if (tier < 0 || tier > info.MaxTier) tier = info.MaxTier;
            Effects.Add(GearEffect.New(info, tier));
            return null;
        }

        public string RemoveCurse(string curse)
        {
            if (!Editable) return Problem;
            Curses.Remove(curse);
            return null;
        }

        /// <summary>Забыть все правки этой вещи — вернуть как в игре.</summary>
        public void Reset()
        {
            Quality = OrigQuality;
            Effects = new List<GearEffect>(OrigEffectObjects);
            foreach (GearEffect e in Effects) e.ResetTier();
            Curses = new List<string>(OrigCurses);
        }

        /// <summary>Правки этой вещи — заменами в тексте сохранения.</summary>
        public List<Replacement> Replacements()
        {
            List<Replacement> list = new List<Replacement>();
            if (IsNew || IsEmptySlot || !FieldsChanged) return list;
            if (Quality != OrigQuality)
                list.Add(new Replacement(Node.Get("quality"), Quality.ToString(CultureInfo.InvariantCulture)));
            List<string> eff = EffectStrings();
            if (!Same(eff, OrigEffects))
                list.Add(new Replacement(Node.Get("effects"), MiniJson.StringArray(eff)));
            if (!Same(Curses, OrigCurses))
                list.Add(new Replacement(Node.Get("curses"), MiniJson.StringArray(Curses)));
            // пустые ячейки игра пересчитывает сама, но пишем честно — как после её же нормализации
            if (EmptySlots != OrigEmptySlots && (Quality != OrigQuality || !Same(eff, OrigEffects)))
                list.Add(new Replacement(Node.Get("emptySlots"), EmptySlots.ToString(CultureInfo.InvariantCulture)));
            return list;
        }

        /// <summary>Что поменялось — одной строкой для журнала и истории.</summary>
        public string DescribeChanges()
        {
            List<string> parts = new List<string>();
            if (Quality != OrigQuality)
                parts.Add(L.T("качество ", "quality ") + GearRules.QualityTitle(OrigQuality) + " → " + GearRules.QualityTitle(Quality));

            foreach (GearEffect e in Effects)
            {
                if (e.Raw == null) parts.Add("+" + e.Describe());
                else if (e.TierChanged) parts.Add(e.Describe() + L.T(" (ступень ", " (tier ") + (e.Tier + 1) + ")");
            }
            foreach (GearEffect e in OrigEffectObjects)
                if (!Effects.Contains(e)) parts.Add("−" + e.Title);
            foreach (string c in OrigCurses) if (!Curses.Contains(c)) parts.Add(L.T("снято проклятие", "curse removed"));
            return Title + ": " + string.Join(", ", parts.ToArray());
        }
    }

    /// <summary>Состояние героя для редактора снаряжения.</summary>
    internal enum GearState { Ok, NoRaid, InLocation, Broken }

    /// <summary>
    /// Снаряжение одного героя: надетое (NeedLoading_ReturnToTheFortress_N) и сумка (CurInventory_N).
    /// Обе записи — приостановленная вылазка героя: их пишет GameManager.ReturnToTheFortress,
    /// читает продолжение вылазки и экран героя, очищает конец вылазки. Если же герой сейчас в
    /// подземелье (он curChar, этаж сохранён), вещи берутся из записей этажа dungData_data и
    /// dungData_invData — формат тот же.
    /// </summary>
    internal sealed partial class HeroGear
    {
        public int HeroIndex;
        public bool InDungeon;       // вещи из записей места в подземелье (этаж или особая локация), а не вылазки
        public bool InStatic;        // это место — особая локация (Лагерь, арена): записи staticData_*
        public GearState State;
        public string StateText = "";
        public string EquipKey;
        public string BagKey;
        public TextValue EquipText;
        public TextValue BagText;
        public JsonNode EquipRoot;
        public JsonNode BagRoot;
        public readonly List<GearItem> Items = new List<GearItem>();

        public delegate TextValue TextReader(string key);
        public delegate IntValue IntReader(string key);

        public static HeroGear Load(int heroIndex, TextReader readText, IntReader readInt, Catalog cat)
        {
            HeroGear g = new HeroGear();
            g.HeroIndex = heroIndex;
            g.EquipKey = GearRules.EquipKey(heroIndex);
            g.BagKey = GearRules.BagKey(heroIndex);

            // Герой, которого игра прямо сейчас держит в подземелье, живёт в записях места, где он стоит:
            //   обычный этаж — dungData_data (надетое и прочее состояние) и dungData_invData (сумка),
            //     пишет SaveFloor;
            //   статичная локация (Лагерь — Locations._shop, арены боссов) — staticData_data и staticData_invData,
            //     пишет SaveStaticLocation (GameManager.AutoSave: location == _shop и подобные).
            // При продолжении игра (DungeonSaving.LoadFloor) берёт основную запись своего места, а
            // запасную reserve_* — только если основная пуста; запись вылазки к тому времени устарела.
            // Из основных записей непуста обычно ровно одна: уходя с места, игра её очищает (запасные
            // остаются — 19.09.2026 в reserve_staticData_data лежала арена из вылазки некроманта).
            // Если непусты обе — не знаем, где герой, не правим.
            // ⚠️ После сбоя (NeedLoadingAutosaveReserve = 1 при NeedLoadingAutosave = 0 и NeedLoading = 0)
            // главное меню само переписывает основные записи запасными (HasSavePrefs/HasStaticPrefs(true))
            // — правка пропала бы, такого героя тоже не правим.
            IntValue cur = readInt("curChar");
            if (cur.State == ValueState.Ok && cur.Value == heroIndex)
            {
                bool floor = NotEmpty(readText(GearRules.FloorEquipKey));
                bool stat = NotEmpty(readText(GearRules.StaticEquipKey));
                bool floorSaved = Flag(readInt, "NeedLoadingAutosave");
                bool restore = Flag(readInt, "NeedLoadingAutosaveReserve") && !floorSaved && !Flag(readInt, "NeedLoading");
                if ((floor || stat) && restore)
                {
                    g.State = GearState.InLocation;
                    g.StateText = L.T("Игра после сбоя собирается восстановить подземелье из запасной копии — правка "
                                      + "пропала бы. Запустите игру, дайте ей загрузиться, выйдите — тогда можно править.",
                                      "After a crash the game is about to restore the dungeon from its reserve copy — an edit "
                                      + "would be lost. Start the game, let it load, quit — then you can edit.");
                    return g;
                }
                if (floor && stat)
                {
                    g.State = GearState.InLocation;
                    g.StateText = L.T("В сохранении есть и этаж, и особая локация — не понять, где стоит герой. "
                                      + "Ничего не трогаю: зайдите в игру, сделайте шаг и выйдите.",
                                      "The save holds both a floor and a special location — it is unclear where the hero is. "
                                      + "Nothing is touched: enter the game, take a step and quit.");
                    return g;
                }
                if (floor || stat)
                {
                    g.InDungeon = true;
                    g.InStatic = stat;
                    g.EquipKey = stat ? GearRules.StaticEquipKey : GearRules.FloorEquipKey;
                    g.BagKey = stat ? GearRules.StaticBagKey : GearRules.FloorBagKey;
                }
                else if (floorSaved)
                {
                    g.State = GearState.InLocation;
                    g.StateText = L.T("Игра отметила сохранённый этаж, но его запись пуста — ничего не трогаю.",
                                      "The game marks a saved floor, but its record is empty — nothing is touched.");
                    return g;
                }
            }

            g.ReadLimits(readInt);
            g.EquipText = readText(g.EquipKey);
            g.BagText = readText(g.BagKey);
            if (g.EquipText.State != ValueState.Ok || g.EquipText.Text.Trim().Length == 0)
            {
                g.State = GearState.NoRaid;
                g.StateText = L.T("У героя нет начатой вылазки. Стартовые вещи игра в сохранении не хранит — "
                                  + "выдаёт их в начале вылазки; тогда их и можно будет править.",
                                  "The hero has no run in progress. The game does not keep starting gear in the save — "
                                  + "it hands it out when a run starts; then it can be edited.");
                return g;
            }
            try
            {
                g.EquipRoot = MiniJson.Parse(g.EquipText.Text);
                // запись должна сама подтверждать, что она того вида, за который мы её приняли
                JsonNode st = g.EquipRoot.Get("staticLocation");
                bool markedStatic = st != null && st.Kind == JsonKind.True;
                if (g.InDungeon && markedStatic != g.InStatic)
                {
                    g.State = GearState.InLocation;
                    g.StateText = g.InStatic
                        ? L.T("Запись особой локации не помечена как особая — не понимаю её, ничего не трогаю.",
                              "The special location record is not marked as special — it is not understood, nothing is touched.")
                        : L.T("Этаж помечен как особая локация — не понимаю, где стоит герой, ничего не трогаю.",
                              "The floor is marked as a special location — it is unclear where the hero is, nothing is touched.");
                    return g;
                }
                if (g.InStatic)
                {
                    bool camp = g.EquipRoot.GetInt("location", -1) == 6;   // Locations._shop — в русской версии игры это Лагерь
                    g.StateText = L.T("Герой сейчас " + (camp ? "в Лагере" : "в особой локации подземелья")
                                      + " — вещи правятся прямо в её сохранении, игра продолжит оттуда уже с новыми вещами.",
                                      "The hero is now " + (camp ? "in the Camp" : "in a special dungeon location")
                                      + " — items are edited right in its save, and the game continues from there with the new items.");
                }
                else if (g.InDungeon)
                    g.StateText = L.T("Герой сейчас в подземелье — вещи правятся прямо в сохранении этажа, "
                                      + "игра продолжит с него уже с новыми вещами.",
                                      "The hero is now in the dungeon — items are edited right in the floor's save, "
                                      + "and the game continues from it with the new items.");
                foreach (string[] slot in GearRules.Slots)
                {
                    JsonNode n = g.EquipRoot.Get(slot[0]);
                    if (n == null || n.Kind != JsonKind.Object) continue;
                    string path = n.GetString("path");
                    if (string.IsNullOrEmpty(path))
                    {
                        // пустое место игра пишет вещью без пути — сюда можно надеть новую
                        GearItem empty = g.EmptySlot(slot, n);
                        if (empty != null) g.Items.Add(empty);
                        continue;
                    }
                    GearItem worn = MakeItem(g.EquipKey, slot[1], n, cat);
                    worn.SlotKey = slot[0];
                    g.Items.Add(worn);
                }

                if (g.BagText.State == ValueState.Ok && g.BagText.Text.Trim().Length > 0)
                {
                    g.BagRoot = MiniJson.Parse(g.BagText.Text);
                    JsonNode list = g.BagRoot.Get("Items");
                    if (list != null && list.Kind == JsonKind.Array)
                    {
                        foreach (JsonNode n in list.Items)
                        {
                            if (n.Kind != JsonKind.Object || string.IsNullOrEmpty(n.GetString("path"))) continue;
                            g.Items.Add(MakeItem(g.BagKey, GearRules.BagPlace, n, cat));
                        }
                    }
                }
            }
            catch (FormatException ex)
            {
                g.Items.Clear();
                g.State = GearState.Broken;
                g.StateText = L.T("Не смог прочитать запись снаряжения (" + ex.Message + ") — ничего не трогаю.",
                                  "Could not read the gear record (" + ex.Message + ") — nothing is touched.");
                return g;
            }

            g.State = GearState.Ok;
            return g;
        }

        private static bool Flag(IntReader readInt, string key)
        {
            IntValue v = readInt(key);
            return v.State == ValueState.Ok && v.Value != 0;
        }

        private static bool NotEmpty(TextValue v)
        {
            return v.State == ValueState.Ok && v.Text.Trim().Length > 0;
        }

        private static GearItem MakeItem(string key, string place, JsonNode n, Catalog cat)
        {
            GearItem it = new GearItem();
            it.SourceKey = key;
            it.Place = place;
            it.Node = n;
            it.Path = n.GetString("path");
            it.Count = n.GetInt("count", 1);
            it.Info = cat.ItemByPath(it.Path);
            it.OrigQuality = n.GetInt("quality", 0);
            it.OrigEmptySlots = n.GetInt("emptySlots", 0);
            it.OrigEffects = n.GetStrings("effects");
            it.OrigCurses = n.GetStrings("curses");
            it.Quality = it.OrigQuality;
            it.Curses = new List<string>(it.OrigCurses);
            foreach (string raw in it.OrigEffects)
            {
                GearEffect e = GearEffect.FromSave(raw, cat);
                e.RememberOriginal();
                if (it.Info != null && e.Info != null && it.Info.Defaults.Contains(e.Info)) e.IsDefault = true;
                it.Effects.Add(e);
            }
            it.OrigEffectObjects.AddRange(it.Effects);

            if (it.Info == null || it.Info.Kind == GearKind.Other)
                it.Problem = L.T("Эта вещь редактором не правится: ", "The editor does not edit this item: ") + KindNote(it.Path);
            else if (n.Get("quality") == null || n.Get("effects") == null || n.Get("curses") == null
                     || n.Get("emptySlots") == null || n.Get("effects").Kind != JsonKind.Array
                     || n.Get("curses").Kind != JsonKind.Array || n.Get("quality").Kind != JsonKind.Number)
                it.Problem = L.T("Запись вещи не такая, как ожидалось, — не трогаю.",
                                 "The item record is not as expected — it is left untouched.");
            return it;
        }

        private static string KindNote(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.StartsWith("items/trash/"))
                return L.T("свойства артефакта зашиты в самой игре, в сохранении лежит только «какой это артефакт».",
                           "an artifact's properties are built into the game; the save only says which artifact it is.");
            if (p.StartsWith("items/usables/")) return L.T("у расходников нет эффектов.", "consumables have no effects.");
            return L.T("её нет в справочнике.", "it is not in the catalog.");
        }

        public bool Changed
        {
            get
            {
                foreach (GearItem it in Items) if (it.Changed) return true;
                return false;
            }
        }

        /// <summary>
        /// Новые тексты записей, в которых есть правки: ключ → текст. Правки полей — заменами на
        /// месте, байт в байт вокруг них. Новая надетая вещь встаёт на место прежней в записи героя.
        /// Если состав сумки поменялся (новые, удалённые, переложенные из слота), список сумки
        /// собирается заново из кусков: каждая оставшаяся вещь — её же текстом со своими правками.
        /// </summary>
        public Dictionary<string, string> BuildTexts()
        {
            Dictionary<string, List<Replacement>> byKey = new Dictionary<string, List<Replacement>>();
            Dictionary<string, string> result = new Dictionary<string, string>();
            bool reshaped = BagReshaped;
            foreach (GearItem it in Items)
            {
                if (it.IsEmptySlot) continue;
                if (it.IsNew)
                {
                    if (it.SlotNode != null) AddTo(byKey, EquipKey, new Replacement(it.SlotNode, it.NewText()));
                    continue;   // новая в сумке — ниже, вместе со всей сумкой
                }
                if (it.MovedToBag || it.Deleted) continue;   // её место заняла новая или её больше нет
                if (it.InBag && reshaped) continue;            // сумка собирается целиком
                foreach (Replacement r in it.Replacements()) AddTo(byKey, it.SourceKey, r);
            }
            if (reshaped)
            {
                List<string> parts = new List<string>();
                foreach (GearItem it in Items)
                {
                    if (!it.InBag || it.Deleted) continue;
                    if (it.IsNew) parts.Add(it.NewText());
                    else if (it.MovedToBag) parts.Add(it.CurrentText(EquipText.Text));
                    else parts.Add(it.CurrentText(BagText.Text));
                }
                string array = "[" + string.Join(",", parts.ToArray()) + "]";
                JsonNode list = BagRoot != null ? BagRoot.Get("Items") : null;
                if (list != null && list.Kind == JsonKind.Array)
                    AddTo(byKey, BagKey, new Replacement(list, array));
                else if (BagRoot == null && BagText.State != ValueState.Foreign)
                    result[BagKey] = "{\"Items\":" + array + "}";   // сумки не было или она пуста — так её пишет игра
                else
                    throw new FormatException(L.T("запись сумки не такая, как ожидалось", "the bag record is not as expected"));
            }
            foreach (KeyValuePair<string, List<Replacement>> pair in byKey)
            {
                string source = pair.Key == EquipKey ? EquipText.Text : BagText.Text;
                result[pair.Key] = MiniJson.Splice(source, pair.Value);
            }
            foreach (string text in result.Values) MiniJson.Parse(text);   // проверка: получился настоящий JSON
            return result;
        }

        private static void AddTo(Dictionary<string, List<Replacement>> byKey, string key, Replacement r)
        {
            if (!byKey.ContainsKey(key)) byKey[key] = new List<Replacement>();
            byKey[key].Add(r);
        }

        /// <summary>Что поменялось в записи — для вопроса перед записью и журнала.</summary>
        public string DescribeChanges(string key)
        {
            List<string> parts = new List<string>();
            foreach (GearItem it in Items)
            {
                if (it.IsEmptySlot || !it.Changed) continue;
                string at, what;
                if (it.IsNew)
                {
                    at = it.SlotNode != null ? EquipKey : BagKey;
                    what = (it.SlotNode != null ? L.T("надето новое: ", "new item worn: ") : L.T("в сумку: ", "to the bag: "))
                         + it.Title + QualityNote(it);
                }
                else if (it.MovedToBag)
                {
                    at = it.Deleted ? EquipKey : BagKey;
                    what = it.Title + (it.Deleted ? L.T(" — снята и удалена", " — taken off and deleted")
                                                  : L.T(" — снята в сумку", " — taken off to the bag"));
                    if (!it.Deleted && it.FieldsChanged) what += "; " + it.DescribeChanges();
                }
                else if (it.Deleted)
                {
                    at = BagKey;
                    what = L.T("удалено: ", "deleted: ") + it.Title;
                }
                else
                {
                    at = it.SourceKey;
                    what = it.DescribeChanges();
                }
                if (at == key) parts.Add(what);
            }
            return string.Join("; ", parts.ToArray());
        }

        private static string QualityNote(GearItem it)
        {
            return it.Info != null && it.Info.Kind != GearKind.Other ? " (" + GearRules.QualityTitle(it.Quality) + ")" : "";
        }
    }

    /// <summary>Правила и подписи, общие для снаряжения.</summary>
    internal static class GearRules
    {
        /// <summary>
        /// Места для вещей в записи героя и их подписи — на языке программы. Свойства, а не поля:
        /// поле могло бы заполниться раньше, чем Program.Main выберет язык.
        /// </summary>
        public static string[][] Slots
        {
            get
            {
                return new string[][]
                {
                    new string[] { "head", L.T("Голова", "Head") },
                    new string[] { "body", L.T("Тело", "Body") },
                    new string[] { "weapon", L.T("Оружие", "Weapon") },
                    new string[] { "shield", L.T("Вторая рука", "Off-hand") },
                    new string[] { "shieldWeapon", L.T("Оружие во 2-й руке", "Off-hand weapon") },
                    new string[] { "art1", L.T("Артефакт 1", "Artifact 1") },
                    new string[] { "art2", L.T("Артефакт 2", "Artifact 2") },
                    new string[] { "art3", L.T("Артефакт 3", "Artifact 3") },
                    new string[] { "ring1", L.T("Кольцо 1", "Ring 1") },
                    new string[] { "ring2", L.T("Кольцо 2", "Ring 2") },
                };
            }
        }

        /// <summary>Качества, которые можно выбрать. Выше легендарного не даём: в игре так вещи не выпадают.</summary>
        public const int MaxQuality = 4;

        /// <summary>Подпись места «в сумке» — по ней вещь и считается лежащей в сумке (язык за время работы не меняется).</summary>
        public static string BagPlace { get { return L.T("Сумка", "Bag"); } }

        /// <summary>Берсерк (CharacterClasses.BERSERKER): во второй руке у него оружие.</summary>
        public const int BerserkerIndex = 4;

        public static string EquipKey(int hero) { return "NeedLoading_ReturnToTheFortress_" + hero; }
        public static string BagKey(int hero) { return "CurInventory_" + hero; }

        /// <summary>Записи сохранённого этажа: чей он — говорит curChar.</summary>
        public const string FloorEquipKey = "dungData_data";
        public const string FloorBagKey = "dungData_invData";

        /// <summary>Записи особой локации (Лагерь, арена): чья она — тоже curChar.</summary>
        public const string StaticEquipKey = "staticData_data";
        public const string StaticBagKey = "staticData_invData";

        /// <summary>Запись места в подземелье (этаж или особая локация), а не вылазки героя.</summary>
        public static bool IsFloorKey(string key)
        {
            return key == FloorEquipKey || key == FloorBagKey || key == StaticEquipKey || key == StaticBagKey;
        }

        public static bool IsGearKey(string key)
        {
            if (IsFloorKey(key)) return true;
            for (int i = 0; i < GameData.Heroes.Length; i++)
                if (key == EquipKey(i) || key == BagKey(i)) return true;
            return false;
        }

        /// <summary>
        /// Качество по-русски — как в игре (перевод, термины UI/Inventory/Rare0_It … Rare4_It):
        /// обычное → особое → древнее → эпическое → легендарное. Выше легендарного вещи в игре не
        /// бывает; «мифическое» (9) в коде объявлено, но ни одной вещи не выдаётся, и перевода у
        /// него нет. 5 и 6 — служебные метки комплектов и артефактов, тоже без перевода.
        /// </summary>
        public static string QualityTitle(int q)
        {
            switch (q)
            {
                case 0: return L.T("обычное", "common");
                case 1: return L.T("особое", "unusual");
                case 2: return L.T("древнее", "ancient");
                case 3: return L.T("эпическое", "epic");
                case 4: return L.T("легендарное", "legendary");
                case 5: return L.T("комплект", "set");
                case 6: return L.T("артефакт", "artifact");
                case 9: return L.T("мифическое", "mythic");
                default: return L.T("качество ", "quality ") + q;
            }
        }
    }
}
