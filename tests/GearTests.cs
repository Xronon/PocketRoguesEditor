using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Самопроверка снаряжения: справочник, разбор JSON, правила игры для вещей, запись и отмена
    /// на временном разделе реестра. Образец похож на снаряжение некроманта от 18.09.2026, но
    /// собран здесь же — настоящие данные игры не читаются.
    /// </summary>
    internal static partial class SelfTest
    {
        private static string Q(string s) { return "\"" + s + "\""; }

        private static string ItemJson(string path, string[] effects, int quality, int emptySlots, string[] curses, int id)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"path\":").Append(Q(path)).Append(",\"effects\":[");
            for (int i = 0; i < effects.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(effects[i]));
            sb.Append("],\"count\":1,\"quality\":").Append(quality)
              .Append(",\"upgradeProgress\":0,\"emptySlots\":").Append(emptySlots)
              .Append(",\"removedEffectsCount\":0,\"wasCreatedInFortress\":false,\"curses\":[");
            for (int i = 0; i < curses.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(curses[i]));
            sb.Append("],\"isLocked\":false,\"uniqueID\":").Append(id).Append("}");
            return sb.ToString();
        }

        private const string LessMp = "Effects/ArmorEffects/1.35/Armor Effect - LessMpOnLowHp";
        private const string Stone = "Effects/ArmorEffects/Armor Effect - Stone Force";
        private const string AddHp = "Effects/ArmorEffects/1.23/Armor Effect - Add HP";
        private const string MissCurse = "Effects/Curses/Curse - Miss Chance~0";

        private static string SampleEquip()
        {
            return "{\"position\":{\"x\":3.9423160552978517,\"y\":9.835034370422364},"
                 + "\"head\":" + ItemJson("Items/Helmets/Head2.2rustHelmet",
                        new string[] { LessMp + "~2", Stone + "~0", AddHp + "~0" }, 3, 0, new string[0], 919884) + ","
                 + "\"body\":" + ItemJson("Items/Armor/Armor5.5CloakPlus", new string[0], 0, 0, new string[0], 818690) + ","
                 + "\"shieldWeapon\":" + ItemJson("", new string[0], 0, 0, new string[0], 0) + ","
                 + "\"weapon\":" + ItemJson("Items/Weapons/Wiz5Battle2", new string[0], 0, 0, new string[0], 574456) + ","
                 + "\"ring1\":" + ItemJson("Items/Rings/Ring - Hollow",
                        new string[] { "Effects/RingEffects/Ring Effect - Add HP Reg Percent~0" }, 2, 0, new string[0], 640235) + ","
                 + "\"hp_cur\":99999,\"usableSlots\":[-1,-1]}";
        }

        private static string SampleBag(string robeDefault)
        {
            return "{\"Items\":[" + ItemJson("Items/Armor/Armor4Robe", new string[] { robeDefault + "~0", AddHp + "~1" },
                                             1, 0, new string[] { MissCurse }, 111)
                 + "," + ItemJson("Items/Usables/EatCheese", new string[0], 0, 0, new string[0], 222) + "]}";
        }

        /// <summary>
        /// Описания эффектов — как их строит игра: число ступени, десятые, тип урона, проклятия,
        /// кольца и артефакты. Образцы сверены с кодом игры (ItemEffect.GetDescription,
        /// ItemEffectContainer.Initialize) и её переводом 19.09.2026.
        /// </summary>
        private static void DescriptionSuite(Catalog cat)
        {
            EffectInfo hp = cat.EffectByPath(AddHp);
            Check(hp.DescribeAt(4, "") == "Ув. макс. запас ОЗ на 32 ед.", "описание Искры Жизни с числом ступени 5: " + hp.DescribeAt(4, ""));
            Check(hp.DescribeAt(0, "") == "Ув. макс. запас ОЗ на 10 ед.", "описание Искры Жизни на ступени 1");

            EffectInfo aura = cat.EffectByPath("Effects/ArmorEffects/Armor Effect - Life Aura");
            Check(aura != null && aura.Decimal && aura.ValueAt(4) == "1,4" && aura.Range() == "0,2 → 1,4",
                  "значения в десятых игра делит на 10: " + (aura != null ? aura.Range() : "нет эффекта"));
            Check(aura.DescribeAt(4, "").Contains("на 1,4 ед./с"), "описание с десятыми: " + aura.DescribeAt(4, ""));

            EffectInfo fire = cat.EffectByPath("Effects/ArmorEffects/1.37/Armor Effect - DmgPercent Fire");
            Check(fire != null && fire.DescribeAt(0, "") == "Весь наносимый Огненный урон ув. на 12%",
                  "тип урона подставлен, как в игре: " + (fire != null ? fire.DescribeAt(0, "") : "нет эффекта"));

            EffectInfo miss = cat.EffectByPath(MissCurse.Split('~')[0]);
            Check(miss.DescribeAt(0, "") == "Каждая атака с шансом в 8% может промахнуться по противнику",
                  "проклятие: число из самого проклятия: " + miss.DescribeAt(0, ""));

            EffectInfo reg = cat.EffectByPath("Effects/RingEffects/Ring Effect - Add HP Reg Percent");
            Check(reg.DescribeAt(0, "") == "Увеличивает скорость восстановления ОЗ на 6%"
                  && reg.DescribeAt(3, "") == "Увеличивает скорость восстановления ОЗ на 15%",
                  "кольцо: число по ступени кольца: " + reg.DescribeAt(3, ""));
            EffectInfo trade = cat.EffectByPath("Effects/RingEffects/Ring Effect - Up Def - Down Dmg");
            Check(trade.DescribeAt(4, "") == "Физ. защита увеличена на 20%, но наносимый урон снижен на 5%",
                  "кольцо с двумя числами: " + trade.DescribeAt(4, ""));
            EffectInfo skill = cat.EffectByPath("Effects/RingEffects/1_37/Ring Effect - Add Skill Lvl");
            string sd = skill.DescribeAt(2, "~1");
            Check(sd.Contains("'" + skill.Skills[1].ToUpperInvariant() + "'") && sd.EndsWith("увеличен на 6"),
                  "кольцо навыка: навык по номеру из сохранения (~1): " + sd);
            EffectInfo rainbow = cat.EffectByPath("Effects/RingEffects/MainRings/Ring Effect Base - Rainbow");
            Check(rainbow.DescribeAt(0, "~-2") == "Любой силовой навык получает +1 к текущему уровню",
                  "радужное кольцо: кнопка атаки (~-2 — силовой): " + rainbow.DescribeAt(0, "~-2"));

            ItemInfo claw = cat.ItemByPath("items/trash/135/itemart_dmgmod2");
            Check(claw != null && claw.Properties.Count == 1
                  && claw.Properties[0].DescribeAt(claw.Properties[0].DefaultLevel, "") == "+2 к наносимому урону",
                  "артефакт: свойства из справочника («Сломанный коготь»)");

            // все эффекты брони и оружия описаны (кроме одного, у которого описания нет и в игре),
            // нигде не осталось разметки и неподставленных пометок
            int noText = 0, leftovers = 0;
            string sample = "";
            foreach (EffectInfo e in cat.Effects)
            {
                if ((e.Class == "ItemEffectArmor" || e.Class == "ItemEffectWeapon") && e.Description.Length == 0) noText++;
                string d = e.DescribeAt(e.IsRingEffect ? e.DefaultLevel : e.MaxTier, e.Kind == 69 ? "~-1" : "~0");
                // «[тек. этаж * 4]» — это текст игры; пометка — «[» и латинская заглавная: [DMG_TYPE]
                int br = d.IndexOf('[');
                bool mark = br >= 0 && br + 1 < d.Length && d[br + 1] >= 'A' && d[br + 1] <= 'Z';
                if (d.IndexOf('{') >= 0 || mark || d.IndexOf('<') >= 0) { leftovers++; sample = d; }
            }
            Check(noText <= 1, "у эффектов брони и оружия есть описания (без описания: " + noText + ")");
            Check(leftovers == 0, "в описаниях нет пометок и разметки" + (sample.Length > 0 ? ": " + sample : ""));

            Check(GearRules.QualityTitle(1) == "особое" && GearRules.QualityTitle(2) == "древнее",
                  "названия качества — как в игре (особое, древнее)");
        }

        private const string RingReg = "Effects/RingEffects/Ring Effect - Add HP Reg Percent";
        private const string RingAgi = "Effects/RingEffects/Ring Effect - Add Stat Agility";
        private const string RingGold = "Effects/RingEffects/MainRings/Ring Effect Base - Gold";
        private const string RingRainbow = "Effects/RingEffects/MainRings/Ring Effect Base - Rainbow";

        /// <summary>
        /// Правила колец (разобраны 19.09.2026, SO_ItemEquip.NormalizeEffects — ветка SO_ItemRing,
        /// SO_ItemRing.NormalizeEffects, AddRandomEffect): очки — качество + 2, эффект стоит ступень,
        /// один эффект каждого рода, без несовместимых, врождённый не убирается, хвост кольца навыка цел.
        /// </summary>
        private static void RingSuite(Catalog cat)
        {
            string equip = "{\"ring1\":" + ItemJson("Items/Rings/Ring - Hollow", new string[] { RingReg + "~0" }, 2, 0, new string[0], 1)
                         + ",\"ring2\":" + ItemJson("Items/Rings/Ring - Gold",
                               new string[] { RingGold + "~0", "Effects/RingEffects/Ring Effect - Aura Anti Humanoid~0" }, 0, 0, new string[0], 2)
                         + ",\"art1\":" + ItemJson("Items/Rings/Ring - Rainbow", new string[] { RingRainbow + "~0~-2" }, 1, 0, new string[0], 3)
                         + ",\"hp_cur\":10}";
            Dictionary<string, string> texts = new Dictionary<string, string>();
            texts[GearRules.EquipKey(5)] = equip;
            Dictionary<string, int> ints = new Dictionary<string, int>();
            ints["curChar"] = 1;
            HeroGear g = LoadFrom(texts, ints, cat, 5);
            GearItem hollow = Find(g, "Кольцо 1"), gold = Find(g, "Кольцо 2"), rainbow = Find(g, "Артефакт 1");
            Check(hollow != null && hollow.Editable && hollow.IsRing && hollow.RingBudget == 4 && hollow.RingPoints == 1,
                  "полое кольцо (древнее) правится: 4 очка, занято 1");

            EffectInfo agi = cat.EffectByPath(RingAgi);
            Check(agi != null && hollow.TierForNew(agi) == 2, "новый эффект — высшая ступень, что влезает: 3-я");
            Check(hollow.AddEffect(agi, hollow.TierForNew(agi)) == null && hollow.RingPoints == 4, "эффект добавлен, очки 4 из 4");
            bool sameKind = false;
            foreach (EffectInfo e in hollow.Addable()) if (e.Kind == agi.Kind) sameKind = true;
            Check(!sameKind, "после «+Ловкость» других прибавок к характеристикам в списке нет — один род");
            EffectInfo other = null;
            foreach (EffectInfo e in cat.ItemByPath("items/rings/ring - hollow").Pool)
                if (e.Kind != agi.Kind && !hollow.Has(e) && e.Class == "ItemEffectArtifact") { other = e; break; }
            Check(hollow.AddEffect(other, 0) != null, "очков нет — эффект не добавить");
            GearEffect reg = hollow.Effects[0];
            Check(hollow.SetTier(reg, 1) != null, "ступень выше бюджета не поднять");
            Check(hollow.SetQuality(4) == null && hollow.RingBudget == 6 && hollow.SetTier(reg, 1) == null && hollow.RingPoints == 5,
                  "легендарное — 6 очков, ступень поднялась");
            Check(hollow.SetQuality(0) != null, "качество ниже занятых очков не опустить");

            // несовместимые: берём из пула эффект, у которого есть несовместимый в том же пуле
            hollow.Reset();
            EffectInfo x = null, y = null;
            foreach (EffectInfo e in cat.ItemByPath("items/rings/ring - hollow").Pool)
                foreach (EffectInfo i in e.Incompatible)
                    if (x == null && cat.ItemByPath("items/rings/ring - hollow").Pool.Contains(i) && i.Kind != e.Kind
                        && e.Kind != reg.Info.Kind && i.Kind != reg.Info.Kind) { x = e; y = i; }
            Check(x != null && hollow.AddEffect(x, 0) == null && !hollow.Addable().Contains(y) && hollow.AddEffect(y, 0) != null,
                  "несовместимый эффект не предлагается и не добавляется" + (x != null ? " («" + x.Title + "» и «" + y.Title + "»)" : ""));

            Check(gold.Editable && gold.Effects[0].IsDefault && gold.RemoveEffect(gold.Effects[0]) != null,
                  "врождённый эффект золотого перстня не убрать");
            Check(gold.RingPoints == 2 && gold.RingBudget == 2 && gold.AddEffect(other, 0) != null, "у обычного перстня очки заняты");

            GearEffect rb = rainbow.Effects[0];
            Check(rainbow.SetQuality(2) == null && rainbow.SetTier(rb, 1) == null && rb.ToSave() == RingRainbow + "~1~-2",
                  "кольцо навыка: ступень меняется, кнопка атаки в хвосте цела");

            Dictionary<string, string> built = g.BuildTexts();
            JsonNode root = MiniJson.Parse(built[GearRules.EquipKey(5)]);
            JsonNode r1 = root.Get("ring1");
            List<string> eff = r1.GetStrings("effects");
            Check(eff.Count == 2 && eff[0] == RingReg + "~0" && eff[1] == x.Path + "~0" && r1.GetInt("emptySlots", -1) == 0,
                  "в тексте кольца новые эффекты, пустые ячейки не тронуты");
            Check(root.Get("ring2").GetStrings("effects").Count == 2 && root.Get("art1").GetInt("quality", -1) == 2,
                  "нетронутое кольцо как было, радужное — с новым качеством");
        }

        private static void GearSuite(string regPath)
        {
            // --- справочник, вшитый в программу ---
            Catalog cat = Catalog.Embedded();
            Check(cat.Effects.Count >= 200 && cat.Items.Count >= 250, "справочник вшит и прочитан");
            ItemInfo helm = cat.ItemByPath("Items/Helmets/Head2.2rustHelmet");
            Check(helm != null && helm.Title == "Ржавый шлем" && helm.Kind == GearKind.Head, "Ржавый шлем найден по пути из сохранения");
            Check(helm.Defaults.Count == 0 && helm.Pool.Count > 10, "у шлема нет врождённых, пул есть");
            EffectInfo hp = cat.EffectByPath(AddHp);
            Check(hp != null && hp.Title == "Искра Жизни" && hp.Levels.Length == 5 && hp.Levels[4] == 32, "Искра Жизни: 5 ступеней, до 32");
            int black = 0;
            foreach (EffectInfo e in cat.Effects) if (e.Blacklisted) black++;
            Check(black == 4, "в чёрном списке 4 эффекта");
            ItemInfo robe = cat.ItemByPath("items/armor/armor4robe");
            Check(robe != null && robe.Defaults.Count == 1, "у робы один врождённый эффект");

            DescriptionSuite(cat);
            RingSuite(cat);

            // --- JSON: без замен текст не меняется ни на байт ---
            string equip = SampleEquip();
            JsonNode root = MiniJson.Parse(equip);
            Check(MiniJson.Splice(equip, new List<Replacement>()) == equip, "склейка без замен — тот же текст");
            Check(root.Get("head").GetInt("quality", -1) == 3 && root.Get("head").GetStrings("effects").Count == 3, "JSON шлема прочитан");
            bool threw = false;
            try { MiniJson.Parse("{\"a\":[1,2"); } catch (FormatException) { threw = true; }
            Check(threw, "битый JSON отвергнут");

            // --- загрузка героя ---
            string bag = SampleBag(robe.Defaults[0].Path);
            Dictionary<string, string> texts = new Dictionary<string, string>();
            texts[GearRules.EquipKey(5)] = equip;
            texts[GearRules.BagKey(5)] = bag;
            Dictionary<string, int> ints = new Dictionary<string, int>();
            ints["curChar"] = 5;
            ints["NeedLoadingAutosave"] = 0;
            HeroGear g = LoadFrom(texts, ints, cat, 5);
            Check(g.State == GearState.Ok, "некромант с вылазкой загружен");
            Check(g.Items.Count == 6, "вещей 6: 4 надето (пустое место пропущено) и 2 в сумке");

            GearItem head = Find(g, "Голова");
            GearItem ring = Find(g, "Кольцо 1");
            GearItem robeItem = null, cheese = null;
            foreach (GearItem it in g.Items)
            {
                if (it.Place != "Сумка") continue;
                if (it.Path.EndsWith("Armor4Robe")) robeItem = it; else cheese = it;
            }
            Check(head != null && head.Editable && head.Capacity == 3, "шлем правится, норма 3");
            Check(ring != null && ring.Editable && ring.IsRing && ring.RingBudget == 4 && ring.RingPoints == 1,
                  "кольцо правится: древнее — 4 очка, занято 1");
            Check(cheese != null && !cheese.Editable, "сыр не правится");
            Check(robeItem != null && robeItem.Editable && robeItem.Capacity == 2 && robeItem.Effects[0].IsDefault,
                  "роба: норма 1 + 1 врождённый, врождённый распознан");

            // --- правила ---
            Check(head.AddEffect(cat.EffectByPath("Effects/ArmorEffects/1.23/Armor Effect - Add Dmg Persent"), 4) != null,
                  "в полную вещь эффект не добавить");
            Check(head.SetQuality(2) != null, "качество ниже числа эффектов не опустить");
            Check(head.SetQuality(4) == null && head.Capacity == 4, "качество подняли до легендарного");
            EffectInfo weaponEff = null;
            foreach (EffectInfo e in cat.Effects) if (e.Class == "ItemEffectWeapon" && !e.Blacklisted) { weaponEff = e; break; }
            Check(head.AddEffect(weaponEff, 0) != null, "эффект оружия на шлем не добавить");
            EffectInfo blackArmor = null;
            foreach (EffectInfo e in cat.Effects) if (e.Blacklisted && e.Class == "ItemEffectArmor") { blackArmor = e; break; }
            Check(head.AddEffect(blackArmor, 0) != null, "эффект из чёрного списка не добавить");
            Check(head.AddEffect(hp, 0) != null, "повтор эффекта не добавить");
            List<EffectInfo> addable = head.Addable();
            bool addableOk = addable.Count > 0;
            foreach (EffectInfo e in addable)
                if (e.Class != "ItemEffectArmor" || e.Blacklisted || head.Has(e) || !helm.Pool.Contains(e)) addableOk = false;
            Check(addableOk, "в списке «добавить» только подходящие эффекты из пула шлема");
            EffectInfo added = addable[0];
            Check(head.AddEffect(added, 99) == null && head.Effects.Count == 4, "эффект добавлен");
            Check(head.Effects[3].Tier == added.MaxTier, "неверная ступень заменена высшей");
            Check(head.SetTier(head.Effects[2], 4) == null && head.Effects[2].Tier == 4, "Искра Жизни поднята до 5-й ступени");
            Check(head.SetTier(head.Effects[2], 5) != null, "шестой ступени нет");
            Check(robeItem.RemoveEffect(robeItem.Effects[0]) != null, "врождённый эффект не убрать");
            Check(robeItem.RemoveCurse(MissCurse) == null && robeItem.Curses.Count == 0, "проклятие снято");

            // --- сборка текста ---
            Dictionary<string, string> built = g.BuildTexts();
            Check(built.Count == 2, "правки в двух записях: снаряжение и сумка");
            string newEquip = built[GearRules.EquipKey(5)];
            JsonNode ne = MiniJson.Parse(newEquip);
            JsonNode nh = ne.Get("head");
            Check(nh.GetInt("quality", -1) == 4 && nh.GetStrings("effects").Count == 4 && nh.GetInt("emptySlots", -1) == 0,
                  "в тексте шлема: легендарный, 4 эффекта, пустых ячеек 0");
            Check(nh.GetStrings("effects")[0] == LessMp + "~2" && nh.GetStrings("effects")[2] == AddHp + "~4",
                  "нетронутый эффект записан как был, правленый — с новой ступенью");
            Check(newEquip.Contains("3.9423160552978517") && newEquip.Contains("\"hp_cur\":99999"),
                  "остальное в записи не тронуто, дробные числа байт в байт");
            Check(newEquip.Substring(0, newEquip.IndexOf("\"head\"")) == equip.Substring(0, equip.IndexOf("\"head\"")),
                  "всё до шлема совпадает");
            Check(newEquip.Substring(newEquip.IndexOf("\"body\"")) == equip.Substring(equip.IndexOf("\"body\"")),
                  "всё после шлема совпадает");
            JsonNode nb = MiniJson.Parse(built[GearRules.BagKey(5)]).Get("Items").Items[0];
            Check(nb.GetStrings("curses").Count == 0 && nb.GetStrings("effects").Count == 2, "в сумке у робы снято проклятие");
            string desc = g.DescribeChanges(GearRules.EquipKey(5));
            Check(desc.Contains("Ржавый шлем") && desc.Contains("легендарное") && desc.Contains("+"), "подпись правки понятная: " + desc);

            head.Reset();
            robeItem.Reset();
            Check(!g.Changed && g.BuildTexts().Count == 0, "«вернуть как в игре» убирает все правки");

            // --- состояния героя ---
            ints["NeedLoadingAutosave"] = 1;
            Check(LoadFrom(texts, ints, cat, 5).State == GearState.InLocation, "этаж отмечен, а записи этажа нет — не правим");
            FloorSuite(texts, ints, bag, cat);
            StaticSuite(texts, ints, bag, cat);
            GiveSuite(cat);
            ints["NeedLoadingAutosave"] = 0;
            Check(LoadFrom(texts, ints, cat, 2).State == GearState.NoRaid, "у героя без вылазки вещей нет");
            texts[GearRules.EquipKey(5)] = "{\"head\":{";
            Check(LoadFrom(texts, ints, cat, 5).State == GearState.Broken, "битая запись — не трогаем");

            GearEngineCycle(regPath, equip, bag, cat);
        }

        /// <summary>Запись сохранённого этажа: похожа на мага в катакомбах от 19.09.2026, собрана здесь.</summary>
        private static string SampleFloor()
        {
            string empty = ItemJson("", new string[0], 0, 0, new string[0], 0);
            return "{\"position\":{\"x\":30.24357032775879,\"y\":32.743412017822269},"
                 + "\"head\":" + ItemJson("Items/Helmets/Head1.1frontlet", new string[0], 0, 0, new string[0], 11) + ","
                 + "\"body\":" + ItemJson("Items/Armor/Armor3.4NudeCostum", new string[0], 0, 0, new string[0], 12) + ","
                 + "\"weapon\":" + ItemJson("Items/Weapons/NecrStaff6", new string[0], 0, 0, new string[0], 13) + ","
                 + "\"hp_cur\":122,\"mp_cur\":180,\"floor\":[0,1,1,0,2],\"deep\":2,"
                 + "\"headSecChar\":" + empty + ",\"weaponSecChar\":" + empty + ",\"hp_curSecChar\":0}";
        }

        /// <summary>Герой в подземелье: вещи из записей этажа, правка уходит туда же.</summary>
        private static void FloorSuite(Dictionary<string, string> texts, Dictionary<string, int> ints, string bag, Catalog cat)
        {
            string floor = SampleFloor();
            texts[GearRules.FloorEquipKey] = floor;
            texts[GearRules.FloorBagKey] = bag;
            try
            {
                HeroGear fl = LoadFrom(texts, ints, cat, 5);
                Check(fl.State == GearState.Ok && fl.InDungeon && fl.EquipKey == GearRules.FloorEquipKey
                      && fl.BagKey == GearRules.FloorBagKey, "герой в подземелье: вещи из записей этажа");
                GearItem fh = Find(fl, "Голова");
                Check(fh != null && fh.Editable && fh.SourceKey == GearRules.FloorEquipKey, "шлем из этажа найден и правится");
                Check(Find(fl, "Оружие") != null && fl.Items.Count == 5,
                      "вещей 5: 3 надето, 2 в сумке; места второго героя не показаны");
                Check(fh.SetQuality(2) == null, "качество шлема в подземелье поднято");
                Dictionary<string, string> built = fl.BuildTexts();
                Check(built.Count == 1 && built.ContainsKey(GearRules.FloorEquipKey),
                      "правка ушла в запись этажа, запись вылазки и сумка не тронуты");
                string nf = built[GearRules.FloorEquipKey];
                Check(MiniJson.Parse(nf).Get("head").GetInt("quality", -1) == 2
                      && nf.Substring(nf.IndexOf("\"body\"")) == floor.Substring(floor.IndexOf("\"body\"")),
                      "в записи этажа поменялся только шлем, карта этажа и остальное — байт в байт");

                Check(LoadFrom(texts, ints, cat, 2).State == GearState.NoRaid, "этаж принадлежит текущему герою, не другому");
                texts["staticData_data"] = "{\"x\":1}";
                Check(LoadFrom(texts, ints, cat, 5).State == GearState.InLocation,
                      "в сохранении и этаж, и особая локация — не понять, где герой, не правим");
                texts.Remove("staticData_data");
                texts["reserve_staticData_data"] = "{\"staticLocation\":true}";
                Check(LoadFrom(texts, ints, cat, 5).State == GearState.Ok,
                      "старая запасная копия особой локации обычному этажу не мешает (случай из живого сохранения 19.09.2026)");
                texts.Remove("reserve_staticData_data");
                texts[GearRules.FloorEquipKey] = floor.Replace("\"deep\":2,", "\"deep\":2,\"staticLocation\":true,");
                Check(LoadFrom(texts, ints, cat, 5).State == GearState.InLocation, "этаж помечен особой локацией — не правим");
                texts[GearRules.FloorEquipKey] = floor;
                Check(GearRules.IsGearKey(GearRules.FloorEquipKey) && GearRules.IsGearKey(GearRules.FloorBagKey)
                      && !GearRules.IsGearKey("dungData_objData") && !GearRules.IsGearKey("reserve_dungData_data"),
                      "записи этажа с вещами — наши, прочие записи этажа — чужие");
                Check(Engine.TitleOfGearKey(GearRules.FloorEquipKey).Contains("подземель"), "подпись записи этажа понятная");
            }
            finally
            {
                texts.Remove(GearRules.FloorEquipKey);
                texts.Remove(GearRules.FloorBagKey);
            }
        }

        /// <summary>
        /// Герой в особой локации — Лагерь (в коде _shop), как у мага 19.09.2026: вещи в staticData_data и
        /// staticData_invData, флаги NeedLoading = 1, NeedLoadingAutosave = 0, запись этажа пуста.
        /// </summary>
        private static void StaticSuite(Dictionary<string, string> texts, Dictionary<string, int> ints, string bag, Catalog cat)
        {
            string shop = SampleFloor().Replace("\"deep\":2,", "\"deep\":10,\"staticLocation\":true,\"location\":6,");
            texts[GearRules.StaticEquipKey] = shop;
            texts[GearRules.StaticBagKey] = bag;
            texts["reserve_dungData_data"] = SampleFloor();   // старая запасная копия этажа — не мешает
            int autosave = ints["NeedLoadingAutosave"];
            ints["NeedLoadingAutosave"] = 0;
            ints["NeedLoading"] = 1;
            try
            {
                HeroGear sg = LoadFrom(texts, ints, cat, 5);
                Check(sg.State == GearState.Ok && sg.InDungeon && sg.InStatic && sg.EquipKey == GearRules.StaticEquipKey
                      && sg.BagKey == GearRules.StaticBagKey, "герой в Лагере: вещи из записей особой локации");
                Check(sg.StateText.Contains("в Лагере"), "окно говорит, что герой в Лагере: " + sg.StateText);
                GearItem sh = Find(sg, "Голова");
                Check(sh != null && sh.SetQuality(2) == null, "шлем в Лагере правится");
                Dictionary<string, string> built = sg.BuildTexts();
                Check(built.Count == 1 && built.ContainsKey(GearRules.StaticEquipKey),
                      "правка ушла в запись особой локации, остальные записи не тронуты");
                Check(GearRules.IsGearKey(GearRules.StaticEquipKey) && GearRules.IsGearKey(GearRules.StaticBagKey)
                      && !GearRules.IsGearKey("staticData_objData") && !GearRules.IsGearKey("reserve_staticData_data"),
                      "записи локации с вещами — наши, прочие — чужие");

                texts[GearRules.StaticEquipKey] = SampleFloor();
                Check(LoadFrom(texts, ints, cat, 5).State == GearState.InLocation,
                      "запись локации без пометки «особая» — не понимаем её, не правим");
                texts[GearRules.StaticEquipKey] = shop;

                ints["NeedLoading"] = 0;
                ints["NeedLoadingAutosaveReserve"] = 1;
                HeroGear rg = LoadFrom(texts, ints, cat, 5);
                Check(rg.State == GearState.InLocation && rg.StateText.Contains("сбоя"),
                      "после сбоя игра восстановит запасную копию — не правим");
                ints.Remove("NeedLoadingAutosaveReserve");
                ints["NeedLoading"] = 1;
            }
            finally
            {
                texts.Remove(GearRules.StaticEquipKey);
                texts.Remove(GearRules.StaticBagKey);
                texts.Remove("reserve_dungData_data");
                ints.Remove("NeedLoading");
                ints.Remove("NeedLoadingAutosaveReserve");
                ints["NeedLoadingAutosave"] = autosave;
            }
        }

        private static HeroGear LoadFrom(Dictionary<string, string> texts, Dictionary<string, int> ints, Catalog cat, int hero)
        {
            return HeroGear.Load(hero,
                new HeroGear.TextReader(delegate(string key)
                {
                    string t;
                    return texts.TryGetValue(key, out t) ? TextValue.Of(t) : TextValue.Missing();
                }),
                new HeroGear.IntReader(delegate(string key)
                {
                    int v;
                    return ints.TryGetValue(key, out v) ? IntValue.Of(v) : IntValue.Missing();
                }), cat);
        }

        private static GearItem Find(HeroGear g, string place)
        {
            foreach (GearItem it in g.Items) if (it.Place == place) return it;
            return null;
        }

        /// <summary>Запись снаряжения и её отмена на временном разделе реестра.</summary>
        private static void GearEngineCycle(string regPath, string equip, string bag, Catalog cat)
        {
            string dir = Path.Combine(Path.GetTempPath(), "PocketRoguesEditor-GearTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(regPath))
                {
                    k.SetValue(GameData.RegistryName("money"), 100, RegistryValueKind.DWord);
                    k.SetValue(GameData.RegistryName("curChar"), 5, RegistryValueKind.DWord);
                    k.SetValue(GameData.RegistryName(GearRules.EquipKey(5)), Utf8Z(equip), RegistryValueKind.Binary);
                    k.SetValue(GameData.RegistryName(GearRules.BagKey(5)), Utf8Z(bag), RegistryValueKind.Binary);
                }
                PrefsStore store = new PrefsStore(regPath);
                Engine engine = new Engine(store, dir, new Engine.GameCheck(GameCheck));

                // строка туда-обратно, в том числе кириллица
                store.WriteText("CurInventory_3", "{\"Items\":[],\"заметка\":\"проверка\"}");
                Check(store.ReadText("CurInventory_3").Text == "{\"Items\":[],\"заметка\":\"проверка\"}", "строка с кириллицей записана и прочитана");
                store.DeleteText("CurInventory_3");
                Check(store.ReadText("CurInventory_3").State == ValueState.Missing, "строка удалена");
                Check(store.ReadText("money").State == ValueState.Foreign, "число строкой не читается");

                Snapshot snap = engine.Load();
                Check(snap.GetText(GearRules.EquipKey(5)).Text == equip, "снимок содержит запись снаряжения как есть");
                HeroGear g = HeroGear.Load(5,
                    new HeroGear.TextReader(delegate(string key) { return GearRules.IsGearKey(key) ? snap.GetText(key) : engine.ReadText(key); }),
                    new HeroGear.IntReader(engine.Read), cat);
                Check(g.State == GearState.Ok, "герой из реестра загружен");

                GearItem head = Find(g, "Голова");
                head.SetQuality(4);
                head.AddEffect(head.Addable()[0], 4);
                Dictionary<string, string> texts = g.BuildTexts();
                Dictionary<string, string> titles = new Dictionary<string, string>();
                titles[GearRules.EquipKey(5)] = "Некромант: снаряжение — " + g.DescribeChanges(GearRules.EquipKey(5));

                FakeRunning = true;
                Check(!engine.SaveGear(snap, texts, titles).Ok, "при запущенной игре снаряжение не пишется");
                FakeRunning = false;

                Outcome o = engine.SaveGear(snap, texts, titles);
                Check(o.Ok, "снаряжение записано: " + o.Message);
                Check(store.ReadText(GearRules.EquipKey(5)).Text == texts[GearRules.EquipKey(5)], "в реестре новая запись");
                Check(store.ReadText(GearRules.BagKey(5)).Text == bag, "сумка не тронута");
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(regPath))
                {
                    byte[] raw = (byte[])k.GetValue(GameData.RegistryName(GearRules.EquipKey(5)));
                    Check(raw[raw.Length - 1] == 0 && raw[raw.Length - 2] != 0, "строка записана как у Unity: один ноль в конце");
                }

                Check(!engine.SaveGear(snap, texts, titles).Ok, "старый снимок — повторная запись отклонена");

                List<EditRecord> history = engine.History();
                Check(history.Count == 1 && history[0].Changes[0].IsText && history[0].Summary().Contains("Некромант: снаряжение"),
                      "правка снаряжения в истории");
                Check(history[0].Changes[0].OldText == equip, "в файле отмены прежняя запись целиком");

                o = engine.Undo(history[0]);
                Check(o.Ok, "отмена снаряжения: " + o.Message);
                Check(store.ReadText(GearRules.EquipKey(5)).Text == equip, "запись вернулась байт в байт");

                // герой в подземелье: запись этажа — туда и обратно
                string floor = SampleFloor();
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(regPath, true))
                {
                    k.SetValue(GameData.RegistryName("NeedLoadingAutosave"), 1, RegistryValueKind.DWord);
                    k.SetValue(GameData.RegistryName(GearRules.FloorEquipKey), Utf8Z(floor), RegistryValueKind.Binary);
                }
                Snapshot fsnap = engine.Load();
                HeroGear fg = HeroGear.Load(5,
                    new HeroGear.TextReader(delegate(string key) { return GearRules.IsGearKey(key) ? fsnap.GetText(key) : engine.ReadText(key); }),
                    new HeroGear.IntReader(engine.Read), cat);
                Check(fg.State == GearState.Ok && fg.InDungeon, "герой в подземелье загружен из реестра");
                Find(fg, "Голова").SetQuality(3);
                Dictionary<string, string> ftexts = fg.BuildTexts();
                Dictionary<string, string> ftitles = new Dictionary<string, string>();
                ftitles[GearRules.FloorEquipKey] = "Некромант (в подземелье): снаряжение — " + fg.DescribeChanges(GearRules.FloorEquipKey);
                o = engine.SaveGear(fsnap, ftexts, ftitles);
                Check(o.Ok && store.ReadText(GearRules.FloorEquipKey).Text == ftexts[GearRules.FloorEquipKey],
                      "запись этажа записана: " + o.Message);
                Check(store.ReadText(GearRules.EquipKey(5)).Text == equip, "запись вылазки при этом не тронута");
                // правки в самопроверке идут в одну секунду — порядок в истории не опора, ищем по записи
                EditRecord floorEdit = null;
                foreach (EditRecord r in engine.History())
                    if (!r.Undone && r.Changes.Count == 1 && r.Changes[0].Key == GearRules.FloorEquipKey
                        && r.Changes[0].OldText == floor) floorEdit = r;
                Check(floorEdit != null, "правка этажа есть в истории");
                o = floorEdit != null ? engine.Undo(floorEdit) : Outcome.Fail("нет правки");
                Check(o.Ok && store.ReadText(GearRules.FloorEquipKey).Text == floor,
                      "отмена вернула запись этажа байт в байт: " + o.Message);

                string bad = Path.Combine(dir, "bad.undo");
                File.WriteAllText(bad, "#when\t2026-09-18 11:00:00\nmoney\t1\t=YQ==\t=Yg==\t0\tчужое\n", new UTF8Encoding(true));
                Check(Engine.ReadRecord(bad) == null, "строковая правка чужого ключа из файла отвергнута");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static byte[] Utf8Z(string s)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            byte[] b = new byte[utf8.Length + 1];
            Buffer.BlockCopy(utf8, 0, b, 0, utf8.Length);
            return b;
        }
    }
}
