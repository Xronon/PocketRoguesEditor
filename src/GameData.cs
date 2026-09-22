using System;
using System.Collections.Generic;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>Один герой игры: номер, имя в коде игры, имя для окна, ключ гильдии.</summary>
    internal sealed class Hero
    {
        public readonly int Index;
        public readonly string ClassKey;   // как в перечислении CharacterClasses игры
        public readonly string GuildKey;   // уровень гильдии героя в лагере: потолок прокачки навыков в игре
        private readonly string _ru, _en;

        public Hero(int index, string classKey, string ru, string en, string guildKey)
        {
            Index = index;
            ClassKey = classKey;
            _ru = ru;
            _en = en;
            GuildKey = guildKey;
        }

        public string Title { get { return L.T(_ru, _en); } }
    }

    /// <summary>Атрибут героя: окончание ключа в реестре и имя для окна.</summary>
    internal sealed class HeroAttribute
    {
        public readonly string Suffix;
        private readonly string _ru, _en;

        public HeroAttribute(string suffix, string ru, string en)
        {
            Suffix = suffix;
            _ru = ru;
            _en = en;
        }

        public string Title { get { return L.T(_ru, _en); } }
    }

    /// <summary>
    /// Навык героя. Список и названия взяты из файлов игры
    /// (SkillsDataBase → CharacterSkill → SerializedVariable).
    /// </summary>
    internal sealed class Skill
    {
        public readonly int HeroIndex;
        public readonly int Slot;          // 0 — основной, 1 — силовой, 2 — специальный
        public readonly string Key;        // ключ уровня в реестре, например «skill_wizBlackFire»
        public readonly bool IsDefault;    // стартовый: уровень ниже 1 игра считает за 1
        private readonly string _ru, _en;

        public Skill(int heroIndex, int slot, string key, bool isDefault, string ru, string en)
        {
            HeroIndex = heroIndex;
            Slot = slot;
            Key = key;
            IsDefault = isDefault;
            _ru = ru;
            _en = en;
        }

        public string Title { get { return L.T(_ru, _en); } }
    }

    /// <summary>Одно редактируемое число: ключ игры, подпись, допустимые пределы.</summary>
    internal sealed class Field
    {
        public readonly string Key;
        public readonly string Title;
        public readonly int Min;
        public readonly int Max;
        public readonly bool AtLeastOne;   // игра считает всё, что ниже 1, единицей

        public Field(string key, string title, int min, int max)
            : this(key, title, min, max, false)
        {
        }

        public Field(string key, string title, int min, int max, bool atLeastOne)
        {
            Key = key;
            Title = title;
            Min = min;
            Max = max;
            AtLeastOne = atLeastOne;
        }

        /// <summary>
        /// Какое число видит игра. Отсутствующее значение для неё ноль, а у стартового
        /// навыка всё, что не больше нуля, — единица (SkillHolder.LoadSkill).
        /// </summary>
        public int GameValue(IntValue v)
        {
            int raw = v.State == ValueState.Ok ? v.Value : 0;
            if (AtLeastOne && raw <= 0) return 1;
            return raw;
        }
    }

    /// <summary>
    /// Всё, что редактор знает об игре. Чистые данные и функции, ни диска, ни реестра.
    /// Правила взяты из кода игры (Assembly-CSharp), у каждого — где именно.
    /// </summary>
    internal static class GameData
    {
        /// <summary>Где игра хранит данные: PlayerPrefs Unity для компании EtherGaming.</summary>
        public const string RegistryPath = @"Software\EtherGaming\Pocket Rogues";

        /// <summary>Имя процесса игры (файл «Pocket Rogues.exe»).</summary>
        public const string ProcessName = "Pocket Rogues";

        public const string GoldKey = "money";

        /// <summary>Язык игры: 0 английский, 1 русский, 2 украинский, 3 португальский, 4 немецкий.</summary>
        public const string LanguageKey = "lang";

        // Предел атрибута — из самой игры: ChoseCharsManager.UpgradeAttribute
        // не даёт поднять выше 50. Предел навыка 25: старая система навыков
        // (SO_Skill.ReloadSkill) срезает до 25, а в экране героя навык качается
        // до уровня гильдии, у которой максимум тоже 25. Остальные пределы —
        // просто разумный потолок против опечатки лишним нулём.
        public const int AttributeMax = 50;
        public const int SkillMax = 25;
        public const int PointsMax = 999999;
        public const int GoldMax = 999999999;

        /// <summary>Порядок совпадает с перечислением CharacterClasses игры. Названия — из перевода игры.</summary>
        public static readonly Hero[] Heroes = new Hero[]
        {
            new Hero(0, "WARRIOR", "Воин", "Warrior", "bld_war"),
            new Hero(1, "ARCHER", "Лучник", "Archer", "bld_arch"),
            new Hero(2, "WIZARD", "Маг", "Wizard", "bld_wzrd"),
            new Hero(3, "HUNTER", "Охотник", "Hunter", "bld_hunt"),
            new Hero(4, "BERSERKER", "Берсерк", "Berserk", "bld_bers"),
            new Hero(5, "NECROM", "Некромант", "Necromancer", "bld_necr"),
        };

        /// <summary>Ключи — как в игре («intelegence» с её опечаткой), названия — из её перевода.</summary>
        public static readonly HeroAttribute[] Attributes = new HeroAttribute[]
        {
            new HeroAttribute("_endurance", "Выносливость", "Endurance"),
            new HeroAttribute("_strength", "Сила", "Strength"),
            new HeroAttribute("_agility", "Ловкость", "Agility"),
            new HeroAttribute("_intelegence", "Разум", "Intelligence"),
        };

        /// <summary>Названия кнопок атаки — из перевода игры (Characters/SkillTypes).</summary>
        public static string SlotTitle(int slot)
        {
            return slot == 0 ? L.T("Основной", "Main") : slot == 1 ? L.T("Силовой", "Powerful") : L.T("Специальный", "Special");
        }

        public const int SlotCount = 3;

        /// <summary>
        /// Все 54 навыка: по три на каждую из трёх кнопок у каждого героя, в порядке экрана героя.
        /// Таблица собрана скриптом из файлов игры: после её обновления пересобирается, руками не правится.
        /// </summary>
        public static readonly Skill[] Skills = new Skill[]
        {
            new Skill(0, 0, "skill_spdAtck", true, "Рубящий удар", "Slashing strike"),
            new Skill(0, 0, "skill_spdAtckStun", false, "Освященное оружие", "Blessed weapon"),
            new Skill(0, 0, "skill_spdAtckDmg", false, "Рваные раны", "Torn wounds"),
            new Skill(0, 1, "skill_strAtck", true, "Жнец", "Reaper"),
            new Skill(0, 1, "skill_strAtck360", false, "Рокот боя", "Roar of battle"),
            new Skill(0, 1, "skill_strAtckRange", false, "Бросок щита", "Shield throw"),
            new Skill(0, 2, "skill_block", true, "Стальная крепость", "Steel fortress"),
            new Skill(0, 2, "skill_blockIron", false, "Поглотитель боли", "Pain eater"),
            new Skill(0, 2, "skill_blockWalkable", false, "Обет святости", "Vow of sanctity"),
            new Skill(1, 0, "skill_spdArch", true, "Быстрый выстрел", "Quick shot"),
            new Skill(1, 0, "skill_spdArchSpeed", false, "Зазубренная стрела", "Jagged arrow"),
            new Skill(1, 0, "skill_spdArchSlowdown", false, "Калечащий выстрел", "Crippling shot"),
            new Skill(1, 1, "skill_strArch", true, "Мощный выстрел", "Powerful shot"),
            new Skill(1, 1, "skill_spdArchMulti", false, "Эльфийский выстрел", "Elven shot"),
            new Skill(1, 1, "skill_strArchThrough", false, "Сквозной выстрел", "Through shot"),
            new Skill(1, 2, "skill_spcArchHide", true, "Тень", "Shadow"),
            new Skill(1, 2, "skill_spcArchHealGrass", false, "Лечебные травы", "Healing herbs"),
            new Skill(1, 2, "skill_spcArchExpl", false, "Дымовая шашка", "Smoke bomb"),
            new Skill(2, 0, "skill_wizFireball", true, "Огненный шар", "Fireball"),
            new Skill(2, 0, "skill_wizLightball", false, "Шаровая молния", "Ball lightning"),
            new Skill(2, 0, "skill_wizLightblade", false, "Клинок Света", "Blade of Light"),
            new Skill(2, 1, "skill_wizBlackFire", true, "Огненное дыхание", "Fire breath"),
            new Skill(2, 1, "skill_wizIceThorn", false, "Ледяной шип", "Ice spike"),
            new Skill(2, 1, "skill_wizInferno", false, "Силовая волна", "Power wave"),
            new Skill(2, 2, "skill_wizShield", true, "Барьер", "Barrier"),
            new Skill(2, 2, "skill_wizHeal", false, "Восстановление", "Restoration"),
            new Skill(2, 2, "skill_wizControl", false, "Подчинение", "Mind control"),
            new Skill(3, 0, "skill_huntAtckSpd", true, "Выпад", "Lunge"),
            new Skill(3, 0, "skill_huntAtckBlood", false, "Кровопийца", "Bloodsucker"),
            new Skill(3, 0, "skill_huntAtckRange", false, "Казнь", "Execution"),
            new Skill(3, 1, "skill_huntArchMain", true, "Арбалетный выстрел", "Crossbow shot"),
            new Skill(3, 1, "skill_huntArchMulti", false, "Арбалетный залп", "Crossbow volley"),
            new Skill(3, 1, "skill_huntArchStun", false, "Паутинный болт", "Web bolt"),
            new Skill(3, 2, "skill_huntSpecStep", true, "Шаг убийцы", "Assassin's step"),
            new Skill(3, 2, "skill_huntSpecThrow", false, "Веер клинков", "Fan of blades"),
            new Skill(3, 2, "skill_huntSpecSign", false, "Метка Жертвы", "Victim's Mark"),
            new Skill(4, 0, "skill_bersAtckStun", true, "Крушитель", "Crusher"),
            new Skill(4, 0, "skill_bersAtckDefence", false, "Костолом", "Bonecrusher"),
            new Skill(4, 0, "skill_bersAtckBloodMP", false, "Коготь боли", "Claw of pain"),
            new Skill(4, 1, "skill_bersStrRotate", true, "Стальной вихрь", "Steel whirlwind"),
            new Skill(4, 1, "skill_bersStrRandom", false, "Неистовство", "Fury"),
            new Skill(4, 1, "skill_bersStrRangeOnce", false, "Алая Жатва", "Crimson Harvest"),
            new Skill(4, 2, "skill_bersSpecAngry", true, "Ярость", "Rage"),
            new Skill(4, 2, "skill_bersSpecMad", false, "Боевое Безумие", "Battle Madness"),
            new Skill(4, 2, "skill_bersSpecBloodHP", false, "Упоение Битвой", "Battle Frenzy"),
            new Skill(5, 0, "skill_necrSpd_DarkSphere", true, "Сфера Тьмы", "Sphere of Darkness"),
            new Skill(5, 0, "skill_necrSpd_Flame", false, "Дыхание смерти", "Breath of Death"),
            new Skill(5, 0, "skill_necrSpd_Soul", false, "Проклятая душа", "Cursed soul"),
            new Skill(5, 1, "skill_necrStr_Spawner", true, "Печать Проклятых", "Seal of the Damned"),
            new Skill(5, 1, "skill_necrStr_Blood", false, "Алая Печать", "Crimson Seal"),
            new Skill(5, 1, "skill_necrStr_Ress", false, "Печать Смерти", "Death Seal"),
            new Skill(5, 2, "skill_necrSpec_Skeletons", true, "Призыв Скелета", "Summon Skeleton"),
            new Skill(5, 2, "skill_necrSpec_Archers", false, "Призыв Скелета-Лучника", "Summon Skeleton-Archer"),
            new Skill(5, 2, "skill_necrSpec_Zombies", false, "Призыв Зомби", "Summon Zombie"),
        };

        public static Field Gold()
        {
            return new Field(GoldKey, L.T("Золото", "Gold"), 0, GoldMax);
        }

        public static Field Points(Hero hero)
        {
            return new Field("curPoints" + hero.Index, hero.Title + L.T(": очки навыков", ": skill points"), 0, PointsMax);
        }

        public static Field AttributeOf(Hero hero, HeroAttribute a)
        {
            return new Field(hero.ClassKey + a.Suffix,
                             hero.Title + ": " + (L.Ru ? a.Title.ToLowerInvariant() : a.Title), 0, AttributeMax);
        }

        public static Field SkillField(Skill s)
        {
            // стартовый навык ниже 1 не опустить: игра всё равно покажет 1
            return new Field(s.Key, Heroes[s.HeroIndex].Title + L.T(": «" + s.Title + "»", ": “" + s.Title + "”"),
                             s.IsDefault ? 1 : 0, SkillMax, s.IsDefault);
        }

        public static List<Skill> SkillsOf(Hero hero)
        {
            List<Skill> list = new List<Skill>();
            foreach (Skill s in Skills) if (s.HeroIndex == hero.Index) list.Add(s);
            return list;
        }

        /// <summary>Все поля редактора в том порядке, в каком они показаны в окне.</summary>
        public static List<Field> AllFields()
        {
            List<Field> list = new List<Field>();
            list.Add(Gold());
            foreach (Hero h in Heroes)
            {
                list.Add(Points(h));
                foreach (HeroAttribute a in Attributes) list.Add(AttributeOf(h, a));
            }
            foreach (Skill s in Skills) list.Add(SkillField(s));
            return list;
        }

        /// <summary>Поле по ключу; null — такого поля у редактора нет.</summary>
        public static Field FieldOf(string key)
        {
            foreach (Field f in AllFields()) if (f.Key == key) return f;
            return null;
        }

        /// <summary>
        /// Имя значения в реестре. Unity дописывает к ключу «_h» и подпись —
        /// вариант хеша djb2 по байтам UTF-8: h = h * 33 XOR байт, 32 бита.
        /// Проверено на всех 646 значениях игры 18.09.2026.
        /// </summary>
        public static string RegistryName(string key)
        {
            return key + "_h" + UnityHash(key).ToString();
        }

        public static uint UnityHash(string key)
        {
            uint h = 5381;
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            unchecked
            {
                foreach (byte b in bytes) h = (h * 33) ^ b;
            }
            return h;
        }
    }
}
