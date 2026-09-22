using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>Вид вещи — от него зависит, какие эффекты на ней вообще возможны.</summary>
    internal enum GearKind { Head, Body, Shield, Weapon, Ring, Other }

    /// <summary>Эффект из файлов игры.</summary>
    internal sealed class EffectInfo
    {
        public int Index;
        public string Class;         // ItemEffectArmor / ItemEffectWeapon / ItemEffectArtifact / ItemEffectCurse ...
        public string Path;          // как пишется в сохранении, до «~»
        public string Name;          // имя объекта в игре
        public string Title;         // из перевода игры, на языке программы (Catalog.Parse)
        public int[] Levels;         // значения по ступеням; пусто — ступеней нет
        public bool Blacklisted;     // игра такие эффекты вычищает при загрузке
        public bool Decimal;         // значения ступеней в десятых: игра делит их на 10
        public int Value;            // число проклятия; у колец и артефактов — value
        public int DefaultLevel;     // ступень кольца/артефакта, заданная в самом ассете
        public float[][] RingLevels = new float[0][];   // ступени колец: value1, value2, value3
        public string[] Skills = new string[0];         // навыки «кольца навыка» по номеру из сохранения
        public string Description = "";                 // из перевода игры, с {0}…{3}, {TITLE}, {TYPE}
        public int Kind = -1;        // род эффекта кольца (ArtifactEffects): двух одного рода кольцо не держит
        public List<EffectInfo> Incompatible = new List<EffectInfo>();   // с чем игра его на кольце не сочетает
        internal int[] IncompatibleIndexes = new int[0];

        /// <summary>
        /// Высшая ступень (с нуля). У колец ступени — customLvls; эффект без них — одна ступень.
        /// У брони и оружия — customLvlValues.
        /// </summary>
        public int MaxTier
        {
            get
            {
                if (IsRingEffect) return RingLevels.Length > 0 ? RingLevels.Length - 1 : 0;
                return Levels.Length > 0 ? Levels.Length - 1 : 0;
            }
        }

        /// <summary>Значение на ступени для списка ступеней: у колец — числа ступени кольца («20» или «20/5»).</summary>
        public string TierLabel(int tier)
        {
            if (!IsRingEffect) return ValueAt(tier);
            if (tier < 0 || tier >= RingLevels.Length) return "";
            float[] lv = RingLevels[tier];
            string s = Num(lv[0], "0.##");
            if (lv[1] != 0f) s += "/" + Num(lv[1], "0.##");
            return s;
        }

        /// <summary>Несовместим ли с другим эффектом — в любую сторону (игра проверяет одну, мы строже).</summary>
        public bool ClashesWith(EffectInfo other)
        {
            return other != null && (Incompatible.Contains(other) || other.Incompatible.Contains(this));
        }

        public bool IsRingEffect { get { return Class == "ItemEffectArtifact" || Class == "ItemEffectArt_RingSkill"; } }

        /// <summary>«10 → 32» — разброс значений по ступеням, для списка «добавить эффект».</summary>
        public string Range()
        {
            if (IsRingEffect)
                return RingLevels.Length > 1 ? TierLabel(0) + " → " + TierLabel(RingLevels.Length - 1) : "";
            if (Levels.Length == 0) return "";
            return ValueAt(0) + " → " + ValueAt(Levels.Length - 1);
        }

        /// <summary>Значение на ступени так, как его пишет игра: у «десятых» — «1,4», у прочих — «14».</summary>
        public string ValueAt(int tier)
        {
            if (tier < 0 || tier >= Levels.Length) return "";
            return Decimal ? Num(Levels[tier] / 10f, "0.0") : Levels[tier].ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Описание эффекта с числами, как в игре. tier — ступень из сохранения («путь~ступень»),
        /// tail — всё после неё (у кольца навыка «~номер навыка~кнопка»). Пусто — описания в игре нет.
        /// Правила — из кода игры:
        ///   броня, оружие — ItemEffect.GetDescription: {0} = значение ступени (в десятых — с одним знаком);
        ///   проклятие — {0} = его число (ItemEffectContainer.Initialize(ItemEffectCurse));
        ///   кольцо, артефакт — если у эффекта есть ступени кольца: {0} = value, {1}…{3} = value1…3
        ///   этой ступени; у кольца навыка {TITLE} — название навыка заглавными, {TYPE} — кнопка
        ///   строчными, а {0} = value1. Нет ступеней кольца — как у брони на ступени 0.
        /// </summary>
        public string DescribeAt(int tier, string tail)
        {
            string s = Description;
            if (s.Length == 0) return "";
            if (Class == "ItemEffectCurse") return s.Replace("{0}", Value.ToString(CultureInfo.InvariantCulture));
            if (IsRingEffect && tier >= 0 && tier < RingLevels.Length)
            {
                float[] lv = RingLevels[tier];
                if (Class == "ItemEffectArt_RingSkill")
                {
                    // после ступени — одно число (DungeonSaving.GetEffects): не меньше нуля — номер
                    // навыка из allAvailableSkills, отрицательное — кнопка атаки: −1 основной,
                    // −2 силовой, −3 специальный
                    string[] t = (tail ?? "").Split('~');
                    int x;
                    if (t.Length > 1 && int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out x))
                    {
                        if (x >= 0 && x < Skills.Length) s = s.Replace("{TITLE}", Skills[x].ToUpperInvariant());
                        int slot = -x - 1;
                        if (x < 0 && slot < GameData.SlotCount)
                            s = s.Replace("{TYPE}", GameData.SlotTitle(slot).ToLowerInvariant());
                    }
                    return s.Replace("{0}", Num(lv[0], "0.##"));
                }
                s = s.Replace("{0}", Value.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < 3; i++) s = s.Replace("{" + (i + 1) + "}", Num(lv[i], "0.##"));
                return s;
            }
            string v = Levels.Length > 0 ? ValueAt(IsRingEffect ? 0 : Math.Max(0, Math.Min(tier, MaxTier)))
                                         : (IsRingEffect ? Value.ToString(CultureInfo.InvariantCulture) : "0");
            return s.Replace("{0}", v);
        }

        /// <summary>Число как пишет игра: по-русски — запятая, по-английски — точка.</summary>
        private static string Num(float v, string format)
        {
            return L.Num(v, format);
        }
    }

    /// <summary>Вещь снаряжения из файлов игры.</summary>
    internal sealed class ItemInfo
    {
        public string ResourcePath;              // нижний регистр, как в таблице путей игры
        public string Class;
        public string Title;
        public int TemplateQuality;
        public List<EffectInfo> Defaults = new List<EffectInfo>();   // врождённые
        public List<EffectInfo> Pool = new List<EffectInfo>();       // что игра может выкинуть на вещь
        public List<EffectInfo> Properties = new List<EffectInfo>(); // свойства артефакта: в ассете, не в сохранении

        // для выдачи новой вещи (справочник с 21.09.2026)
        public string SavePath = "";             // что игра пишет в запись вещи (savingObjectPath), точный регистр
        public int Price;                        // PriceNormal — для порядка «от дешёвых к дорогим»
        public bool Unique;                      // уникальная: у героя может быть одна
        public bool CanBeRare;                   // бывает редкой; нет — качество не меняется (уникальные)
        public List<int> Heroes = new List<int>();   // чьё оружие или вторая рука (CharacterClasses); пусто — общая
        public int WeaponSub = -1;               // подвид оружия (WeaponTypesAdditional): 0 меч … 8 жезл
        public float StatA;                      // броня; у оружия — урон от
        public float StatB;                      // у щита — защита от урона; у оружия — урон до
        public bool IsArtifact;

        /// <summary>
        /// Сохранение держит вещь как её саму. У части вещей второго и третьего уровня в образце
        /// записан путь чужой вещи (скопировали ассет и не поправили): «Гладиус» пишется как
        /// «Душегуб», новые щиты — путём вещи, которой в игре нет. Игра сохраняет ровно этот путь
        /// (DungeonSaving.GetInventoryString) и по нему же грузит — такая вещь после перезагрузки
        /// становится другой или пропадает. Выдавать их незачем.
        /// </summary>
        public bool SavesAsItself
        {
            get { return SavePath.Length > 0 && string.Equals(SavePath, ResourcePath, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Расходник, который лежит в сумке (зелья, еда, свитки), а не пассивная вещь или покупка лавки.</summary>
        public bool IsUsable
        {
            get { return Class == "SO_ItemUsable" && ResourcePath.StartsWith("items/usables/", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Можно ли выдать: снаряжение, кольцо, артефакт или расходник, который сохранение удержит.</summary>
        public bool Givable
        {
            get
            {
                if (!SavesAsItself) return false;
                if (Class == "SO_ItemShield_Alt") return false;
                return Kind != GearKind.Other || IsArtifact || IsUsable;
            }
        }

        /// <summary>
        /// Сила — мерой самой игры (сортировка сумки «по эффективности», InventoryManager.OrderOnEfficiency):
        /// броня у шлема, доспеха и щита, средний урон у оружия, у остального — цена.
        /// </summary>
        public float Strength
        {
            get
            {
                if (Kind == GearKind.Weapon) return (StatA + StatB) / 2f;
                if (Kind == GearKind.Head || Kind == GearKind.Body) return StatA;
                if (Kind == GearKind.Shield && (StatA > 0f || StatB > 0f)) return StatA + StatB / 1000f;
                return Price;
            }
        }

        /// <summary>«урон 12–18», «броня 7» — рядом с названием в списке выдачи.</summary>
        public string StatText
        {
            get
            {
                if (Kind == GearKind.Weapon)
                    return L.T("урон ", "damage ") + Math.Round(StatA).ToString(CultureInfo.InvariantCulture) + "–"
                         + Math.Round(StatB).ToString(CultureInfo.InvariantCulture);
                if ((Kind == GearKind.Head || Kind == GearKind.Body || Kind == GearKind.Shield) && StatA > 0f)
                    return L.T("броня ", "armor ") + Math.Round(StatA).ToString(CultureInfo.InvariantCulture);
                return "";
            }
        }

        public GearKind Kind
        {
            get
            {
                switch (Class)
                {
                    case "SO_ItemHead": return GearKind.Head;
                    case "SO_ItemBody": return GearKind.Body;
                    case "SO_ItemShield": return GearKind.Shield;
                    case "SO_ItemWeapon": return GearKind.Weapon;
                    case "SO_ItemRing": return GearKind.Ring;
                    default: return GearKind.Other;
                }
            }
        }

        /// <summary>
        /// Какого класса эффекты игра грузит на такую вещь (DungeonSaving.GetEffects). Кольцо грузит
        /// ItemEffectArtifact (и его наследника — кольцо навыка), но добавлять редактор даёт только
        /// обычные: кольцу навыка нужен ещё номер навыка, который игра выбирает сама.
        /// </summary>
        public string EffectClass
        {
            get
            {
                if (Kind == GearKind.Weapon) return "ItemEffectWeapon";
                if (Kind == GearKind.Ring) return "ItemEffectArtifact";
                return "ItemEffectArmor";
            }
        }
    }

    /// <summary>
    /// Справочник снаряжения. Вшит в программу при сборке (data\catalog.tsv, собран скриптом
    /// из файлов игры) — программа остаётся одним файлом. Названия и
    /// описания в нём на двух языках; Parse берёт те, на каком сейчас говорит программа (L.Ru).
    /// </summary>
    internal sealed class Catalog
    {
        public const string ResourceName = "PocketRoguesEditor.catalog.tsv";

        public readonly List<EffectInfo> Effects = new List<EffectInfo>();
        public readonly List<ItemInfo> Items = new List<ItemInfo>();
        private readonly Dictionary<string, EffectInfo> _effectByPath =
            new Dictionary<string, EffectInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemInfo> _itemByPath =
            new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);

        private static Catalog _embedded;

        /// <summary>Справочник, вшитый в программу.</summary>
        public static Catalog Embedded()
        {
            if (_embedded != null) return _embedded;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (s == null) throw new InvalidOperationException(L.T("В программу не вшит справочник снаряжения (",
                                                                       "The gear catalog is not built into the program (") + ResourceName + ").");
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    _embedded = Parse(r.ReadToEnd());
                }
            }
            return _embedded;
        }

        public static Catalog Parse(string text)
        {
            Catalog c = new Catalog();
            List<string[]> itemRows = new List<string[]>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p[0] == "E" && p.Length >= 8)
                {
                    EffectInfo e = new EffectInfo();
                    e.Index = int.Parse(p[1], CultureInfo.InvariantCulture);
                    e.Class = p[2];
                    e.Path = p[3];
                    e.Name = p[4];
                    e.Title = p[5];
                    e.Levels = ParseInts(p[6], ' ');
                    e.Blacklisted = p[7] == "1";
                    if (p.Length >= 14)
                    {
                        e.Decimal = p[8] == "1";
                        e.Value = int.Parse(p[9], CultureInfo.InvariantCulture);
                        e.DefaultLevel = int.Parse(p[10], CultureInfo.InvariantCulture);
                        e.RingLevels = ParseRingLevels(p[11]);
                        e.Skills = p[12].Length > 0 ? p[12].Split('|') : new string[0];
                        e.Description = p[13];
                    }
                    if (p.Length >= 16)
                    {
                        e.Kind = int.Parse(p[14], CultureInfo.InvariantCulture);
                        e.IncompatibleIndexes = ParseInts(p[15], ',');
                    }
                    if (p.Length >= 19 && !L.Ru)
                    {
                        // английские: название, описание, навыки кольца (с 1.8)
                        e.Title = L.Pick(e.Title, p[16]);
                        e.Description = L.Pick(e.Description, p[17]);
                        if (p[18].Length > 0) e.Skills = p[18].Split('|');
                    }
                    if (e.Index != c.Effects.Count) throw new FormatException(L.T("справочник: номера эффектов идут не по порядку",
                                                                                  "catalog: effect numbers are out of order"));
                    c.Effects.Add(e);
                    if (e.Path.Length > 0) c._effectByPath[e.Path] = e;
                }
                else if (p[0] == "I" && p.Length >= 7)
                {
                    itemRows.Add(p);
                }
            }
            foreach (EffectInfo e in c.Effects)
                foreach (int i in e.IncompatibleIndexes) e.Incompatible.Add(c.Effects[i]);
            foreach (string[] p in itemRows)
            {
                ItemInfo it = new ItemInfo();
                it.ResourcePath = p[1];
                it.Class = p[2];
                it.Title = p[3];
                it.TemplateQuality = int.Parse(p[4], CultureInfo.InvariantCulture);
                foreach (int i in ParseInts(p[5], ',')) it.Defaults.Add(c.Effects[i]);
                foreach (int i in ParseInts(p[6], ',')) it.Pool.Add(c.Effects[i]);
                if (p.Length >= 8) foreach (int i in ParseInts(p[7], ',')) it.Properties.Add(c.Effects[i]);
                if (p.Length >= 17)
                {
                    it.SavePath = p[8];
                    it.Price = int.Parse(p[9], CultureInfo.InvariantCulture);
                    it.Unique = p[10] == "1";
                    it.CanBeRare = p[11] == "1";
                    it.Heroes.AddRange(ParseInts(p[12], ','));
                    it.WeaponSub = int.Parse(p[13], CultureInfo.InvariantCulture);
                    it.StatA = float.Parse(p[14], NumberStyles.Float, CultureInfo.InvariantCulture);
                    it.StatB = float.Parse(p[15], NumberStyles.Float, CultureInfo.InvariantCulture);
                    it.IsArtifact = p[16] == "1";
                }
                if (p.Length >= 18) it.Title = L.Pick(it.Title, p[17]);
                c.Items.Add(it);
                c._itemByPath[it.ResourcePath] = it;
            }
            return c;
        }

        private static int[] ParseInts(string s, char sep)
        {
            List<int> list = new List<int>();
            foreach (string part in s.Split(sep))
            {
                if (part.Length == 0) continue;
                list.Add(int.Parse(part, CultureInfo.InvariantCulture));
            }
            return list.ToArray();
        }

        /// <summary>«2/0/0 4/0/0» → ступени кольца по три числа.</summary>
        private static float[][] ParseRingLevels(string s)
        {
            List<float[]> list = new List<float[]>();
            foreach (string part in s.Split(' '))
            {
                if (part.Length == 0) continue;
                string[] v = part.Split('/');
                if (v.Length != 3) throw new FormatException(L.T("справочник: ступень кольца не из трёх чисел: ",
                                                                 "catalog: a ring tier is not three numbers: ") + part);
                float[] lv = new float[3];
                for (int i = 0; i < 3; i++) lv[i] = float.Parse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                list.Add(lv);
            }
            return list.ToArray();
        }

        /// <summary>Вещь по пути из сохранения («Items/Helmets/...»); null — такой в справочнике нет.</summary>
        public ItemInfo ItemByPath(string path)
        {
            ItemInfo it;
            return path != null && _itemByPath.TryGetValue(path, out it) ? it : null;
        }

        /// <summary>Эффект по пути из сохранения без «~ступени»; null — такого нет.</summary>
        public EffectInfo EffectByPath(string path)
        {
            EffectInfo e;
            return path != null && _effectByPath.TryGetValue(path, out e) ? e : null;
        }
    }
}
