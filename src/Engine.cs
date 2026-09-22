using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PocketRoguesEditor
{
    /// <summary>Что было в реестре по всем полям редактора на один момент.</summary>
    internal sealed class Snapshot
    {
        public readonly Dictionary<string, IntValue> Values = new Dictionary<string, IntValue>();
        public readonly Dictionary<string, TextValue> Texts = new Dictionary<string, TextValue>();

        public IntValue Get(string key)
        {
            IntValue v;
            return Values.TryGetValue(key, out v) ? v : IntValue.Missing();
        }

        public TextValue GetText(string key)
        {
            TextValue v;
            return Texts.TryGetValue(key, out v) ? v : TextValue.Missing();
        }
    }

    /// <summary>
    /// Одно изменённое значение: как было и как станет. Число (поля окна) или строка
    /// (запись снаряжения героя) — у строки вместо «было → стало» подпись Title.
    /// </summary>
    internal sealed class Change
    {
        public string Key;
        public string Title;
        public bool Existed;     // было ли значение в реестре до правки
        public int OldValue;     // если не было — игра видела ноль
        public int NewValue;
        public bool Remove;      // отмена: значение нужно убрать, а не записать

        public bool IsText;      // строка снаряжения
        public string OldText = "";
        public string NewText = "";

        public string Describe()
        {
            if (IsText) return Remove ? Title + L.T(" (запись убрана, как до правки)", " (record removed, as before the edit)") : Title;
            string after = Remove ? Format(NewValue) + L.T(" (значение убрано, как до правки)", " (value removed, as before the edit)")
                                  : Format(NewValue);
            return Title + ": " + Format(OldValue) + " → " + after;
        }

        /// <summary>Разряды через неразрывный пробел: «12 561» не рвётся переносом строки.</summary>
        public static string Format(int value)
        {
            return value.ToString("#,0", Digits);
        }

        private static readonly NumberFormatInfo Digits = MakeDigits();

        private static NumberFormatInfo MakeDigits()
        {
            NumberFormatInfo nf = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nf.NumberGroupSeparator = ((char)0xA0).ToString();   // неразрывный пробел
            nf.NegativeSign = "-";
            return nf;
        }
    }

    /// <summary>Правка, записанная на диск, — из неё строится отмена.</summary>
    internal sealed class EditRecord
    {
        public string File;
        public DateTime When;
        public bool Undone;
        public string UndoneNote = "";
        public bool IsUndo;               // сама была отменой другой правки
        public List<Change> Changes = new List<Change>();

        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Change c in Changes)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(c.Describe());
            }
            return sb.ToString();
        }
    }

    /// <summary>Итог записи для окна: получилось или нет и почему.</summary>
    internal sealed class Outcome
    {
        public bool Ok;
        public bool Stale;      // пока окно было открыто, данные изменились
        public string Message = "";
        public List<Change> Applied = new List<Change>();

        public static Outcome Fail(string message)
        {
            Outcome o = new Outcome();
            o.Message = message;
            return o;
        }
    }

    /// <summary>
    /// Движок редактора: чтение, запись с резервной копией, отмена прошлых правок.
    /// Окно ничего не пишет само — только через этот класс.
    /// </summary>
    internal sealed class Engine
    {
        public delegate bool GameCheck();

        /// <summary>Сколько полных копий .reg держать. Файлы отмены не удаляются: они крошечные.</summary>
        public const int KeepRegBackups = 30;

        private readonly PrefsStore _store;
        private readonly string _dataDir;
        private readonly GameCheck _gameRunning;

        public Engine(PrefsStore store, string dataDir, GameCheck gameRunning)
        {
            _store = store;
            _dataDir = dataDir;
            _gameRunning = gameRunning;
        }

        // имена — на языке программы, но уже существующие не меняются: копии и журнал не теряются при смене языка
        public string BackupDir { get { return Path.Combine(_dataDir, L.FileName(_dataDir, "Резервные копии", "Backups")); } }
        public string LogFile { get { return Path.Combine(_dataDir, LogName); } }
        public string LogName { get { return L.FileName(_dataDir, "Журнал правок.txt", "Edit log.txt"); } }

        /// <summary>Запущена ли игра. Настоящая проверка — по имени процесса.</summary>
        public static bool IsGameProcessRunning()
        {
            Process[] list = Process.GetProcessesByName(GameData.ProcessName);
            bool running = list.Length > 0;
            foreach (Process p in list) p.Dispose();
            return running;
        }

        public bool GameRunning() { return _gameRunning(); }

        /// <summary>
        /// Есть ли вообще данные игры. Золото игра пишет с первой же минуты,
        /// поэтому «нет золота» значит «игра на этом ПК не сохранялась».
        /// </summary>
        public bool HasGameData()
        {
            return _store.KeyExists() && _store.Read(GameData.GoldKey).State == ValueState.Ok;
        }

        /// <summary>Прочитать одно значение, не относящееся к полям редактора (гильдия, выбранный герой).</summary>
        public IntValue Read(string key)
        {
            return _store.Read(key);
        }

        public TextValue ReadText(string key)
        {
            return _store.ReadText(key);
        }

        /// <summary>Все поля окна и все записи снаряжения героев на один момент.</summary>
        public Snapshot Load()
        {
            Snapshot s = new Snapshot();
            foreach (Field f in GameData.AllFields()) s.Values[f.Key] = _store.Read(f.Key);
            for (int i = 0; i < GameData.Heroes.Length; i++)
            {
                s.Texts[GearRules.EquipKey(i)] = _store.ReadText(GearRules.EquipKey(i));
                s.Texts[GearRules.BagKey(i)] = _store.ReadText(GearRules.BagKey(i));
            }
            s.Texts[GearRules.FloorEquipKey] = _store.ReadText(GearRules.FloorEquipKey);
            s.Texts[GearRules.FloorBagKey] = _store.ReadText(GearRules.FloorBagKey);
            s.Texts[GearRules.StaticEquipKey] = _store.ReadText(GearRules.StaticEquipKey);
            s.Texts[GearRules.StaticBagKey] = _store.ReadText(GearRules.StaticBagKey);
            return s;
        }

        /// <summary>
        /// Записать правку снаряжения: новые тексты записей героя. Проверки, копия, отмена
        /// и журнал — те же, что у чисел.
        /// </summary>
        public Outcome SaveGear(Snapshot baseline, Dictionary<string, string> texts, Dictionary<string, string> titles)
        {
            List<Change> changes = new List<Change>();
            foreach (KeyValuePair<string, string> pair in texts)
            {
                if (!GearRules.IsGearKey(pair.Key)) return Outcome.Fail(L.T("Чужая запись: ", "Not a gear record: ") + pair.Key);
                TextValue now = baseline.GetText(pair.Key);
                if (now.State == ValueState.Foreign)
                    return Outcome.Fail(L.T("Запись снаряжения в игре не строка — не трогаю.",
                                            "The gear record in the game is not a string — it is left untouched."));
                if (now.State == ValueState.Ok && now.Text == pair.Value) continue;

                Change c = new Change();
                c.Key = pair.Key;
                c.IsText = true;
                c.Existed = now.State == ValueState.Ok;
                c.OldText = now.Text;
                c.NewText = pair.Value;
                string title;
                c.Title = titles != null && titles.TryGetValue(pair.Key, out title) ? title : pair.Key;
                changes.Add(c);
            }
            if (changes.Count == 0) return Outcome.Fail(L.T("Менять нечего: снаряжение такое же, как в игре.",
                                                            "Nothing to change: the gear is the same as in the game."));

            Outcome guard = CheckBeforeWrite(baseline);
            if (guard != null) return guard;

            return Apply(changes, false);
        }

        /// <summary>
        /// Что изменится, если записать желаемые значения. Значения, которые
        /// совпадают с нынешними, и «не числа» в список не попадают.
        /// </summary>
        public static List<Change> Diff(Snapshot baseline, Dictionary<string, int> desired)
        {
            List<Change> list = new List<Change>();
            foreach (Field f in GameData.AllFields())
            {
                int wanted;
                if (!desired.TryGetValue(f.Key, out wanted)) continue;

                IntValue now = baseline.Get(f.Key);
                if (now.State == ValueState.Foreign) continue;
                // сравниваем с тем, что видит игра: «нет» для неё ноль, а у стартового
                // навыка всё ниже 1 — единица; такое переписывать незачем
                if (wanted == f.GameValue(now)) continue;

                Change c = new Change();
                c.Key = f.Key;
                c.Title = f.Title;
                c.Existed = now.State == ValueState.Ok;
                // было: точное число из реестра, чтобы отмена вернула его байт в байт;
                // если значения не было — то, что видела игра (для подписи «было → стало»)
                c.OldValue = c.Existed ? now.Value : f.GameValue(now);
                c.NewValue = wanted;
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// Проверить желаемые значения по пределам. Пределы мягкие: если в игре уже
        /// стоит больше предела, его можно оставить, но не поднять ещё выше.
        /// </summary>
        public static string Validate(Snapshot baseline, Dictionary<string, int> desired)
        {
            foreach (Field f in GameData.AllFields())
            {
                int wanted;
                if (!desired.TryGetValue(f.Key, out wanted)) continue;

                int current = f.GameValue(baseline.Get(f.Key));
                int max = Math.Max(f.Max, current);
                int min = Math.Min(f.Min, current);
                if (wanted > max || wanted < min)
                    return f.Title + L.T(": допустимо от ", ": allowed from ") + Change.Format(min)
                         + L.T(" до ", " to ") + Change.Format(max) + ".";
            }
            return null;
        }

        /// <summary>Записать правку: проверки, резервная копия, запись, сверка, журнал.</summary>
        public Outcome Save(Snapshot baseline, Dictionary<string, int> desired)
        {
            string invalid = Validate(baseline, desired);
            if (invalid != null) return Outcome.Fail(invalid);

            List<Change> changes = Diff(baseline, desired);
            if (changes.Count == 0) return Outcome.Fail(L.T("Менять нечего: все значения такие же, как в игре.",
                                                            "Nothing to change: all values are the same as in the game."));

            Outcome guard = CheckBeforeWrite(baseline);
            if (guard != null) return guard;

            return Apply(changes, false);
        }

        /// <summary>
        /// Вернуть значения, которые были до прошлой правки. Отмена — тоже правка:
        /// с резервной копией и своей записью, так что и её можно отменить.
        /// </summary>
        public Outcome Undo(EditRecord record)
        {
            if (record.Undone) return Outcome.Fail(L.T("Эта правка уже отменена.", "This edit is already undone."));

            Snapshot now = Load();
            Outcome guard = CheckBeforeWrite(now);
            if (guard != null) return guard;

            List<Change> back = PlanUndo(record, now);
            if (back.Count == 0)
            {
                MarkUndone(record, L.T("значения уже были как до правки", "the values were already as before the edit"));
                return Outcome.Fail(L.T("Возвращать нечего: значения и так как до этой правки.",
                                        "Nothing to restore: the values are already as before this edit."));
            }

            Outcome result = Apply(back, true);
            if (result.Ok) MarkUndone(record, L.T("отменена ", "undone ") + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            return result;
        }

        /// <summary>Что сделает отмена: для каждого значения — от нынешнего к тому, что было.</summary>
        public static List<Change> PlanUndo(EditRecord record, Snapshot now)
        {
            List<Change> back = new List<Change>();
            foreach (Change c in record.Changes)
            {
                if (c.IsText)
                {
                    // снаряжение возвращается целой записью героя, как было до правки
                    TextValue cur = now.GetText(c.Key);
                    if (cur.State == ValueState.Foreign) continue;
                    Change t = new Change();
                    t.Key = c.Key;
                    t.IsText = true;
                    t.Existed = cur.State == ValueState.Ok;
                    t.OldText = cur.Text;
                    if (c.Existed)
                    {
                        if (cur.State == ValueState.Ok && cur.Text == c.OldText) continue;
                        t.NewText = c.OldText;
                    }
                    else
                    {
                        if (cur.State == ValueState.Missing) continue;
                        t.Remove = true;
                    }
                    t.Title = TitleOfGearKey(c.Key) + L.T(" — вернётся как было до правки", " — back to how it was before the edit");
                    back.Add(t);
                    continue;
                }

                IntValue current = now.Get(c.Key);
                if (current.State == ValueState.Foreign) continue;
                Field f = GameData.FieldOf(c.Key);   // ReadRecord пропускает только известные ключи

                Change b = new Change();
                b.Key = c.Key;
                b.Title = c.Title;
                b.Existed = current.State == ValueState.Ok;
                b.OldValue = b.Existed ? current.Value : f.GameValue(current);
                if (c.Existed)
                {
                    if (current.State == ValueState.Ok && current.Value == c.OldValue) continue;
                    b.NewValue = c.OldValue;
                }
                else
                {
                    if (current.State == ValueState.Missing) continue;
                    b.NewValue = f.GameValue(IntValue.Missing());   // что увидит игра: 0, у стартового навыка 1
                    b.Remove = true;    // до правки значения не было — убрать его
                }
                back.Add(b);
            }
            return back;
        }

        /// <summary>Общие проверки перед любой записью. null — можно писать.</summary>
        private Outcome CheckBeforeWrite(Snapshot baseline)
        {
            if (_gameRunning())
                return Outcome.Fail(L.T("Игра запущена. Закройте её: при выходе она запишет свои "
                    + "значения поверх правки.",
                    "The game is running. Close it: on exit it writes its own values over the edit."));

            if (!HasGameData())
                return Outcome.Fail(L.T("Не нашёл данных Pocket Rogues в реестре.", "No Pocket Rogues data found in the registry."));

            // Пока окно было открыто, могли поиграть: тогда в окне старые числа,
            // и запись затёрла бы свежий прогресс. Такое не пишем.
            Snapshot fresh = Load();
            bool stale = false;
            foreach (KeyValuePair<string, IntValue> pair in baseline.Values)
                if (!fresh.Get(pair.Key).SameAs(pair.Value)) stale = true;
            foreach (KeyValuePair<string, TextValue> pair in baseline.Texts)
                if (!fresh.GetText(pair.Key).SameAs(pair.Value)) stale = true;
            if (stale)
            {
                Outcome o = Outcome.Fail(L.T("Пока окно было открыто, игра изменила свои данные. "
                    + "Правка не записана. Окно показывает свежие значения — повторите правку.",
                    "The game changed its data while the window was open. The edit was not written. "
                    + "The window now shows fresh values — repeat the edit."));
                o.Stale = true;
                return o;
            }
            return null;
        }

        /// <summary>«Некромант: снаряжение» / «Некромант: сумка» — по ключу записи.</summary>
        public static string TitleOfGearKey(string key)
        {
            if (key == GearRules.FloorEquipKey) return L.T("Герой в подземелье: снаряжение", "Hero in the dungeon: gear");
            if (key == GearRules.FloorBagKey) return L.T("Герой в подземелье: сумка", "Hero in the dungeon: bag");
            if (key == GearRules.StaticEquipKey) return L.T("Герой в особой локации: снаряжение", "Hero in a special location: gear");
            if (key == GearRules.StaticBagKey) return L.T("Герой в особой локации: сумка", "Hero in a special location: bag");
            for (int i = 0; i < GameData.Heroes.Length; i++)
            {
                if (key == GearRules.EquipKey(i)) return GameData.Heroes[i].Title + L.T(": снаряжение", ": gear");
                if (key == GearRules.BagKey(i)) return GameData.Heroes[i].Title + L.T(": сумка", ": bag");
            }
            return key;
        }

        /// <summary>
        /// Сама запись. Сначала резервные копии — без них ничего не пишем. Если
        /// запись сорвалась посередине, уже записанное возвращается назад.
        /// </summary>
        internal Outcome Apply(List<Change> changes, bool isUndo)
        {
            DateTime now = DateTime.Now;
            string stamp = now.ToString("yyyy-MM-dd HH-mm-ss");

            string undoFile;
            try
            {
                Directory.CreateDirectory(BackupDir);
                string regFile = UniquePath(Path.Combine(BackupDir, stamp + ".reg"));
                _store.Export(regFile);
                undoFile = UniquePath(Path.Combine(BackupDir, stamp + ".undo"));
                WriteRecord(undoFile, now, changes, isUndo);
            }
            catch (Exception ex)
            {
                return Outcome.Fail(L.T("Не смог сделать резервную копию, поэтому ничего не менял.",
                                        "Could not make a backup, so nothing was changed.") + "\n\n" + ex.Message);
            }

            List<Change> done = new List<Change>();
            try
            {
                foreach (Change c in changes)
                {
                    if (c.IsText)
                    {
                        if (c.Remove) _store.DeleteText(c.Key);
                        else _store.WriteText(c.Key, c.NewText);
                    }
                    else if (c.Remove) _store.Delete(c.Key);
                    else _store.Write(c.Key, c.NewValue);
                    done.Add(c);
                }
            }
            catch (Exception ex)
            {
                string rollback = RollBack(done);
                TryDelete(undoFile);   // правки не случилось — и отменять нечего
                Log(now, isUndo ? L.T("ОТМЕНА НЕ УДАЛАСЬ", "UNDO FAILED") : L.T("ПРАВКА НЕ УДАЛАСЬ", "EDIT FAILED"),
                    changes, ex.Message + " " + rollback);
                return Outcome.Fail(L.T("Запись не удалась: ", "Writing failed: ") + ex.Message + "\n\n" + rollback);
            }

            Log(now, isUndo ? L.T("Отмена правки", "Undo") : L.T("Правка", "Edit"), changes, null);
            TrimBackups();

            Outcome ok = new Outcome();
            ok.Ok = true;
            ok.Applied = changes;
            // строка состояния в окне одна: длинный список уводим в журнал
            string what = changes.Count <= 3
                ? DescribeAll(changes)
                : L.T("значений " + changes.Count + " (подробно — в «" + LogName + "»)",
                      changes.Count + " values (details in “" + LogName + "”)");
            ok.Message = (isUndo ? L.T("Вернул как было: ", "Restored: ") : L.T("Записал: ", "Written: ")) + what;
            return ok;
        }

        private string RollBack(List<Change> done)
        {
            if (done.Count == 0) return L.T("Ничего не успело измениться.", "Nothing had changed yet.");
            try
            {
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    Change c = done[i];
                    if (c.IsText)
                    {
                        if (c.Existed) _store.WriteText(c.Key, c.OldText);
                        else _store.DeleteText(c.Key);
                    }
                    else if (c.Existed) _store.Write(c.Key, c.OldValue);
                    else _store.Delete(c.Key);
                }
                return L.T("Уже записанное вернул назад.", "What was already written has been rolled back.");
            }
            catch (Exception ex)
            {
                string folder = Path.GetFileName(BackupDir);
                return L.T("Вернуть назад не вышло (" + ex.Message + "). Полная копия до правки — в папке "
                    + "«" + folder + "», её можно вернуть двойным щелчком по файлу .reg.",
                    "Rolling back failed (" + ex.Message + "). A full copy from before the edit is in the “"
                    + folder + "” folder; double-click its .reg file to restore it.");
            }
        }

        /// <summary>
        /// Список правок по строке, не длиннее max строк: окно вопроса Windows не умеет
        /// прокручиваться и при полусотне навыков вылезло бы за экран.
        /// </summary>
        public static string DescribeLines(List<Change> changes, int max)
        {
            StringBuilder sb = new StringBuilder();
            int shown = changes.Count <= max ? changes.Count : max - 1;
            for (int i = 0; i < shown; i++) sb.Append("•  ").AppendLine(changes[i].Describe());
            if (shown < changes.Count)
                sb.Append(L.T("…и ещё ", "…and ")).Append(changes.Count - shown)
                  .AppendLine(L.T(" (полный список попадёт в «Журнал правок.txt»)", " more (the full list goes to the edit log)"));
            return sb.ToString();
        }

        public static string DescribeAll(List<Change> changes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Change c in changes)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(c.Describe());
            }
            return sb.ToString();
        }

        // --- файлы правок ---------------------------------------------------

        // Формат .undo — обычный текст, по строке на значение:
        //   ключ <TAB> было ли значение (1/0) <TAB> было <TAB> стало <TAB> убрать (1/0) <TAB> подпись
        // У строк снаряжения «было» и «стало» — это «=» и base64 от UTF-8 (JSON длинный и может
        // содержать что угодно). Подпись — одной строкой, без табуляций.
        // Служебные строки начинаются с «#».

        private static void WriteRecord(string file, DateTime when, List<Change> changes, bool isUndo)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Pocket Rogues Editor " + Program.Version);
            sb.AppendLine("#when\t" + when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            if (isUndo) sb.AppendLine("#kind\tundo");
            foreach (Change c in changes)
            {
                string oldField = c.IsText ? "=" + ToBase64(c.OldText) : c.OldValue.ToString(CultureInfo.InvariantCulture);
                string newField = c.IsText ? "=" + ToBase64(c.NewText) : c.NewValue.ToString(CultureInfo.InvariantCulture);
                sb.Append(c.Key).Append('\t')
                  .Append(c.Existed ? "1" : "0").Append('\t')
                  .Append(oldField).Append('\t')
                  .Append(newField).Append('\t')
                  .Append(c.Remove ? "1" : "0").Append('\t')
                  .Append(OneLine(c.Title)).AppendLine();
            }
            WriteAllTextSafely(file, sb.ToString());
        }

        private static string ToBase64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
        }

        private static string FromBase64(string field)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(field.Substring(1)));
        }

        private static string OneLine(string s)
        {
            return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>Разобрать файл правки. null — файл битый, в списке его не будет.</summary>
        public static EditRecord ReadRecord(string file)
        {
            try
            {
                EditRecord r = new EditRecord();
                r.File = file;
                bool hasWhen = false;
                foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('\t');

                    if (line.StartsWith("#"))
                    {
                        if (parts[0] == "#when" && parts.Length > 1)
                        {
                            r.When = DateTime.ParseExact(parts[1], "yyyy-MM-dd HH:mm:ss",
                                                         CultureInfo.InvariantCulture);
                            hasWhen = true;
                        }
                        else if (parts[0] == "#kind" && parts.Length > 1 && parts[1] == "undo") r.IsUndo = true;
                        else if (parts[0] == "#undone")
                        {
                            r.Undone = true;
                            r.UndoneNote = parts.Length > 1 ? parts[1] : "";
                        }
                        continue;
                    }

                    if (parts.Length < 6) return null;
                    Change c = new Change();
                    c.Key = parts[0];
                    c.Existed = parts[1] == "1";
                    c.IsText = parts[2].StartsWith("=");
                    if (c.IsText)
                    {
                        if (!parts[3].StartsWith("=")) return null;
                        c.OldText = FromBase64(parts[2]);
                        c.NewText = FromBase64(parts[3]);
                        if (!GearRules.IsGearKey(c.Key)) return null;   // строки — только записи снаряжения
                    }
                    else
                    {
                        c.OldValue = int.Parse(parts[2], CultureInfo.InvariantCulture);
                        c.NewValue = int.Parse(parts[3], CultureInfo.InvariantCulture);
                        if (!IsKnownKey(c.Key)) return null;   // чужие ключи из файла не пишем
                    }
                    c.Remove = parts[4] == "1";
                    c.Title = parts[5];
                    r.Changes.Add(c);
                }
                if (!hasWhen || r.Changes.Count == 0) return null;
                return r;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsKnownKey(string key)
        {
            foreach (Field f in GameData.AllFields()) if (f.Key == key) return true;
            return false;
        }

        /// <summary>Все прошлые правки, новые сверху.</summary>
        public List<EditRecord> History()
        {
            List<EditRecord> list = new List<EditRecord>();
            if (!Directory.Exists(BackupDir)) return list;

            foreach (string f in Directory.GetFiles(BackupDir, "*.undo"))
            {
                EditRecord r = ReadRecord(f);
                if (r != null) list.Add(r);
            }
            list.Sort(delegate(EditRecord a, EditRecord b) { return b.When.CompareTo(a.When); });
            return list;
        }

        private static void MarkUndone(EditRecord record, string note)
        {
            try
            {
                File.AppendAllText(record.File, "#undone\t" + note + Environment.NewLine, Encoding.UTF8);
                record.Undone = true;
                record.UndoneNote = note;
            }
            catch { }   // отметка — удобство, а не условие: отмена уже записана
        }

        // --- журнал и уборка ------------------------------------------------

        private void Log(DateTime when, string what, List<Change> changes, string error)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(when.ToString("dd.MM.yyyy HH:mm:ss")).Append("  ").AppendLine(what);
                foreach (Change c in changes) sb.Append("    ").AppendLine(c.Describe());
                if (error != null) sb.Append(L.T("    Причина: ", "    Reason: ")).AppendLine(error);
                File.AppendAllText(LogFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }   // журнал не должен ронять запись, которая уже прошла
        }

        private void TrimBackups()
        {
            try
            {
                string[] files = Directory.GetFiles(BackupDir, "*.reg");
                if (files.Length <= KeepRegBackups) return;
                Array.Sort(files, StringComparer.Ordinal);   // имя начинается с даты — сортировка по времени
                for (int i = 0; i < files.Length - KeepRegBackups; i++) TryDelete(files[i]);
            }
            catch { }
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
        }

        /// <summary>Запись через временный файл: оборванная запись не оставит полфайла.</summary>
        private static void WriteAllTextSafely(string file, string text)
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(true));
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }

        private static void TryDelete(string file)
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); }
            catch { }
        }
    }
}
