"""Run a CI command with live memory and test-pool diagnostics every 30 seconds.

Usage: python Tools/_MalinovStation/ci/run_with_diagnostics.py dotnet test ...
Output goes to the job log because runner shutdown may prevent artifact upload.
"""

from datetime import datetime, timezone
from pathlib import Path
import subprocess
import sys
import time


def report(results_dir):
    print(f'[test diagnostics] {datetime.now(timezone.utc).isoformat()}', flush=True)
    try:
        memory = Path('/proc/meminfo')
        if memory.exists():
            for line in memory.read_text().splitlines():
                if line.startswith(('MemTotal:', 'MemAvailable:', 'SwapTotal:', 'SwapFree:')):
                    print(f'[test diagnostics] {line}', flush=True)
            processes = subprocess.run(
                ['ps', '-eo', 'pid,ppid,rss,comm', '--sort=-rss'],
                capture_output=True, text=True, timeout=5, check=True,
            )
            for line in processes.stdout.splitlines()[:7]:
                print(f'[test diagnostics] {line}', flush=True)

        # Cgroup counters can distinguish an OOM from an external shutdown on Linux.
        for name in ('memory.current', 'memory.max', 'memory.events'):
            path = Path('/sys/fs/cgroup') / name
            if path.exists():
                print(f'[test diagnostics] {name}: {path.read_text().strip()}', flush=True)

        # These files are flushed on each test borrow, before NUnit writes its XML.
        for path in sorted(results_dir.glob('gravestone-*.txt')):
            with path.open('rb') as stream:
                stream.seek(max(0, path.stat().st_size - 16384))
                lines = stream.read().decode('utf-8', errors='replace').splitlines()
            last_test = next((line for line in reversed(lines) if line.startswith('#')), '(no test recorded)')
            killed = ' (pair killed)' if 'Test pair killed.' in lines else ''
            print(f'[test diagnostics] {path.name}{killed}: {last_test}', flush=True)
    except (OSError, subprocess.SubprocessError) as error:
        print(f'[test diagnostics] Could not collect diagnostics: {error}', flush=True)


def run(command, results_dir=Path('test_results'), interval=30):
    started = time.monotonic()
    report(results_dir)
    process = subprocess.Popen(command)
    try:
        while True:
            try:
                exit_code = process.wait(timeout=interval)
                break
            except subprocess.TimeoutExpired:
                report(results_dir)
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
    report(results_dir)
    print(f'[test diagnostics] Command exited with code {exit_code} after {time.monotonic() - started:.1f}s', flush=True)
    return exit_code


if __name__ == '__main__':
    if len(sys.argv) < 2:
        sys.exit('Usage: run_with_diagnostics.py COMMAND [ARG ...]')
    sys.exit(run(sys.argv[1:]))
