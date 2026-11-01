using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Management;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FortisRdgControl {
  public sealed class MainForm : Form {
    private readonly string root;
    private readonly string psExe;
    private readonly string[] keys = { "repo2", "repo1", "updater", "watcher" };
    private readonly string[] names = { "Fortis Repo 2", "Fortis Repo 1", "RDG Updater", "CI Watcher" };
    private readonly string[] descriptions = {
      "Защищённое подключение Fortis Repo 2",
      "Отдельное подключение Fortis Repo 1",
      "Windows watcher готовых сборок и обновление RDG",
      "Docker watcher исходников, проверок и CI-сборок"
    };
    private readonly Dictionary<string, Process> owned = new Dictionary<string, Process>();
    private readonly Dictionary<string, Process> oneShots = new Dictionary<string, Process>();
    private List<string> previousWatcherLines = new List<string>();
    private readonly Dictionary<string, Panel> cards = new Dictionary<string, Panel>();
    private readonly Dictionary<string, RichTextBox> terminals = new Dictionary<string, RichTextBox>();
    private readonly Dictionary<string, TextBox> inputFields = new Dictionary<string, TextBox>();
    private readonly Dictionary<string, Panel> consoleBlocks = new Dictionary<string, Panel>();
    private readonly Dictionary<string, Label> statuses = new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> hints = new Dictionary<string, Label>();
    private readonly Dictionary<string, Button> startButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, Button> stopButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, Button> expandButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();
    private readonly Dictionary<string, bool> external = new Dictionary<string, bool>();
    private readonly Dictionary<string, string> lastStates = new Dictionary<string, string>();
    private readonly Dictionary<string, StringBuilder> buffers = new Dictionary<string, StringBuilder>();
    private FlowLayoutPanel list;
    private Label footer;
    private NotifyIcon tray;
    private System.Windows.Forms.Timer timer;
    private Mutex updaterMutex;
    private bool updaterMutexOwned = false;
    private volatile bool checking = false;
    private bool exiting = false;
    private bool dockerOnline = false;
    private bool fortisOnline = false;
    private bool watcherOnline = false;
    private readonly Color bg = Color.FromArgb(12, 17, 27);
    private readonly Color card = Color.FromArgb(24, 33, 49);
    private readonly Color stroke = Color.FromArgb(52, 68, 91);
    private readonly Color fg = Color.FromArgb(236, 242, 252);
    private readonly Color muted = Color.FromArgb(151, 169, 192);
    private readonly Color blue = Color.FromArgb(52, 105, 213);
    private readonly Color green = Color.FromArgb(87, 220, 159);
    private readonly Color red = Color.FromArgb(241, 117, 130);

    public MainForm(string folder) {
      root = folder;
      psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                           @"WindowsPowerShell\v1.0\powershell.exe");
      Text = "Fortis / RDG Control Center";
      Size = new Size(1180, 900);
      MinimumSize = new Size(790, 560);
      StartPosition = FormStartPosition.CenterScreen;
      BackColor = bg;
      ForeColor = fg;
      Font = new Font("Segoe UI", 10f);
      BuildUi();
      timer = new System.Windows.Forms.Timer();
      timer.Interval = 6500;
      timer.Tick += delegate { PollStatusAsync(); };
      timer.Start();
      Shown += delegate { Append("repo2", "Приложение запущено. Repo 1 и Repo 2 запускаются отдельными командами. Общие настройки по-прежнему проверяются штатными Guard."); PollStatusAsync(); };
      FormClosing += OnFormClosing;
    }

    private Button MakeButton(string text, Color color) {
      Button b = new Button();
      b.Text = text;
      b.FlatStyle = FlatStyle.Flat;
      b.FlatAppearance.BorderColor = stroke;
      b.BackColor = color;
      b.ForeColor = fg;
      b.Size = new Size(152, 34);
      b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
      b.Margin = new Padding(3);
      b.Cursor = Cursors.Hand;
      return b;
    }

    private void BuildUi() {
      Panel header = new Panel();
      header.Dock = DockStyle.Top;
      header.Height = 102;
      header.Padding = new Padding(22, 12, 22, 10);
      header.BackColor = Color.FromArgb(18, 29, 47);
      Label title = new Label();
      title.Text = "Fortis / RDG Control Center";
      title.Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
      title.Location = new Point(22, 12); title.AutoSize = true; title.ForeColor = fg;
      Label sub = new Label();
      sub.Text = "Четыре штатных подключения + пользовательские скрипты по пути";
      sub.Location = new Point(24, 48); sub.AutoSize = true; sub.ForeColor = muted;
      Button refresh = MakeButton("↻ Обновить статус", Color.FromArgb(35, 50, 72));
      refresh.Anchor = AnchorStyles.Right | AnchorStyles.Top;
      refresh.Location = new Point(978, 19);
      refresh.Click += delegate { PollStatusAsync(); };
      Button scripts = MakeButton("Скрипты…", Color.FromArgb(37, 69, 109));
      scripts.Width = 145;
      scripts.Location = new Point(820, 19);
      scripts.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      scripts.Click += delegate { using(var dialog = new ScriptCatalogForm(root)) dialog.ShowDialog(this); };
      header.Controls.Add(scripts);
      header.Resize += delegate { refresh.Left = header.ClientSize.Width - refresh.Width - 22; scripts.Left = header.ClientSize.Width - scripts.Width - 22; };
      Label notice = new Label();
      notice.Text = "Repo 1 и Repo 2 имеют независимые кнопки запуска. Общие Docker/Guard-настройки проверяются отдельно. Закрытие окна сворачивает приложение в трей при активных процессах.";
      notice.Location = new Point(24, 75);
      notice.AutoEllipsis = true; notice.Width = 1050; notice.Height = 19;
      notice.ForeColor = Color.FromArgb(174, 197, 235);
      header.Resize += delegate { notice.Width = Math.Max(300, header.ClientSize.Width - 48); };
      header.Controls.Add(title); header.Controls.Add(sub); header.Controls.Add(refresh); header.Controls.Add(notice);
      Controls.Add(header);

      footer = new Label();
      footer.Dock = DockStyle.Bottom;
      footer.Height = 35;
      footer.Padding = new Padding(18, 8, 0, 0);
      footer.BackColor = Color.FromArgb(21, 32, 50);
      footer.ForeColor = muted;
      footer.Text = "Проверка Docker…   •   Логи процессов хранятся в памяти приложения; CI watcher пишет в свой штатный watch.log.";
      Controls.Add(footer);

      list = new FlowLayoutPanel();
      list.Dock = DockStyle.Fill;
      list.FlowDirection = FlowDirection.TopDown;
      list.WrapContents = false;
      list.AutoScroll = true;
      list.Padding = new Padding(15, 14, 15, 15);
      list.BackColor = bg;
      Controls.Add(list);
      list.BringToFront();
      header.BringToFront();
      footer.BringToFront();
      foreach (string k in keys) {
        expanded[k] = false; external[k] = false; lastStates[k] = "Остановлен";
        buffers[k] = new StringBuilder();
        BuildCard(k);
      }
      list.SizeChanged += delegate { ResizeCards(); };
      ResizeCards();

      tray = new NotifyIcon();
      tray.Text = "Fortis / RDG Control Center";
      tray.Icon = SystemIcons.Application;
      tray.Visible = true;
      tray.DoubleClick += delegate { ShowFromTray(); };
      ContextMenuStrip menu = new ContextMenuStrip();
      menu.Items.Add("Открыть", null, delegate { ShowFromTray(); });
      menu.Items.Add("Выход (без активных Windows-процессов)", null, delegate { TryQuit(); });
      tray.ContextMenuStrip = menu;
    }

    private void BuildCard(string id) {
      int i = Array.IndexOf(keys, id);
      Panel outer = new Panel();
      outer.Height = 140; outer.Width = 1050;
      outer.Margin = new Padding(2, 3, 8, 10);
      outer.BackColor = card;
      outer.BorderStyle = BorderStyle.FixedSingle;
      Label n = new Label();
      n.Text = (i+1).ToString(); n.TextAlign = ContentAlignment.MiddleCenter;
      n.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
      n.BackColor = Color.FromArgb(39, 73, 119);
      n.Location = new Point(14, 13); n.Size = new Size(39, 39);
      Label title = new Label();
      title.Text = names[i]; title.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
      title.ForeColor = fg; title.Location = new Point(68, 12); title.AutoSize = true;
      Label description = new Label();
      description.Text = descriptions[i]; description.ForeColor = muted;
      description.Location = new Point(68, 39); description.AutoSize = true;
      Label status = new Label();
      status.Text = "○ Остановлен"; status.ForeColor = muted;
      status.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      status.TextAlign = ContentAlignment.MiddleRight;
      status.Size = new Size(260, 30);
      status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      status.Location = new Point(770, 16);
      statuses[id] = status;
      outer.Controls.Add(n); outer.Controls.Add(title); outer.Controls.Add(description); outer.Controls.Add(status);

      Panel screen = new Panel();
      screen.BackColor = Color.FromArgb(9, 14, 24);
      screen.BorderStyle = BorderStyle.FixedSingle;
      screen.Location = new Point(15, 69);
      screen.Size = new Size(1012, 251);
      screen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      screen.Visible = false;
      Label screenTitle = new Label();
      screenTitle.Text = "КОНСОЛЬ / ЖУРНАЛ · " + names[i];
      screenTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
      screenTitle.ForeColor = Color.FromArgb(142, 181, 239);
      screenTitle.Location = new Point(10, 7); screenTitle.AutoSize = true;
      RichTextBox term = new RichTextBox();
      term.Location = new Point(7, 29);
      term.Size = new Size(996, 178);
      term.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      term.ReadOnly = true;
      term.BorderStyle = BorderStyle.None;
      term.BackColor = Color.FromArgb(9, 14, 24);
      term.ForeColor = Color.FromArgb(189, 210, 229);
      term.Font = new Font("Consolas", 9f);
      term.WordWrap = false;
      term.ScrollBars = RichTextBoxScrollBars.Both;
      term.ShortcutsEnabled = true;
      TextBox input = new TextBox();
      input.Location = new Point(8, 214); input.Size = new Size(870, 27);
      input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      input.BackColor = Color.FromArgb(29, 42, 59); input.ForeColor = fg;
      input.BorderStyle = BorderStyle.FixedSingle;
      input.Font = new Font("Consolas", 9f);
      input.Visible = id != "watcher";
      Button send = MakeButton("Отправить ↵", Color.FromArgb(37, 69, 109));
      send.Size = new Size(111, 27); send.Location = new Point(885, 213);
      send.Anchor = AnchorStyles.Right | AnchorStyles.Top;
      send.Visible = id != "watcher";
      input.KeyDown += delegate(object sender, KeyEventArgs e) {
        if(e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendInput(id); }
      };
      send.Click += delegate { SendInput(id); };
      screen.Controls.Add(term); screen.Controls.Add(screenTitle);
      screen.Controls.Add(input); screen.Controls.Add(send);
      consoleBlocks[id] = screen; terminals[id] = term; inputFields[id] = input;
      outer.Controls.Add(screen);

      Panel controls = new Panel();
      controls.Height = 42; controls.Location = new Point(12, 71);
      controls.Width = 1018; controls.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
      Button start = MakeButton("▶ Запустить", blue);
      start.Location = new Point(0, 4); start.Width = 145;
      start.Click += delegate { StartService(id); };
      Button stop = MakeButton("■ Остановить", Color.FromArgb(103, 45, 59));
      stop.Location = new Point(154, 4); stop.Width = 145;
      stop.Click += delegate { StopService(id); };
      Button toggle = MakeButton("▾ Развернуть консоль", Color.FromArgb(38, 56, 82));
      toggle.Location = new Point(308, 4); toggle.Width = 201;
      toggle.Click += delegate { ToggleConsole(id); };
      Button clear = MakeButton("Очистить вывод", Color.FromArgb(32, 45, 63));
      clear.Location = new Point(518, 4); clear.Width = 142;
      clear.Click += delegate { ClearLog(id); };
      startButtons[id] = start; stopButtons[id] = stop; expandButtons[id] = toggle;
      controls.Controls.Add(start); controls.Controls.Add(stop); controls.Controls.Add(toggle); controls.Controls.Add(clear);
      outer.Controls.Add(controls);

      Label hint = new Label();
      hint.Text = "Логи появятся при запуске. Просмотр консоли не влияет на процесс.";
      hint.Location = new Point(17, 117); hint.Height = 18; hint.Width = 1010;
      hint.ForeColor = muted; hint.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
      hints[id] = hint;
      outer.Controls.Add(hint);
      cards[id] = outer;
      list.Controls.Add(outer);
      LayoutCard(id);
    }

    private void ResizeCards() {
      if (list == null) return;
      int w = Math.Max(690, list.ClientSize.Width - 40);
      foreach (string k in keys) {
        Panel c = cards[k]; c.Width = w;
        statuses[k].Left = w - statuses[k].Width - 24;
        consoleBlocks[k].Width = w - 33;
        hints[k].Width = w - 36;
      }
    }

    private void LayoutCard(string id) {
      Panel c = cards[id];
      bool open = expanded[id];
      consoleBlocks[id].Visible = open;
      Panel controls = null;
      foreach (Control co in c.Controls) if (co is Panel && co != consoleBlocks[id] && co.Controls.Count > 0) {
        if (co.Controls.OfType<Button>().Any()) controls = (Panel)co;
      }
      if (controls != null) controls.Top = open ? 329 : 70;
      hints[id].Top = open ? 374 : 116;
      c.Height = open ? 401 : 140;
      expandButtons[id].Text = open ? "▴ Свернуть консоль" : "▾ Развернуть консоль";
    }

    private void ToggleConsole(string id) {
      expanded[id] = !expanded[id];
      LayoutCard(id);
      if (expanded[id]) {
        terminals[id].SelectionStart = terminals[id].TextLength;
        terminals[id].ScrollToCaret();
      }
    }

    private void Append(string id, string message) {
      if (IsDisposed || !IsHandleCreated) return;
      if (InvokeRequired) {
        try { BeginInvoke((Action)(delegate { Append(id, message); })); }
        catch (InvalidOperationException) { }
        return;
      }
      string text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
      StringBuilder b = buffers[id];
      b.AppendLine(text);
      if (b.Length > 160000) b.Remove(0, b.Length - 120000);
      RichTextBox box = terminals[id];
      if (box.TextLength > 160000) box.Text = b.ToString();
      else box.AppendText(text + Environment.NewLine);
      if (expanded[id]) { box.SelectionStart = box.TextLength; box.ScrollToCaret(); }
      hints[id].Text = message.Length > 145 ? message.Substring(0, 145) + "…" : message;
    }

    private void SendInput(string id) {
      Process p;
      if (!owned.TryGetValue(id, out p) || !IsAlive(p)) {
        Append(id, "Ввод недоступен: этот процесс не запущен приложением.");
        return;
      }
      TextBox box = inputFields[id];
      string line = box.Text;
      if (line.Length == 0) return;
      try {
        p.StandardInput.WriteLine(line);
        p.StandardInput.Flush();
        box.Clear();
        // Do not echo user input: may contain credentials.
        Append(id, "Строка отправлена в stdin (содержимое скрыто).");
      } catch (Exception ex) { Append(id, "[ERR] Не удалось отправить ввод: " + ex.Message); }
    }

    private void ClearLog(string id) {
      buffers[id].Clear(); terminals[id].Clear();
      Append(id, "История вывода в приложении очищена. Работа процесса не затронута.");
    }

    private Process StartProcess(string id, string filename, string args, bool trackLong) {
      Process p = new Process();
      p.StartInfo.FileName = filename;
      p.StartInfo.Arguments = args;
      p.StartInfo.WorkingDirectory = root;
      p.StartInfo.UseShellExecute = false;
      p.StartInfo.CreateNoWindow = true;
      p.StartInfo.RedirectStandardOutput = true;
      p.StartInfo.RedirectStandardError = true;
      p.StartInfo.RedirectStandardInput = trackLong;
      p.EnableRaisingEvents = true;
      p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Append(id, e.Data); };
      p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Append(id, "[ERR] " + e.Data); };
      p.Exited += delegate {
        int exit = -1; try { exit = p.ExitCode; } catch { }
        Append(id, "Процесс завершился. Код: " + exit);
        try { BeginInvoke((Action)(delegate { if (id == "updater") ReleaseUpdaterMutex(); PollStatusAsync(); })); } catch { }
      };
      try {
        if (!p.Start()) throw new Exception("Process.Start вернул false");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (trackLong) owned[id] = p;
        else oneShots[id] = p;
        Append(id, "Запущена команда: " + Path.GetFileName(filename) + " (PID " + p.Id + ")");
        return p;
      } catch { p.Dispose(); throw; }
    }

    private string ScriptArgs(string file) {
      return "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, file) + "\"";
    }

    private bool IsAlive(Process p) {
      if (p == null) return false;
      try { return !p.HasExited; } catch { return false; }
    }

    private bool OwnedActive(string id) {
      Process p;
      return owned.TryGetValue(id, out p) && IsAlive(p);
    }

    private bool BusyOneshot(string id) {
      Process p;
      return oneShots.TryGetValue(id, out p) && IsAlive(p);
    }

    private bool ShouldBlockExternal(string id) {
      if (external.ContainsKey(id) && external[id]) {
        MessageBox.Show("Процесс " + Pretty(id) + " уже обнаружен вне приложения.\n" +
          "Чтобы видеть его вывод здесь, сначала корректно останови старое окно, затем запусти через Control Center.\n\n" +
          "Существующую внешнюю сессию приложение не трогает.", "Внешняя сессия", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
      }
      return false;
    }

    private string Pretty(string id) { return names[Array.IndexOf(keys, id)]; }

    private void StartService(string id) {
      if (OwnedActive(id) || BusyOneshot(id)) { Append(id, "Уже запущен; повторный запуск отменён."); return; }
      if (ShouldBlockExternal(id)) return;
      try {
        if (id == "repo2") {
          if (!Confirm("Подключить Fortis Repo 2?", "Будет вызван существующий защищённый FortisRepo2-quiet-logs.ps1.\nНикаких контейнеров не создаём.")) return;
          Append(id, "Запуск Repo 2 через текущий установленный скрипт и Fortis Guard…");
          StartProcess(id, psExe, ScriptArgs("Repo2-Worker.ps1"), true);
        } else if (id == "repo1") {
          if (!Confirm("Подключить Fortis Repo 1?", "Будет вызван действующий run-tunnel-quiet-logs.ps1 -Mode Run.\nRepo 2 не требуется как запущенный процесс, но общие Docker/Guard-проверки остаются обязательными.")) return;
          StartProcess(id, psExe, ScriptArgs("Repo1-Worker.ps1"), true);
        } else if (id == "updater") {
          if (!fortisOnline) { MessageBox.Show("Контейнер Fortis не работает. Сначала запусти Docker/Fortis."); return; }
          if (!AcquireUpdaterMutex()) return;
          try {
            if (!Confirm("Запустить RDG Updater?", "Проверь, что старое окно Update-RDG.ps1 -Watch остановлено (Ctrl+C).\n\n" +
              "Ручной updater в старой интерактивной PowerShell-консоли не всегда обнаружим.\n" +
              "Новая версия автоматически устанавливает только проверенные сборки.\n" +
              "При несовпадении ready.json с установленной RDG запуск будет отменён.")) {
              ReleaseUpdaterMutex(); return;
            }
            StartProcess(id, psExe, ScriptArgs("Updater-Worker.ps1"), true);
          } catch { ReleaseUpdaterMutex(); throw; }
        } else if (id == "watcher") {
          if (!fortisOnline) { MessageBox.Show("Fortis не работает. Запусти Docker Desktop."); return; }
          Append(id, "Проверяем существующий CI watcher (дубликаты запрещены)…");
          StartProcess(id, psExe, ScriptArgs("Watcher-Start.ps1"), false);
        }
        if (!expanded[id]) ToggleConsole(id);
        PollStatusAsync();
      } catch (Exception ex) { Append(id, "[ERROR] " + ex.Message); MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void StopService(string id) {
      if (id == "watcher") {
        if (!watcherOnline) { Append(id,"Watcher не запущен."); return; }
        if (!Confirm("Остановить CI watcher?", "Остановка через SIGTERM только точному процессу python3 -B tools/rdg-ci.py --watch.\nТекущая сборка может прерваться. RDG и updater останутся работать.")) return;
        try { StartProcess(id, psExe, ScriptArgs("Watcher-Stop.ps1"), false); } catch(Exception ex){ Append(id, "[ERROR] " + ex.Message); }
        return;
      }
      if (!OwnedActive(id)) {
        Append(id, "Нет процесса, запущенного этим приложением. Внешние сессии не завершаются.");
        return;
      }
      string warning = id == "updater" ?
        "ВНИМАНИЕ: остановка updater может прервать развёртывание RDG.\nПродолжай только если он сейчас ожидает новую сборку (не обновляет сервер)." :
        "Будет принудительно завершена только сессия и её дочерние процессы, запущенные этим окном приложения.\nАктивные вызовы Fortis могут прерваться.";
      if (!Confirm("Остановить " + Pretty(id) + "?", warning + "\n\nЭто не останавливает Docker-контейнеры и RDG.")) return;
      Process p = owned[id];
      try {
        // The scripts start tunnel-client subprocesses. taskkill /T confines termination to the owned PID tree.
        int pid = p.Id;
        Append(id, "Запрос на завершение своего дерева процессов PID=" + pid);
        Task.Run(delegate {
          Result r = Command("taskkill.exe", "/PID " + pid + " /T /F", 12000);
          Append(id, r.Output + (r.Error.Length > 0 ? " " + r.Error : ""));
          if (r.Code != 0) Append(id, "Команда завершения сообщила код " + r.Code);
        });
      } catch(Exception ex) { Append(id, "[ERROR] " + ex.Message); }
    }

    private bool Confirm(string title, string text) {
      return MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private bool AcquireUpdaterMutex() {
      if (updaterMutexOwned) { MessageBox.Show("Updater уже управляется этим приложением."); return false; }
      try {
        if (updaterMutex == null) updaterMutex = new Mutex(false, @"Local\FortisRdgUpdaterWatchV2");
        try { updaterMutexOwned = updaterMutex.WaitOne(0); }
        catch(AbandonedMutexException) { updaterMutexOwned = true; }
        if (!updaterMutexOwned) {
          MessageBox.Show("RDG Updater уже запущен через прежний ярлык v2.\nСначала останови его через Ctrl+C.", "Updater уже работает");
          return false;
        }
        return true;
      } catch (Exception ex) { MessageBox.Show("Ошибка блокировки updater: " + ex.Message); return false; }
    }

    private void ReleaseUpdaterMutex() {
      if (updaterMutexOwned && !OwnedActive("updater")) {
        try { updaterMutex.ReleaseMutex(); } catch { }
        updaterMutexOwned = false;
      }
    }

    private void SetState(string id, string state) {
      lastStates[id] = state;
      statuses[id].Text = (state == "Запущен" ? "● " : state == "Внешний процесс" ? "◉ " : "○ ") + state;
      statuses[id].ForeColor = state == "Запущен" ? green : state == "Внешний процесс" ? Color.FromArgb(243,194,107) : muted;
      bool alive = OwnedActive(id);
      bool inFlight = BusyOneshot(id);
      if (id == "watcher") {
        startButtons[id].Enabled = !watcherOnline && !inFlight && fortisOnline;
        stopButtons[id].Enabled = watcherOnline && !inFlight;
      } else {
        startButtons[id].Enabled = !alive && !external[id] && !inFlight && (id != "updater" || fortisOnline);
        stopButtons[id].Enabled = alive;
      }
    }

    private sealed class Snapshot {
      public bool Docker; public bool Fortis; public bool Watcher;
      public string WatcherLog = "";
      public Dictionary<string, bool> Externals = new Dictionary<string, bool>();
      public string Message;
    }
    private sealed class Result { public int Code; public string Output=""; public string Error=""; }

    private static Result Command(string exe, string args, int timeout) {
      Result r = new Result();
      try {
        using(Process p = new Process()) {
          p.StartInfo.FileName=exe; p.StartInfo.Arguments=args;
          p.StartInfo.UseShellExecute=false; p.StartInfo.CreateNoWindow=true;
          p.StartInfo.RedirectStandardOutput=true; p.StartInfo.RedirectStandardError=true;
          p.Start();
          Task<string> o = p.StandardOutput.ReadToEndAsync();
          Task<string> e = p.StandardError.ReadToEndAsync();
          if(!p.WaitForExit(timeout)) {
            try { p.Kill(); } catch { }
            r.Code=-2; r.Error="Превышен таймаут диагностики"; return r;
          }
          Task.WaitAll(o,e);
          r.Code=p.ExitCode; r.Output=o.Result.Trim(); r.Error=e.Result.Trim();
        }
      } catch (Exception ex) { r.Code=-3; r.Error=ex.Message; }
      return r;
    }

    private static Dictionary<string,bool> ExternalWindows() {
      Dictionary<string,bool> a = new Dictionary<string,bool>();
      foreach (string s in new [] {"repo2", "repo1", "updater"}) a[s] = false;
      using(ManagementObjectSearcher search = new ManagementObjectSearcher("SELECT ProcessId,Name,CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'")) {
        foreach(ManagementObject o in search.Get()) {
          string cmd = Convert.ToString(o["CommandLine"]);
          if (String.IsNullOrEmpty(cmd)) continue;
          // Workers launched by this app have different names and will not match these rules.
          if (Regex.IsMatch(cmd, @"FortisRepo2-quiet-logs\.ps1", RegexOptions.IgnoreCase) &&
              Regex.IsMatch(cmd, @"-Action\s+Start", RegexOptions.IgnoreCase)) a["repo2"] = true;
          if (Regex.IsMatch(cmd, @"run-tunnel-quiet-logs\.ps1", RegexOptions.IgnoreCase) &&
              Regex.IsMatch(cmd, @"-Mode\s+Run", RegexOptions.IgnoreCase)) a["repo1"] = true;
          if ((Regex.IsMatch(cmd, @"Start-Updater\.ps1", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(cmd, @"Update-RDG\.ps1", RegexOptions.IgnoreCase)) &&
               Regex.IsMatch(cmd, @"-Watch", RegexOptions.IgnoreCase)) a["updater"] = true;
        }
      }
      return a;
    }

    private void PollStatusAsync() {
      if(checking || IsDisposed) return;
      checking = true;
      Task.Run(delegate {
        Snapshot s = new Snapshot();
        try {
          Result docker = Command("docker.exe", "inspect --type container --format \"{{.State.Running}}\" fortis-repo-test", 7000);
          s.Docker = docker.Code == 0;
          s.Fortis = docker.Code == 0 && docker.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
          if (s.Fortis) {
            Result top = Command("docker.exe", "top fortis-repo-test -eo pid,args", 7000);
            if(top.Code == 0) s.Watcher = Regex.IsMatch(top.Output, @"python3(?:\.\d+)?\s+-B\s+tools/rdg-ci\.py\s+--watch", RegexOptions.IgnoreCase);
            if (s.Watcher) {
              Result log = Command("docker.exe", "exec fortis-repo-test tail -n 100 /workspace/repo/Workfiles/rdg-ci/watch.log", 7000);
              if (log.Code == 0) s.WatcherLog = log.Output;
            }
          }
          s.Externals = ExternalWindows();
          s.Message = s.Docker ? "Docker доступен" : "Docker/Fortis недоступен";
        } catch(Exception ex) { s.Message="Ошибка проверки: "+ex.Message; }
        try {
          if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(delegate { ApplySnapshot(s); checking = false; }));
          else checking=false;
        } catch { checking=false; }
      });
    }

    private void ApplySnapshot(Snapshot s) {
      dockerOnline=s.Docker; fortisOnline=s.Fortis; watcherOnline=s.Watcher;
      foreach (string k in new [] {"repo2","repo1","updater"}) {
        bool ext = s.Externals.ContainsKey(k) && s.Externals[k];
        if (ext && !external[k]) Append(k, "Найдена внешняя консоль. Её stdout не может быть перехвачен приложением; не запускаем дубликат.");
        if (!ext && external[k]) Append(k, "Внешний процесс больше не обнаружен.");
        external[k]=ext;
        if (OwnedActive(k)) SetState(k, "Запущен");
        else if (ext) SetState(k,"Внешний процесс");
        else SetState(k,"Остановлен");
      }
      SetState("watcher", watcherOnline ? "Запущен" : "Остановлен");
      footer.Text = s.Message + "   •   Fortis: " + (fortisOnline?"running":"offline") +
         "   •   CI watcher: " + (watcherOnline?"running":"stopped") +
         "   •   Консоли можно сворачивать, процессы при этом продолжают работу.";
      if (watcherOnline) MergeWatcherLog(s.WatcherLog);
      else previousWatcherLines.Clear();
      if (!OwnedActive("updater")) ReleaseUpdaterMutex();
    }

    private void MergeWatcherLog(string snapshot) {
      if (String.IsNullOrEmpty(snapshot)) return;
      List<string> now = snapshot.Replace("\r", "").Split(new [] {'\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();
      if (now.Count == 0) return;
      int overlap = Math.Min(previousWatcherLines.Count, now.Count);
      for (; overlap > 0; overlap--) {
        bool matches = true;
        for (int n = 0; n < overlap; n++) {
          if (!String.Equals(previousWatcherLines[previousWatcherLines.Count - overlap + n], now[n], StringComparison.Ordinal)) {
            matches = false; break;
          }
        }
        if (matches) break;
      }
      for (int n = overlap; n < now.Count; n++) Append("watcher", now[n]);
      previousWatcherLines = now;
    }

    private void ShowFromTray() {
      Show(); WindowState=FormWindowState.Normal; Activate();
    }

    private void TryQuit() {
      if (keys.Any(k => k != "watcher" && OwnedActive(k))) {
        MessageBox.Show("Сначала останови процессы Fortis/Updater, запущенные через приложение.\n" +
          "Иначе Windows может прервать их вывод или оставить неполную сессию.\n" +
          "Приложение остаётся доступным в трее.", "Есть активные процессы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      exiting=true;
      Close();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e) {
      if (!exiting && e.CloseReason == CloseReason.UserClosing &&
          keys.Any(k => k != "watcher" && OwnedActive(k))) {
        e.Cancel=true; Hide();
        tray.BalloonTipTitle="Fortis / RDG Control Center";
        tray.BalloonTipText="Приложение свёрнуто в трей; управляемые процессы продолжают работать.";
        tray.ShowBalloonTip(1500);
        return;
      }
      if(timer!=null) timer.Stop();
      if (tray!=null) { tray.Visible=false; tray.Dispose(); }
      ReleaseUpdaterMutex();
    }

    public static void Run(string path) {
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new MainForm(path));
    }
  }
}
