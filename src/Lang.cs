using System;
using System.Globalization;
using System.IO;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Язык программы (с 1.8, перевод на английский). Выбирается один раз при запуске: окна
    /// собираются кодом, и сменить язык на ходу — значит перестроить их все; поэтому переключатель
    /// перезапускает программу. «Как в игре» — по языку, выбранному в самой игре (ключ «lang»:
    /// SettingsManager.UpdateCurrentLocalization — 0 английский, 1 русский, 2 украинский,
    /// 3 португальский, 4 немецкий); игра на этом компьютере не запускалась — по языку Windows.
    /// По-русски — только при русском, иначе по-английски.
    /// </summary>
    internal static class L
    {
        public const string Auto = "auto";
        public const string English = "en";
        public const string Russian = "ru";

        /// <summary>Писать ли по-русски. Ставит Program.Main до первого окна.</summary>
        public static bool Ru = true;

        /// <summary>Своя надпись: по-русски или по-английски.</summary>
        public static string T(string ru, string en)
        {
            return Ru ? ru : en;
        }

        /// <summary>Название из справочника: английского нет — русское, чем ничего.</summary>
        public static string Pick(string ru, string en)
        {
            return Ru || string.IsNullOrEmpty(en) ? ru : en;
        }

        /// <summary>
        /// Язык по настройке: «ru», «en» или «auto» (как в игре). gameLang — число «lang» из
        /// сохранения игры, null — игра его не записала.
        /// </summary>
        public static bool Decide(string setting, int? gameLang)
        {
            if (setting == Russian) return true;
            if (setting == English) return false;
            if (gameLang.HasValue) return gameLang.Value == 1;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        }

        /// <summary>Дробное число: «2,5» по-русски, «2.5» по-английски.</summary>
        public static string Num(float v, string format)
        {
            string s = v.ToString(format, CultureInfo.InvariantCulture);
            return Ru ? s.Replace('.', ',') : s;
        }

        /// <summary>
        /// Имя своего файла или папки рядом с программой. Если уже есть на одном из языков — оно:
        /// резервные копии и журнал не теряются, когда язык сменили. Нет ни одного — на языке программы.
        /// </summary>
        public static string FileName(string dir, string ru, string en)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, ru)) || Directory.Exists(Path.Combine(dir, ru))) return ru;
                if (File.Exists(Path.Combine(dir, en)) || Directory.Exists(Path.Combine(dir, en))) return en;
            }
            catch (Exception) { }
            return T(ru, en);
        }
    }
}
