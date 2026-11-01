from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parent
class RegistrationChecks(unittest.TestCase):
    def test_existing_four_entries_unchanged(self):
        source=(ROOT/'ControlCenter.cs').read_text(encoding='utf-8-sig')
        self.assertIn('private readonly string[] keys = { "repo2", "repo1", "updater", "watcher" };',source)
        for name in ('Repo2-Worker.ps1','Repo1-Worker.ps1','Updater-Worker.ps1','Watcher-Start.ps1','Watcher-Stop.ps1'):
            self.assertTrue((ROOT/name).exists())
    def test_registration_ui_and_local_storage(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        for feature in ('OpenFileDialog','File.Replace','LocalApplicationData','DeleteEntry','StartEntry',
                        'StopEntry','CreateNoWindow=true','Mode="hidden"','IsLive(', 'LogPath('):
            self.assertIn(feature,source)
        self.assertIn('DtdProcessing=DtdProcessing.Prohibit',source)
    def test_runner_is_independent_and_cooperative(self):
        source=(ROOT/'Script-Runner.ps1').read_text(encoding='utf-8-sig')
        for text in ('-File $', '[RUNNER ERROR]', 'WaitForExit(500)','File.Exists(stopFile)','[EXIT]'):
            if text=='-File $': continue
            self.assertIn(text,source)
        self.assertNotIn('docker.exe',source)
    def test_powershell_sources_keep_bom(self):
        for name in ('Start-ControlCenter.ps1','Start-ScriptCatalog.ps1','Test-Compile.ps1','Test-ScriptCatalogCompile.ps1','Script-Runner.ps1'):
            self.assertTrue((ROOT/name).read_bytes().startswith(b'\xef\xbb\xbf'))
    def test_project_compile_uses_both_cs_files(self):
        for name in ('Start-ControlCenter.ps1','Test-Compile.ps1'):
            source=(ROOT/name).read_text(encoding='utf-8-sig')
            self.assertIn("ScriptCatalog.cs",source)
            self.assertIn("ControlCenter.cs",source)
    def test_catalog_replace_uses_real_backup_path(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('File.Replace(temp,catalogPath,backup)',source)
        self.assertNotIn('File.Replace(temp,catalogPath,null)',source)
        self.assertIn('PruneCatalogBackups()',source)
    def test_catalog_layout_is_responsive_and_has_empty_state(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('split.Panel2MinSize=470',source)
        self.assertIn('split.SplitterDistance=',source)
        self.assertIn('emptyState.Visible=entries.Count==0',source)
        self.assertIn('output.WordWrap=true',source)
        self.assertIn('detailTitle.Text=entries.Count==0?',source)
        self.assertIn('Журнал пока не создан',source)
        self.assertIn('items.View=View.Details',source)
    def test_script_name_tracks_path_until_manually_edited(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('Name=System.IO.Path.GetFileNameWithoutExtension(d.FileName)',source)
        self.assertIn('if(!nameManual) nameBox.Text=System.IO.Path.GetFileNameWithoutExtension(d.FileName)',source)
    def test_standalone_catalog_launcher_does_not_start_legacy_services(self):
        source=(ROOT/'Start-ScriptCatalog.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('FortisRdgControl.ScriptCatalogForm',source)
        self.assertIn('[System.Windows.Forms.Application]::Run($form)',source)
        for legacy in ('Repo1-Worker.ps1','Repo2-Worker.ps1','Updater-Worker.ps1','Watcher-Start.ps1'):
            self.assertNotIn(legacy,source)
    def test_catalog_compile_script_does_not_open_gui(self):
        source=(ROOT/'Test-ScriptCatalogCompile.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('Add-Type -Path $catalog',source)
        self.assertIn('SCRIPT_CATALOG_COMPILE_OK',source)
        self.assertNotIn('[System.Windows.Forms.Application]::Run',source)
    def test_add_dialog_prefers_ps1_and_explains_interpreter(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('FilterIndex=1',source)
        self.assertIn('PowerShell-скрипты (*.ps1)|*.ps1',source)
        self.assertIn('IsInterpreterPath(d.FileName)',source)
        self.assertIn('Выберите сам файл скрипта',source)
        self.assertIn('browseFile.Click',source)
        self.assertIn('browseFolder.Click',source)
    def test_invalid_interpreter_records_are_visible_and_cannot_run(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('IsInterpreterPath(e.Path)',source)
        self.assertIn('Выбран интерпретатор',source)
        self.assertIn('Эту запись нельзя запустить',source)
        self.assertIn('Keep invalid records visible',source)
    def test_catalog_recovers_from_backup_and_keeps_bad_records_visible(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('TryLoadEntries(backup,out backupError)',source)
        self.assertIn('Keep invalid records visible',source)
        self.assertIn('catalogWarning=',source)
    def test_running_entries_cannot_be_edited_or_deleted(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('Нельзя менять путь, режим или аргументы во время выполнения',source)
        self.assertIn('Удаление работающей записи скроет её из центра',source)
    def test_empty_catalog_state_is_brought_to_front(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('emptyState.Visible=entries.Count==0',source)
        self.assertIn('emptyState.BringToFront()',source)
    def test_list_shows_name_status_and_mode(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('items.Columns.Add("Название"',source)
        self.assertIn('items.Columns.Add("Состояние"',source)
        self.assertIn('items.Columns.Add("Режим"',source)
        self.assertIn('row.SubItems.Add(StatusText(e))',source)
        self.assertIn('tick.Tick += delegate { RefreshStatuses(); }',source)
        self.assertIn('private void RefreshStatuses()',source)
        self.assertIn('RefreshItems(e.Id)',source)
    def test_log_panel_has_useful_empty_state_and_no_flicker(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('private void SetOutputText(string value)',source)
        self.assertIn('Журнал пока не создан',source)
        self.assertIn('ОТДЕЛЬНОЕ ОКНО • ВЫВОД НЕ ПЕРЕХВАТЫВАЕТСЯ',source)
    def test_start_refuses_invalid_records_and_protects_live_edits(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('string problem=EntryProblem(e)',source)
        self.assertIn('Эту запись нельзя запустить',source)
        self.assertIn('Сначала остановите скрипт',source)
    def test_batch_command_uses_cmd_compatible_quoting(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('/d /s /c',source)
    def test_repo1_worker_delegates_independently_to_pinned_launcher(self):
        source=(ROOT/'Repo1-Worker.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('run-tunnel-quiet-logs.ps1',source)
        self.assertIn('& $script -Mode Run',source)
        self.assertIn('Test-Path -LiteralPath $script -PathType Leaf',source)
        self.assertNotIn('repo2-worker-*.xml',source)
        self.assertNotIn('Get-WmiObject -Class Win32_Process',source)
        self.assertNotIn('Сначала запустите Repo 2',source)
    def test_main_control_center_has_independent_repo_lifecycles(self):
        source=(ROOT/'ControlCenter.cs').read_text(encoding='utf-8-sig')
        start=source[source.index('private void StartService('):source.index('private void StopService(')]
        stop=source[source.index('private void StopService('):source.index('private bool Confirm(',source.index('private void StopService('))]
        self.assertIn('ScriptArgs("Repo2-Worker.ps1")',start)
        self.assertIn('ScriptArgs("Repo1-Worker.ps1")',start)
        self.assertNotIn('if (!OwnedActive("repo2")',start)
        self.assertNotIn('Сначала запусти Fortis Repo 2',start)
        self.assertNotIn('id == "repo2" && (OwnedActive("repo1")',stop)
        self.assertIn('StartProcess(id, psExe',start)
        self.assertIn('taskkill.exe',stop)
    def test_repo2_worker_publishes_a_process_identity_marker(self):
        source=(ROOT/'Repo2-Worker.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('repo2-worker-{0}.xml',source)
        self.assertIn('$workerProcess.StartTime.ToBinary()',source)
        self.assertIn('Move-Item -LiteralPath $markerTemp -Destination $readyMarker',source)
        self.assertIn('Remove-Item -LiteralPath $readyMarker',source)
        self.assertIn('& $repo2 -Action Start',source)
    def test_runner_terminates_owned_process_tree(self):
        source=(ROOT/'Script-Runner.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('private static void KillTree(Process child)',source)
        self.assertIn('"/PID "+child.Id+" /T /F"',source)
        self.assertIn('terminating the owned process tree',source)
    def test_catalog_terminates_only_verified_owned_tree(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('private static void KillProcessTree(int processId)',source)
        self.assertIn('KillProcessTree(pid[e.Id])',source)
        self.assertIn('whose start time was verified by IsLive',source)
    def test_log_output_updates_incrementally_without_forced_scroll(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        block=source[source.index('private void SetOutputText(string value)'):
                     source.index('private void RefreshSelected()',source.index('private void SetOutputText(string value)'))]
        self.assertIn('String.Equals(shownOutput,value,StringComparison.Ordinal)',block)
        self.assertIn('value.StartsWith(shownOutput,StringComparison.Ordinal)',block)
        self.assertIn('output.AppendText(value.Substring(shownOutput.Length))',block)
        self.assertIn('EM_GETFIRSTVISIBLELINE',block)
        self.assertIn('EM_GETLINECOUNT',block)
        self.assertIn('EM_LINESCROLL',block)
        self.assertIn('if(follow)',block)
        self.assertIn('output.ScrollToCaret()',block)
        self.assertIn('SendMessage(output.Handle,0x000B',block)
        self.assertNotIn('if(output.Text!=content)',source)

    def test_external_powershell_detection_requires_exact_path_and_user(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        for marker in ('using System.Management;', 'using System.Security.Principal;',
                       'ManagementObjectSearcher(', 'CommandLineToArgvW(', 'LocalFree(ptr)',
                       'private static string DirectPowerShellFile(string commandLine)',
                       'String.Equals(args[i],"-File",StringComparison.OrdinalIgnoreCase)',
                       'Path.IsPathRooted(file)', 'Path.GetFullPath(file)',
                       'Path.GetExtension(file)', 'pathToIds.TryGetValue(filename',
                       'GetOwnerSid', 'WindowsIdentity.GetCurrent().User.Value',
                       'proc.SessionId!=ownSession', 'proc.StartTime',
                       'ThreadPool.QueueUserWorkItem', 'BeginExternalScan()',
                       'Работает (внешний)', 'Несколько внешних',
                       'externalScanReady?"Остановлен":"Не проверен"'):
            self.assertIn(marker, source)
        self.assertIn('tick.Interval=1200;',source)
        self.assertNotIn('Process.GetProcessesByName(',source)
        self.assertNotIn('GetOwnerSid() == null',source)

    def test_external_wmi_owner_lookup_includes_handle_key(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        start=source.index('private static Dictionary<string,ExternalRun> ScanExternalRuns')
        end=source.index('private void BeginExternalScan()',start)
        block=source[start:end]
        self.assertIn('SELECT Handle, ProcessId, Name, CommandLine FROM Win32_Process',block)
        self.assertIn('item.InvokeMethod("GetOwnerSid",null,null)',block)
        self.assertIn('owner["ReturnValue"]==null',block)
        self.assertIn('Convert.ToUInt32(owner["ReturnValue"])!=0',block)
        # Owner lookup must not be swallowed as a benign process-exit race.
        owner=block.index('item.InvokeMethod("GetOwnerSid",null,null)')
        race_try=block.index('try {',owner)
        self.assertGreater(race_try,owner)
        self.assertIn('catch(InvalidOperationException) { }',block[race_try:])

    def test_command_line_pinvoke_uses_exact_windows_api_entry_points(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('[DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]\n    private static extern IntPtr CommandLineToArgvW',source)
        self.assertIn('[DllImport("kernel32.dll", ExactSpelling=true)]\n    private static extern IntPtr LocalFree',source)

    def test_external_stop_does_not_kill_unrelated_or_children(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        stop=source[source.index('private void StopEntry()'):source.index('private void OpenLog()',source.index('private void StopEntry()'))]
        for marker in ('visible.Ambiguous', 'ScanExternalRuns(snapshot)',
                       'SameExternal(visible,verified)', 'SameExternal(verified,final)',
                       'final.Pid', 'final.Started', 'p.SessionId!=Process.GetCurrentProcess().SessionId',
                       'p.Kill()', 'MessageBoxDefaultButton.Button2', 'MessageBoxButtons.YesNo'):
            self.assertIn(marker,stop)
        external=stop[stop.index('if(!IsLive(e)) {'):stop.index('      if(MessageBox.Show("Завершить запущенный из центра')]
        self.assertNotIn('KillProcessTree(',external)
        self.assertNotIn('taskkill.exe',external)
        self.assertNotIn('/T /F',external)
        self.assertIn('if(!matches.TryGetValue(e.Id,out verified)',external)
        # Owned mode keeps existing approved runner/stop-token semantics.
        self.assertIn('KillProcessTree(pid[e.Id])',stop)
        self.assertIn('File.WriteAllText(marker,"stop"',stop)

    def test_catalog_does_not_ambiguously_bind_winforms_timer(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();', source)
        self.assertNotIn('private readonly Timer tick = new Timer();', source)
        # ObjectDisposedException derives from InvalidOperationException in .NET.
        # A separate later catch(ObjectDisposedException) is unreachable (CS0160).
        self.assertNotIn('catch(InvalidOperationException) { }\n        catch(ObjectDisposedException)', source)

    def test_windows_catalog_compilers_reference_management(self):
        for name in ('Start-ScriptCatalog.ps1','Test-ScriptCatalogCompile.ps1',
                     'Test-CatalogOnly.ps1'):
            text=(ROOT/name).read_text(encoding='utf-8-sig')
            self.assertIn("'System.Management.dll'",text)
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('catch(Exception) {',source)
        self.assertIn('externalScanReady=(found!=null)',source)
        self.assertIn('externalRuns.Clear();',source)

    def test_one_click_fortis_rdg_bundle_import_button(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('AddButton(top, "Готовый набор", delegate { ImportBuiltInBundle(); }',source)
        for name in ('Fortis Repo 2','Fortis Repo 1','RDG Updater','CI Watcher — запуск','CI Watcher — остановка'):
            self.assertIn(name,source)
        for file in ('Repo2-Worker.ps1','Repo1-Worker.ps1','Updater-Worker.ps1','Watcher-Start.ps1','Watcher-Stop.ps1'):
            self.assertIn(file,source)
    def test_bundle_records_have_stable_ids_and_are_deduplicated(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('BundleId=Attr(node,"bundle")',source)
        self.assertIn('SetAttr(xml,n,"bundle",e.BundleId)',source)
        self.assertIn('FindBundleEntry(item.Id,path)',source)
        self.assertIn('BundleId=item.Id',source)
    def test_bundle_import_preflights_known_external_paths(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        for part in ('FortisRDGLauncherV2','Start-Fortis.ps1','FortisRepoPlus2','FortisRepo2-quiet-logs.ps1',
                     'chatgpt-repo-mode','run-tunnel-quiet-logs.ps1','RDG-Server','Update-RDG.ps1','Common.ps1'):
            self.assertIn(part,source)
        self.assertIn('File.Exists(path)',source)
        self.assertIn('Проверка внешних файлов выше',source)
    def test_bundle_import_never_starts_scripts_or_services(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        start=source.index('private void ImportBuiltInBundle()')
        end=source.index('private bool EditDialog(',start)
        method=source[start:end]
        self.assertIn('SaveEntries()',method)
        self.assertIn('RefreshItems(',method)
        self.assertNotIn('StartEntry()',method)
        self.assertNotIn('Process.Start(',method)
        self.assertIn('Ничего запускаться не будет',method)
    def test_bundle_import_warns_about_legacy_interpreter_rows(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('старые записи powershell.exe',source)
        self.assertIn('Импорт их не удаляет и не запускает',source)
    def test_text_import_has_multiline_editor_and_preview(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('AddButton(top, "Импорт из текста"',source)
        self.assertIn('private void ImportFromText()',source)
        self.assertIn('private bool ParseTextManifest(',source)
        self.assertIn('Multiline=true,AcceptsReturn=true,AcceptsTab=true',source)
        self.assertIn('apply.Enabled=valid',source)
    def test_text_import_supports_alias_and_environment(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        self.assertIn('Environment.ExpandEnvironmentVariables',source)
        self.assertIn('value.StartsWith("@app',source)
        self.assertIn('launcherRoot,value.Substring(5)',source)
        self.assertIn('Path.GetFullPath(value)',source)
    def test_ready_text_manifest_lists_all_five_workers(self):
        manifest=(ROOT/'TEXT-IMPORT-FORTIS-RDG.txt').read_text(encoding='utf-8')
        lines=[s.strip() for s in manifest.splitlines() if s.strip() and not s.startswith('#')]
        self.assertEqual(len(lines),5)
        for filename in ('Repo1-Worker.ps1','Repo2-Worker.ps1','Updater-Worker.ps1','Watcher-Start.ps1','Watcher-Stop.ps1'):
            self.assertTrue(any(filename in line for line in lines))
    def test_text_manifest_registration_is_transactional(self):
        source=(ROOT/'ScriptCatalog.cs').read_text()
        block=source[source.index('private bool ParseTextManifest('):source.index('private RegisteredScript FindBundleEntry(')]
        for token in ('IsInterpreterPath(path)','File.Exists(path)','FindDuplicate(entry)',
                      'ПОВТОР В ТЕКСТЕ','return problems==0 && additions.Count>0',
                      'foreach(RegisteredScript e in approved)','SaveEntries();',
                      'foreach(RegisteredScript e in added) entries.Remove(e)'):
            self.assertIn(token,block)
        self.assertNotIn('Process.Start(',block)
        self.assertNotIn('StartEntry(',block)
if __name__ == '__main__':unittest.main()
