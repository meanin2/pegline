#!/usr/bin/env python3
"""Optional maintainer audit (Python standard library only).

This is NOT a C# compiler, test runner, security certification, or Windows UI test.
The normal Windows build has no Python dependency. Run from any working directory.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import platform
import re
import shutil
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def mask_noncode(text: str) -> str:
    """Preserve newlines/positions while masking C# 5 strings and comments."""
    output = list(text)
    i = 0
    while i < len(text):
        start = i
        if text.startswith('//', i):
            i = text.find('\n', i)
            if i < 0:
                i = len(text)
        elif text.startswith('/*', i):
            end = text.find('*/', i + 2)
            if end < 0:
                raise ValueError('Unterminated block comment')
            i = end + 2
        elif text.startswith('@"', i):
            i += 2
            while i < len(text):
                if text.startswith('""', i):
                    i += 2
                elif text[i] == '"':
                    i += 1
                    break
                else:
                    i += 1
            else:
                raise ValueError('Unterminated verbatim string')
        elif text[i] in ('"', "'"):
            quote = text[i]
            i += 1
            while i < len(text):
                if text[i] == '\\':
                    i += 2
                elif text[i] == quote:
                    i += 1
                    break
                elif text[i] in '\r\n':
                    raise ValueError('Newline in a regular string/character literal')
                else:
                    i += 1
            else:
                raise ValueError('Unterminated quoted literal')
        else:
            i += 1
            continue
        for j in range(start, min(i, len(text))):
            if text[j] not in '\r\n':
                output[j] = ' '
    return ''.join(output)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--json', type=Path, help='Write an audit report')
    args = parser.parse_args()
    checks = []

    def check(name: str, fn):
        try:
            detail = fn()
            checks.append({'check': name, 'status': 'PASS', 'detail': detail or ''})
        except Exception as error:
            checks.append({'check': name, 'status': 'FAIL', 'detail': str(error)})

    def require(value, message):
        if not value:
            raise AssertionError(message)

    files = sorted(ROOT.glob('src/*.cs')) + sorted(ROOT.glob('tests/*.cs'))
    all_source = '\n'.join(p.read_text(encoding='utf-8-sig') for p in files)
    for path in files:
        def syntax_check(path=path):
            text = path.read_text(encoding='utf-8-sig')
            code = mask_noncode(text)
            stack = []
            matching = {')': '(', ']': '[', '}': '{'}
            for pos, char in enumerate(code):
                if char in '([{':
                    stack.append((char, pos))
                elif char in ')]}':
                    require(stack and stack[-1][0] == matching[char],
                            'Mismatched delimiter at line %s' % (code[:pos].count('\n') + 1))
                    stack.pop()
            require(not stack, 'Unclosed delimiter')
            require(not re.search(r'\$@?"|@\$"', text), 'Interpolated string is newer than C# 5')
            require(not re.search(r'\?\.|\?\?=|\busing\s+var\b|\bnameof\s*\(|\breadonly\s+struct\b|\bnew\s*\(', code),
                    'Selected unsupported post-C# 5 syntax detected')
            return 'Literal/comment boundaries and delimiters; selected language-version tripwires only.'
        check('C# source structure: ' + str(path.relative_to(ROOT)), syntax_check)

    for name in ('Pegline.csproj', 'app.config', 'app.manifest'):
        check('XML parse: ' + name, lambda name=name: ET.parse(ROOT / name) is not None)

    def dependencies():
        tree = ET.parse(ROOT / 'Pegline.csproj')
        tags = [e.tag.rsplit('}', 1)[-1] for e in tree.iter()]
        require('PackageReference' not in tags and 'ProjectReference' not in tags, 'Unexpected package/project dependency')
        require(not (ROOT / 'packages.config').exists(), 'Unexpected package manifest')
        require(not (ROOT / 'node_modules').exists(), 'Unexpected browser/node dependency')
        return 'Framework assemblies only; no third-party package declarations.'
    check('Dependency declarations', dependencies)

    def no_network():
        source = '\n'.join(p.read_text(encoding='utf-8-sig') for p in ROOT.glob('src/*.cs'))
        code = mask_noncode(source)
        require(not re.search(r'\bHttpClient\b|\bWebClient\b|\bWebRequest\b|\bSocket\b|\bTcpClient\b|\bUdpClient\b|\bSystem\.Net\b', code), 'Network client API found')
        require(not re.search(r'https?://|wss?://', source), 'Network endpoint in application source')
        require(not re.search(r'TelemetryClient|ApplicationInsights|SentrySdk|AutoUpdater|UpdateManager', code), 'Telemetry/updater identifier found')
        return 'No matched network-client APIs, endpoints, or telemetry/updater SDK identifiers. External viewers and OS services are outside this check.'
    check('No app network/telemetry/updater surface (static)', no_network)

    def build_contract():
        build = (ROOT / 'build.cmd').read_text(encoding='utf-8-sig')
        launch = (ROOT / 'Build-and-run.cmd').read_text(encoding='utf-8-sig')
        require('/langversion:5' in build and '/platform:x64' in build, 'Wrong build target')
        require('/recurse:src\\*.cs' in build and 'tests\\TestProgram.cs' in build, 'Missing build inputs')
        require(build.lower().count('if errorlevel 1') >= 3, 'Build/test gates missing')
        require('if errorlevel 1' in launch.lower(), 'Launcher failure gate missing')
        require(not re.search(r'curl|wget|Invoke-WebRequest|ExecutionPolicy|nuget\s|dotnet\s+restore', build + launch, re.I), 'Unexpected installation/network step')
        require('bin\\test-results.txt' in build and 'bin\\build.log' in build, 'Evidence logs missing')
        return 'Application compilation, test compilation, and test execution gate the launcher; no package restore/download commands.'
    check('Offline, fail-closed build/launch scripts', build_contract)

    def safety_contract():
        source = '\n'.join(p.read_text(encoding='utf-8-sig') for p in ROOT.glob('src/*.cs'))
        require('File.Replace(temp, path, null)' in source, 'Atomic replacement missing')
        require('RecycleOption.SendToRecycleBin' in source, 'Recycle operation missing')
        require('SafeFiles.WriteChecked(target, bytes, expectedHash' in source and 'store.Backup(file)' in source and '!= loadedHash' in source, 'Save safeguards missing')
        require('FileMode.CreateNew' in source and 'AtomicCreate(path, bytes)' in source and 'expectedHash == null' in source, 'No-clobber creation guard missing')
        require('Directory.Delete(' not in source, 'Recursive application delete present')
        require('File.Delete(path)' not in source and 'File.Delete(card.Path)' not in source, 'Direct source delete present')
        return 'Selected source-level guardrails found; not an execution or race-safety proof.'
    check('File-safety implementation tripwires', safety_contract)

    def regressions():
        model = (ROOT / 'src/EditorModel.cs').read_text(encoding='utf-8-sig')
        surface = (ROOT / 'src/EditorSurface.cs').read_text(encoding='utf-8-sig')
        editor = (ROOT / 'src/EditorWindow.cs').read_text(encoding='utf-8-sig')
        require('Mark NewCopy()' in model and 'c.Id = Guid.NewGuid()' in model, 'New marks lack fresh IDs')
        require('defaults().Clone()' not in surface and 'defaults.Clone()' not in editor, 'Shared style ID can escape')
        require('revision != savedRevision' in model and 'redoBeforeTransaction' in model, 'Revision/gesture history safeguards missing')
        require('Images.PutClipboard(image, null)' in editor, 'Edited copy might attach original file')
        require('BaseImage = new CroppedBitmap(BaseImage' in model, 'Crop no longer preserves editable base/layers')
        return 'Editor regression guardrails present; behavior requires executing the authored tests.'
    check('Editor identity, transaction and edited-copy safeguards (static)', regressions)

    def lifecycle():
        controller = (ROOT / 'src/Controller.cs').read_text(encoding='utf-8-sig')
        watcher = (ROOT / 'src/Storage.cs').read_text(encoding='utf-8-sig')
        imaging = (ROOT / 'src/Imaging.cs').read_text(encoding='utf-8-sig')
        require('handledClipboard.Contains(sequence)' in controller and 'generation != collectionGeneration' in controller, 'Clipboard epoch checks missing')
        require('WaitAsync(stop.Token)' in watcher and 'if (disposed) return' in watcher, 'Watcher cancellation checks missing')
        require('SemaphoreSlim(2)' in watcher and 'pending.Count >= 512' in watcher, 'Watcher concurrency/burst bounds missing')
        require('FromDib(Bitmap bitmap)' in imaging and 'RepairDibAlpha' in imaging, 'DIB pixel preservation path missing')
        require('CanIncludeInClipboardHistory' in imaging and 'CanUploadToCloudClipboard' in imaging, 'Clipboard OS opt-out formats missing')
        return 'Lifecycle/pixel guardrails present, not proof of native behavior or privacy controls being honored by Windows.'
    check('Collection lifecycle and clipboard safeguards (static)', lifecycle)

    def visual_harness():
        smoke = (ROOT / 'tests/VisualSmoke.cs').read_text(encoding='utf-8-sig')
        build = (ROOT / 'build.cmd').read_text(encoding='utf-8-sig')
        require('tests\\VisualSmoke.cs' in build and 'VisualSmoke.Run' in (ROOT / 'tests/TestProgram.cs').read_text(encoding='utf-8-sig'), 'Smoke harness not wired into compiler inputs')
        require('new Store(scratch)' in smoke and 'controller.Start()' not in smoke and 'CaptureDesktop' not in smoke, 'Smoke harness isolation changed')
        require('RenderTargetBitmap' in smoke and 'SnapshotWindow' in smoke, 'Real WPF rendering path missing')
        require((ROOT / 'Verify-Windows.cmd').is_file(), 'Missing verification launcher')
        return 'Optional real-WPF rendering harness wired; not executed in this environment.'
    check('Windows-only visual verification harness (static)', visual_harness)

    test_names = re.findall(r'Test\("([^"\n]+)"', (ROOT / 'tests/TestProgram.cs').read_text(encoding='utf-8-sig'))
    def tests_manifest():
        require(len(test_names) >= 40, 'Missing authored test cases')
        require(len(test_names) == len(set(test_names)), 'Duplicate test case names')
        require('return failed == 0 ? 0 : 1;' in (ROOT / 'tests/TestProgram.cs').read_text(encoding='utf-8-sig'), 'Tests do not propagate failures')
        return '%d uniquely named C# cases inventoried, NOT executed.' % len(test_names)
    check('C# test inventory and failure contract', tests_manifest)
    check('Original icon container', lambda: require((ROOT / 'assets/pegline.ico').read_bytes()[:4] == b'\0\0\x01\0', 'Invalid ICO header'))
    check('Required delivery documents', lambda: require(all((ROOT / n).is_file() for n in ('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'VERIFICATION.md', 'docs/FEATURE_PARITY.md', 'docs/WINDOWS_ACCEPTANCE.md', 'docs/BUILDING.md')), 'Required document missing'))

    result = {
        'scope': 'Static source/configuration audit only; NOT C# compilation or application runtime verification.',
        'platform': platform.system() + ' ' + platform.machine(),
        'tool_availability': {name: bool(shutil.which(name)) for name in ('dotnet', 'mono', 'csc', 'mcs', 'wine')},
        'checks_passed': sum(c['status'] == 'PASS' for c in checks),
        'checks_failed': sum(c['status'] == 'FAIL' for c in checks),
        'authored_csharp_tests': len(test_names), 'executed_csharp_tests': 0,
        'windows_compilation': 'NOT RUN', 'windows_desktop_acceptance': 'NOT RUN',
        'checks': checks, 'authored_test_names': test_names,
        'source_sha256': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files},
    }
    for item in checks:
        print('%s  %s' % (item['status'], item['check']))
        if item['status'] == 'FAIL':
            print('      ' + item['detail'])
    print('\n%d static checks passed; %d failed. C# tests executed: 0.' % (result['checks_passed'], result['checks_failed']))
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return 1 if result['checks_failed'] else 0

if __name__ == '__main__':
    sys.exit(main())
