using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Выдача, замена и удаление вещей (с 21.09.2026). В моде вещь создаёт сама игра, здесь игры нет —
    /// запись вещи собираем сами, по тем же правилам и в том же виде, как её пишет игра
    /// (DungeonSaving.GetInventoryString): путь из образца, качество, эффекты «путь~ступень»,
    /// случайный номер-отпечаток (как SO_Item.CreateUniqueID).
    /// </summary>
    internal sealed partial class GearItem
    {
        public bool IsNew;           // создана в редакторе — в сохранении её ещё нет
        public bool Deleted;         // удалена из сумки
        public bool MovedToBag;      // снята при замене и переложена в сумку (запись её — в записи героя)
        public bool IsEmptySlot;     // пустое место на герое: сюда можно надеть вещь
        public string SlotKey;       // место в записи героя («head», «ring1»…) — у надетых и пустых мест
        public JsonNode SlotNode;    // куда в записи героя встаёт новая вещь (у новой надетой)
        public int NewId;            // номер-отпечаток новой вещи

        /// <summary>Лежит ли в сумке (в том числе новая и переложенная из слота).</summary>
        public bool InBag { get { return Place == GearRules.BagPlace; } }

        /// <summary>
        /// Запись новой вещи — поля и их порядок как у игры (DungItem): так её пишет JsonUtility.
        /// </summary>
        public string NewText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"path\":").Append(MiniJson.Quote(Path));
            sb.Append(",\"effects\":").Append(MiniJson.StringArray(EffectStrings()));
            sb.Append(",\"count\":").Append(Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"quality\":").Append(Quality.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"upgradeProgress\":0");
            sb.Append(",\"emptySlots\":").Append(EmptySlots.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"removedEffectsCount\":0,\"wasCreatedInFortress\":false");
            sb.Append(",\"curses\":").Append(MiniJson.StringArray(Curses));
            sb.Append(",\"isLocked\":false,\"uniqueID\":").Append(NewId.ToString(CultureInfo.InvariantCulture));
            return sb.Append('}').ToString();
        }

        /// <summary>Нынешний текст вещи из сохранения — с её правками, чтобы переложить в другую запись.</summary>
        public string CurrentText(string source)
        {
            return MiniJson.SpliceRange(source, Node.Start, Node.End, Replacements());
        }
    }

    internal sealed partial class HeroGear
    {
        /// <summary>Мест в сумке — сколько дают Склады (bld_boat), как Character.Awake.</summary>
        public int BagLimit = 15;
        private int _jeweler;        // bld_jeweler: кольцо 1 с уровня 1, кольцо 2 — с 2
        private int _arts;           // bld_arts: артефакт 2 с уровня 1, артефакт 3 — с 2
        private Random _random;

        /// <summary>Случай для новых вещей. Самопроверка подставляет свой, с постоянным зерном.</summary>
        public Random Random
        {
            get { if (_random == null) _random = new Random(); return _random; }
            set { _random = value; }
        }

        private void ReadLimits(IntReader readInt)
        {
            BagLimit = BagSizeFromBoat(Level(readInt, "bld_boat"));
            _jeweler = Level(readInt, "bld_jeweler");
            _arts = Level(readInt, "bld_arts");
        }

        private static int Level(IntReader readInt, string key)
        {
            IntValue v = readInt(key);
            return v.State == ValueState.Ok ? v.Value : 0;
        }

        /// <summary>Character.Awake: bld_boat 1 → 20, 2 → 25, 3 → 30, 4–6 → 35, иначе 15.</summary>
        public static int BagSizeFromBoat(int boat)
        {
            if (boat == 1) return 20;
            if (boat == 2) return 25;
            if (boat == 3) return 30;
            if (boat >= 4 && boat <= 6) return 35;
            return 15;
        }

        /// <summary>Чем закрыто место; null — открыто. Постройки — как у игры (Character.AddItem).</summary>
        public string SlotLock(string slotKey)
        {
            // названия построек — из перевода игры (Fortress/Buildings/Jeweler, Artifacts)
            if (slotKey == "ring1" && _jeweler < 1) return Locked(L.T("Ювелирная мастерская", "Jewelry workshop"), 1);
            if (slotKey == "ring2" && _jeweler < 2) return Locked(L.T("Ювелирная мастерская", "Jewelry workshop"), 2);
            if (slotKey == "art2" && _arts < 1) return Locked(L.T("Мастерская артефактов", "Artifacts workshop"), 1);
            if (slotKey == "art3" && _arts < 2) return Locked(L.T("Мастерская артефактов", "Artifacts workshop"), 2);
            return null;
        }

        private static string Locked(string building, int level)
        {
            return L.T("Слот закрыт: его открывает " + building + ", уровень " + level + ".",
                       "The slot is locked: it opens with " + building + ", level " + level + ".");
        }

        /// <summary>
        /// Пустое место на герое — строкой в списке, чтобы туда можно было надеть вещь. Второе оружие
        /// бывает только у берсерка.
        /// </summary>
        private GearItem EmptySlot(string[] slot, JsonNode node)
        {
            if (slot[0] == "shieldWeapon" && HeroIndex != GearRules.BerserkerIndex) return null;
            GearItem it = new GearItem();
            it.IsEmptySlot = true;
            it.SourceKey = EquipKey;
            it.Place = slot[1];
            it.SlotKey = slot[0];
            it.Node = node;
            it.Path = "";
            string locked = SlotLock(slot[0]);
            it.Problem = locked != null ? locked : L.T("Здесь пусто — можно надеть вещь.", "Empty — an item can be put on here.");
            return it;
        }

        /// <summary>Сколько вещей в сумке сейчас — с новыми, без удалённых.</summary>
        public int BagCount
        {
            get
            {
                int n = 0;
                foreach (GearItem it in Items) if (it.InBag && !it.Deleted) n++;
                return n;
            }
        }

        /// <summary>Есть ли у героя такая вещь (для уникальных): надетая или в сумке, кроме except.</summary>
        private bool Holds(ItemInfo info, GearItem except)
        {
            foreach (GearItem it in Items)
            {
                if (it == except || it.Deleted || it.IsEmptySlot) continue;
                if (it.Info == info) return true;
                if (info.SavePath.Length > 0 && string.Equals(it.Path, info.SavePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Подходит ли вещь к месту на герое.</summary>
        public static bool Fits(string slotKey, ItemInfo info)
        {
            if (info == null || slotKey == null) return false;
            switch (slotKey)
            {
                case "head": return info.Kind == GearKind.Head;
                case "body": return info.Kind == GearKind.Body;
                case "weapon":
                case "shieldWeapon": return info.Kind == GearKind.Weapon;
                case "shield": return info.Kind == GearKind.Shield;
                case "ring1":
                case "ring2": return info.Kind == GearKind.Ring;
                case "art1":
                case "art2":
                case "art3": return info.IsArtifact;
                default: return false;
            }
        }

        private string CanGive(ItemInfo info, GearItem except)
        {
            if (State != GearState.Ok) return StateText;
            if (info == null) return L.T("Вещь не выбрана.", "No item chosen.");
            if (!info.Givable) return L.T("Эту вещь сохранение игры не удержит — её не выдаю.",
                                          "The game save cannot keep this item — it is not given.");
            if (info.Unique && Holds(info, except))
                return L.T("«" + info.Title + "» — вещь уникальная, и у героя она уже есть.",
                           "“" + info.Title + "” is unique, and the hero already has it.");
            return null;
        }

        /// <summary>Отказ, когда в сумке нет места (просто «Инвентарь заполнен», без подробностей).</summary>
        public static string BagFull { get { return L.T("Инвентарь заполнен.", "The inventory is full."); } }

        /// <summary>Положить новую вещь в сумку. null — получилось, иначе причина отказа.</summary>
        public string AddToBag(ItemInfo info)
        {
            string no = CanGive(info, null);
            if (no != null) return no;
            if (BagCount >= BagLimit) return BagFull;
            GearItem it = ItemFactory.Create(info, Random, BagKey, GearRules.BagPlace);
            Items.Add(it);
            LastTouched = it;
            return null;
        }

        /// <summary>
        /// Заменить: надетую — новая встаёт на её место, старая уходит в сумку (нет места — «Инвентарь
        /// заполнен»); вещь в сумке — новая встаёт на её место, старая удаляется; пустое место —
        /// новая надевается.
        /// </summary>
        public string Replace(GearItem target, ItemInfo info)
        {
            if (target == null || target.Deleted) return L.T("Выберите вещь слева.", "Choose an item on the left.");
            string no = CanGive(info, target);
            if (no != null) return no;
            int at = Items.IndexOf(target);
            if (at < 0) return L.T("Этой вещи уже нет.", "This item is gone.");

            if (target.InBag)
            {
                GearItem fresh = ItemFactory.Create(info, Random, BagKey, GearRules.BagPlace);
                Items.Insert(at, fresh);
                Forget(target);
                LastTouched = fresh;
                return null;
            }

            if (target.SlotKey == null) return L.T("Эту вещь заменить нельзя.", "This item cannot be replaced.");
            string locked = SlotLock(target.SlotKey);
            if (target.IsEmptySlot && locked != null) return locked;
            if (!Fits(target.SlotKey, info)) return L.T("«" + info.Title + "» сюда не надевается.", "“" + info.Title + "” does not go here.");
            // снятая уходит в сумку — если это вещь из сохранения; новую, ещё не записанную, просто убираем
            bool toBag = !target.IsEmptySlot && !target.IsNew;
            if (toBag && BagCount >= BagLimit) return BagFull;

            GearItem worn = ItemFactory.Create(info, Random, EquipKey, target.Place);
            worn.SlotKey = target.SlotKey;
            worn.SlotNode = target.IsNew ? target.SlotNode : target.Node;
            Items[at] = worn;
            if (toBag)
            {
                target.MovedToBag = true;
                target.Place = GearRules.BagPlace;
                Items.Add(target);
            }
            LastTouched = worn;
            return null;
        }

        /// <summary>Удалить вещь из сумки. Новую, ещё не записанную, — просто убрать.</summary>
        public string Delete(GearItem it)
        {
            if (it == null || it.Deleted) return L.T("Выберите вещь в сумке.", "Choose an item in the bag.");
            if (!it.InBag) return L.T("Удалять можно вещи из сумки. Надетую — замените.",
                                      "Only items in the bag can be deleted. Replace a worn one instead.");
            Forget(it);
            return null;
        }

        private void Forget(GearItem it)
        {
            if (it.IsNew) Items.Remove(it);
            else it.Deleted = true;
        }

        /// <summary>Последняя выданная вещь — окно выберет её в списке.</summary>
        public GearItem LastTouched;

        /// <summary>Правки в составе сумки: новые, удалённые, переложенные из слота.</summary>
        public bool BagReshaped
        {
            get
            {
                foreach (GearItem it in Items)
                    if (it.InBag && (it.IsNew || it.Deleted || it.MovedToBag)) return true;
                return false;
            }
        }
    }

    /// <summary>Новая вещь по образцу — по правилам самой игры.</summary>
    internal static class ItemFactory
    {
        /// <summary>
        /// Снаряжение и кольца — легендарные, кроме вещей, которые не
        /// бывают редкими: те — с качеством образца. Эффекты — как игра накидывает выпавшей вещи:
        ///   врождённые — ступень по качеству (SO_ItemHead.ChangeQuality: Clamp(качество, 0, 4));
        ///   случайные — из пула вещи, пока не наберётся норма (качество + врождённые), ступень —
        ///     с убывающей вероятностью: вес ступени i — 0,3 в степени i
        ///     (SO_ItemEquip.GenerateEffectLevelWithDecreasingProbability);
        ///   кольцо — случайные эффекты разного рода, по первой ступени, пока хватает очков
        ///     (качество + 2), с врождённым на первой ступени.
        /// Номер-отпечаток — случайный от 100000 до 999999, как SO_Item.CreateUniqueID.
        /// </summary>
        public static GearItem Create(ItemInfo info, Random random, string sourceKey, string place)
        {
            GearItem it = new GearItem();
            it.IsNew = true;
            it.Info = info;
            it.Path = info.SavePath;
            it.SourceKey = sourceKey;
            it.Place = place;
            it.Count = 1;
            it.NewId = random.Next(100000, 1000000);

            if (info.Kind == GearKind.Other)
            {
                // артефакт и расходник: качество в записи — 0, эффектов нет (так пишет игра)
                it.Problem = info.IsArtifact
                    ? L.T("Свойства артефакта зашиты в самой игре — править у него нечего.",
                          "An artifact's properties are built into the game — there is nothing to edit.")
                    : L.T("У расходников нет эффектов.", "Consumables have no effects.");
                return it;
            }

            int quality = info.CanBeRare ? GearRules.MaxQuality : info.TemplateQuality;
            it.Quality = quality;
            it.OrigQuality = quality;
            foreach (EffectInfo d in info.Defaults)
            {
                int tier = it.IsRing ? 0 : Math.Min(Math.Max(quality, 0), d.MaxTier);
                GearEffect e = GearEffect.New(d, tier);
                e.IsDefault = true;
                it.Effects.Add(e);
            }

            if (it.IsRing)
            {
                while (it.RingPoints + 1 <= it.RingBudget)
                {
                    List<EffectInfo> pool = it.Addable();
                    if (pool.Count == 0) break;
                    it.Effects.Add(GearEffect.New(pool[random.Next(pool.Count)], 0));
                }
            }
            else
            {
                while (it.Effects.Count < it.Capacity)
                {
                    List<EffectInfo> pool = it.Addable();
                    if (pool.Count == 0) break;
                    EffectInfo pick = pool[random.Next(pool.Count)];
                    it.Effects.Add(GearEffect.New(pick, RollTier(pick.MaxTier, random)));
                }
            }
            it.OrigEffectObjects.AddRange(it.Effects);
            return it;
        }

        /// <summary>Ступень с убывающей вероятностью: вес ступени i — 0,3^i, как у игры.</summary>
        public static int RollTier(int maxTier, Random random)
        {
            if (maxTier <= 0) return 0;
            double total = 0;
            for (int i = 0; i <= maxTier; i++) total += Math.Pow(0.3, i);
            double roll = random.NextDouble() * total;
            for (int i = 0; i <= maxTier; i++)
            {
                roll -= Math.Pow(0.3, i);
                if (roll < 0) return i;
            }
            return maxTier;
        }
    }
}
