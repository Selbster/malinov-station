import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / 'ci' / 'run_with_diagnostics.py'
SPEC = importlib.util.spec_from_file_location('run_with_diagnostics', SCRIPT)
diagnostics = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(diagnostics)


class RunWithDiagnosticsTest(unittest.TestCase):
    def test_preserves_success_and_failure_exit_codes(self):
        for code in (0, 7):
            with self.subTest(code=code), patch.object(diagnostics, 'report'), contextlib.redirect_stdout(io.StringIO()):
                actual = diagnostics.run([sys.executable, '-c', f'raise SystemExit({code})'])
                self.assertEqual(actual, code)

    def test_reports_progress_while_command_is_running(self):
        with patch.object(diagnostics, 'report') as report, contextlib.redirect_stdout(io.StringIO()):
            code = diagnostics.run([sys.executable, '-c', 'import time; time.sleep(0.2)'], interval=0.05)
        self.assertEqual(code, 0)
        self.assertGreater(report.call_count, 2)

    def test_reports_last_borrowed_test_and_killed_pair(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'gravestone-1.txt').write_text(
                'Test pair initialized.\n#1: OldTest\n#2: CurrentTest\n', encoding='utf-8')
            (root / 'gravestone-2.txt').write_text(
                '#1: FinishedTest\nTest pair killed.\nStack trace\n', encoding='utf-8')
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                diagnostics.report(root)
            self.assertIn('gravestone-1.txt: #2: CurrentTest', output.getvalue())
            self.assertIn('gravestone-2.txt (pair killed): #1: FinishedTest', output.getvalue())
            self.assertNotIn('OldTest', output.getvalue())

    def test_linux_memory_and_oom_counters_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'proc').mkdir()
            (root / 'proc' / 'meminfo').write_text('MemTotal: 1000 kB\nMemAvailable: 200 kB\n')
            cgroup = root / 'sys' / 'fs' / 'cgroup'
            cgroup.mkdir(parents=True)
            (cgroup / 'memory.events').write_text('oom 1\noom_kill 1\n')
            output = io.StringIO()
            with patch.object(diagnostics, 'Path', side_effect=lambda path: root / path.lstrip('/')), \
                    patch.object(diagnostics.subprocess, 'run') as ps, contextlib.redirect_stdout(output):
                ps.return_value.stdout = 'PID PPID RSS COMMAND\n42 1 500 testhost\n'
                diagnostics.report(root)
            self.assertIn('MemAvailable: 200 kB', output.getvalue())
            self.assertIn('42 1 500 testhost', output.getvalue())
            self.assertIn('oom_kill 1', output.getvalue())

    def test_unreadable_diagnostics_do_not_hide_command_failure(self):
        output = io.StringIO()
        with patch.object(diagnostics.Path, 'exists', side_effect=OSError('Unreadable')), \
                contextlib.redirect_stdout(output):
            code = diagnostics.run([sys.executable, '-c', 'raise SystemExit(7)'])
        self.assertEqual(code, 7)
        self.assertIn('Could not collect diagnostics', output.getvalue())


if __name__ == '__main__':
    unittest.main()
