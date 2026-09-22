using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PocketRoguesEditor
{
    /// <summary>Каким оказалось значение в реестре.</summary>
    internal enum ValueState
    {
        /// <summary>Значения нет — игра считает его нулём.</summary>
        Missing,
        /// <summary>Обычное целое, 4 байта. Такое редактор правит.</summary>
        Ok,
        /// <summary>Что-то другое (дробное, строка). Не трогаем.</summary>
        Foreign,
    }

    /// <summary>Прочитанное значение: состояние и само число.</summary>
    internal struct IntValue
    {
        public ValueState State;
        public int Value;

        public static IntValue Missing()
        {
            IntValue v = new IntValue();
            v.State = ValueState.Missing;
            return v;
        }

        public static IntValue Of(int value)
        {
            IntValue v = new IntValue();
            v.State = ValueState.Ok;
            v.Value = value;
            return v;
        }

        public static IntValue Foreign()
        {
            IntValue v = new IntValue();
            v.State = ValueState.Foreign;
            return v;
        }

        public bool SameAs(IntValue other)
        {
            return State == other.State && (State != ValueState.Ok || Value == other.Value);
        }

        public override string ToString()
        {
            switch (State)
            {
                case ValueState.Ok: return Value.ToString();
                case ValueState.Missing: return L.T("нет", "none");
                default: return L.T("не число", "not a number");
            }
        }
    }

    /// <summary>Прочитанная строка игры (Unity пишет строки как REG_BINARY: UTF-8 и ноль в конце).</summary>
    internal struct TextValue
    {
        public ValueState State;
        public string Text;

        public static TextValue Missing()
        {
            TextValue v = new TextValue();
            v.State = ValueState.Missing;
            v.Text = "";
            return v;
        }

        public static TextValue Of(string text)
        {
            TextValue v = new TextValue();
            v.State = ValueState.Ok;
            v.Text = text;
            return v;
        }

        public static TextValue Foreign()
        {
            TextValue v = new TextValue();
            v.State = ValueState.Foreign;
            v.Text = "";
            return v;
        }

        public bool SameAs(TextValue other)
        {
            return State == other.State && (State != ValueState.Ok || string.Equals(Text, other.Text, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Единственное место, где программа трогает реестр. Работает с одним ключом —
    /// в жизни это ключ игры, в самопроверке временный.
    /// </summary>
    internal sealed class PrefsStore
    {
        public readonly string Path;   // относительно HKEY_CURRENT_USER

        public PrefsStore(string path)
        {
            Path = path;
        }

        public bool KeyExists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, false))
            {
                return key != null;
            }
        }

        /// <summary>
        /// Прочитать целое по ключу игры. Имя в реестре считается само
        /// (<see cref="GameData.RegistryName"/>).
        /// </summary>
        public IntValue Read(string gameKey)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, false))
            {
                if (key == null) return IntValue.Missing();
                return ReadFrom(key, GameData.RegistryName(gameKey));
            }
        }

        private static IntValue ReadFrom(RegistryKey key, string name)
        {
            object raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw == null) return IntValue.Missing();

            RegistryValueKind kind;
            try { kind = key.GetValueKind(name); }
            catch (IOException) { return IntValue.Missing(); }   // успели удалить

            // Unity пишет дробные числа как REG_DWORD длиной 8 байт — .NET отдаёт
            // их типом long. Настоящее целое игры — только 4-байтовый int.
            if (kind == RegistryValueKind.DWord && raw is int) return IntValue.Of((int)raw);
            return IntValue.Foreign();
        }

        /// <summary>
        /// Записать целое и сразу перечитать. Бросает исключение, если запись не
        /// удалась или прочиталось не то, что записали.
        /// </summary>
        public void Write(string gameKey, int value)
        {
            string name = GameData.RegistryName(gameKey);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, true))
            {
                if (key == null) throw new InvalidOperationException(L.T("Нет раздела реестра ", "No registry key ") + Path);

                IntValue before = ReadFrom(key, name);
                if (before.State == ValueState.Foreign)
                    throw new InvalidOperationException(L.T("Значение «" + gameKey + "» не целое число — не трогаю.",
                                                            "Value “" + gameKey + "” is not an integer — left untouched."));

                key.SetValue(name, value, RegistryValueKind.DWord);

                IntValue after = ReadFrom(key, name);
                if (after.State != ValueState.Ok || after.Value != value)
                    throw new InvalidOperationException(L.T("Записал «" + gameKey + "» = " + value + ", а прочиталось " + after + ".",
                                                            "Wrote “" + gameKey + "” = " + value + ", but read back " + after + "."));
            }
        }

        /// <summary>
        /// Убрать значение. Нужно только отмене правки, которая создала значение
        /// с нуля: вернуть «как было» — значит, чтобы его снова не было.
        /// </summary>
        public void Delete(string gameKey)
        {
            string name = GameData.RegistryName(gameKey);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, true))
            {
                if (key == null) return;

                IntValue before = ReadFrom(key, name);
                if (before.State == ValueState.Foreign)
                    throw new InvalidOperationException(L.T("Значение «" + gameKey + "» не целое число — не трогаю.",
                                                            "Value “" + gameKey + "” is not an integer — left untouched."));

                key.DeleteValue(name, false);
            }
        }

        // --- строки (снаряжение) -------------------------------------------

        public TextValue ReadText(string gameKey)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, false))
            {
                if (key == null) return TextValue.Missing();
                return ReadTextFrom(key, GameData.RegistryName(gameKey));
            }
        }

        private static TextValue ReadTextFrom(RegistryKey key, string name)
        {
            object raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw == null) return TextValue.Missing();

            RegistryValueKind kind;
            try { kind = key.GetValueKind(name); }
            catch (IOException) { return TextValue.Missing(); }

            byte[] bytes = raw as byte[];
            if (kind != RegistryValueKind.Binary || bytes == null) return TextValue.Foreign();

            int length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0) length--;   // ноль в конце — от Unity
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return TextValue.Of(strict.GetString(bytes, 0, length));
            }
            catch (ArgumentException)
            {
                return TextValue.Foreign();   // не UTF-8 — не наша строка
            }
        }

        /// <summary>Записать строку так, как её пишет Unity, и сразу перечитать.</summary>
        public void WriteText(string gameKey, string text)
        {
            string name = GameData.RegistryName(gameKey);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, true))
            {
                if (key == null) throw new InvalidOperationException(L.T("Нет раздела реестра ", "No registry key ") + Path);

                TextValue before = ReadTextFrom(key, name);
                if (before.State == ValueState.Foreign)
                    throw new InvalidOperationException(L.T("Значение «" + gameKey + "» не строка игры — не трогаю.",
                                                            "Value “" + gameKey + "” is not a game string — left untouched."));

                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                byte[] bytes = new byte[utf8.Length + 1];
                Buffer.BlockCopy(utf8, 0, bytes, 0, utf8.Length);
                key.SetValue(name, bytes, RegistryValueKind.Binary);

                TextValue after = ReadTextFrom(key, name);
                if (after.State != ValueState.Ok || !string.Equals(after.Text, text, StringComparison.Ordinal))
                    throw new InvalidOperationException(L.T("Записал «" + gameKey + "», а прочиталось другое.",
                                                            "Wrote “" + gameKey + "”, but read back something else."));
            }
        }

        public void DeleteText(string gameKey)
        {
            string name = GameData.RegistryName(gameKey);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Path, true))
            {
                if (key == null) return;
                TextValue before = ReadTextFrom(key, name);
                if (before.State == ValueState.Foreign)
                    throw new InvalidOperationException(L.T("Значение «" + gameKey + "» не строка игры — не трогаю.",
                                                            "Value “" + gameKey + "” is not a game string — left untouched."));
                key.DeleteValue(name, false);
            }
        }

        /// <summary>
        /// Полная копия раздела файлом .reg через штатную reg.exe. Такой файл
        /// возвращается двойным щелчком и без этой программы.
        /// </summary>
        public void Export(string file)
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string reg = System.IO.Path.Combine(system, "reg.exe");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = reg;
            psi.Arguments = "export \"HKCU\\" + Path + "\" \"" + file + "\" /y";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                // вывод читается до ожидания, иначе переполненный буфер повесит обе стороны
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(60000))
                {
                    try { p.Kill(); } catch { }
                    throw new InvalidOperationException(L.T("reg.exe не уложилась в минуту.", "reg.exe took longer than a minute."));
                }
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(L.T("reg.exe завершилась с кодом ", "reg.exe exited with code ") + p.ExitCode
                        + ": " + output.Trim());
            }

            FileInfo info = new FileInfo(file);
            if (!info.Exists || info.Length == 0)
                throw new InvalidOperationException(L.T("Резервная копия не появилась: ", "The backup did not appear: ") + file);
        }
    }
}
