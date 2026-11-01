from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent

class MirrorInspectorSourceChecks(unittest.TestCase):
    def test_launcher_opens_gui_by_default_and_preserves_cli(self):
        text = (ROOT / 'Fortis-Windows-Mirror-Status.ps1').read_text(encoding='utf-8-sig')
        self.assertIn('Mirror-Workflow.UI.cs', text)
        self.assertIn('[FortisMirror.WorkflowWindow]::Open(', text)
        self.assertIn('if ($Cli)', text)
        self.assertIn('Mirror-Status.Report.ps1', text)
        self.assertTrue((ROOT / 'Fortis-Windows-Mirror-Status.ps1').read_bytes().startswith(b'\xef\xbb\xbf'))

    def test_ui_is_on_sta_thread_and_check_does_not_block_ui(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for token in ('Application.Run(new MirrorWindow(', 'SetApartmentState(ApartmentState.STA)',
                      'Directory.GetDirectories(root)', 'ThreadPool.QueueUserWorkItem',
                      'BeginInvoke(', 'BeginCheck(false)', 'BeginCheck(true)',
                      'CancellationTokenSource', 'proc.Kill()', 'WaitForExit(100)'):
            self.assertIn(token, ui)

    def test_git_is_read_only_and_remote_is_allowlisted(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for cmd in ('rev-parse --verify', 'symbolic-ref --quiet', 'status --porcelain',
                    'remote get-url origin', 'ls-remote --exit-code'):
            self.assertIn(cmd, ui)
        for forbidden in ('"push ', '"checkout ', '"reset ', '"clean ', '"commit '):
            self.assertNotIn(forbidden, ui)
        self.assertIn('GIT_TERMINAL_PROMPT', ui)
        self.assertIn('GCM_INTERACTIVE', ui)
        self.assertIn('GIT_OPTIONAL_LOCKS', ui)
        self.assertIn('AlexPastukhh', ui)
        self.assertIn('timeoutMs', ui)

    def test_github_https_uses_verified_openssl_without_config_mutations(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        report = (ROOT / 'Mirror-Status.Report.ps1').read_text(encoding='utf-8-sig')
        for text in (ui, report):
            self.assertIn('http.sslBackend=openssl', text)
            self.assertIn('http.sslVerify=true', text)
            self.assertNotIn('http.sslVerify=false', text)
            self.assertNotIn('sslVerify false', text)
            self.assertNotIn('config --global', text)
            self.assertNotIn('config --local', text)
        self.assertIn('bool https = origin.Output.StartsWith("https://github.com/"', ui)
        self.assertIn('bool forceOpenSsl = false', ui)
        self.assertIn('ClassifyGitError', ui)
        for reason in ('ОШИБКА TLS', 'ОШИБКА ДОСТУПА', 'ОШИБКА DNS', 'ОШИБКА СЕТИ'):
            self.assertIn(reason, ui)
        self.assertIn('dirty.Timeout ? "Таймаут"', ui)
        self.assertIn('if (local.Timeout || branch.Timeout)', ui)
        self.assertIn('raw credential/URL errors', ui)

    def test_multiple_selection_checks_subset_only_and_never_bulk_pulls(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        self.assertIn('list.MultiSelect = true;', ui)
        self.assertIn('MakeButton("Проверить выбранные", 195)', ui)
        self.assertIn('checkSelected.Click += delegate { BeginCheck(false); };', ui)
        self.assertIn('checkSelected.Enabled = !running && hasSelectedGit;', ui)
        self.assertIn('foreach (ListViewItem item in list.SelectedItems)', ui)
        self.assertIn('if (row.IsGit) targets.Add(row);', ui)
        self.assertIn('if (all)', ui)
        self.assertIn('checkAll.Click += delegate { BeginCheck(true); };', ui)
        self.assertIn('pullOne.Enabled = !running && list.SelectedItems.Count == 1', ui)
        self.assertIn('if (running || list.SelectedItems.Count != 1) return;', ui)
        self.assertIn('if (!row.IsGit || expected == null || !expected.CanPull) return;', ui)
        self.assertIn('Для выбора нескольких используйте Ctrl или Shift.', ui)
        self.assertNotIn('checkOne.', ui)
        self.assertNotIn('list.MultiSelect = false;', ui)

    def test_pull_is_single_repo_explicit_confirmed_and_fast_forward_only(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        self.assertIn('pullOne.Click += delegate { BeginPull(); }', ui)
        self.assertIn('CheckResult expected = row.Last;', ui)
        self.assertIn('!expected.CanPull', ui)
        self.assertIn('MessageBoxButtons.YesNo', ui)
        self.assertIn('MessageBoxDefaultButton.Button2', ui)
        self.assertIn('CheckResult fresh = CheckRepository(row.Path, CancellationToken.None)', ui)
        for condition in ('!fresh.CanPull', 'fresh.LocalFull != expected.LocalFull',
                          'fresh.RemoteFull != expected.RemoteFull', 'fresh.Changes != "Нет"'):
            self.assertIn(condition, ui)
        self.assertIn('GitPullFastForward(row.Path, fresh.UseOpenSsl)', ui)
        self.assertIn('pull --ff-only --no-rebase --no-recurse-submodules origin main', ui)
        self.assertIn('-c core.hooksPath=/dev/null', ui)
        self.assertIn('-c submodule.recurse=false', ui)
        self.assertIn('psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "1";', ui)
        self.assertIn('e.Cancel = true;', ui)
        self.assertIn('cancel.Enabled = running && !writing;', ui)
        self.assertIn('result.Ok', ui)
        self.assertIn('НЕЗАКОНЧЕННАЯ ОПЕРАЦИЯ', ui)

    def test_pull_requires_clean_main_known_shas_not_failed_network(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for condition in ('State == "ОТСТАЁТ"', 'BehindCount > 0', 'AheadCount == 0',
                          'Branch == "main"',
                          'Changes == "Нет"', 'Regex.IsMatch(LocalFull,',
                          'Regex.IsMatch(RemoteFull,'):
            self.assertIn(condition, ui)
        self.assertIn('local.Output.ToLowerInvariant()', ui)
        self.assertIn('data.RemoteFull = remoteHash', ui)
        self.assertIn('log -1 --format=%h%x09%as%x09%s', ui)
        self.assertIn('Последний локальный коммит:', ui)
        self.assertIn('list.Columns.Add("Последний локальный коммит", 310)', ui)
        self.assertIn('row.Item.SubItems[7].Text = result.Commit', ui)
        self.assertIn('row.Item.SubItems[7].Text = checkedState.Commit', ui)
        self.assertIn('GitHub main:', ui)
        self.assertIn('status --porcelain=v1 --untracked-files=all', ui)
        self.assertNotIn('status --porcelain=v1 --untracked-files=normal --ignore-submodules=all', ui)
        self.assertEqual(ui.count('pull --ff-only --no-rebase --no-recurse-submodules origin main'), 1)

    def test_behind_history_counts_only_after_verified_git_metadata(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for marker in ('Отстаёт на', 'GitFetchMain(path, https)',
                       'rev-list --left-right --count', 'TryCounts(counts.Output, data)',
                       'data.BehindCount = behind', 'data.AheadCount = ahead',
                       'ИСТОРИЯ НЕИЗВЕСТНА', 'ЛОКАЛЬНО ВПЕРЕДИ', 'ИСТОРИИ РАЗОШЛИСЬ',
                       'fetch --no-tags --no-recurse-submodules origin main',
                       'GIT_OPTIONAL_LOCKS', 'http.sslVerify=true'):
            self.assertIn(marker, ui)
        self.assertIn('data.Behind = "0"', ui)
        self.assertIn('if (data.BehindCount < 0)', ui)
        self.assertIn('rev-parse --is-shallow-repository', ui)
        self.assertIn('НЕПОЛНАЯ ИСТОРИЯ', ui)
        self.assertIn('data.Behind = "?"', ui)
        self.assertIn('if (data.AheadCount > 0 && data.BehindCount > 0)', ui)
        self.assertIn('data.State = "ОТСТАЁТ"', ui)
        self.assertIn('psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "1"', ui)


    def test_no_unused_pull_flag_causes_windows_add_type_failure(self):
        # Windows PowerShell 5.1 Add-Type reports CS0414 as SOURCE_CODE_ERROR
        # when a private field is only written and never used.
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        self.assertNotIn('private bool pulling;', ui)
        self.assertNotIn('pulling = true;', ui)
        self.assertNotIn('pulling = false;', ui)
        self.assertIn('private bool writing;', ui)
        self.assertIn('if (writing)', ui)

    def test_normal_check_inspects_windows_and_docker_together(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for text in ('Windows: изменения', 'Docker: изменения',
                     'CheckDockerSelected(targets, token)', 'CheckRepository(row.Path, token)',
                     'psi.FileName = "docker.exe"', 'exec --user 1000:1000 fortis-repo-test',
                     '--stdout --repo', 'tools/Export-Docker-Mirror-Status.py',
                     'checkedDocker.TryGetValue(row.Item.Text', 'DockerLabel(row.Item.Text)',
                     'bool dockerOk = remoteDocker != null && remoteDocker.Live',
                     'Нет репо в Docker', 'Нет docker.exe', 'Ошибка Docker exec',
                     'Таймаут Docker', 'status', 'DateTime.UtcNow', 'CapturedUtc',
                     'ParseDockerTsv', 'PYTHONDONTWRITEBYTECODE'):
            self.assertIn(text, ui)
        self.assertNotIn('http://localhost:', ui)
        self.assertNotIn('docker.exe run ', ui)
        self.assertNotIn('docker.exe stop ', ui)
        self.assertNotIn('docker.exe restart ', ui)

    def test_manual_tsv_remains_fallback_with_stale_marker(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for text in ('Импорт TSV (резерв)', '(TSV)', '(старый)',
                     'ParseDockerSnapshot', 'TryLoadDockerCache',
                     'FORTIS_DOCKER_STATUS_V1', 'ImportDockerSnapshot',
                     'FortisGitMirror'):
            self.assertIn(text, ui)
        exporter = (ROOT / 'tools/Export-Docker-Mirror-Status.py').read_text()
        self.assertIn('status", "--porcelain=v1", "--untracked-files=all', exporter)
        self.assertIn('GIT_OPTIONAL_LOCKS', exporter)
        self.assertIn('Workfiles/windows-docker-audit', exporter)
        self.assertIn('def collect(root, names=None):', exporter)
        self.assertIn('parser.add_argument("--stdout"', exporter)
        self.assertIn('parser.add_argument("--repo"', exporter)

    def test_compact_columns_and_full_ordered_selection_details(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        # The requested columns are numbered from one in the Windows GUI:
        # 2 = GitHub main, 6 = Windows changes, 7 = behind, 9 = Docker.
        columns = [
            ('Репозиторий', 190),
            ('GitHub main', 135),
            ('Локальный main', 105),
            ('GitHub SHA', 105),
            ('Ветка', 100),
            ('Windows: изменения', 112),
            ('Отстаёт на', 82),
            ('Последний локальный коммит', 310),
            ('Docker: изменения', 135),
        ]
        for title, width in columns:
            self.assertIn('list.Columns.Add("%s", %d);' % (title, width), ui)
        self.assertIn('detail.WordWrap = true;', ui)
        self.assertIn('detail.ScrollBars = ScrollBars.Vertical;', ui)
        self.assertIn('detail.SelectionStart = 0;', ui)
        self.assertIn('detail.ScrollToCaret();', ui)
        self.assertIn('new RowStyle(SizeType.Absolute, 238)', ui)

        single = ui.split('    private void ShowSelection() {', 1)[1].split(
            '      } else if (list.SelectedItems.Count > 1)', 1)[0]
        # Full strings, never the short SHA or ellipsized ListView subitems.
        self.assertIn('v.LocalFull', single)
        self.assertIn('v.RemoteFull', single)
        self.assertIn('v.Commit.Replace(', single)
        self.assertNotIn('SubItems[', single)
        ordered_labels = [
            'Репозиторий: ', 'GitHub main: ', 'Локальный main: ',
            'GitHub SHA: ', 'Ветка: ', 'Windows: изменения: ',
            'Отстаёт на: ', 'Последний локальный коммит: ',
            'Docker: изменения: '
        ]
        positions = [single.index('lines.AppendLine("%s"' % title)
                     for title in ordered_labels]
        self.assertEqual(positions, sorted(positions))
        self.assertLess(positions[-1], single.index('Путь Windows: '))
        self.assertIn('DockerDetails(selected)', single)
        self.assertIn('StringBuilder', single)

    def test_copy_paths_and_single_repo_full_commit_guards(self):
        ui = (ROOT / 'Mirror-Status.UI.cs').read_text(encoding='utf-8-sig')
        for text in ('Копировать пути', 'CopyPaths()', 'Clipboard.SetText',
                     'Коммит всего (Windows)', 'git add -A', 'git commit',
                     'CanCommitAll', 'LocalFull == RemoteFull',
                     'list.SelectedItems.Count != 1', 'BeginCommitAll()',
                     'MessageBoxDefaultButton.Button2', 'GitWrite(row.Path, "add -A")',
                     'commit -m ', 'Push не выполняется', 'writing = true',
                     'if (writing)', 'git status'):
            self.assertIn(text, ui)
        self.assertNotIn('git push origin main', ui)
        self.assertNotIn('reset --hard', ui)

    def test_exporter_produces_parseable_read_only_docker_status(self):
        import importlib.util
        import subprocess
        import tempfile
        import datetime
        import os
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'Repos'
            repo = root / 'sample'
            repo.mkdir(parents=True)
            def git(*args):
                subprocess.run(['git', '-C', str(repo), *args], check=True,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            git('init', '-q')
            git('-c', 'user.name=Unit Test', '-c', 'user.email=unit@example.com',
                'commit', '--allow-empty', '-qm', 'init')
            (repo / 'new.txt').write_text('untracked')
            module = ROOT / 'tools/Export-Docker-Mirror-Status.py'
            spec = importlib.util.spec_from_file_location('docker_status_export', module)
            instance = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(instance)
            output = Path(folder) / 'report.tsv'
            now = datetime.datetime.now(datetime.timezone.utc)
            records = instance.export(root, output, now)
            self.assertEqual(len(records), 1)
            self.assertEqual(records[0][0], 'sample')
            self.assertEqual(records[0][2], 1)
            lines = output.read_text().splitlines()
            self.assertEqual(lines[0].split('\t')[0], 'FORTIS_DOCKER_STATUS_V1')
            self.assertEqual(len(lines[1].split('\t')[1]), 40)
            self.assertEqual(instance.collect(root, ['sample'])[0][2], 1)
            self.assertEqual(instance.collect(root, ['missing']), [])
            self.assertRaises(ValueError, instance.collect, root, ['../outside'])
            no_output = Path(folder) / 'would-not-write.tsv'
            probe = subprocess.run(
                ['python3', '-B', str(module), '--root', str(root),
                 '--output', str(no_output), '--stdout', '--repo', 'sample'],
                text=True, capture_output=True, check=True,
            )
            self.assertFalse(no_output.exists())
            self.assertEqual(probe.stderr, '')
            self.assertIn('sample\t', probe.stdout)
            self.assertTrue(probe.stdout.startswith('FORTIS_DOCKER_STATUS_V1\t'))
            missing = subprocess.run(
                ['python3', '-B', str(module), '--root', str(root),
                 '--stdout', '--repo', 'missing'],
                text=True, capture_output=True, check=True,
            )
            self.assertEqual(len(missing.stdout.splitlines()), 1)


    def test_safe_windows_mirror_reconciliation_is_owner_explicit(self):
        script = (ROOT / 'tools/Sync-Windows-Verified-Mirrors.ps1').read_text(encoding='utf-8')
        for value in ('param([switch]$Apply)', 'if (-not $Apply)', 'stash push --include-untracked',
                      'fetch --no-tags --no-recurse-submodules origin main',
                      'merge --ff-only', 'FETCH_HEAD', 'originPattern',
                      'core.hooksPath=/dev/null', 'Get-GitValue', 'GIT_TERMINAL_PROMPT',
                      'AlreadyUpdated', 'if ($plan.AlreadyUpdated)', 'Write-Host $afterStash'):
            self.assertIn(value, script)
        self.assertNotIn('stash drop', script.lower().split('param([switch]$Apply)')[-1].split('Write-Host')[0])
        self.assertNotIn('reset --hard', script)
        self.assertNotIn('git.exe push', script)
        self.assertNotIn('git.exe clean', script)
        # Explicitly excluded projects are not in the target table.
        targets = script.split('$targets = @(', 1)[1].split('function Get-GitValue', 1)[0]
        for forbidden in ('obs-planning-docs', 'rd-graphs', 'security'):
            self.assertNotIn(forbidden, targets)

    def test_original_cli_report_kept(self):
        report = (ROOT / 'Mirror-Status.Report.ps1').read_text(encoding='utf-8')
        self.assertIn('Need review:', report)
        self.assertIn('ls-remote', report)

if __name__ == '__main__':
    unittest.main()
