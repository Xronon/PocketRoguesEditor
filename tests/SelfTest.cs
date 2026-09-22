using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Самопроверка. Гоняет движок на ВРЕМЕННОМ разделе реестра и во временной
    /// папке — настоящие данные игры не читает и не трогает. В конце всё за собой
    /// удаляет и проверяет, что удалило.
    /// </summary>
    internal static partial class SelfTest
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegSetValueExW(SafeRegistryHandle key, string name, int reserved,
                                                 int type, byte[] data, int size);

        private const int REG_DWORD = 4;

        private static int _passed;
        private static int _failed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("  ПРОВАЛ: " + what);
        }

        private static bool FakeRunning;
        private static bool GameCheck() { return FakeRunning; }

        internal static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            HashAndFields();

            string regPath = @"Software\PocketRoguesEditor-SelfTest\" + Guid.NewGuid().ToString("N");
            string dir = Path.Combine(Path.GetTempPath(), "PocketRoguesEditor-SelfTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                EngineCycle(regPath, dir);
                RecordParsing(dir);
                GearSuite(regPath + "-gear");
                LanguageSuite();
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  АВАРИЯ: " + ex);
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\PocketRoguesEditor-SelfTest", false); }
                catch { }
                try { Directory.Delete(dir, true); }
                catch { }
            }

            using (RegistryKey left = Registry.CurrentUser.OpenSubKey(@"Software\PocketRoguesEditor-SelfTest"))
                Check(left == null, "временный раздел реестра удалён");
            Check(!Directory.Exists(dir), "временная папка удалена");

            Console.WriteLine();
            Console.WriteLine("Пройдено: " + _passed + ", провалено: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        // --- чистые функции ---------------------------------------------------

        private static void HashAndFields()
        {
            // пары из настоящего реестра игры от 18.09.2026
            Check(GameData.UnityHash("money") == 174583445u, "подпись money");
            Check(GameData.UnityHash("gems") == 2087624729u, "подпись gems");
            Check(GameData.UnityHash("moneyDefault") == 291048830u, "подпись moneyDefault");
            Check(GameData.UnityHash("curPoints2") == 795947596u, "подпись curPoints2");
            Check(GameData.UnityHash("WIZARD_endurance") == 4284724744u, "подпись WIZARD_endurance");
            Check(GameData.UnityHash("LvlUp") == 218989302u, "подпись LvlUp");
            Check(GameData.UnityHash("guildLvl") == 729970144u, "подпись guildLvl");
            Check(GameData.RegistryName("money") == "money_h174583445", "имя значения в реестре");

            List<Field> fields = GameData.AllFields();
            Check(fields.Count == 1 + 6 * 5 + 54, "полей 85: золото, по пять на героя и 54 навыка");
            HashSet<string> keys = new HashSet<string>();
            foreach (Field f in fields) keys.Add(f.Key);
            Check(keys.Count == fields.Count, "ключи полей не повторяются");
            Check(!keys.Contains("gems"), "кристаллов среди полей нет");
            Check(keys.Contains("NECROM_intelegence"), "атрибут с опечаткой игры");
            Check(GameData.AttributeOf(GameData.Heroes[2], GameData.Attributes[0]).Max == 50, "предел атрибута 50");

            SkillTable(keys);
        }

        /// <summary>Таблица навыков и правило «стартовый не ниже 1».</summary>
        private static void SkillTable(HashSet<string> keys)
        {
            // пары из настоящего реестра игры от 18.09.2026
            Check(GameData.UnityHash("skill_wizBlackFire") == 4051855152u, "подпись skill_wizBlackFire");
            Check(GameData.UnityHash("bld_wzrd") == 133774923u, "подпись bld_wzrd");
            Check(GameData.UnityHash("bld_war") == 2476912948u, "подпись bld_war");

            Check(GameData.Skills.Length == 54, "навыков 54");
            HashSet<string> skillKeys = new HashSet<string>();
            bool shapeOk = true, namesOk = true;
            foreach (Hero h in GameData.Heroes)
            {
                for (int slot = 0; slot < 3; slot++)
                {
                    int count = 0, defaults = 0;
                    foreach (Skill sk in GameData.Skills)
                    {
                        if (sk.HeroIndex != h.Index || sk.Slot != slot) continue;
                        count++;
                        if (sk.IsDefault) defaults++;
                    }
                    if (count != 3 || defaults != 1) shapeOk = false;
                }
            }
            foreach (Skill sk in GameData.Skills)
            {
                skillKeys.Add(sk.Key);
                if (!sk.Key.StartsWith("skill_") || sk.Title.Length == 0) namesOk = false;
            }
            Check(shapeOk, "у каждого героя по три навыка на кнопку, из них один стартовый");
            Check(skillKeys.Count == 54 && namesOk, "ключи навыков уникальны, у всех есть название");
            Check(!keys.Contains("skill_wizPoison") && !keys.Contains("skill_spcArchRoll")
                  && !keys.Contains("skill_strAtckSniper"), "навыков из старых версий игры нет");

            Field def = GameData.FieldOf("skill_wizBlackFire");
            Field plain = GameData.FieldOf("skill_wizIceThorn");
            Check(def != null && def.AtLeastOne && def.Min == 1 && def.Max == 25, "стартовый навык: от 1 до 25");
            Check(plain != null && !plain.AtLeastOne && plain.Min == 0 && plain.Max == 25, "обычный навык: от 0 до 25");
            Check(def.GameValue(IntValue.Missing()) == 1, "стартовый без значения — 1");
            Check(def.GameValue(IntValue.Of(0)) == 1 && def.GameValue(IntValue.Of(-3)) == 1, "стартовый с нулём — 1");
            Check(def.GameValue(IntValue.Of(7)) == 7, "стартовый с 7 — 7");
            Check(plain.GameValue(IntValue.Missing()) == 0, "обычный без значения — 0");
            Check(GameData.FieldOf("WIZARD_intelegence").Title.Contains("разум"), "характеристика называется «разум», как в игре");
        }

        // --- полный цикл на временном разделе ------------------------------------

        private static void EngineCycle(string regPath, string dir)
        {
            PrefsStore store = new PrefsStore(regPath);
            Engine engine = new Engine(store, dir, new Engine.GameCheck(GameCheck));

            Check(!engine.HasGameData(), "нет раздела — нет данных");

            // раздел, похожий на игровой
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(regPath))
            {
                k.SetValue(GameData.RegistryName("money"), 12561, RegistryValueKind.DWord);
                k.SetValue(GameData.RegistryName("gems"), 165, RegistryValueKind.DWord);
                k.SetValue(GameData.RegistryName("curPoints2"), 1988, RegistryValueKind.DWord);
                k.SetValue(GameData.RegistryName("WIZARD_endurance"), 50, RegistryValueKind.DWord);
                k.SetValue(GameData.RegistryName("WIZARD_strength"), 60, RegistryValueKind.DWord);
                // так Unity пишет дробные: REG_DWORD длиной 8 байт
                byte[] dbl = BitConverter.GetBytes(3.5d);
                Check(RegSetValueExW(k.Handle, GameData.RegistryName("curPoints3"), 0, REG_DWORD, dbl, 8) == 0,
                      "удалось записать 8-байтовый DWORD для проверки");
                // так Unity пишет строки: REG_BINARY с нулём в конце
                k.SetValue(GameData.RegistryName("WARRIOR_strength"), Encoding.UTF8.GetBytes("abc\0"),
                           RegistryValueKind.Binary);
            }

            Check(engine.HasGameData(), "данные найдены");

            Snapshot s = engine.Load();
            Check(s.Get("money").State == ValueState.Ok && s.Get("money").Value == 12561, "золото прочитано");
            Check(s.Get("WARRIOR_endurance").State == ValueState.Missing, "отсутствующее значение — Missing");
            Check(s.Get("curPoints3").State == ValueState.Foreign, "8-байтовый DWORD — не целое");
            Check(s.Get("WARRIOR_strength").State == ValueState.Foreign, "строка — не целое");

            bool threw = false;
            try { store.Write("curPoints3", 5); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw, "запись поверх не-целого запрещена");
            Check(engine.Load().Get("curPoints3").State == ValueState.Foreign, "не-целое осталось как было");

            // --- разница и пределы ---
            Dictionary<string, int> want = Current(s);
            want["money"] = 50000;
            want["WARRIOR_endurance"] = 50;
            want["WARRIOR_agility"] = 0;       // было нет, стало 0 — для игры ничего не меняется
            want["curPoints3"] = 7;            // не целое — пропускается
            List<Change> diff = Engine.Diff(s, want);
            Check(diff.Count == 2, "в разнице ровно два значения");
            Check(Engine.Validate(s, want) == null, "допустимые значения проходят");

            Dictionary<string, int> bad = Current(s);
            bad["WARRIOR_endurance"] = 51;
            Check(Engine.Validate(s, bad) != null, "атрибут 51 не проходит");
            bad = Current(s);
            bad["WIZARD_strength"] = 60;
            Check(Engine.Validate(s, bad) == null, "уже стоящие 60 можно оставить");
            bad["WIZARD_strength"] = 61;
            Check(Engine.Validate(s, bad) != null, "выше уже стоящего нельзя");
            bad = Current(s);
            bad["money"] = -1;
            Check(Engine.Validate(s, bad) != null, "отрицательное золото не проходит");

            // --- игра запущена: ничего не пишется и копий нет ---
            FakeRunning = true;
            Outcome o = engine.Save(s, want);
            FakeRunning = false;
            Check(!o.Ok, "при запущенной игре запись отклонена");
            Check(engine.Load().Get("money").Value == 12561, "при запущенной игре золото не тронуто");
            Check(!Directory.Exists(engine.BackupDir) || Directory.GetFiles(engine.BackupDir).Length == 0,
                  "при отказе резервных копий нет");

            // --- устаревшее окно: игра успела поменять данные ---
            store.Write("curPoints2", 1990);
            o = engine.Save(s, want);
            Check(!o.Ok && o.Stale, "устаревшие числа в окне распознаны");
            Check(engine.Load().Get("money").Value == 12561, "при устаревшем окне золото не тронуто");
            store.Write("curPoints2", 1988);

            // --- обычная запись ---
            for (int i = 0; i < 35; i++)   // старые копии: после записи должно остаться 30
            {
                Directory.CreateDirectory(engine.BackupDir);
                File.WriteAllText(Path.Combine(engine.BackupDir, "2000-01-01 00-00-" + i.ToString("00") + ".reg"), "x");
            }
            o = engine.Save(s, want);
            Check(o.Ok, "запись прошла: " + o.Message);
            Snapshot after = engine.Load();
            Check(after.Get("money").Value == 50000, "золото записано");
            Check(after.Get("WARRIOR_endurance").State == ValueState.Ok && after.Get("WARRIOR_endurance").Value == 50,
                  "отсутствовавшее значение создано");
            Check(after.Get("WARRIOR_agility").State == ValueState.Missing, "ноль вместо «нет» не записан");
            Check(after.Get("curPoints3").State == ValueState.Foreign, "не-целое не тронуто");
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(regPath))
                Check((int)k.GetValue(GameData.RegistryName("gems")) == 165, "кристаллы не тронуты");

            string[] regs = Directory.GetFiles(engine.BackupDir, "*.reg");
            Check(regs.Length == Engine.KeepRegBackups, "копий .reg осталось 30");
            string newest = NewestReg(regs);
            string regText = File.ReadAllText(newest, Encoding.Unicode);
            Check(regText.Contains("PocketRoguesEditor-SelfTest") && regText.Contains("money_h174583445"),
                  "копия .reg содержит раздел и значения");
            Check(regText.Contains("\"money_h174583445\"=dword:00003111"), "в копии золото до правки");
            Check(File.Exists(engine.LogFile) && File.ReadAllText(engine.LogFile).Contains("12" + (char)0xA0 + "561"),
                  "журнал правок записан");

            List<EditRecord> history = engine.History();
            Check(history.Count == 1 && history[0].Changes.Count == 2 && !history[0].IsUndo, "в истории одна правка");

            // --- повторная запись без изменений ---
            o = engine.Save(engine.Load(), Current(engine.Load()));
            Check(!o.Ok && engine.History().Count == 1, "без изменений ничего не пишется");

            // --- отмена ---
            store.Write("money", 51000);   // «поиграли» после правки
            EditRecord first = engine.History()[0];
            List<Change> plan = Engine.PlanUndo(first, engine.Load());
            Check(plan.Count == 2, "отмена трогает оба значения");
            o = engine.Undo(first);
            Check(o.Ok, "отмена прошла: " + o.Message);
            after = engine.Load();
            Check(after.Get("money").Value == 12561, "золото вернулось к исходному");
            Check(after.Get("WARRIOR_endurance").State == ValueState.Missing, "созданное значение убрано");

            history = engine.History();
            Check(history.Count == 2, "в истории правка и её отмена");
            EditRecord undoRec = null, editRec = null;
            foreach (EditRecord r in history) { if (r.IsUndo) undoRec = r; else editRec = r; }
            Check(editRec != null && editRec.Undone, "правка помечена отменённой");
            Check(undoRec != null && !undoRec.Undone, "отмена записана отдельной правкой");
            Check(!engine.Undo(editRec).Ok, "дважды одну правку не отменить");

            // --- отмена отмены ---
            o = engine.Undo(undoRec);
            Check(o.Ok, "отмену можно отменить: " + o.Message);
            after = engine.Load();
            Check(after.Get("money").Value == 51000, "золото снова как перед отменой");
            Check(after.Get("WARRIOR_endurance").Value == 50, "созданное значение вернулось");

            // --- сбой посередине: уже записанное возвращается ---
            List<Change> broken = new List<Change>();
            Change good = new Change();
            good.Key = "money"; good.Title = "Золото"; good.Existed = true; good.OldValue = 51000; good.NewValue = 777;
            broken.Add(good);
            Change foreign = new Change();
            foreign.Key = "curPoints3"; foreign.Title = "Лучник: свободные очки"; foreign.Existed = true;
            foreign.OldValue = 0; foreign.NewValue = 5;
            broken.Add(foreign);
            int undoFilesBefore = Directory.GetFiles(engine.BackupDir, "*.undo").Length;
            o = engine.Apply(broken, false);
            Check(!o.Ok, "сбой посередине распознан");
            Check(engine.Load().Get("money").Value == 51000, "после сбоя золото вернулось назад");
            Check(Directory.GetFiles(engine.BackupDir, "*.undo").Length == undoFilesBefore,
                  "после сбоя лишней правки в истории нет");
            Check(File.ReadAllText(engine.LogFile).Contains("НЕ УДАЛАСЬ"), "сбой записан в журнал");

            SkillCycle(store, engine);
        }

        /// <summary>Запись и отмена навыков: стартовые, обычные, стартовый с нулём в реестре.</summary>
        private static void SkillCycle(PrefsStore store, Engine engine)
        {
            store.Write("skill_wizFireball", 0);        // стартовый с нулём: игра показывает 1
            Snapshot s = engine.Load();
            Check(s.Get("skill_wizBlackFire").State == ValueState.Missing, "стартового навыка в реестре нет");

            Dictionary<string, int> want = Current(s);
            Check(want["skill_wizBlackFire"] == 1 && want["skill_wizFireball"] == 1 && want["skill_wizIceThorn"] == 0,
                  "окно показывает то, что видит игра: 1, 1, 0");
            Check(Engine.Diff(s, want).Count == 0, "показанное как есть — не правка");

            Dictionary<string, int> bad = Current(s);
            bad["skill_wizBlackFire"] = 0;
            Check(Engine.Validate(s, bad) != null, "стартовый навык ниже 1 не проходит");
            bad = Current(s);
            bad["skill_wizIceThorn"] = 26;
            Check(Engine.Validate(s, bad) != null, "навык 26 не проходит");

            want["skill_wizBlackFire"] = 25;
            want["skill_wizIceThorn"] = 25;
            want["skill_wizFireball"] = 25;
            want["skill_wizShield"] = 25;
            Outcome o = engine.Save(s, want);
            Check(o.Ok, "навыки записаны: " + o.Message);
            Check(o.Message.Contains("значений 4"), "длинная правка в строке состояния — числом");
            Snapshot after = engine.Load();
            Check(after.Get("skill_wizBlackFire").Value == 25 && after.Get("skill_wizIceThorn").Value == 25
                  && after.Get("skill_wizFireball").Value == 25, "навыки стали 25");

            EditRecord rec = engine.History()[0];
            Change fire = null;
            foreach (Change c in rec.Changes) if (c.Key == "skill_wizFireball") fire = c;
            Check(fire != null && fire.Existed && fire.OldValue == 0, "в файле отмены — точное старое число 0");

            o = engine.Undo(rec);
            Check(o.Ok, "отмена навыков: " + o.Message);
            after = engine.Load();
            Check(after.Get("skill_wizBlackFire").State == ValueState.Missing, "созданный стартовый навык убран");
            Check(after.Get("skill_wizIceThorn").State == ValueState.Missing, "созданный обычный навык убран");
            Check(after.Get("skill_wizFireball").State == ValueState.Ok && after.Get("skill_wizFireball").Value == 0,
                  "навык с нулём вернулся к нулю байт в байт");

            List<Change> many = new List<Change>();
            for (int i = 0; i < 30; i++)
            {
                Change c = new Change();
                c.Key = "money"; c.Title = "строка " + i; c.OldValue = i; c.NewValue = i + 1;
                many.Add(c);
            }
            string lines = Engine.DescribeLines(many, 20);
            Check(lines.Split('\n').Length == 21 && lines.Contains("ещё 11"), "длинный список урезан до 20 строк");
            Check(Engine.DescribeLines(many.GetRange(0, 5), 20).Split('\n').Length == 6, "короткий список целиком");
        }

        private static void RecordParsing(string dir)
        {
            string f1 = Path.Combine(dir, "garbage.undo");
            File.WriteAllText(f1, "это не файл правки\n");
            Check(Engine.ReadRecord(f1) == null, "мусорный файл отклонён");

            string f2 = Path.Combine(dir, "foreign.undo");
            File.WriteAllText(f2, "#when\t2026-09-18 11:00:00\ngems\t1\t165\t9999\t0\tКристаллы\n");
            Check(Engine.ReadRecord(f2) == null, "файл с чужим ключом отклонён");

            string f3 = Path.Combine(dir, "ok.undo");
            File.WriteAllText(f3, "# Pocket Rogues Editor 1.0\n#when\t2026-09-18 11:00:00\n"
                                + "money\t1\t10\t20\t0\tЗолото\n#undone\tотменена\n", new UTF8Encoding(true));
            EditRecord r = Engine.ReadRecord(f3);
            Check(r != null && r.Changes.Count == 1 && r.Undone && r.Changes[0].NewValue == 20, "правильный файл прочитан");
        }

        private static Dictionary<string, int> Current(Snapshot s)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            foreach (Field f in GameData.AllFields())
            {
                IntValue v = s.Get(f.Key);
                if (v.State != ValueState.Foreign) d[f.Key] = f.GameValue(v);
            }
            return d;
        }

        private static string NewestReg(string[] files)
        {
            string best = files[0];
            foreach (string f in files)
                if (string.CompareOrdinal(Path.GetFileName(f), Path.GetFileName(best)) > 0) best = f;
            return best;
        }
    }
}
