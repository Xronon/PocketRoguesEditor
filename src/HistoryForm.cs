using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Окно прошлых правок: что и когда меняли, и кнопка «вернуть как было».
    /// Отмена идёт через Engine — с той же резервной копией и проверками, что запись.
    /// </summary>
    internal sealed class HistoryForm : Form
    {
        private readonly Engine _engine;
        private readonly ListView _list;
        private readonly Button _undo;
        private readonly Label _hint;
        private List<EditRecord> _records = new List<EditRecord>();

        /// <summary>Была ли в этом окне запись в игру — тогда главное окно перечитает данные.</summary>
        public bool ChangedGame;
        public string LastMessage = "";

        public HistoryForm(Engine engine)
        {
            _engine = engine;

            Text = L.T("Прошлые правки", "Past edits");
            Icon = AppIcon.Get();
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(820, 440);
            MinimumSize = new Size(560, 300);
            Padding = new Padding(10);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(L.T("Когда", "When"), 130);
            _list.Columns.Add(L.T("Что поменялось", "What changed"), 520);
            _list.Columns.Add(L.T("Состояние", "State"), 140);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.DoubleClick += delegate { ShowDetails(); };

            _hint = new Label();
            _hint.Dock = DockStyle.Bottom;
            _hint.AutoSize = false;
            _hint.Height = 40;
            _hint.ForeColor = SystemColors.GrayText;
            _hint.Text = L.T("Отмена ставит значения, которые были до выбранной правки. Если после неё вы "
                       + "играли, заработанное с тех пор по этим значениям пропадёт — программа покажет, что именно.",
                       "Undo puts back the values from before the chosen edit. If you played after it, "
                       + "whatever you earned in those values since then is lost — the program shows exactly what.");

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Padding = new Padding(0, 6, 0, 0);

            _undo = new Button();
            _undo.Text = L.T("Вернуть как было до этой правки", "Restore as before this edit");
            _undo.AutoSize = true;
            _undo.Click += OnUndo;
            buttons.Controls.Add(_undo);

            Button folder = new Button();
            folder.Text = L.T("Открыть папку копий", "Open backups folder");
            folder.AutoSize = true;
            folder.Click += OnOpenFolder;
            buttons.Controls.Add(folder);

            Button close = new Button();
            close.Text = L.T("Закрыть", "Close");
            close.AutoSize = true;
            close.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(close);
            CancelButton = close;

            Controls.Add(_list);
            Controls.Add(_hint);
            Controls.Add(buttons);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.ApplyToForm(this);
            _list.BackColor = Theme.Entry;
            _list.ForeColor = Theme.Fg;
            Fill();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        private void Fill()
        {
            _records = _engine.History();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (EditRecord r in _records)
            {
                ListViewItem item = new ListViewItem(r.When.ToString("dd.MM.yyyy HH:mm"));
                item.SubItems.Add((r.IsUndo ? L.T("Отмена: ", "Undo: ") : "") + r.Summary());
                item.SubItems.Add(r.Undone ? L.T("отменена", "undone") : "");
                item.Tag = r;
                if (r.Undone) item.ForeColor = Theme.Muted;
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            if (_list.Items.Count == 0)
            {
                ListViewItem empty = new ListViewItem("");
                empty.SubItems.Add(L.T("Правок пока не было.", "No edits yet."));
                empty.ForeColor = Theme.Muted;
                _list.Items.Add(empty);
            }
            UpdateButtons();
        }

        private EditRecord Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as EditRecord;
        }

        private void UpdateButtons()
        {
            EditRecord r = Selected();
            _undo.Enabled = r != null && !r.Undone;
        }

        private void ShowDetails()
        {
            EditRecord r = Selected();
            if (r == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine((r.IsUndo ? L.T("Отмена правки от ", "Undo from ") : L.T("Правка от ", "Edit from "))
                          + r.When.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine();
            sb.Append(Engine.DescribeLines(r.Changes, 25));
            if (r.Undone) sb.AppendLine().Append(L.T("Отменена: ", "Undone: ")).Append(r.UndoneNote);
            MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnUndo(object sender, EventArgs e)
        {
            EditRecord r = Selected();
            if (r == null || r.Undone) return;

            if (_engine.GameRunning())
            {
                MessageBox.Show(this, L.T("Игра запущена. Закройте её: при выходе она запишет свои значения "
                    + "поверх отмены.", "The game is running. Close it: on exit it writes its own values over the undo."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<Change> plan = Engine.PlanUndo(r, _engine.Load());
            if (plan.Count == 0)
            {
                MessageBox.Show(this, L.T("Возвращать нечего: значения и так как до этой правки.",
                                          "Nothing to restore: the values are already as before this edit."),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("Вернуть значения, которые были до правки от ", "Restore the values from before the edit of ")
                          + r.When.ToString("dd.MM.yyyy HH:mm") + "?");
            sb.AppendLine();
            int shown = plan.Count <= 20 ? plan.Count : 19;   // окно вопроса не прокручивается
            for (int i = 0; i < shown; i++)
            {
                Change c = plan[i];
                if (c.IsText)
                {
                    sb.Append("•  ").AppendLine(c.Title);
                    continue;
                }
                string after = c.Remove ? Change.Format(c.NewValue) + L.T(" (значения до правки не было)", " (there was no value before the edit)")
                                        : Change.Format(c.NewValue);
                sb.Append("•  ").Append(c.Title).Append(L.T(": сейчас ", ": now ")).Append(Change.Format(c.OldValue))
                  .Append(L.T(" → станет ", " → becomes ")).AppendLine(after);
            }
            if (shown < plan.Count) sb.Append(L.T("…и ещё ", "…and ")).Append(plan.Count - shown).AppendLine(L.T("", " more"));
            sb.AppendLine();
            sb.Append(L.T("Перед этим будет сделана резервная копия, и эту отмену тоже можно будет отменить.",
                          "A backup is made first, and this undo can be undone too."));

            if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Outcome o = _engine.Undo(r);
            if (o.Ok)
            {
                ChangedGame = true;
                LastMessage = o.Message;
                Fill();
                MessageBox.Show(this, o.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Fill();
                MessageBox.Show(this, o.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_engine.BackupDir);
                Process.Start("explorer.exe", "\"" + _engine.BackupDir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("Не смог открыть папку: ", "Could not open the folder: ") + ex.Message, Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
