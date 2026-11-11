"""Static registration/security checks for browser Mirror launch via Script Center.

Windows/WinForms behavior is verified by the owner after two source PR merges.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent


class MirrorBrowserLauncherContract(unittest.TestCase):
    def test_new_adapter_is_windows_powershell_51_and_delegates_to_mirror(self):
        path = ROOT / 'Fortis-Mirror-Browser.ps1'
        raw = path.read_bytes()
        self.assertTrue(raw.startswith(b'\xef\xbb\xbf'), 'PS 5.1 source should have UTF-8 BOM')
        code = raw.decode('utf-8-sig')
        for marker in ('#requires -Version 5.1', "'Windows_NT'", "$env:USERPROFILE",
                       "'docke-windows-mirror'", "'Start-MirrorAPI.ps1'",
                       "'main'", "'MIRROR_SOURCE_UNEXPECTED_ORIGIN'",
                       "'MIRROR_SOURCE_NOT_ON_MAIN'", "Get-Command git.exe",
                       "Get-Command py.exe", "Get-Command docker.exe",
                       '& $launcher -MirrorRoot $MirrorRoot -Port $Port -OpenBrowser'):
            with self.subTest(marker=marker):
                self.assertIn(marker, code)
        for forbidden in ('git pull', 'git fetch', 'git clone', 'git push',
                          'git checkout', 'git switch', 'git reset', 'git stash',
                          'Invoke-Expression', 'Start-Process', 'catalog.xml',
                          'ImportFromText', 'Manage-Integrations', 'GatewaySecret'):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, code.split('#>', 1)[-1])

    def test_legacy_script_center_entry_defaults_to_browser_and_keeps_cli(self):
        code=(ROOT / 'Fortis-Windows-Mirror-Status.ps1').read_bytes()
        self.assertEqual(code[:3], bytes((239, 187, 191)))
        source=code.decode('utf-8-sig')
        for marker in ('if ($Cli -or $Notify)', "'Fortis-Mirror-Browser.ps1'",
                       "'Fortis-Windows-Mirror-Status.ps1'",
                       '& $legacy -MirrorRoot $MirrorRoot -Cli:$Cli -Notify:$Notify',
                       '& $browser -MirrorRoot $MirrorRoot',
                       'MIRROR_BROWSER_ADAPTER_MISSING_UPDATE_SCRIPT_CENTER',
                       'MIRROR_SOURCE_UNEXPECTED_ORIGIN'):
            with self.subTest(marker=marker):self.assertIn(marker,source)
        self.assertLess(source.index('if ($Cli -or $Notify)'),
                        source.index('& $browser -MirrorRoot $MirrorRoot'))
        for forbidden in ('git pull','git fetch','git clone','git push','git reset',
                          'git stash','Start-Process','Invoke-Expression',
                          'Set-Content','catalog.xml','Manage-Integrations'):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden,source.split('#>',1)[-1])

    def test_source_docs_require_owner_window_mode_on_existing_entry(self):
        readme=(ROOT / 'README.md').read_text(encoding='utf-8-sig')
        self.assertIn('legacy Script Center entry',readme)
        self.assertIn('mode window',readme)
        self.assertIn('-Cli',readme)
        self.assertIn('no automatic catalog migration',readme)

    def test_import_text_is_opt_in_window_mode_and_targets_wrapper(self):
        blob = (ROOT / 'TEXT-IMPORT-MIRROR-BROWSER.txt').read_bytes()
        self.assertTrue(blob.startswith(b'\xef\xbb\xbf'))
        items = [line.strip() for line in blob.decode('utf-8-sig').splitlines()
                 if line.strip() and not line.lstrip().startswith('#')]
        self.assertEqual(['Fortis Mirror — браузер | @app\\Fortis-Mirror-Browser.ps1 | window'], items)
        source = (ROOT / 'ScriptCatalog.cs').read_text(encoding='utf-8-sig')
        self.assertIn('ImportFromText()', source)
        self.assertIn('mode=="окно"', source)
        self.assertIn('mode="window"', source)
        self.assertNotIn('Fortis-Mirror-Browser.ps1', source,
                         'No silent auto registration into the owner catalog')
        self.assertIn('Fortis-Windows-Mirror-Status.ps1',
                      [file.name for file in ROOT.iterdir()])

    def test_docs_explain_order_and_owner_registration(self):
        readme = (ROOT / 'README.md').read_text(encoding='utf-8-sig')
        for marker in ('Fortis-Mirror-Browser.ps1', 'TEXT-IMPORT-MIRROR-BROWSER.txt',
                       '«Импорт из текста»', '«Проверить»', '«Импортировать»',
                       'window', 'Start-MirrorAPI.ps1 -OpenBrowser'):
            self.assertIn(marker, readme)


if __name__ == '__main__':
    unittest.main()
