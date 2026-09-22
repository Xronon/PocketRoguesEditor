using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>Точка входа. Ничего не решает сама — поднимает окно и ловит аварии.</summary>
    internal static class Program
    {
        public const string Version = "1.8";
        public const string Title = "Pocket Rogues Editor";

        /// <summary>Рядом с программой: она переносная, всё своё держит при себе.</summary>
        public static string DataDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        [STAThread]
        internal static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.ThreadException += OnThreadException;

            try
            {
                L.Ru = L.Decide(Settings.LoadLanguage(), GameLanguage());
                MainForm form = new MainForm();
                // /gear — сразу открыть «Снаряжение» (для ярлыка и для проверок снимком окна)
                foreach (string a in args)
                    if (string.Equals(a, "/gear", StringComparison.OrdinalIgnoreCase)) form.StartOnGear = true;
                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                Report(ex);
                return 1;
            }
        }

        /// <summary>Язык, выбранный в самой игре (число «lang»); null — игра его не записала.</summary>
        internal static int? GameLanguage()
        {
            try
            {
                IntValue v = new PrefsStore(GameData.RegistryPath).Read(GameData.LanguageKey);
                if (v.State == ValueState.Ok) return v.Value;
            }
            catch (Exception) { }
            return null;
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Report(e.Exception);
        }

        /// <summary>Показать аварию человеческим языком и успокоить насчёт сохранения.</summary>
        internal static void Report(Exception ex)
        {
            string details = ex == null ? L.T("неизвестная ошибка", "unknown error") : ex.ToString();
            string log = L.FileName(DataDir, "Журнал работы.txt", "Error log.txt");
            try
            {
                File.AppendAllText(Path.Combine(DataDir, log),
                    DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "  " + details + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("Программа наткнулась на непредвиденное.", "The program ran into something unexpected."));
            sb.AppendLine();
            sb.AppendLine(L.T("Сохранение игры программа меняет только по кнопке «Записать в игру»,",
                              "The program changes the game save only with the “Write to game” button,"));
            sb.AppendLine(L.T("и перед этим всегда делает резервную копию.", "and always makes a backup first."));
            sb.AppendLine();
            sb.AppendLine(L.T("Подробности — в файле «" + log + "» рядом с программой.",
                              "Details are in “" + log + "” next to the program."));
            sb.AppendLine();
            sb.AppendLine(ex == null ? "" : ex.Message);

            try
            {
                MessageBox.Show(sb.ToString(), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    /// <summary>Настройки окна — тема и язык. Файл settings.ini рядом с программой.</summary>
    internal static class Settings
    {
        private static string FilePath
        {
            get { return Path.Combine(Program.DataDir, "settings.ini"); }
        }

        /// <summary>Все строки «имя = значение»; файла нет или не читается — пусто.</summary>
        private static Dictionary<string, string> Load()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return d;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == ';') continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    d[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        private static void Save(string name, string value)
        {
            Dictionary<string, string> d = Load();
            d[name] = value;
            StringBuilder sb = new StringBuilder("; Pocket Rogues Editor\r\n");
            foreach (KeyValuePair<string, string> pair in d) sb.Append(pair.Key).Append(" = ").Append(pair.Value).Append("\r\n");
            try
            {
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }   // тема и язык — удобство; не запомнились, и ладно
        }

        public static bool LoadDark()
        {
            string v;
            return Load().TryGetValue("dark", out v) && v == "1";
        }

        public static void SaveDark(bool dark)
        {
            Save("dark", dark ? "1" : "0");
        }

        /// <summary>«auto» (как в игре), «en» или «ru».</summary>
        public static string LoadLanguage()
        {
            string v;
            if (Load().TryGetValue("language", out v) && (v == L.English || v == L.Russian)) return v;
            return L.Auto;
        }

        public static void SaveLanguage(string language)
        {
            Save("language", language);
        }
    }
}
