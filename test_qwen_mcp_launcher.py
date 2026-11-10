"""Linux-only source checks for opt-in Qwen MCP Script Catalog adapter.

Never executes Windows PowerShell, Docker, uv or a live proxy.
"""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parent

class QwenLauncherContract(unittest.TestCase):
    def test_launcher_is_bom_windows_and_uses_owner_tested_proxy_route(self):
        raw=(ROOT/'Start-Qwen-Fortis-MCP.ps1').read_bytes()
        self.assertTrue(raw.startswith(b'\xef\xbb\xbf'))
        code=raw.decode('utf-8-sig')
        for token in ('#requires -Version 5.1', 'Windows_NT', 'Get-Command py.exe',
                      'Get-Command docker.exe', 'py --version' if False else "'-m','uv','tool','run','mcp-proxy'",
                      'Get-NetTCPConnection', 'QWEN_PROXY_PORT_BUSY',
                      'QWEN_FORTIS_CONTAINER_NOT_RUNNING', 'fortis-repo-test',
                      '127.0.0.1', '18765', '/workspace/files/Qwen-Fortis-MCP/container-home',
                      '/opt/dc/node_modules/@wonderwhy-er/desktop-commander/dist/index.js',
                      "'--user','1000:1000'", 'QWEN_FORTIS_MCP_LAUNCH_ERROR'):
            with self.subTest(token=token):self.assertIn(token,code)
        for forbidden in ('docker run', 'docker start', 'docker stop',
                          'docker restart', 'git pull', 'git fetch', 'git clone',
                          'winget install', 'pip install', 'Invoke-Expression',
                          'Stop-Process', 'taskkill', 'Remove-Item',
                          'gateway-stdio.mjs', 'Start-Process'):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden,code.split('#>',1)[-1])
    def test_opt_in_import_remains_window_and_separate(self):
        raw=(ROOT/'TEXT-IMPORT-QWEN-MCP.txt').read_bytes()
        self.assertTrue(raw.startswith(b'\xef\xbb\xbf'))
        items=[s.strip() for s in raw.decode('utf-8-sig').splitlines()
               if s.strip() and not s.lstrip().startswith('#')]
        self.assertEqual([r'Qwen — Fortis MCP | @app\Start-Qwen-Fortis-MCP.ps1 | window'],items)
        ui=(ROOT/'ScriptCatalog.cs').read_text(encoding='utf-8-sig')
        self.assertNotIn('Start-Qwen-Fortis-MCP.ps1',ui)
        self.assertIn('ImportFromText()',ui)
    def test_documentation_covers_owner_control(self):
        doc=(ROOT/'docs/QWEN-MCP-LAUNCHER.md').read_text(encoding='utf-8')
        for phrase in ('TEXT-IMPORT-QWEN-MCP.txt','«Импорт из текста»',
                       '«Проверить»','«Импортировать»','18765',
                       'Ctrl+C','UID 1000','read/write'):
            self.assertIn(phrase,doc)

if __name__=='__main__':unittest.main()
