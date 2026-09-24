"""Run with python -m unittest discover -s Tools/_MalinovStation/tests."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / 'actions_extract_failures.py'
PASSED_REPORT = '<test-run><test-case name="Passed" result="Passed" /></test-run>'
FAILED_REPORT = '''<test-run><test-suite><test-case name="Failed"
    fullname="Example.Failed" result="Failed">
    <failure><message>Expected true</message></failure>
    <output>Test log</output>
</test-case></test-suite></test-run>'''


class ExtractFailuresTest(unittest.TestCase):
    def run_extractor(self, reports):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            logs = root / 'test_results' / 'logs'
            logs.mkdir(parents=True)
            for name, report in reports.items():
                (logs / name).write_text(report, encoding='utf-8')
            output = root / 'github-output'
            result = subprocess.run(
                [sys.executable, str(SCRIPT)],
                cwd=root,
                env={**os.environ, 'GITHUB_OUTPUT': str(output)},
                capture_output=True,
                text=True,
                encoding='utf-8',
                timeout=30,
            )
            values = dict(line.split('=', 1) for line in
                          output.read_text(encoding='utf-8').splitlines()) if output.exists() else {}
            return result, values

    def test_missing_reports_produce_empty_matrix_and_warnings(self):
        result, values = self.run_extractor({})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.count('::warning::NUnit report not found:'), 2)
        self.assertEqual(json.loads(values['matrix']), [])
        self.assertEqual(values['count'], '0')

    def test_missing_integration_report_preserves_unit_failures(self):
        result, values = self.run_extractor({'Content.Tests.xml': FAILED_REPORT})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('::warning::NUnit report not found:', result.stdout)
        self.assertEqual(values['count'], '1')
        self.assertEqual(json.loads(values['matrix']), [{
            'name': 'Failed',
            'fullname': 'Example.Failed',
            'failure': 'Expected true',
            'output': 'Test log',
        }])

    def test_complete_reports_extract_integration_failures(self):
        result, values = self.run_extractor({
            'Content.Tests.xml': PASSED_REPORT,
            'Content.IntegrationTests.xml': FAILED_REPORT,
        })
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn('::warning::', result.stdout)
        self.assertEqual(values['count'], '1')
        self.assertEqual(json.loads(values['matrix'])[0]['fullname'], 'Example.Failed')

    def test_malformed_report_remains_an_error(self):
        result, values = self.run_extractor({'Content.Tests.xml': '<test-run>'})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('ExpatError', result.stderr)
        self.assertEqual(values, {})


if __name__ == '__main__':
    unittest.main()
