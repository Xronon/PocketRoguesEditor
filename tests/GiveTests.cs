using System;
using System.Collections.Generic;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Самопроверка выдачи, замены и удаления вещей (21.09.2026). Записи героя — свои образцы в
    /// формате игры; настоящие данные не читаются.
    /// </summary>
    internal static partial class SelfTest
    {
        private static string EmptyItem() { return ItemJson("", new string[0], 0, 0, new string[0], 0); }

        /// <summary>Запись некроманта с пустыми местами: вторая рука, второе кольцо, артефакты.</summary>
        private static string EquipWithEmpty()
        {
            return "{\"position\":{\"x\":1.5,\"y\":2.5},"
                 + "\"head\":" + ItemJson("Items/Helmets/Head2.2rustHelmet",
                        new string[] { LessMp + "~2", AddHp + "~0" }, 2, 0, new string[0], 919884) + ","
                 + "\"body\":" + ItemJson("Items/Armor/Armor5.5CloakPlus", new string[0], 0, 0, new string[0], 818690) + ","
                 + "\"shield\":" + EmptyItem() + ","
                 + "\"shieldWeapon\":" + EmptyItem() + ","
                 + "\"weapon\":" + ItemJson("Items/Weapons/Wiz5Battle2", new string[0], 0, 0, new string[0], 574456) + ","
                 + "\"art1\":" + EmptyItem() + ",\"art2\":" + EmptyItem() + ",\"art3\":" + EmptyItem() + ","
                 + "\"ring1\":" + ItemJson("Items/Rings/Ring - Hollow",
                        new string[] { "Effects/RingEffects/Ring Effect - Add HP Reg Percent~0" }, 2, 0, new string[0], 640235) + ","
                 + "\"ring2\":" + EmptyItem() + ","
                 + "\"hp_cur\":99999,\"usableSlots\":[-1,-1]}";
        }

        private static ItemInfo FirstGivable(Catalog cat, GearKind kind, bool unique)
        {
            foreach (ItemInfo it in cat.Items)
                if (it.Givable && it.Kind == kind && it.Unique == unique && (kind != GearKind.Head || it.CanBeRare || unique))
                    return it;
            return null;
        }

        private static List<JsonNode> BagItems(string bagText)
        {
            return MiniJson.Parse(bagText).Get("Items").Items;
        }

        private static void GiveSuite(Catalog cat)
        {
            // --- справочник: что можно выдать ---
            ItemInfo gladius = cat.ItemByPath("items/weapons/tier2/meleesword10");
            Check(gladius != null && !gladius.SavesAsItself && !gladius.Givable,
                  "«Гладиус» не выдаётся: игра пишет его в сохранение как «Душегуба»");
            ItemInfo staff = cat.ItemByPath("Items/Weapons/Wiz5Battle2");
            Check(staff != null && staff.Givable && staff.SavePath == "Items/Weapons/Wiz5Battle2" && staff.Heroes.Count == 1,
                  "посох выдаётся, путь в сохранение — точный, у оружия есть класс");
            ItemInfo claw = cat.ItemByPath("items/trash/135/itemart_dmgmod2");
            Check(claw != null && claw.IsArtifact && claw.Givable, "артефакт выдаётся");
            ItemInfo cheeseInfo = cat.ItemByPath("items/usables/eatcheese");
            Check(cheeseInfo != null && cheeseInfo.IsUsable && cheeseInfo.Givable, "расходник выдаётся");
            int passives = 0;
            foreach (ItemInfo it in cat.Items)
                if (it.ResourcePath.StartsWith("items/passives/") && it.Givable) passives++;
            Check(passives == 0, "пассивные вещи не выдаются");
            ItemInfo helmA = FirstGivable(cat, GearKind.Head, false);
            ItemInfo ringInfo = FirstGivable(cat, GearKind.Ring, false);
            ItemInfo uniqueHelm = FirstGivable(cat, GearKind.Head, true);
            Check(helmA != null && helmA.CanBeRare && ringInfo != null && uniqueHelm != null, "есть шлем, кольцо и уникальный шлем для проверки");

            // --- ступени случайных эффектов: чем выше, тем реже ---
            Random r = new Random(11);
            int[] seen = new int[5];
            for (int i = 0; i < 3000; i++) seen[ItemFactory.RollTier(4, r)]++;
            Check(seen[0] > seen[1] && seen[1] > seen[2] && seen[2] > seen[3] && seen[0] > 1500,
                  "ступени как у игры: вес 0,3 в степени ступени (" + seen[0] + "/" + seen[1] + "/" + seen[2] + "/" + seen[3] + "/" + seen[4] + ")");

            // --- герой: пустые места и пределы по постройкам ---
            string equip = EquipWithEmpty();
            string bag = SampleBag(cat.ItemByPath("items/armor/armor4robe").Defaults[0].Path);
            Dictionary<string, string> texts = new Dictionary<string, string>();
            texts[GearRules.EquipKey(5)] = equip;
            texts[GearRules.BagKey(5)] = bag;
            Dictionary<string, int> ints = new Dictionary<string, int>();
            ints["curChar"] = 5;
            ints["NeedLoadingAutosave"] = 0;
            ints["bld_boat"] = 0;
            ints["bld_jeweler"] = 2;
            ints["bld_arts"] = 0;
            HeroGear g = LoadFrom(texts, ints, cat, 5);
            g.Random = new Random(5);
            Check(g.State == GearState.Ok && g.BagLimit == 15 && g.BagCount == 2, "сумка: 2 из 15 (Склады не построены)");
            GearItem emptyRing = Find(g, "Кольцо 2");
            GearItem emptyShield = Find(g, "Вторая рука");
            GearItem art2 = Find(g, "Артефакт 2");
            Check(emptyRing != null && emptyRing.IsEmptySlot && g.SlotLock("ring2") == null, "пустое второе кольцо видно и открыто");
            Check(art2 != null && art2.IsEmptySlot && g.SlotLock("art2") != null, "второй артефакт закрыт без Мастерской артефактов");
            Check(Find(g, "Оружие во 2-й руке") == null, "второе оружие у некроманта не показано");
            Check(!g.Changed && g.BuildTexts().Count == 0, "пустые места сами по себе ничего не меняют");

            // --- новая вещь в сумку ---
            Check(g.AddToBag(helmA) == null, "шлем выдан в сумку");
            GearItem fresh = g.LastTouched;
            Check(fresh.IsNew && fresh.Quality == 4 && fresh.Effects.Count == fresh.Capacity && fresh.Path == helmA.SavePath,
                  "новый шлем легендарный, эффектов ровно норма (" + fresh.Effects.Count + " из " + fresh.Capacity + ")");
            bool fromPool = true;
            foreach (GearEffect e in fresh.Effects)
                if (!e.IsDefault && (!helmA.Pool.Contains(e.Info) || e.Tier < 0 || e.Tier > e.Info.MaxTier)) fromPool = false;
            Check(fromPool, "эффекты нового шлема — из его пула, ступени в пределах");
            Check(fresh.NewId >= 100000 && fresh.NewId <= 999999, "номер новой вещи — шестизначный, как у игры");
            Check(g.Changed && g.BagCount == 3, "в сумке 3");

            Dictionary<string, string> built = g.BuildTexts();
            Check(built.Count == 1 && built.ContainsKey(GearRules.BagKey(5)), "выдача меняет только сумку");
            List<JsonNode> items = BagItems(built[GearRules.BagKey(5)]);
            JsonNode last = items[items.Count - 1];
            Check(items.Count == 3 && last.GetString("path") == helmA.SavePath && last.GetInt("quality", -1) == 4
                  && last.GetInt("uniqueID", 0) == fresh.NewId && last.GetStrings("effects").Count == fresh.Effects.Count,
                  "новая вещь записана в конец сумки, в формате игры");
            Check(built[GearRules.BagKey(5)].StartsWith(bag.Substring(0, bag.LastIndexOf(']'))),
                  "прежние вещи сумки — байт в байт");

            // новую можно править до записи, как любую
            GearEffect some = null;
            foreach (GearEffect e in fresh.Effects) if (!e.IsDefault) { some = e; break; }
            Check(some != null && fresh.SetTier(some, some.Info.MaxTier) == null, "ступень эффекта новой вещи правится");
            Check(BagItems(g.BuildTexts()[GearRules.BagKey(5)])[2].GetStrings("effects").Contains(some.Info.Path + "~" + some.Info.MaxTier),
                  "правка новой вещи — в её записи");

            // --- удалить из сумки ---
            GearItem cheese = null;
            foreach (GearItem it in g.Items) if (it.Path == "Items/Usables/EatCheese") cheese = it;
            Check(cheese != null && g.Delete(cheese) == null && g.BagCount == 2, "сыр удалён из сумки");
            items = BagItems(g.BuildTexts()[GearRules.BagKey(5)]);
            bool noCheese = true;
            foreach (JsonNode n in items) if (n.GetString("path") == "Items/Usables/EatCheese") noCheese = false;
            Check(items.Count == 2 && noCheese, "в записи сумки сыра нет");
            Check(g.Delete(Find(g, "Голова")) != null, "надетую вещь удалить нельзя — только заменить");

            // --- замена надетой: старая — в сумку со своими правками ---
            GearItem head = Find(g, "Голова");
            Check(head.SetQuality(3) == null, "у старого шлема поднято качество до замены");
            ItemInfo helmB = null;
            foreach (ItemInfo it in cat.Items)
                if (it.Givable && it.Kind == GearKind.Head && !it.Unique && it != helmA && it != head.Info) { helmB = it; break; }
            Check(g.Replace(head, helmB) == null, "шлем заменён");
            built = g.BuildTexts();
            JsonNode eq = MiniJson.Parse(built[GearRules.EquipKey(5)]);
            Check(eq.Get("head").GetString("path") == helmB.SavePath && eq.Get("head").GetInt("quality", -1) == 4,
                  "на голове новый легендарный шлем");
            Check(eq.Get("ring1").GetString("path") == "Items/Rings/Ring - Hollow" && eq.Get("hp_cur") != null,
                  "остальная запись героя цела");
            items = BagItems(built[GearRules.BagKey(5)]);
            JsonNode moved = items[items.Count - 1];
            Check(moved.GetString("path") == "Items/Helmets/Head2.2rustHelmet" && moved.GetInt("quality", -1) == 3
                  && moved.GetInt("uniqueID", 0) == 919884,
                  "старый шлем в сумке — тот же, с поднятым качеством");
            Check(g.DescribeChanges(GearRules.EquipKey(5)).Contains("надето новое")
                  && g.DescribeChanges(GearRules.BagKey(5)).Contains("снята в сумку"),
                  "в вопросе перед записью видно, что надето и что ушло в сумку");

            // --- пустое место: надеть кольцо ---
            Check(g.Replace(emptyRing, helmA) != null, "шлем на место кольца не надевается");
            Check(g.Replace(emptyRing, ringInfo) == null, "кольцо надето на пустое место");
            GearItem ring2 = g.LastTouched;
            Check(ring2.IsRing && ring2.Quality == 4 && ring2.RingPoints <= ring2.RingBudget && ring2.RingPoints >= ring2.RingBudget - 1,
                  "новое кольцо легендарное, очки заняты по правилам (" + ring2.RingPoints + " из " + ring2.RingBudget + ")");
            Check(MiniJson.Parse(g.BuildTexts()[GearRules.EquipKey(5)]).Get("ring2").GetString("path") == ringInfo.SavePath,
                  "кольцо записано во второй слот");
            Check(g.Replace(art2, claw) != null, "в закрытый слот артефакта не надевается");
            Check(g.Replace(Find(g, "Артефакт 1"), claw) == null, "артефакт надет в открытый слот");
            Check(g.Replace(emptyShield, staff) != null, "посох во вторую руку не надевается");
            Check(g.AddToBag(gladius) != null, "вещь, которую сохранение не удержит, не выдаётся");

            // --- уникальная: вторую не даём ---
            Check(g.AddToBag(uniqueHelm) == null, "уникальный шлем выдан");
            Check(g.AddToBag(uniqueHelm) != null, "второй такой же уникальный — нет");

            // --- сумка полна ---
            while (g.BagCount < g.BagLimit) g.AddToBag(cheeseInfo);
            Check(g.AddToBag(cheeseInfo) == "Инвентарь заполнен.", "в полную сумку — «Инвентарь заполнен.»");
            Check(g.Replace(Find(g, "Тело"), FirstGivable(cat, GearKind.Body, false)) == "Инвентарь заполнен.",
                  "заменить надетую при полной сумке нельзя: снятую некуда положить");
            GearItem anyBag = null;
            foreach (GearItem it in g.Items) if (it.InBag && !it.Deleted) { anyBag = it; break; }
            Check(g.Replace(anyBag, helmA) == null, "заменить вещь в сумке при полной сумке можно — место то же");
            built = g.BuildTexts();
            Check(BagItems(built[GearRules.BagKey(5)]).Count == g.BagLimit, "в записи сумки ровно " + g.BagLimit + " вещей");

            // --- пустая сумка: запись создаётся в формате игры ---
            texts[GearRules.BagKey(5)] = "";
            HeroGear e0 = LoadFrom(texts, ints, cat, 5);
            e0.Random = new Random(3);
            Check(e0.BagCount == 0 && e0.AddToBag(staff) == null, "в пустую сумку выдано");
            string made = e0.BuildTexts()[GearRules.BagKey(5)];
            Check(made.StartsWith("{\"Items\":[{\"path\":\"Items/Weapons/Wiz5Battle2\"") && BagItems(made).Count == 1,
                  "пустая сумка записана как у игры: {\"Items\":[…]}");
        }
    }
}
