using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace FortisRdgControl {
  public sealed class RegisteredScript {
    public string Id, BundleId, Name, Path, Arguments, WorkingDirectory, Mode, LogPath, Token;
    public override string ToString() { return Name; }
  }
  public sealed class ScriptProfile {
    public string Id, Name;
    public readonly List<string> ScriptIds = new List<string>();
    public override string ToString() { return Name; }
  }
  public sealed class ScriptCatalogForm : Form {
    private readonly string launcherRoot;
    private readonly string dataRoot;
    private readonly string catalogPath;
    private readonly string profilesPath;
    private readonly List<ScriptProfile> profiles = new List<ScriptProfile>();
    private string profilesWarning = "";
    private readonly string shell;
    private readonly List<RegisteredScript> entries = new List<RegisteredScript>();
    private readonly ListView items = new ListView();
    private readonly RichTextBox output = new RichTextBox();
    private readonly Label detailTitle = new Label();
    private readonly Label detailPath = new Label();
    private readonly Label detailMeta = new Label();
    private readonly Label detailStatus = new Label();
    private readonly Label logHeader = new Label();
    private readonly Label footer = new Label();
    private readonly Label emptyState = new Label();
    private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
    private readonly Dictionary<string, Process> owned = new Dictionary<string, Process>();
    private readonly Dictionary<string, DateTime> launched = new Dictionary<string, DateTime>();
    private readonly Dictionary<string, int> pid = new Dictionary<string, int>();
    private string catalogWarning = "";
    private bool loading = false;
    private string shownOutput = "";
    private DateTime lastExternalCheckUtc = DateTime.MinValue;
    private bool externalScanRunning = false;
    private bool externalScanReady = false;
    private readonly Dictionary<string, ExternalRun> externalRuns =
      new Dictionary<string, ExternalRun>(StringComparer.OrdinalIgnoreCase);
    private sealed class ExternalRun {
      public int Pid;
      public DateTime Started;
      public bool Ambiguous;
    }
    private const int EM_GETFIRSTVISIBLELINE = 0xCE;
    private const int EM_GETLINECOUNT = 0xBA;
    private const int EM_LINESCROLL = 0xB6;
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
    // The API name already includes its W suffix. ExactSpelling prevents
    // charset name mangling (for example, probing CommandLineToArgvWW).
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argc);
    [DllImport("kernel32.dll", ExactSpelling=true)]
    private static extern IntPtr LocalFree(IntPtr mem);
    private sealed class BundleDefinition {
      public string Id, Name, FileName;
      public BundleDefinition(string id,string name,string fileName) { Id=id;Name=name;FileName=fileName; }
    }
    public ScriptCatalogForm(string folder) {
      launcherRoot = folder;
      shell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
      dataRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortisRdgControlCenter", "scripts");
      catalogPath = System.IO.Path.Combine(dataRoot, "catalog.xml");
      profilesPath = System.IO.Path.Combine(dataRoot, "profiles.xml");
      Text = "Control Center — пользовательские скрипты";
      Size = new Size(1280, 760); MinimumSize = new Size(960, 620); StartPosition = FormStartPosition.CenterScreen;
      Font = new Font("Segoe UI", 9.5f); BackColor = Color.FromArgb(13,21,35); ForeColor = Color.FromArgb(232,240,250);

      var layout = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3, BackColor=BackColor };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
      Controls.Add(layout);

      var top = new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(12,9,8,7), WrapContents=false, AutoScroll=true, BackColor=Color.FromArgb(18,30,49) };
      AddButton(top, "＋ Добавить", delegate { AddEntry(); }, 126, true);
      AddButton(top, "Импорт из текста", delegate { ImportFromText(); }, 168, true);
      AddButton(top, "Готовый набор", delegate { ImportBuiltInBundle(); }, 140, false);
      AddButton(top, "Профили", delegate { ManageProfiles(); }, 112, true);
      AddButton(top, "Изменить", delegate { EditEntry(); }, 112, false);
      AddButton(top, "Удалить", delegate { DeleteEntry(); }, 100, false);
      AddButton(top, "▶ Запустить", delegate { StartEntry(); }, 118, true);
      AddButton(top, "■ Остановить", delegate { StopEntry(); }, 118, false);
      AddButton(top, "Открыть журнал", delegate { OpenLog(); }, 142, false);
      layout.Controls.Add(top,0,0);

      var split = new SplitContainer { Dock=DockStyle.Fill, BackColor=BackColor, Orientation=Orientation.Vertical, FixedPanel=FixedPanel.Panel1, SplitterWidth=7 };
      var leftLayout = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=2, BackColor=Color.FromArgb(22,34,53), Padding=new Padding(0) };
      leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
      leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
      var leftTitle = new Label { Text="ПОДКЛЮЧЁННЫЕ СКРИПТЫ", Dock=DockStyle.Fill, Padding=new Padding(14,12,4,4), ForeColor=Color.FromArgb(166,190,220), BackColor=Color.FromArgb(22,34,53), Font=new Font("Segoe UI",8.5f,FontStyle.Bold) };
      items.Dock=DockStyle.Fill; items.View=View.Details; items.FullRowSelect=true; items.MultiSelect=false; items.HideSelection=false;
      items.HeaderStyle=ColumnHeaderStyle.Nonclickable; items.GridLines=true; items.BorderStyle=BorderStyle.None;
      items.BackColor=Color.FromArgb(22,34,53); items.ForeColor=Color.FromArgb(232,240,250); items.Font=new Font("Segoe UI",9.5f);
      items.Columns.Add("Название",180); items.Columns.Add("Состояние",95); items.Columns.Add("Режим",85);
      items.SelectedIndexChanged += delegate { if(!loading) RefreshSelected(); };
      var listHost = new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(22,34,53) };
      emptyState.Dock=DockStyle.Fill; emptyState.Text="Здесь пока нет скриптов.\n\nНажмите «＋ Добавить», чтобы подключить существующий .ps1, .cmd, .bat, .py или .exe.\n\nФайлы не копируются и не перемещаются.";
      emptyState.TextAlign=ContentAlignment.MiddleCenter; emptyState.Padding=new Padding(24); emptyState.ForeColor=Color.FromArgb(151,174,202); emptyState.BackColor=Color.FromArgb(22,34,53);
      listHost.Controls.Add(items); listHost.Controls.Add(emptyState);
      leftLayout.Controls.Add(leftTitle,0,0); leftLayout.Controls.Add(listHost,0,1);
      split.Panel1.Controls.Add(leftLayout);
      items.Resize += delegate {
        if(items.Columns.Count==3) items.Columns[0].Width=Math.Max(100,items.ClientSize.Width-items.Columns[1].Width-items.Columns[2].Width-8);
      };

      var rightLayout = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3, BackColor=Color.FromArgb(13,21,35), Padding=new Padding(0) };
      rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,106));
      rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
      rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
      var details = new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(19,31,49), Padding=new Padding(16,10,16,8) };
      detailTitle.Location=new Point(16,10); detailTitle.Size=new Size(650,28); detailTitle.Font=new Font("Segoe UI Semibold",15,FontStyle.Bold); detailTitle.ForeColor=Color.FromArgb(239,245,253); detailTitle.AutoEllipsis=true;
      detailStatus.Location=new Point(690,13); detailStatus.Size=new Size(210,23); detailStatus.Anchor=AnchorStyles.Top|AnchorStyles.Right; detailStatus.TextAlign=ContentAlignment.MiddleRight; detailStatus.Font=new Font("Segoe UI",9,FontStyle.Bold); detailStatus.ForeColor=Color.FromArgb(151,174,202);
      detailPath.Location=new Point(17,43); detailPath.Size=new Size(900,22); detailPath.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; detailPath.AutoEllipsis=true; detailPath.ForeColor=Color.FromArgb(166,190,220);
      detailMeta.Location=new Point(17,70); detailMeta.Size=new Size(900,20); detailMeta.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; detailMeta.AutoEllipsis=true; detailMeta.ForeColor=Color.FromArgb(145,164,190);
      details.Controls.Add(detailTitle); details.Controls.Add(detailStatus); details.Controls.Add(detailPath); details.Controls.Add(detailMeta);
      details.Resize += delegate {
        detailStatus.Left=Math.Max(250,details.ClientSize.Width-detailStatus.Width-16);
        detailTitle.Width=Math.Max(220,detailStatus.Left-detailTitle.Left-14);
        detailPath.Width=Math.Max(220,details.ClientSize.Width-34);
        detailMeta.Width=Math.Max(220,details.ClientSize.Width-34);
      };
      var logBar = new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(24,38,59), Padding=new Padding(16,8,12,4) };
      logHeader.Dock=DockStyle.Fill; logHeader.Text="ВЫВОД И ЖУРНАЛ"; logHeader.ForeColor=Color.FromArgb(180,199,225); logHeader.Font=new Font("Segoe UI",8.5f,FontStyle.Bold);
      logBar.Controls.Add(logHeader);
      output.Dock=DockStyle.Fill; output.ReadOnly=true; output.Font=new Font("Consolas",9.5f); output.BackColor=Color.FromArgb(8,13,22); output.ForeColor=Color.FromArgb(207,221,241); output.WordWrap=true; output.ScrollBars=RichTextBoxScrollBars.Vertical; output.BorderStyle=BorderStyle.None;
      rightLayout.Controls.Add(details,0,0); rightLayout.Controls.Add(logBar,0,1); rightLayout.Controls.Add(output,0,2);
      split.Panel2.Controls.Add(rightLayout);
      layout.Controls.Add(split,0,1);

      footer.Dock=DockStyle.Fill; footer.Padding=new Padding(12,7,8,4); footer.BackColor=Color.FromArgb(18,30,49); footer.ForeColor=Color.FromArgb(142,164,192); footer.AutoEllipsis=true;
      layout.Controls.Add(footer,0,2);
      Shown += delegate {
        split.Panel1MinSize=280; split.Panel2MinSize=470;
        int maxLeft=split.ClientSize.Width-split.Panel2MinSize-split.SplitterWidth;
        if(maxLeft>=split.Panel1MinSize) split.SplitterDistance=Math.Max(split.Panel1MinSize,Math.Min(390,maxLeft));
      };
      LoadEntries(); LoadProfiles(); RefreshItems();
      tick.Interval=1200; tick.Tick += delegate { RefreshStatuses(); }; tick.Start();
      FormClosing += delegate { tick.Stop(); }; // user scripts are independent of the UI
    }
    private void AddButton(Control bar, string name, Action action, int width, bool primary) {
      var b = new Button { Text=name, Width=width, Height=36, AutoSize=false, FlatStyle=FlatStyle.Flat,
        FlatAppearance={ BorderColor=Color.FromArgb(66,91,125), BorderSize=1 },
        BackColor=primary?Color.FromArgb(42,91,155):Color.FromArgb(34,51,75),
        ForeColor=Color.FromArgb(240,246,255), Font=new Font("Segoe UI",9.5f,FontStyle.Bold),
        Margin=new Padding(0,0,8,0), Cursor=Cursors.Hand };
      b.Click += delegate { action(); }; bar.Controls.Add(b);
    }
    private RegisteredScript Current { get { return items.SelectedItems.Count>0 ? items.SelectedItems[0].Tag as RegisteredScript : null; } }
    private void LoadEntries() {
      bool hasPrimary=File.Exists(catalogPath);
      if(!hasPrimary && !Directory.Exists(dataRoot)) return;
      string primaryError=hasPrimary?"":"Основной файл каталога отсутствует.";
      if(hasPrimary && TryLoadEntries(catalogPath,out primaryError)) return;
      string[] backups=new string[0];
      try {
        if(Directory.Exists(dataRoot)) backups=Directory.GetFiles(dataRoot,"catalog.xml.*.bak");
        Array.Sort(backups,delegate(string a,string b) {
          return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
        });
      } catch { }
      foreach(string backup in backups) {
        string backupError="";
        if(TryLoadEntries(backup,out backupError)) {
          catalogWarning="Основной каталог повреждён; загружена резервная копия "+System.IO.Path.GetFileName(backup)+".";
          return;
        }
      }
      catalogWarning="Не удалось прочитать каталог скриптов: "+primaryError+" Записи не удалены; проверьте резервные копии.";
    }
    private bool TryLoadEntries(string source,out string error) {
      error="";
      try {
        var settings = new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null };
        using(var reader=XmlReader.Create(source,settings)) {
          var xml = new XmlDocument { XmlResolver=null }; xml.Load(reader);
          if(xml.DocumentElement==null || xml.DocumentElement.Name!="Scripts") throw new InvalidDataException("Корневой элемент Scripts не найден.");
          XmlNodeList nodes=xml.SelectNodes("/Scripts/Script");
          if(nodes==null) throw new InvalidDataException("Не найден раздел Scripts/Script.");
          var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          foreach(XmlNode node in nodes) {
            if(entries.Count>=200) break;
            var e = new RegisteredScript {
              Id=Attr(node,"id"), BundleId=Attr(node,"bundle"), Name=Attr(node,"name"), Path=Attr(node,"path"),
              Arguments=Attr(node,"arguments"), WorkingDirectory=Attr(node,"cwd"), Mode=Attr(node,"mode")
            };
            Guid parsed;
            if(!Guid.TryParse(e.Id,out parsed) || !ids.Add(e.Id)) {
              e.Id=Guid.NewGuid().ToString("D");
              while(!ids.Add(e.Id)) e.Id=Guid.NewGuid().ToString("D");
            }
            if(String.IsNullOrWhiteSpace(e.Name))
              e.Name=String.IsNullOrWhiteSpace(e.Path)?"Без названия":System.IO.Path.GetFileNameWithoutExtension(e.Path);
            entries.Add(e); // Keep invalid records visible so they can be corrected or removed.
          }
        }
        return true;
      } catch(Exception ex) {
        entries.Clear();
        error=ex.Message;
        return false;
      }
    }
    // Profiles are references to catalog registrations, not copied commands.
    // Persist separately so editing a profile never rewrites the catalog.
    private void LoadProfiles() {
      if(!File.Exists(profilesPath))return;
      string problem="";
      if(TryLoadProfiles(profilesPath,out problem))return;
      string[] backups=new string[0];
      try {
        backups=Directory.GetFiles(dataRoot,"profiles.xml.*.bak");
        Array.Sort(backups,delegate(string a,string b) {
          return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
        });
      }catch { }
      foreach(string backup in backups) {
        string error="";
        if(TryLoadProfiles(backup,out error)) {
          profilesWarning="Основной файл профилей повреждён; загружена резервная копия "+Path.GetFileName(backup)+". Сохраните профили для восстановления.";
          return;
        }
      }
      profilesWarning="Не удалось загрузить профили: "+problem+". Файл сохранён без изменений.";
    }
    private bool TryLoadProfiles(string path,out string error) {
      error="";
      var loaded=new List<ScriptProfile>();
      try {
        var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null };
        using(var reader=XmlReader.Create(path,settings)) {
          var xml=new XmlDocument { XmlResolver=null };xml.Load(reader);
          if(xml.DocumentElement==null || xml.DocumentElement.Name!="Profiles" ||
             Attr(xml.DocumentElement,"version")!="1")throw new InvalidDataException("Неподдерживаемый формат профилей.");
          var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          foreach(XmlNode node in xml.DocumentElement.ChildNodes) {
            if(node.NodeType!=XmlNodeType.Element || node.Name!="Profile")throw new InvalidDataException("Неожиданный элемент профиля.");
            string id=Attr(node,"id"),name=Attr(node,"name");
            Guid parsed;
            if(!Guid.TryParse(id,out parsed) || String.IsNullOrWhiteSpace(name) || name.Length>80 ||
               !ids.Add(id) || !names.Add(name.Trim()) || loaded.Count>=100)
              throw new InvalidDataException("Некорректный или повторяющийся профиль.");
            var profile=new ScriptProfile { Id=id,Name=name.Trim() };
            foreach(XmlNode child in node.ChildNodes) {
              if(child.NodeType!=XmlNodeType.Element || child.Name!="Script")throw new InvalidDataException("Некорректный состав профиля.");
              string scriptId=Attr(child,"id");
              if(!Guid.TryParse(scriptId,out parsed) || profile.ScriptIds.Count>=200 ||
                 profile.ScriptIds.Exists(delegate(string x) { return String.Equals(x,scriptId,StringComparison.OrdinalIgnoreCase); }))
                throw new InvalidDataException("Некорректная ссылка на скрипт в профиле.");
              profile.ScriptIds.Add(scriptId);
            }
            loaded.Add(profile);
          }
        }
        profiles.Clear();profiles.AddRange(loaded);return true;
      } catch(Exception ex) { error=ex.Message;return false; }
    }
    private void SaveProfiles() {
      // Never silently replace a damaged primary file when no valid backup was loaded.
      if(profilesWarning.StartsWith("Не удалось загрузить профили:"))
        throw new InvalidDataException(profilesWarning);
      Directory.CreateDirectory(dataRoot);
      var xml=new XmlDocument();var root=xml.CreateElement("Profiles");xml.AppendChild(root);
      SetAttr(xml,root,"version","1");
      foreach(ScriptProfile profile in profiles) {
        var node=xml.CreateElement("Profile");root.AppendChild(node);
        SetAttr(xml,node,"id",profile.Id);SetAttr(xml,node,"name",profile.Name);
        foreach(string scriptId in profile.ScriptIds) {
          var child=xml.CreateElement("Script");node.AppendChild(child);SetAttr(xml,child,"id",scriptId);
        }
      }
      string temp=profilesPath+"."+Guid.NewGuid().ToString("N")+".tmp";
      string backup=profilesPath+"."+Guid.NewGuid().ToString("N")+".bak";
      try {
        using(var writer=XmlWriter.Create(temp,new XmlWriterSettings { Indent=true,Encoding=new UTF8Encoding(false) }))xml.Save(writer);
        if(File.Exists(profilesPath))File.Replace(temp,profilesPath,backup);
        else File.Move(temp,profilesPath);
        string[] backups=Directory.GetFiles(dataRoot,"profiles.xml.*.bak");
        Array.Sort(backups,delegate(string a,string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
        for(int i=5;i<backups.Length;i++) { try { File.Delete(backups[i]); }catch { } }
        profilesWarning="";
      } finally { if(File.Exists(temp))File.Delete(temp); }
    }
    private static string AskProfileName(Form owner,string title,string current) {
      using(var dialog=new Form { Text=title,Size=new Size(440,175),FormBorderStyle=FormBorderStyle.FixedDialog,
        MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",9.5f) }) {
        var input=new TextBox { Left=15,Top=18,Width=390,Text=current??"" };
        var ok=new Button { Text="OK",Left=212,Top=62,Width=90,DialogResult=DialogResult.OK };
        var cancel=new Button { Text="Отмена",Left=311,Top=62,Width=94,DialogResult=DialogResult.Cancel };
        dialog.Controls.Add(input);dialog.Controls.Add(ok);dialog.Controls.Add(cancel);
        dialog.AcceptButton=ok;dialog.CancelButton=cancel;
        if(dialog.ShowDialog(owner)!=DialogResult.OK)return null;
        string name=input.Text.Trim();
        if(name.Length==0 || name.Length>80) {
          MessageBox.Show(owner,"Название должно содержать от 1 до 80 символов.");return null;
        }
        return name;
      }
    }
    private void ManageProfiles() {
      using(var dialog=new Form { Text="Профили скриптов",Size=new Size(850,630),MinimumSize=new Size(760,560),
        StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",9.5f) }) {
        var list=new ListBox { Left=14,Top=50,Width=270,Height=385,Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Bottom };
        var scripts=new CheckedListBox { Left=300,Top=50,Width=520,Height=385,CheckOnClick=true,
          Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom,IntegralHeight=false };
        var info=new Label { Left=14,Top=444,Width=805,Height=48,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom,
          Text="Выберите профиль. Состав сохраняется отдельно от каталога; отсутствующие регистрации не запускаются." };
        var newButton=new Button { Text="Новый",Left=14,Top=506,Width=100,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        var rename=new Button { Text="Переименовать",Left=121,Top=506,Width=138,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        var remove=new Button { Text="Удалить",Left=14,Top=538,Width=100,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        var save=new Button { Text="Сохранить состав",Left=300,Top=506,Width=153,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        var start=new Button { Text="▶ Запустить профиль",Left=464,Top=506,Width=165,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        var stop=new Button { Text="■ Остановить профиль",Left=638,Top=506,Width=175,Anchor=AnchorStyles.Left|AnchorStyles.Bottom };
        dialog.Controls.Add(new Label { Left=14,Top=17,Width=270,Text="ПРОФИЛИ" });
        dialog.Controls.Add(new Label { Left=300,Top=17,Width=500,Text="ЗАРЕГИСТРИРОВАННЫЕ СКРИПТЫ" });
        dialog.Controls.Add(list);dialog.Controls.Add(scripts);dialog.Controls.Add(info);
        foreach(Control button in new Control[]{newButton,rename,remove,save,start,stop})dialog.Controls.Add(button);
        Action refresh=delegate {
          string previous=(list.SelectedItem as ScriptProfile)==null?null:((ScriptProfile)list.SelectedItem).Id;
          list.Items.Clear();foreach(ScriptProfile p in profiles)list.Items.Add(p);
          if(list.Items.Count>0) {
            int selected=0;
            for(int i=0;i<list.Items.Count;i++)if(((ScriptProfile)list.Items[i]).Id==previous)selected=i;
            list.SelectedIndex=selected;
          }
        };
        list.SelectedIndexChanged += delegate {
          scripts.Items.Clear();var selected=list.SelectedItem as ScriptProfile;
          scripts.Enabled=selected!=null;
          foreach(RegisteredScript entry in entries)
            scripts.Items.Add(entry,selected!=null && selected.ScriptIds.Exists(delegate(string id) { return String.Equals(id,entry.Id,StringComparison.OrdinalIgnoreCase); }));
          // CheckedListBox displays a registered entry's name, not its object type.
          int missing=selected==null?0:selected.ScriptIds.Count-idCount(selected.ScriptIds);
          info.Text=selected==null?"Создайте профиль для выбора скриптов.":
            "Скриптов в профиле: "+selected.ScriptIds.Count+"; отсутствующих регистраций: "+missing+
            ". Сначала нажмите «Сохранить состав», затем запустите или остановите профиль.";
        };
        newButton.Click += delegate {
          if(profiles.Count>=100) { MessageBox.Show(dialog,"Максимум 100 профилей.");return; }
          string name=AskProfileName(dialog,"Новый профиль","");if(name==null)return;
          if(profiles.Exists(delegate(ScriptProfile p) { return String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase); })) {
            MessageBox.Show(dialog,"Профиль с таким именем уже есть.");return;
          }
          var added=new ScriptProfile { Id=Guid.NewGuid().ToString("D"),Name=name };
          profiles.Add(added);
          try { SaveProfiles();refresh();list.SelectedItem=added; }
          catch(Exception ex) { profiles.Remove(added);MessageBox.Show(dialog,ex.Message); }
        };
        rename.Click += delegate {
          var selected=list.SelectedItem as ScriptProfile;if(selected==null)return;
          string name=AskProfileName(dialog,"Переименовать профиль",selected.Name);if(name==null)return;
          if(profiles.Exists(delegate(ScriptProfile p) { return p!=selected && String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase); })) {
            MessageBox.Show(dialog,"Такое имя уже используется.");return;
          }
          string old=selected.Name;selected.Name=name;
          try { SaveProfiles();refresh(); }catch(Exception ex) { selected.Name=old;MessageBox.Show(dialog,ex.Message); }
        };
        remove.Click += delegate {
          var selected=list.SelectedItem as ScriptProfile;if(selected==null)return;
          if(MessageBox.Show(dialog,"Удалить профиль «"+selected.Name+"»? Скрипты и процессы не затрагиваются.",
            "Удалить профиль",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
          int index=profiles.IndexOf(selected);profiles.RemoveAt(index);
          try { SaveProfiles();refresh(); }catch(Exception ex) { profiles.Insert(index,selected);MessageBox.Show(dialog,ex.Message); }
        };
        save.Click += delegate {
          var selected=list.SelectedItem as ScriptProfile;if(selected==null)return;
          var original=new List<string>(selected.ScriptIds);
          selected.ScriptIds.Clear();
          foreach(RegisteredScript entry in scripts.CheckedItems)selected.ScriptIds.Add(entry.Id);
          try { SaveProfiles();list.SelectedIndex=-1;list.SelectedItem=selected; }
          catch(Exception ex) { selected.ScriptIds.Clear();selected.ScriptIds.AddRange(original);MessageBox.Show(dialog,ex.Message); }
        };
        start.Click += delegate { RunProfile(list.SelectedItem as ScriptProfile,true,dialog); };
        stop.Click += delegate { RunProfile(list.SelectedItem as ScriptProfile,false,dialog); };
        refresh();
        if(profilesWarning.Length>0)MessageBox.Show(dialog,profilesWarning,"Профили",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        dialog.ShowDialog(this);
      }
    }
    private int idCount(List<string> ids) {
      int count=0;
      foreach(string id in ids)if(entries.Exists(delegate(RegisteredScript e) { return String.Equals(e.Id,id,StringComparison.OrdinalIgnoreCase); }))count++;
      return count;
    }
    private void RunProfile(ScriptProfile profile,bool launching,Form dialog) {
      if(profile==null)return;
      if(profile.ScriptIds.Count==0) { MessageBox.Show(dialog,"В профиле нет скриптов.");return; }
      string action=launching?"Запустить":"Остановить";
      string description=launching?"Уже запущенные будут пропущены. Скрипты выполняются с вашими правами Windows.":
        "Уже остановленные будут пропущены. Управляемые процессы могут завершиться вместе с дочерними. Внешние PowerShell-процессы останавливаются только по проверенному PID, без дерева.";
      if(MessageBox.Show(dialog,action+" профиль «"+profile.Name+"» ("+profile.ScriptIds.Count+" записей)?\n\n"+description,
        "Подтверждение профиля",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
      var report=new List<string>();
      foreach(string id in new List<string>(profile.ScriptIds)) {
        RegisteredScript entry=entries.Find(delegate(RegisteredScript e) { return String.Equals(e.Id,id,StringComparison.OrdinalIgnoreCase); });
        if(entry==null) { report.Add("ПРОПУЩЕН: регистрация отсутствует ("+id+")");continue; }
        if(launching)StartEntry(entry,true,report);
        else StopEntry(entry,true,report);
      }
      RefreshStatuses();
      MessageBox.Show(dialog,String.Join("\n",report.ToArray()),"Профиль «"+profile.Name+"»: результат",
        MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    private static string Attr(XmlNode n,string key) { var a=n.Attributes[key]; return a==null?"":a.Value; }
    private void SaveEntries() {
      Directory.CreateDirectory(dataRoot);
      var xml=new XmlDocument(); var root=xml.CreateElement("Scripts"); xml.AppendChild(root);
      foreach(var e in entries) {
        var n=xml.CreateElement("Script"); root.AppendChild(n);
        SetAttr(xml,n,"id",e.Id); SetAttr(xml,n,"bundle",e.BundleId); SetAttr(xml,n,"name",e.Name); SetAttr(xml,n,"path",e.Path);
        SetAttr(xml,n,"arguments",e.Arguments); SetAttr(xml,n,"cwd",e.WorkingDirectory); SetAttr(xml,n,"mode",e.Mode);
      }
      string id=Guid.NewGuid().ToString("N");
      string temp=catalogPath+"."+id+".tmp";
      string backup=catalogPath+"."+id+".bak";
      try {
        using(var writer=XmlWriter.Create(temp,new XmlWriterSettings{ Indent=true, Encoding=new UTF8Encoding(false) })) xml.Save(writer);
        if(File.Exists(catalogPath)) File.Replace(temp,catalogPath,backup);
        else File.Move(temp,catalogPath);
        PruneCatalogBackups();
        catalogWarning="";
      } finally {
        if(File.Exists(temp)) File.Delete(temp);
      }
    }
    private void PruneCatalogBackups() {
      try {
        string[] backups=Directory.GetFiles(dataRoot,"catalog.xml.*.bak");
        Array.Sort(backups,delegate(string a,string b) {
          return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
        });
        for(int i=5;i<backups.Length;i++) File.Delete(backups[i]);
      } catch { }
    }
    private static void SetAttr(XmlDocument d,XmlNode n,string name,string value) { var a=d.CreateAttribute(name);a.Value=value??"";n.Attributes.Append(a); }
    private static bool Supported(string path) {
      string ext=System.IO.Path.GetExtension(path).ToLowerInvariant();
      return ext==".ps1" || ext==".cmd" || ext==".bat" || ext==".py" || ext==".exe";
    }
    private static bool IsInterpreterPath(string path) {
      string name=System.IO.Path.GetFileName(path??"");
      return String.Equals(name,"powershell.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"pwsh.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"powershell_ise.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"cmd.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"py.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"python.exe",StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name,"pythonw.exe",StringComparison.OrdinalIgnoreCase);
    }
    private string EntryProblem(RegisteredScript e) {
      try {
        if(e==null) return "Запись не выбрана.";
        if(String.IsNullOrWhiteSpace(e.Path)) return "Не указан файл скрипта.";
        if(IsInterpreterPath(e.Path)) return "Выбран интерпретатор ("+System.IO.Path.GetFileName(e.Path)+"), а не скрипт. Выберите сам файл .ps1, .cmd, .bat или .py.";
        if(!System.IO.Path.IsPathRooted(e.Path)) return "Путь к файлу должен быть абсолютным.";
        if(!Supported(e.Path)) return "Тип файла не поддерживается.";
        if(!File.Exists(e.Path)) return "Файл не найден: "+e.Path;
        if(String.IsNullOrWhiteSpace(e.WorkingDirectory) || !Directory.Exists(e.WorkingDirectory)) return "Рабочая папка не найдена: "+e.WorkingDirectory;
        if(e.Mode!="hidden" && e.Mode!="window") return "Неизвестный режим запуска.";
        return null;
      } catch(Exception ex) { return "Некорректные параметры записи: "+ex.Message; }
    }
    private string StatusText(RegisteredScript e) {
      if(EntryProblem(e)!=null) return "Исправить";
      if(IsLive(e))return "Работает";
      ExternalRun other;
      if(externalRuns.TryGetValue(e.Id,out other))
        return other.Ambiguous?"Несколько внешних":"Работает (внешний)";
      return externalScanReady?"Остановлен":"Не проверен";
    }
    private string ModeText(RegisteredScript e) { return e.Mode=="hidden"?"В центре":"Своё окно"; }
    private RegisteredScript FindDuplicate(RegisteredScript candidate) {
      foreach(RegisteredScript e in entries) {
        if(e.Id==candidate.Id) continue;
        if(String.Equals(e.Path,candidate.Path,StringComparison.OrdinalIgnoreCase) &&
           String.Equals(e.Arguments??"",candidate.Arguments??"",StringComparison.Ordinal) &&
           String.Equals(e.WorkingDirectory??"",candidate.WorkingDirectory??"",StringComparison.OrdinalIgnoreCase))
          return e;
      }
      return null;
    }
    private void RefreshItems(string preferredId=null) {
      string selectedId=preferredId??(Current==null?null:Current.Id);
      loading=true; items.BeginUpdate(); items.Items.Clear();
      ListViewItem selected=null;
      foreach(RegisteredScript e in entries) {
        var row=new ListViewItem(String.IsNullOrWhiteSpace(e.Name)?"Без названия":e.Name);
        row.SubItems.Add(StatusText(e));
        row.SubItems.Add(ModeText(e));
        row.Tag=e;
        if(EntryProblem(e)!=null) row.ForeColor=Color.FromArgb(246,184,112);
        else if(IsLive(e)) row.ForeColor=Color.FromArgb(112,220,166);
        items.Items.Add(row);
        if(selectedId!=null && String.Equals(selectedId,e.Id,StringComparison.OrdinalIgnoreCase)) selected=row;
      }
      items.EndUpdate();
      items.Visible=entries.Count>0; emptyState.Visible=entries.Count==0;
      if(selected==null && items.Items.Count>0) selected=items.Items[0];
      if(selected!=null) { selected.Selected=true; selected.Focused=true; selected.EnsureVisible(); }
      loading=false;
      if(entries.Count==0) emptyState.BringToFront(); else items.BringToFront();
      footer.Text=String.IsNullOrEmpty(catalogWarning)?"Каталог: "+catalogPath:("ПРЕДУПРЕЖДЕНИЕ: "+catalogWarning);
      footer.ForeColor=String.IsNullOrEmpty(catalogWarning)?Color.FromArgb(142,164,192):Color.FromArgb(246,184,112);
      RefreshSelected();
    }
    private void RefreshStatuses() {
      if(loading)return;
      if(!externalScanRunning && (DateTime.UtcNow-lastExternalCheckUtc).TotalSeconds>=6)
        BeginExternalScan();
      foreach(ListViewItem row in items.Items) {
        RegisteredScript e=row.Tag as RegisteredScript;
        if(e==null) continue;
        string problem=EntryProblem(e);
        bool live=problem==null && IsLive(e);
        ExternalRun external=null;
        bool other=problem==null && !live && externalRuns.TryGetValue(e.Id,out external);
        string status=problem!=null?"Исправить":live?"Работает":
          other?(external.Ambiguous?"Несколько внешних":"Работает (внешний)"):
          externalScanReady?"Остановлен":"Не проверен";
        if(row.SubItems[1].Text!=status) row.SubItems[1].Text=status;
        string mode=ModeText(e);
        if(row.SubItems[2].Text!=mode) row.SubItems[2].Text=mode;
        Color color=problem!=null?Color.FromArgb(246,184,112):
          live?Color.FromArgb(112,220,166):
          other?Color.FromArgb(124,192,245):Color.FromArgb(232,240,250);
        if(row.ForeColor!=color) row.ForeColor=color;
      }
      RefreshSelected();
    }

    // Only direct "powershell.exe -File <absolute .ps1 path>" launches can
    // be associated safely. Interactive -Command, -EncodedCommand, batch,
    // Python and service-hosted scripts are intentionally not guessed.
    private static string DirectPowerShellFile(string commandLine) {
      if(String.IsNullOrEmpty(commandLine))return null;
      int argc=0;
      IntPtr ptr=IntPtr.Zero;
      try {
        ptr=CommandLineToArgvW(commandLine,out argc);
        if(ptr==IntPtr.Zero || argc<3 || argc>128)return null;
        var args=new string[argc];
        for(int i=0;i<argc;i++)
          args[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr,i*IntPtr.Size));
        for(int i=1;i<argc-1;i++) {
          if(!String.Equals(args[i],"-File",StringComparison.OrdinalIgnoreCase) &&
             !String.Equals(args[i],"/File",StringComparison.OrdinalIgnoreCase))continue;
          // Reject scripts passed after -Command or an encoded expression:
          // that PowerShell is a host, not safely owned by a single file.
          for(int j=1;j<i;j++)
            if(String.Equals(args[j],"-Command",StringComparison.OrdinalIgnoreCase) ||
               String.Equals(args[j],"-EncodedCommand",StringComparison.OrdinalIgnoreCase) ||
               String.Equals(args[j],"-c",StringComparison.OrdinalIgnoreCase))
              return null;
          string file=args[i+1];
          if(!Path.IsPathRooted(file) ||
             !String.Equals(Path.GetExtension(file),".ps1",StringComparison.OrdinalIgnoreCase))
            return null;
          return Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        }
      } catch(ArgumentException) { return null; }
      catch(NotSupportedException) { return null; }
      finally { if(ptr!=IntPtr.Zero) LocalFree(ptr); }
      return null;
    }

    private static Dictionary<string,ExternalRun> ScanExternalRuns(List<RegisteredScript> scripts) {
      var result=new Dictionary<string,ExternalRun>(StringComparer.OrdinalIgnoreCase);
      var pathToIds=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
      foreach(RegisteredScript entry in scripts) {
        if(!String.Equals(Path.GetExtension(entry.Path??""),".ps1",StringComparison.OrdinalIgnoreCase))
          continue;
        string path;
        try { path=Path.GetFullPath(entry.Path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar); }
        catch(ArgumentException) { continue; }
        catch(NotSupportedException) { continue; }
        List<string> ids;
        if(!pathToIds.TryGetValue(path,out ids)) { ids=new List<string>();pathToIds.Add(path,ids); }
        ids.Add(entry.Id);
      }
      if(pathToIds.Count==0)return result;
      int ownSession=Process.GetCurrentProcess().SessionId;
      string ownSid=WindowsIdentity.GetCurrent().User.Value;
      // GetOwnerSid needs the Win32_Process key property Handle in the
      // projected WMI object. Omitting it makes InvokeMethod throw
      // InvalidOperationException even though ProcessId/CommandLine are present.
      using(var search=new ManagementObjectSearcher(
        "SELECT Handle, ProcessId, Name, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'")) {
        foreach(ManagementObject item in search.Get()) {
          using(item) {
            object value=item["CommandLine"];
            string filename=DirectPowerShellFile(value==null?null:value.ToString());
            if(filename==null)continue;
            List<string> ids;
            if(!pathToIds.TryGetValue(filename,out ids))continue;
            int processId=Convert.ToInt32(item["ProcessId"]);
            if(processId==Process.GetCurrentProcess().Id)continue;
            // Owner lookup is a required security check. Keep it outside the
            // process-exit race handler so WMI failures invalidate the scan
            // instead of silently turning a running process into "stopped".
            using(ManagementBaseObject owner=item.InvokeMethod("GetOwnerSid",null,null)) {
              if(owner==null || owner["Sid"]==null || owner["ReturnValue"]==null ||
                 Convert.ToUInt32(owner["ReturnValue"])!=0)
                throw new UnauthorizedAccessException("Cannot verify external process owner");
              if(!String.Equals(Convert.ToString(owner["Sid"]),
                ownSid,StringComparison.OrdinalIgnoreCase))continue;
            }
            try {
              using(var proc=Process.GetProcessById(processId)) {
                if(proc.HasExited || proc.SessionId!=ownSession)continue;
                DateTime started=proc.StartTime;
                foreach(string id in ids) {
                  ExternalRun previous;
                  if(result.TryGetValue(id,out previous)) previous.Ambiguous=true;
                  else result.Add(id,new ExternalRun { Pid=processId, Started=started });
                }
              }
            } catch(ArgumentException) { }
            catch(InvalidOperationException) { }
            catch(System.ComponentModel.Win32Exception) {
              // If a matching process cannot be inspected, fail the entire
              // external check rather than falsely reporting "Остановлен".
              throw new UnauthorizedAccessException("Cannot verify external process");
            }
          }
        }
      }
      return result;
    }

    private void BeginExternalScan() {
      if(externalScanRunning)return;
      externalScanRunning=true;
      lastExternalCheckUtc=DateTime.UtcNow;
      var snapshot=new List<RegisteredScript>(entries);
      ThreadPool.QueueUserWorkItem(delegate {
        Dictionary<string,ExternalRun> found=null;
        try { found=ScanExternalRuns(snapshot); }
        catch(Exception) {
          // WMI can be unavailable or restricted; do not bring down the UI.
          // Never report "Остановлен" on an unknown external scan.
          found=null;
        }
        try {
          if(!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate {
            if(IsDisposed)return;
            externalScanRunning=false;
            externalScanReady=(found!=null);
            externalRuns.Clear();
            if(found!=null) foreach(var pair in found) externalRuns[pair.Key]=pair.Value;
            RefreshStatuses();
          });
        } catch(InvalidOperationException) { } // Also covers ObjectDisposedException.
      });
    }

    private static bool SameExternal(ExternalRun previous,ExternalRun current) {
      return previous!=null && current!=null && !previous.Ambiguous && !current.Ambiguous &&
        previous.Pid==current.Pid &&
        Math.Abs((previous.Started-current.Started).TotalSeconds)<1;
    }

    private void AddEntry() {
      if(entries.Count>=200) { MessageBox.Show("Достигнут лимит 200 записей.");return; }
      using(var d=new OpenFileDialog {
        Title="Выберите сам файл скрипта — не powershell.exe",
        Filter="PowerShell-скрипты (*.ps1)|*.ps1|Batch-файлы (*.cmd;*.bat)|*.cmd;*.bat|Python-скрипты (*.py)|*.py|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
        FilterIndex=1, CheckFileExists=true, Multiselect=false
      }) {
        if(d.ShowDialog(this)!=DialogResult.OK) return;
        if(IsInterpreterPath(d.FileName)) {
          MessageBox.Show("Это интерпретатор, а не подключаемый скрипт.\n\nВыберите Repo1-Worker.ps1 или Repo2-Worker.ps1 (либо другой нужный .ps1).",
            "Выбран не тот файл",MessageBoxButtons.OK,MessageBoxIcon.Warning);
          return;
        }
        if(!Supported(d.FileName)) { MessageBox.Show("Этот тип файла не поддержан.");return; }
        var e=new RegisteredScript { Id=Guid.NewGuid().ToString("D"), Name=System.IO.Path.GetFileNameWithoutExtension(d.FileName),
          Path=System.IO.Path.GetFullPath(d.FileName), Arguments="", WorkingDirectory=System.IO.Path.GetDirectoryName(d.FileName), Mode="hidden" };
        if(!EditDialog(e)) return;
        RegisteredScript duplicate=FindDuplicate(e);
        if(duplicate!=null) {
          MessageBox.Show("Этот файл с такими же аргументами и рабочей папкой уже зарегистрирован как «"+duplicate.Name+"».","Дубликат",MessageBoxButtons.OK,MessageBoxIcon.Information);
          return;
        }
        try { entries.Add(e);SaveEntries();RefreshItems(e.Id); }
        catch(Exception ex) {
          entries.Remove(e);
          MessageBox.Show("Не удалось сохранить регистрацию в каталог:\n"+catalogPath+"\n\n"+ex.Message,
            "Ошибка сохранения",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
      }
    }
    private string ResolveImportedPath(string raw) {
      string value=Environment.ExpandEnvironmentVariables((raw??"").Trim().Trim('"'));
      if(String.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Не указан путь.");
      string relativePrefix="@app";
      if(String.Equals(value,relativePrefix,StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("После @app укажите имя файла.");
      if(value.StartsWith("@app\\",StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("@app/",StringComparison.OrdinalIgnoreCase))
        value=System.IO.Path.Combine(launcherRoot,value.Substring(5));
      else if(!System.IO.Path.IsPathRooted(value))
        value=System.IO.Path.Combine(launcherRoot,value);
      return System.IO.Path.GetFullPath(value);
    }
    private string ImportBundleIdForPath(string file) {
      string name=System.IO.Path.GetFileName(file);
      if(!String.Equals(System.IO.Path.GetDirectoryName(file),launcherRoot,StringComparison.OrdinalIgnoreCase)) return "";
      if(String.Equals(name,"Repo2-Worker.ps1",StringComparison.OrdinalIgnoreCase)) return "fortis-rdg.repo2";
      if(String.Equals(name,"Repo1-Worker.ps1",StringComparison.OrdinalIgnoreCase)) return "fortis-rdg.repo1";
      if(String.Equals(name,"Updater-Worker.ps1",StringComparison.OrdinalIgnoreCase)) return "fortis-rdg.updater";
      if(String.Equals(name,"Watcher-Start.ps1",StringComparison.OrdinalIgnoreCase)) return "fortis-rdg.watcher.start";
      if(String.Equals(name,"Watcher-Stop.ps1",StringComparison.OrdinalIgnoreCase)) return "fortis-rdg.watcher.stop";
      return "";
    }
    private bool ParseTextManifest(string input,out List<RegisteredScript> additions,out string summary) {
      additions=new List<RegisteredScript>();
      var report=new StringBuilder();
      int problems=0,existing=0,records=0;
      string[] lines=(input??"").Replace("\r","").Split('\n');
      foreach(string rawLine in lines) {
        string line=rawLine.Trim();
        if(line.Length==0 || line.StartsWith("#")) continue;
        records++;
        if(records>50) {
          report.AppendLine("ОШИБКА: максимум 50 записей за один импорт.");
          problems++;
          break;
        }
        string[] fields=line.Split('|');
        if(fields.Length<1 || fields.Length>4) {
          report.AppendLine("Строка "+records+": ОШИБКА — формат: Имя | Путь | Режим | Аргументы");
          problems++;
          continue;
        }
        string rawPath=fields.Length==1?fields[0].Trim():fields[1].Trim();
        string name=fields.Length==1?"":fields[0].Trim();
        string mode=fields.Length>=3?fields[2].Trim().ToLowerInvariant():"hidden";
        string arguments=fields.Length==4?fields[3].Trim():"";
        if(mode=="" || mode=="внутри" || mode=="inside" || mode=="скрытый") mode="hidden";
        if(mode=="окно" || mode=="своё окно") mode="window";
        try {
          string path=ResolveImportedPath(rawPath);
          if(name.Length==0) name=System.IO.Path.GetFileNameWithoutExtension(path);
          if(String.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Пустое название.");
          if(mode!="hidden" && mode!="window") throw new InvalidDataException("Режим: только hidden/внутри или window/окно.");
          if(IsInterpreterPath(path)) throw new InvalidDataException("Указан интерпретатор, а не скрипт.");
          if(!Supported(path)) throw new InvalidDataException("Неподдерживаемое расширение: "+System.IO.Path.GetExtension(path));
          if(!File.Exists(path)) throw new FileNotFoundException("Файл не найден",path);
          string directory=System.IO.Path.GetDirectoryName(path);
          if(String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) throw new InvalidDataException("Рабочая папка не существует.");
          string bundleId=ImportBundleIdForPath(path);
          var entry=new RegisteredScript {
            Id=Guid.NewGuid().ToString("D"),BundleId=bundleId,Name=name,Path=path,
            Arguments=arguments,WorkingDirectory=directory,Mode=mode
          };
          RegisteredScript duplicate=FindDuplicate(entry);
          if(duplicate==null && bundleId.Length>0) duplicate=FindBundleEntry(bundleId,path);
          if(duplicate!=null) {
            existing++;
            report.AppendLine("УЖЕ ЕСТЬ: "+name+" — "+duplicate.Path);
            continue;
          }
          bool alreadyPlanned=false;
          foreach(RegisteredScript prev in additions) {
            if(String.Equals(prev.Path,entry.Path,StringComparison.OrdinalIgnoreCase) &&
               String.Equals(prev.Arguments,entry.Arguments,StringComparison.Ordinal) &&
               String.Equals(prev.WorkingDirectory,entry.WorkingDirectory,StringComparison.OrdinalIgnoreCase)) {
              alreadyPlanned=true; break;
            }
          }
          if(alreadyPlanned) {
            existing++;
            report.AppendLine("ПОВТОР В ТЕКСТЕ: "+name+" — "+path);
            continue;
          }
          additions.Add(entry);
          report.AppendLine("ДОБАВИТЬ: "+name+"  ["+mode+"]");
          report.AppendLine("    "+path);
          if(bundleId.Length>0) {
            report.Append(BundleDependencyReport(new BundleDefinition(bundleId,name,System.IO.Path.GetFileName(path))));
          }
        } catch(Exception ex) {
          problems++;
          report.AppendLine("СТРОКА "+records+": ОШИБКА — "+ex.Message);
          report.AppendLine("    "+rawPath);
        }
      }
      if(records==0) { report.AppendLine("Вставьте текст с путями к скриптам.");problems++; }
      if(entries.Count+additions.Count>200) { report.AppendLine("ОШИБКА: лимит каталога — 200 записей.");problems++; }
      report.AppendLine();
      report.AppendLine("Новых: "+additions.Count+"  |  Уже есть/повторы: "+existing+"  |  Ошибок: "+problems);
      if(entries.Exists(delegate(RegisteredScript e) { return IsInterpreterPath(e.Path); }))
        report.AppendLine("ПРИМЕЧАНИЕ: старые записи powershell.exe останутся как «Исправить»; этот импорт их не меняет.");
      if(problems>0) report.AppendLine("Импорт заблокирован до исправления всех ошибок. Частичное сохранение не выполняется.");
      else report.AppendLine("Проверка пройдена. Скрипты НЕ запускаются; добавятся только записи.");
      summary=report.ToString();
      return problems==0 && additions.Count>0;
    }
    private void ImportFromText() {
      using(var dialog=new Form {
        Text="Импорт скриптов из текста",Size=new Size(1060,810),
        MinimumSize=new Size(820,670),StartPosition=FormStartPosition.CenterParent,
        BackColor=Color.FromArgb(238,242,247),Font=new Font("Segoe UI",9.5f)
      }) {
        var instructions=new Label {
          Left=18,Top=12,Width=1000,Height=69,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,
          Text="Вставьте по одной записи на строку: Название | Путь | Режим | Аргументы (без секретов, необязательно)\n"+
               "Пример: Fortis Repo 2 | @app\\Repo2-Worker.ps1 | hidden\n"+
               "@app — папка этой версии Control Center; работают также полные пути и %LOCALAPPDATA%. Строки с # — комментарии.",
          ForeColor=Color.FromArgb(44,60,82)
        };
        var inputBox=new TextBox {
          Left=18,Top=85,Width=1004,Height=300,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,
          Multiline=true,AcceptsReturn=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,
          Font=new Font("Consolas",10),BackColor=Color.White
        };
        var statusLabel=new Label {
          Left=18,Top=397,Width=1000,Height=24,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,
          Text="ПРЕДВАРИТЕЛЬНАЯ ПРОВЕРКА (без запуска скриптов)",ForeColor=Color.FromArgb(44,60,82)
        };
        var preview=new TextBox {
          Left=18,Top=426,Width=1004,Height=237,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,
          Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,
          Font=new Font("Consolas",9),BackColor=Color.FromArgb(248,250,253),
          Text="Вставьте текст и нажмите «Проверить»."
        };
        var check=new Button {
          Text="Проверить",Left=658,Top=686,Width=118,Height=36,
          Anchor=AnchorStyles.Right|AnchorStyles.Bottom
        };
        var apply=new Button {
          Text="Импортировать",Left=785,Top=686,Width=128,Height=36,Enabled=false,
          Anchor=AnchorStyles.Right|AnchorStyles.Bottom
        };
        var cancel=new Button {
          Text="Отмена",Left=921,Top=686,Width=101,Height=36,
          Anchor=AnchorStyles.Right|AnchorStyles.Bottom,DialogResult=DialogResult.Cancel
        };
        List<RegisteredScript> approved=new List<RegisteredScript>();
        inputBox.TextChanged += delegate {
          apply.Enabled=false;
          approved.Clear();
          preview.Text="Текст изменён. Нажмите «Проверить».";
        };
        check.Click += delegate {
          string report;
          List<RegisteredScript> planned;
          bool valid=ParseTextManifest(inputBox.Text,out planned,out report);
          preview.Text=report;
          approved=planned;
          apply.Enabled=valid;
        };
        apply.Click += delegate {
          if(!apply.Enabled || approved.Count==0) return;
          var added=new List<RegisteredScript>();
          try {
            if(entries.Count+approved.Count>200) throw new InvalidDataException("Достигнут лимит каталога.");
            foreach(RegisteredScript e in approved) {
              if(!File.Exists(e.Path)) throw new FileNotFoundException("Файл исчез до сохранения.",e.Path);
              if(FindDuplicate(e)!=null) throw new InvalidDataException("Запись уже существует: "+e.Name);
              if(e.BundleId!="" && FindBundleEntry(e.BundleId,e.Path)!=null) throw new InvalidDataException("Подключение уже зарегистрировано: "+e.Name);
              entries.Add(e);added.Add(e);
            }
            SaveEntries();
            RefreshItems(added[0].Id);
            MessageBox.Show(dialog,"Добавлено записей: "+added.Count+"\n\nНичего не запускалось.","Импорт завершён",MessageBoxButtons.OK,MessageBoxIcon.Information);
            dialog.DialogResult=DialogResult.OK;
            dialog.Close();
          } catch(Exception ex) {
            foreach(RegisteredScript e in added) entries.Remove(e);
            MessageBox.Show(dialog,"Записи не импортированы.\n\n"+ex.Message,"Ошибка импорта",MessageBoxButtons.OK,MessageBoxIcon.Error);
            apply.Enabled=false;
          }
        };
        dialog.Controls.Add(instructions);dialog.Controls.Add(inputBox);
        dialog.Controls.Add(statusLabel);dialog.Controls.Add(preview);
        dialog.Controls.Add(check);dialog.Controls.Add(apply);dialog.Controls.Add(cancel);
        dialog.CancelButton=cancel;
        dialog.ShowDialog(this);
      }
    }
    private RegisteredScript FindBundleEntry(string bundleId,string fullPath) {
      foreach(RegisteredScript e in entries) {
        if(!String.IsNullOrWhiteSpace(e.BundleId) && String.Equals(e.BundleId,bundleId,StringComparison.OrdinalIgnoreCase)) return e;
        try {
          if(String.Equals(System.IO.Path.GetFullPath(e.Path??""),fullPath,StringComparison.OrdinalIgnoreCase)) return e;
        } catch { }
      }
      return null;
    }
    private string BundleDependencyReport(BundleDefinition item) {
      string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      string user=Environment.GetEnvironmentVariable("USERPROFILE")??"";
      string tunnelCurrent=System.IO.Path.Combine(user,"Repos","tunnel","runtime","windows-repo","current");
      var required=new List<string>();
      required.Add(System.IO.Path.Combine(launcherRoot,"Get-TunnelWindowsMirror.ps1"));
      required.Add(System.IO.Path.Combine(tunnelCurrent,"source-manifest.json"));
      string runtime=System.IO.Path.Combine(@"C:\Tools","tunnel-client","runtime","v0.0.15","tunnel-client-runtime.exe");
      string client=System.IO.Path.Combine(@"C:\Tools","tunnel-client","tunnel-client.exe");
      if(item.Id=="fortis-rdg.repo2") {
        required.Add(System.IO.Path.Combine(tunnelCurrent,"Repo2-Worker.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRDGLauncherV2","Start-Fortis.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRDGLauncherV2","Common.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRepoPlus2","Repo2DirectGuard.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRepoPlus2","FortisRepo2-quiet-logs.ps1"));
        required.Add(client);
        required.Add(runtime);
        required.Add(System.IO.Path.Combine(local,"FortisRepoPlus2","secrets","runtime.key"));
      } else if(item.Id=="fortis-rdg.repo1") {
        required.Add(System.IO.Path.Combine(tunnelCurrent,"Repo1-Worker.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","chatgpt-repo-mode","run-tunnel-quiet-logs.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","chatgpt-repo-mode","control.ps1"));
        required.Add(runtime);
        required.Add(System.IO.Path.Combine(user,"chatgpt-repo-mode","workspace-config.json"));
      } else if(item.Id=="fortis-rdg.updater") {
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRDGLauncherV2","Start-Fortis.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRDGLauncherV2","Common.ps1"));
        required.Add(client);
        required.Add(runtime);
        required.Add(System.IO.Path.Combine(local,"RDG-Server","cicd-bootstrap-ci-20261029t051843803595-4546c7d69e","files","RDG-CICD-Bootstrap","Update-RDG.ps1"));
      } else {
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRDGLauncherV2","Common.ps1"));
        required.Add(System.IO.Path.Combine(tunnelCurrent,"launchers","FortisRepoPlus2","Repo2DirectGuard.ps1"));
        required.Add(client);
        required.Add(runtime);
      }
      var report=new StringBuilder();
      foreach(string path in required) report.AppendLine((File.Exists(path)||Directory.Exists(path)?"  OK  ":"  НЕТ  ")+path);
      if(item.Id=="fortis-rdg.repo2") {
        string profileDir=System.IO.Path.Combine(local,"FortisRepoPlus2","profiles");
        int profileCount=(File.Exists(System.IO.Path.Combine(profileDir,"repo-mode-plus2.yaml"))?1:0)+
          (File.Exists(System.IO.Path.Combine(profileDir,"repo-mode-plus2.yml"))?1:0);
        report.AppendLine((profileCount==1?"  OK  ":"  НЕТ  ")+"Exactly one repo-mode-plus2.yaml/.yml profile in "+profileDir);
      }
      report.AppendLine("  Источник проверяется при запуске: clean main + manifest SHA-256 + восемь файлов.");
      if(item.Id=="fortis-rdg.updater") report.AppendLine("  Docker, контейнер fortis-repo-test и RDG healthz проверяются только при запуске.");
      if(item.Id=="fortis-rdg.watcher.start" || item.Id=="fortis-rdg.watcher.stop") report.AppendLine("  Docker и контейнер fortis-repo-test проверяются только при запуске.");
      return report.ToString();
    }
    private void ImportBuiltInBundle() {
      var definitions=new BundleDefinition[] {
        new BundleDefinition("fortis-rdg.repo2","Fortis Repo 2","Repo2-Worker.ps1"),
        new BundleDefinition("fortis-rdg.repo1","Fortis Repo 1","Repo1-Worker.ps1"),
        new BundleDefinition("fortis-rdg.updater","RDG Updater","Updater-Worker.ps1"),
        new BundleDefinition("fortis-rdg.watcher.start","CI Watcher — запуск","Watcher-Start.ps1"),
        new BundleDefinition("fortis-rdg.watcher.stop","CI Watcher — остановка","Watcher-Stop.ps1")
      };
      var available=new List<BundleDefinition>();
      var missing=new List<string>();
      var already=new List<string>();
      var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
      var report=new StringBuilder();
      report.AppendLine("Стандартный набор Fortis / RDG");
      report.AppendLine("Папка приложения: "+launcherRoot);
      report.AppendLine();
      foreach(BundleDefinition item in definitions) {
        string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(launcherRoot,item.FileName));
        paths[item.Id]=path;
        if(!File.Exists(path)) {
          missing.Add(item.FileName);
          report.AppendLine("НЕТ ФАЙЛА  "+item.Name+" — "+path);
          continue;
        }
        RegisteredScript existing=FindBundleEntry(item.Id,path);
        if(existing!=null) {
          already.Add(item.Name);
          report.AppendLine("УЖЕ ЕСТЬ  "+item.Name+" — "+existing.Path);
          continue;
        }
        available.Add(item);
        report.AppendLine("БУДЕТ ДОБАВЛЕНО  "+item.Name+" — "+path);
        report.Append(BundleDependencyReport(item));
      }
      if(available.Count==0) {
        report.AppendLine();
        report.AppendLine("Новых записей для импорта нет.");
        if(missing.Count>0) report.AppendLine("Скопируйте отсутствующие worker-файлы рядом с Control Center и повторите импорт.");
        MessageBox.Show(this,report.ToString(),"Импорт набора Fortis / RDG",MessageBoxButtons.OK,missing.Count>0?MessageBoxIcon.Warning:MessageBoxIcon.Information);
        return;
      }
      if(entries.Count+available.Count>200) {
        MessageBox.Show(this,"Импорт отменён: лимит каталога — 200 записей.","Импорт набора",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        return;
      }
      report.AppendLine();
      report.AppendLine("Будет добавлено: "+available.Count+". Уже зарегистрировано: "+already.Count+". Не найдено файлов: "+missing.Count+".");
      report.AppendLine("Проверка внешних файлов выше — это только предварительная проверка. Docker и работающие сессии не запускаются и не изменяются.");
      bool hasOldInterpreter=entries.Exists(delegate(RegisteredScript e) { return IsInterpreterPath(e.Path); });
      if(hasOldInterpreter) {
        report.AppendLine("ВНИМАНИЕ: старые записи powershell.exe / других интерпретаторов останутся в списке со статусом «Исправить». Импорт их не удаляет и не запускает.");
      }
      report.AppendLine();
      report.AppendLine("Импортировать найденные записи? Ничего запускаться не будет.");
      if(MessageBox.Show(this,report.ToString(),"Импорт набора Fortis / RDG",MessageBoxButtons.YesNo,MessageBoxIcon.Information,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
      var added=new List<RegisteredScript>();
      try {
        foreach(BundleDefinition item in available) {
          string path=paths[item.Id];
          if(FindBundleEntry(item.Id,path)!=null) continue;
          var e=new RegisteredScript {
            Id=Guid.NewGuid().ToString("D"),BundleId=item.Id,Name=item.Name,Path=path,
            Arguments="",WorkingDirectory=launcherRoot,Mode="hidden"
          };
          RegisteredScript duplicate=FindDuplicate(e);
          if(duplicate!=null) {
            duplicate.BundleId=item.Id;
            already.Add(item.Name);
            continue;
          }
          entries.Add(e);added.Add(e);
        }
        if(added.Count>0) SaveEntries();
        RefreshItems(added.Count>0?added[0].Id:null);
      } catch(Exception ex) {
        foreach(RegisteredScript e in added) entries.Remove(e);
        MessageBox.Show(this,"Не удалось сохранить набор. Новые записи откатились.\n\n"+ex.Message,"Ошибка импорта",MessageBoxButtons.OK,MessageBoxIcon.Error);
        RefreshItems();
        return;
      }
      var done=new StringBuilder();
      done.AppendLine("Импорт завершён.");
      done.AppendLine("Добавлено: "+added.Count);
      done.AppendLine("Уже было: "+already.Count);
      done.AppendLine("Не найдено: "+missing.Count);
      foreach(RegisteredScript e in added) done.AppendLine("  + "+e.Name);
      foreach(string name in missing) done.AppendLine("  ! "+name);
      done.AppendLine();
      done.AppendLine("Все записи добавлены без запуска. Выбери нужную запись и нажми «Запустить».");
      MessageBox.Show(this,done.ToString(),"Импорт набора Fortis / RDG",MessageBoxButtons.OK,missing.Count>0?MessageBoxIcon.Warning:MessageBoxIcon.Information);
    }
    private bool EditDialog(RegisteredScript e) {
      using(var f=new Form { Text="Настройки подключённого скрипта", Size=new Size(820,520), MinimumSize=new Size(820,520), StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, BackColor=Color.FromArgb(242,245,249), Font=new Font("Segoe UI",9.5f) }) {
        var nameBox=new TextBox { Left=22,Top=43,Width=755,Text=e.Name??"" };
        var pathBox=new TextBox { Left=22,Top=112,Width=650,Text=e.Path??"",ReadOnly=true,BackColor=Color.FromArgb(231,236,243) };
        var browseFile=new Button { Text="Обзор…",Left=682,Top=110,Width=95,Height=28 };
        var argsBox=new TextBox { Left=22,Top=190,Width=755,Text=e.Arguments??"" };
        var cwdBox=new TextBox { Left=22,Top=259,Width=650,Text=e.WorkingDirectory??"",ReadOnly=true,BackColor=Color.FromArgb(231,236,243) };
        var browseFolder=new Button { Text="Обзор…",Left=682,Top=257,Width=95,Height=28 };
        var mode=new ComboBox { Left=22,Top=337,Width=365,DropDownStyle=ComboBoxStyle.DropDownList };
        mode.Items.Add("Внутри центра — скрытый запуск и сбор stdout/stderr");
        mode.Items.Add("Своё окно — отдельная консоль, без захвата вывода");
        mode.SelectedIndex=e.Mode=="window"?1:0;
        f.Controls.Add(new Label { Text="НАЗВАНИЕ В СПИСКЕ",Left=22,Top=18,Width=400,ForeColor=Color.FromArgb(55,70,91) });
        f.Controls.Add(nameBox);
        f.Controls.Add(new Label { Text="ФАЙЛ СКРИПТА (выбирайте .ps1, а не powershell.exe)",Left=22,Top=87,Width=700,ForeColor=Color.FromArgb(55,70,91) });
        f.Controls.Add(pathBox); f.Controls.Add(browseFile);
        f.Controls.Add(new Label { Text="АРГУМЕНТЫ (не указывайте пароли и токены)",Left=22,Top=165,Width=700,ForeColor=Color.FromArgb(55,70,91) });
        f.Controls.Add(argsBox);
        f.Controls.Add(new Label { Text="РАБОЧАЯ ПАПКА",Left=22,Top=234,Width=400,ForeColor=Color.FromArgb(55,70,91) });
        f.Controls.Add(cwdBox); f.Controls.Add(browseFolder);
        f.Controls.Add(new Label { Text="СПОСОБ ЗАПУСКА",Left=22,Top=312,Width=400,ForeColor=Color.FromArgb(55,70,91) });
        f.Controls.Add(mode);
        var help=new Label { Left=22,Top=378,Width=755,Height=42,ForeColor=Color.FromArgb(85,101,123),Text="«Внутри центра» показывает текстовый вывод скрипта в журнале. Для программ с интерактивным окном или вводом используйте «Своё окно»." };
        f.Controls.Add(help);
        bool nameManual=false,cwdManual=false,syncing=false;
        nameBox.TextChanged += delegate { if(!syncing) nameManual=true; };
        cwdBox.TextChanged += delegate { if(!syncing) cwdManual=true; };
        browseFile.Click += delegate {
          using(var d=new OpenFileDialog { Title="Выберите сам файл скрипта",Filter="PowerShell (*.ps1)|*.ps1|Batch (*.cmd;*.bat)|*.cmd;*.bat|Python (*.py)|*.py|Executable (*.exe)|*.exe|All files (*.*)|*.*",FilterIndex=1,CheckFileExists=true }) {
            if(d.ShowDialog(f)!=DialogResult.OK) return;
            if(IsInterpreterPath(d.FileName)) {
              MessageBox.Show(f,"Выбран интерпретатор, а не скрипт. Выберите файл .ps1 (например Repo1-Worker.ps1).","Неверный файл",MessageBoxButtons.OK,MessageBoxIcon.Warning);
              return;
            }
            syncing=true;
            pathBox.Text=System.IO.Path.GetFullPath(d.FileName);
            if(!nameManual) nameBox.Text=System.IO.Path.GetFileNameWithoutExtension(d.FileName);
            if(!cwdManual) cwdBox.Text=System.IO.Path.GetDirectoryName(d.FileName)??"";
            syncing=false;
          }
        };
        browseFolder.Click += delegate {
          using(var d=new FolderBrowserDialog { Description="Выберите рабочую папку скрипта",ShowNewFolderButton=false }) {
            if(Directory.Exists(cwdBox.Text)) d.SelectedPath=cwdBox.Text;
            if(d.ShowDialog(f)==DialogResult.OK) cwdBox.Text=d.SelectedPath;
          }
        };
        var ok=new Button { Text="Сохранить",Left=572,Top=444,Width=100,Height=32,BackColor=Color.FromArgb(42,91,155),ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
        var cancel=new Button { Text="Отмена",Left=682,Top=444,Width=95,Height=32,DialogResult=DialogResult.Cancel };
        ok.Click += delegate {
          string selectedPath=pathBox.Text.Trim().Trim('"');
          if(String.IsNullOrWhiteSpace(nameBox.Text)) { MessageBox.Show(f,"Укажите название скрипта.","Проверка",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
          if(IsInterpreterPath(selectedPath)) { MessageBox.Show(f,"Выбран powershell.exe или другой интерпретатор. Нужен сам файл скрипта (.ps1).","Проверка",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
          if(!System.IO.Path.IsPathRooted(selectedPath) || !Supported(selectedPath) || !File.Exists(selectedPath)) { MessageBox.Show(f,"Выберите существующий файл поддерживаемого типа.","Проверка",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
          if(!Directory.Exists(cwdBox.Text)) { MessageBox.Show(f,"Выберите существующую рабочую папку.","Проверка",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
          e.Name=nameBox.Text.Trim();e.Path=System.IO.Path.GetFullPath(selectedPath);e.Arguments=argsBox.Text;
          e.WorkingDirectory=System.IO.Path.GetFullPath(cwdBox.Text);e.Mode=mode.SelectedIndex==1?"window":"hidden";
          f.DialogResult=DialogResult.OK;f.Close();
        };
        f.Controls.Add(ok);f.Controls.Add(cancel);f.AcceptButton=ok;f.CancelButton=cancel;
        return f.ShowDialog(this)==DialogResult.OK;
      }
    }
    private void EditEntry() {
      var cur=Current;if(cur==null)return;
      if(IsLive(cur) || externalRuns.ContainsKey(cur.Id)) { MessageBox.Show(this,"Сначала остановите скрипт. Нельзя менять путь, режим или аргументы во время выполнения: это нарушит контроль запущенного процесса.","Скрипт выполняется",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
      var copy=new RegisteredScript { Id=cur.Id,BundleId=cur.BundleId,Name=cur.Name,Path=cur.Path,Arguments=cur.Arguments,WorkingDirectory=cur.WorkingDirectory,Mode=cur.Mode };
      if(!EditDialog(copy))return;
      RegisteredScript duplicate=FindDuplicate(copy);
      if(duplicate!=null) { MessageBox.Show("Такая же запись уже существует: "+duplicate.Name);return; }
      int index=entries.IndexOf(cur);if(index<0)return;entries[index]=copy;
      try { SaveEntries();RefreshItems(copy.Id); }catch(Exception ex){entries[index]=cur;MessageBox.Show(ex.Message);}
    }
    private void DeleteEntry() {
      var e=Current;if(e==null)return;
      if(IsLive(e) || externalRuns.ContainsKey(e.Id)) { MessageBox.Show(this,"Сначала остановите скрипт. Удаление работающей записи скроет её из центра и лишит возможности управлять запуском.","Скрипт выполняется",MessageBoxButtons.OK,MessageBoxIcon.Warning);return; }
      if(MessageBox.Show(this,"Удалить регистрацию «"+e.Name+"»? Сам файл скрипта останется на месте.","Удаление",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
      int i=entries.IndexOf(e);if(i<0)return;entries.RemoveAt(i);
      try { SaveEntries();RefreshItems(); }catch(Exception ex){entries.Insert(i,e);MessageBox.Show(ex.Message);}
    }
    private static string Quote(string x) { return "\""+x.Replace("\"","\\\"")+"\""; }
    private static string Interpreter(RegisteredScript e,out string args) {
      string ext=System.IO.Path.GetExtension(e.Path).ToLowerInvariant();
      if(ext==".ps1") { args="-NoLogo -NoProfile -ExecutionPolicy Bypass -File "+Quote(e.Path)+" "+e.Arguments;return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"); }
      if(ext==".bat" || ext==".cmd") { args="/d /s /c \"\""+e.Path+"\""+(string.IsNullOrWhiteSpace(e.Arguments)?"":" "+e.Arguments)+"\"";return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"); }
      if(ext==".py") { args=Quote(e.Path)+" "+e.Arguments;return "py.exe"; }
      args=e.Arguments;return e.Path;
    }
    private string LogPath(RegisteredScript e) { return System.IO.Path.Combine(dataRoot,"logs",e.Id+".log"); }
    private string StatePath(RegisteredScript e) { return System.IO.Path.Combine(dataRoot,"runs",e.Id+".xml"); }
    private bool IsLive(RegisteredScript e) {
      int oldPid;DateTime stamp;
      if(!pid.TryGetValue(e.Id,out oldPid) || !launched.TryGetValue(e.Id,out stamp)) {
        string path=StatePath(e);if(!File.Exists(path))return false;
        try {
          var xml=new XmlDocument { XmlResolver=null };xml.Load(path);var n=xml.DocumentElement;
          oldPid=int.Parse(n.GetAttribute("pid"));stamp=DateTime.FromBinary(long.Parse(n.GetAttribute("start")));
          pid[e.Id]=oldPid;launched[e.Id]=stamp;
        }catch{return false;}
      }
      try { using(var p=Process.GetProcessById(oldPid))return !p.HasExited && Math.Abs((p.StartTime-stamp).TotalSeconds)<3; }
      catch{return false;}
    }
    private static void KillProcessTree(int processId) {
      string taskkill=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe");
      using(var killer=new Process()) {
        killer.StartInfo.FileName=taskkill;
        killer.StartInfo.Arguments="/PID "+processId+" /T /F";
        killer.StartInfo.UseShellExecute=false;
        killer.StartInfo.CreateNoWindow=true;
        if(!killer.Start()) throw new InvalidOperationException("Не удалось запустить taskkill.");
        if(!killer.WaitForExit(12000)) {
          try { killer.Kill(); } catch { }
          throw new TimeoutException("Превышено время ожидания остановки дерева процессов.");
        }
        if(killer.ExitCode!=0) throw new InvalidOperationException("taskkill завершился с кодом "+killer.ExitCode+".");
      }
    }
    private void StartEntry() { StartEntry(null,false,null); }
    private void StartEntry(RegisteredScript selected,bool fromProfile,List<string> profileReport) {
      var e=selected??Current;if(e==null)return;
      string problem=EntryProblem(e);
      if(problem!=null) {
        if(fromProfile)profileReport.Add("ОШИБКА: "+e.Name+" — "+problem);
        else MessageBox.Show(this,"Эту запись нельзя запустить.\n\n"+problem+"\n\nИсправьте её через кнопку «Изменить».","Неверная настройка",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        return;
      }
      if(IsLive(e)) { if(fromProfile)profileReport.Add("ПРОПУЩЕН (работает): "+e.Name);else MessageBox.Show("Этот процесс уже запущен. Повторный запуск отменён.");return; }
      // Never start a second copy when the same script was launched manually.
      // Re-check synchronously: the last background WMI snapshot may be stale.
      try {
        var matches=ScanExternalRuns(new List<RegisteredScript>{e});
        ExternalRun found;
        if(matches.TryGetValue(e.Id,out found)) {
          if(fromProfile)profileReport.Add("ПРОПУЩЕН (внешний запуск): "+e.Name+(found.Ambiguous?" — несколько процессов":" — PID "+found.Pid));
          else MessageBox.Show(this, found.Ambiguous?
            "Найдено несколько внешних запусков. Новый запуск отменён.":
            "Скрипт уже запущен вне центра (PID "+found.Pid+"). Новый запуск отменён.",
            "Внешний процесс",MessageBoxButtons.OK,MessageBoxIcon.Information);
          return;
        }
      } catch(Exception ex) {
        if(fromProfile)profileReport.Add("ОШИБКА: "+e.Name+" — проверка внешних процессов: "+ex.Message);
        else MessageBox.Show(this,"Не удалось проверить внешние запуски. Запуск отменён: "+ex.Message,
          "Проверка процесса",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        return;
      }
      if(!fromProfile && MessageBox.Show(this,"Запустить «"+e.Name+"»?\n"+e.Path+"\n\nСкрипт будет выполняться с вашими правами Windows.","Подтверждение",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
      try {
        string args,exe=Interpreter(e,out args);
        Directory.CreateDirectory(System.IO.Path.Combine(dataRoot,"logs"));
        Directory.CreateDirectory(System.IO.Path.Combine(dataRoot,"runs"));
        string runToken="";
        var psi=new ProcessStartInfo { UseShellExecute=false,FileName=exe,Arguments=args,WorkingDirectory=e.WorkingDirectory,CreateNoWindow=false };
        if(e.Mode=="hidden") {
          string token=Guid.NewGuid().ToString("D"); runToken=token;
          string request=System.IO.Path.Combine(dataRoot,"runs",e.Id+"-"+token+".xml");
          var doc=new XmlDocument();var root=doc.CreateElement("RunSpec");doc.AppendChild(root);
          foreach(var pair in new[]{new[]{"Token",token},new[]{"Executable",exe},new[]{"Arguments",args},new[]{"WorkingDirectory",e.WorkingDirectory},new[]{"LogPath",LogPath(e)},new[]{"StopFile",System.IO.Path.Combine(dataRoot,"runs",e.Id+"-"+token+".stop")}}) {
            var n=doc.CreateElement(pair[0]);n.InnerText=pair[1];root.AppendChild(n);
          }
          doc.Save(request);
          psi.FileName=shell;psi.Arguments="-NoLogo -NoProfile -ExecutionPolicy Bypass -File "+Quote(System.IO.Path.Combine(launcherRoot,"Script-Runner.ps1"))+" -RunFile "+Quote(request)+" -Token "+Quote(token);
          psi.CreateNoWindow=true;
        }
        if(e.Mode=="window") {
          // The catalog can have no visible console of its own. A normal
          // child process inherits that hidden console, breaking Read-Host.
          // ShellExecute opens an independent interactive console for -File.
          psi.UseShellExecute=true;
          psi.WindowStyle=ProcessWindowStyle.Normal;
          psi.CreateNoWindow=false;
        }
        var p=Process.Start(psi);if(p==null)throw new InvalidOperationException("Process.Start failed");
        owned[e.Id]=p;pid[e.Id]=p.Id;launched[e.Id]=p.StartTime;
        var info=new XmlDocument();var node=info.CreateElement("Run");info.AppendChild(node);
        node.SetAttribute("pid",p.Id.ToString());node.SetAttribute("start",p.StartTime.ToBinary().ToString());
        node.SetAttribute("mode",e.Mode);node.SetAttribute("token",runToken);
        info.Save(StatePath(e));RefreshStatuses();
        if(fromProfile)profileReport.Add("ЗАПУЩЕН: "+e.Name+" (PID "+p.Id+")");
      }catch(Exception ex){if(fromProfile)profileReport.Add("ОШИБКА: "+e.Name+" — "+ex.Message);else MessageBox.Show("Ошибка запуска: "+ex.Message);}
    }
    private void StopEntry() { StopEntry(null,false,null); }
    private void StopEntry(RegisteredScript selected,bool fromProfile,List<string> profileReport) {
      var e=selected??Current;if(e==null)return;
      if(!IsLive(e)) {
        ExternalRun visible;
        if(fromProfile) {
          // The periodic background result may be stale or unavailable.
          // Read this one registration afresh; never mistake scan failure for stopped.
          try {
            var fresh=ScanExternalRuns(new List<RegisteredScript>{e});
            externalRuns.Remove(e.Id);
            if(fresh.TryGetValue(e.Id,out visible))externalRuns[e.Id]=visible;
            externalScanReady=true;
          }catch(Exception ex) {
            profileReport.Add("ОШИБКА (невозможно проверить): "+e.Name+" — "+ex.Message);
            return;
          }
        }
        if(!externalRuns.TryGetValue(e.Id,out visible) || !externalScanReady) {
          if(fromProfile)profileReport.Add((externalScanReady?"ПРОПУЩЕН (остановлен): ":"ОШИБКА (не проверен): ")+e.Name);
          else MessageBox.Show(this,"Подтверждённый процесс не обнаружен. Для внешних запусков поддерживается прямой запуск PowerShell через -File с абсолютным путём .ps1.",
            "Нет подтверждённого процесса",MessageBoxButtons.OK,MessageBoxIcon.Information);
          return;
        }
        if(visible.Ambiguous) {
          if(fromProfile)profileReport.Add("ОШИБКА (несколько внешних): "+e.Name);
          else MessageBox.Show(this,"Найдено несколько внешних процессов с этим файлом. Остановка заблокирована — нельзя однозначно выбрать PID.",
            "Неоднозначный запуск",MessageBoxButtons.OK,MessageBoxIcon.Warning);
          return;
        }
        try {
          var snapshot=new List<RegisteredScript>{e};
          var matches=ScanExternalRuns(snapshot);
          ExternalRun verified;
          if(!matches.TryGetValue(e.Id,out verified) || !SameExternal(visible,verified))
            throw new InvalidOperationException("Внешний PID изменился или уже завершился; остановка отменена.");
          string question="Остановить ТОЛЬКО внешне запущенный PowerShell-процесс?"+
            "\n\nСкрипт: "+e.Path+"\nPID: "+verified.Pid+
            "\nВремя старта: "+verified.Started.ToString("yyyy-MM-dd HH:mm:ss")+
            "\n\nКонтрольный центр не запускал этот процесс. Дочерние процессы НЕ будут завершены. " +
            "Несохранённые данные внешнего скрипта могут быть потеряны. Продолжить?";
          if(!fromProfile && MessageBox.Show(this,question,"Подтвердить внешнюю остановку",
            MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
            return;
          matches=ScanExternalRuns(snapshot);
          ExternalRun final;
          if(!matches.TryGetValue(e.Id,out final) || !SameExternal(verified,final))
            throw new InvalidOperationException("Процесс изменился после подтверждения. Остановка отменена.");
          using(var p=Process.GetProcessById(final.Pid)) {
            if(p.HasExited || p.SessionId!=Process.GetCurrentProcess().SessionId ||
               Math.Abs((p.StartTime-final.Started).TotalSeconds)>=1)
              throw new InvalidOperationException("PID или время старта изменились. Остановка отменена.");
            p.Kill(); // Exact verified external PowerShell PID only; never taskkill /T.
            if(fromProfile)profileReport.Add("ОСТАНОВЛЕН ВНЕШНИЙ: "+e.Name+" (PID "+final.Pid+")");
          }
          externalRuns.Remove(e.Id);
          externalScanReady=false;
          lastExternalCheckUtc=DateTime.MinValue;
          RefreshStatuses();
        } catch(Exception ex) {
          if(fromProfile)profileReport.Add("ОШИБКА: "+e.Name+" — "+ex.Message);
          else MessageBox.Show(this,"Внешний процесс не остановлен: "+ex.Message,
            "Ошибка остановки",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        return;
      }
      if(!fromProfile) {
        if(MessageBox.Show("Завершить запущенный из центра процесс «"+e.Name+"» и его дерево дочерних процессов?\nОтсоединённые или повышенные процессы могут пережить остановку.","Подтверждение остановки",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
      }
      try {
        if(!IsLive(e))throw new InvalidOperationException("Процесс уже завершился");
        if(e.Mode=="hidden") {
          var stateDoc=new XmlDocument { XmlResolver=null };
          stateDoc.Load(StatePath(e));
          string token=stateDoc.DocumentElement.GetAttribute("token");
          Guid parsed;
          if(!Guid.TryParse(token,out parsed)) throw new InvalidOperationException("Нет подтверждённого токена запуска.");
          string marker=System.IO.Path.Combine(dataRoot,"runs",e.Id+"-"+token+".stop");
          File.WriteAllText(marker,"stop",new UTF8Encoding(false));
          if(fromProfile)profileReport.Add("ОСТАНОВКА ЗАПРОШЕНА: "+e.Name);
        } else {
          // Kill only the exact PID whose start time was verified by IsLive, including its child tree.
          KillProcessTree(pid[e.Id]);
          if(fromProfile)profileReport.Add("ОСТАНОВЛЕН: "+e.Name);
        }
      }catch(Exception ex){if(fromProfile)profileReport.Add("ОШИБКА: "+e.Name+" — "+ex.Message);else MessageBox.Show(ex.Message);}
      RefreshStatuses();
    }
    private void OpenLog() {
      var e=Current;if(e==null)return;
      if(!IsLive(e) && externalRuns.ContainsKey(e.Id)) {
        MessageBox.Show(this,"Этот процесс запущен вне Control Center. Его stdout/stderr не перехватываются; имеющийся локальный журнал относится к прежнему управляемому запуску.",
          "Внешний процесс",MessageBoxButtons.OK,MessageBoxIcon.Information);
        return;
      }
      if(e.Mode!="hidden") { MessageBox.Show(this,"Для режима «Своё окно» stdout/stderr не перехватываются. Переключите скрипт в режим «Внутри центра», если ему не нужен интерактивный ввод.","Журнал недоступен",MessageBoxButtons.OK,MessageBoxIcon.Information);return; }
      string path=LogPath(e);
      if(File.Exists(path)) Process.Start("notepad.exe",Quote(path));
      else MessageBox.Show(this,"Журнал ещё не создан. Сначала запустите скрипт в режиме «Внутри центра».","Журнал пуст",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    // Keep the viewport stationary while new log lines arrive; only follow
    // the tail when the reader was already viewing the last visible lines.
    // Replacing the whole RichTextBox every 1.2s caused visible jumping.
    private void SetOutputText(string value) {
      value=(value??"").Replace("\r\n","\n");
      if(String.Equals(shownOutput,value,StringComparison.Ordinal))return;
      int first=output.IsHandleCreated ?
        SendMessage(output.Handle,EM_GETFIRSTVISIBLELINE,IntPtr.Zero,IntPtr.Zero).ToInt32() : 0;
      int total=output.IsHandleCreated ?
        SendMessage(output.Handle,EM_GETLINECOUNT,IntPtr.Zero,IntPtr.Zero).ToInt32() : 0;
      int page=Math.Max(1,output.ClientSize.Height/Math.Max(1,output.Font.Height));
      bool follow=shownOutput.Length==0 || first+page>=total-2;
      if(shownOutput.Length>0 && value.StartsWith(shownOutput,StringComparison.Ordinal)) {
        output.AppendText(value.Substring(shownOutput.Length));
      } else {
        // The log may have rotated or its 90 KiB display window may have
        // advanced. Suppress intermediate repainting for the replacement.
        if(output.IsHandleCreated) SendMessage(output.Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
        try { output.Text=value; }
        finally {
          if(output.IsHandleCreated) {
            SendMessage(output.Handle,0x000B,new IntPtr(1),IntPtr.Zero);
            output.Invalidate();
          }
        }
      }
      shownOutput=value;
      if(follow) {
        output.SelectionStart=output.TextLength;
        output.ScrollToCaret();
      } else if(output.IsHandleCreated) {
        int now=SendMessage(output.Handle,EM_GETFIRSTVISIBLELINE,IntPtr.Zero,IntPtr.Zero).ToInt32();
        if(now!=first) SendMessage(output.Handle,EM_LINESCROLL,IntPtr.Zero,new IntPtr(first-now));
      }
    }
    private void RefreshSelected() {
      if(loading)return;
      var e=Current;
      if(e==null) {
        detailTitle.Text=entries.Count==0?"Подключите первый скрипт":"Выберите скрипт слева";
        detailPath.Text="";
        detailMeta.Text="Добавление сохраняет ссылку на существующий файл; исходный файл не перемещается.";
        detailStatus.Text=entries.Count==0?"НЕТ ЗАПИСЕЙ":"НЕ ВЫБРАНО";
        detailStatus.ForeColor=Color.FromArgb(151,174,202);
        logHeader.Text="ЖУРНАЛ ВЫПОЛНЕНИЯ";
        SetOutputText("Здесь будет отображаться вывод выбранного скрипта.\n\nДля начала нажмите «＋ Добавить», выберите сам файл скрипта (.ps1, .cmd, .bat, .py или .exe), затем нажмите «Запустить».");
        return;
      }
      string problem=EntryProblem(e);
      bool ownedLive=IsLive(e);
      ExternalRun ext=null;
      bool externalLive=!ownedLive && externalScanReady &&
        externalRuns.TryGetValue(e.Id,out ext);
      bool live=ownedLive || externalLive;
      detailTitle.Text=String.IsNullOrWhiteSpace(e.Name)?"Без названия":e.Name;
      detailPath.Text=e.Path??"";
      string runInfo="";
      int currentPid;
      DateTime startedAt;
      if(ownedLive && pid.TryGetValue(e.Id,out currentPid)) runInfo="PID: "+currentPid;
      if(ownedLive && launched.TryGetValue(e.Id,out startedAt)) runInfo+=(runInfo.Length>0?"  •  ":"")+"Старт: "+startedAt.ToString("yyyy-MM-dd HH:mm:ss");
      if(externalLive && ext!=null) {
        runInfo=ext.Ambiguous?"Внешних процессов несколько; остановка заблокирована":
          "Внешний PowerShell PID: "+ext.Pid+"  •  Старт: "+
          ext.Started.ToString("yyyy-MM-dd HH:mm:ss");
      }
      detailMeta.Text="Рабочая папка: "+(e.WorkingDirectory??"")+"    •    Режим: "+ModeText(e)+(runInfo.Length>0?"    •    "+runInfo:"");
      if(problem!=null) {
        detailStatus.Text="ТРЕБУЕТ ИСПРАВЛЕНИЯ";
        detailStatus.ForeColor=Color.FromArgb(246,184,112);
        logHeader.Text="ПРОВЕРКА НАСТРОЕК";
        SetOutputText("Эта запись не будет запущена, пока ошибка не исправлена.\n\n"+problem+"\n\nНажмите «Изменить», выберите настоящий файл скрипта и сохраните запись.");
        return;
      }
      detailStatus.Text=ownedLive?"● ВЫПОЛНЯЕТСЯ":
        externalLive?(ext.Ambiguous?"● НЕСКОЛЬКО ВНЕШНИХ":"● ВНЕШНИЙ ЗАПУСК"):
        externalScanReady?"○ ОСТАНОВЛЕН":"◌ НЕ ПРОВЕРЕН";
      detailStatus.ForeColor=live?Color.FromArgb(112,220,166):Color.FromArgb(151,174,202);
      logHeader.Text=externalLive?"ВНЕШНИЙ POWERSHELL • ВЫВОД НЕ ПЕРЕХВАТЫВАЕТСЯ":
        e.Mode=="hidden"?"ВЫВОД СКРИПТА • stdout / stderr":"ОТДЕЛЬНОЕ ОКНО • ВЫВОД НЕ ПЕРЕХВАТЫВАЕТСЯ";
      if(externalLive) {
        SetOutputText("Скрипт запущен отдельно от Control Center, поэтому его stdout/stderr недоступны в этом окне."+
          "\n\nОбнаружение: точное совпадение абсолютного пути .ps1 при запуске PowerShell через -File."+
          "\nОстановка: только после подтверждения точного PID и времени старта; без остановки дерева дочерних процессов.");
        return;
      }
      if(e.Mode!="hidden") {
        SetOutputText("Этот скрипт запускается в отдельном окне. Control Center показывает его состояние, но не перехватывает вывод этого окна.\n\nЕсли скрипт поддерживает обычный текстовый вывод и не требует интерактивного ввода, выберите режим «Внутри центра» в настройках.");
        return;
      }
      string path=LogPath(e);
      try {
        if(!File.Exists(path)) {
          SetOutputText(live?"Процесс запущен. Ожидается первый вывод в журнал…":"Журнал пока не создан. После запуска в режиме «Внутри центра» здесь появятся stdout и stderr.");
          return;
        }
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
          int count=(int)Math.Min(90000,f.Length);f.Seek(-count,SeekOrigin.End);
          byte[] bytes=new byte[count];int got=0,n;
          while(got<count && (n=f.Read(bytes,got,count-got))>0)got+=n;
          string content=Encoding.UTF8.GetString(bytes,0,got);
          SetOutputText(content);
        }
      } catch(IOException) { }
    }
  }
}
