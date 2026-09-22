using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Самопроверка перевода (1.8): выбор языка, английский справочник, подписи и то, что правила
    /// снаряжения не зависят от языка. Остальная самопроверка идёт по-русски.
    /// </summary>
    internal static partial class SelfTest
    {
        private static bool HasCyrillic(string s)
        {
            foreach (char c in s ?? "") if (c >= (char)0x400 && c <= (char)0x4FF) return true;
            return false;
        }

        private static void LanguageSuite()
        {
            // --- какой язык выбрать ---
            Check(L.Decide(L.Russian, 0), "«Русский» в настройке — по-русски и при английской игре");
            Check(!L.Decide(L.English, 1), "«English» — по-английски и при русской игре");
            Check(L.Decide(L.Auto, 1), "как в игре: lang = 1 — русский");
            Check(!L.Decide(L.Auto, 0), "как в игре: lang = 0 — английский");
            Check(!L.Decide(L.Auto, 2) && !L.Decide(L.Auto, 4), "как в игре: украинский и немецкий — английский");

            string text;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Catalog.ResourceName))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                text = r.ReadToEnd();

            bool was = L.Ru;
            try
            {
                L.Ru = true;
                Catalog ru = Catalog.Parse(text);
                L.Ru = false;
                Catalog en = Catalog.Parse(text);

                // --- справочник по-английски ---
                Check(en.Effects.Count == ru.Effects.Count && en.Items.Count == ru.Items.Count,
                      "английский справочник того же состава, что русский");
                const string wrath = "Effects/ArmorEffects/1.23/Armor Effect - Add Dmg";
                EffectInfo wr = ru.EffectByPath(wrath);
                EffectInfo we = en.EffectByPath(wrath);
                Check(wr != null && we != null && wr.Title == "Гнев" && we.Title == "Wrath", "«Гнев» по-английски — Wrath");
                string d = we.DescribeAt(we.MaxTier, "");
                Check(d.StartsWith("Base damage increased by ") && !d.Contains("{0}"), "описание по-английски, с числом: " + d);
                int cyr = 0;
                string firstCyr = "";
                foreach (EffectInfo e in en.Effects)
                    if (HasCyrillic(e.Title) || HasCyrillic(e.Description)) { cyr++; if (firstCyr.Length == 0) firstCyr = e.Name; }
                foreach (ItemInfo it in en.Items)
                    if (HasCyrillic(it.Title)) { cyr++; if (firstCyr.Length == 0) firstCyr = it.ResourcePath; }
                Check(cyr == 0, "в английском справочнике нет русских названий и описаний (" + cyr + ", первое: " + firstCyr + ")");
                ItemInfo knight = en.ItemByPath("items/armor/armor10.1strongarmor");
                Check(knight != null && knight.Title == "Knight's armor" && knight.StatText.StartsWith("armor "),
                      "вещь по-английски: Knight's armor, «armor N»");

                // дробные ступени: по-английски с точкой, по-русски с запятой
                EffectInfo dec = null;
                foreach (EffectInfo e in en.Effects)
                    if (e.Decimal && e.Levels.Length > 0 && e.Levels[0] % 10 != 0) { dec = e; break; }
                Check(dec != null && dec.ValueAt(0).Contains(".") && !dec.ValueAt(0).Contains(","), "дробь по-английски с точкой");
                L.Ru = true;
                Check(dec != null && dec.ValueAt(0).Contains(","), "дробь по-русски с запятой");
                L.Ru = false;

                // --- подписи программы ---
                Check(GameData.Heroes[4].Title == "Berserk" && GameData.Attributes[3].Title == "Intelligence",
                      "герои и характеристики — как в английской игре");
                Check(GameData.SkillField(GameData.Skills[0]).Title == "Warrior: “Slashing strike”", "навык по-английски");
                Check(GearRules.QualityTitle(4) == "legendary" && GearRules.BagPlace == "Bag", "качество и сумка по-английски");
                Check(HeroGear.BagFull == "The inventory is full.", "«Инвентарь заполнен» по-английски");
                Check(Engine.TitleOfGearKey(GearRules.BagKey(2)) == "Wizard: bag", "подпись записи по-английски");

                // --- правила снаряжения по-английски работают так же ---
                Dictionary<string, string> texts = new Dictionary<string, string>();
                texts[GearRules.EquipKey(5)] = EquipWithEmpty();
                texts[GearRules.BagKey(5)] = SampleBag(en.ItemByPath("items/armor/armor4robe").Defaults[0].Path);
                Dictionary<string, int> ints = new Dictionary<string, int>();
                ints["curChar"] = 5;
                ints["NeedLoadingAutosave"] = 0;
                ints["bld_boat"] = 0;
                ints["bld_jeweler"] = 2;
                ints["bld_arts"] = 0;
                HeroGear g = LoadFrom(texts, ints, en, 5);
                g.Random = new Random(5);
                Check(g.State == GearState.Ok && g.BagCount == 2 && Find(g, "Ring 2") != null && Find(g, "Ring 2").IsEmptySlot,
                      "по-английски: сумка 2 вещи, пустое «Ring 2» видно");
                Check(g.SlotLock("art2") != null && g.SlotLock("art2").Contains("Artifacts workshop"),
                      "закрытый слот — с английским названием постройки");
                Check(g.AddToBag(FirstGivable(en, GearKind.Head, false)) == null && g.LastTouched.InBag && g.BagCount == 3,
                      "по-английски вещь выдаётся в сумку и считается лежащей в ней");
                Dictionary<string, string> built = g.BuildTexts();
                Check(built.Count == 1 && built.ContainsKey(GearRules.BagKey(5)), "по-английски выдача меняет только сумку");
                Check(!HasCyrillic(g.DescribeChanges(GearRules.BagKey(5))), "подпись правки по-английски: " + g.DescribeChanges(GearRules.BagKey(5)));
            }
            finally
            {
                L.Ru = was;
            }
        }
    }
}
