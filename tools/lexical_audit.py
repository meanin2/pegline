#!/usr/bin/env python3
"""Independent Pygments lexer/delimiter check; never a C# parser or compiler.
Maintainer-only optional dependency: Pygments. Not needed by Windows builds.
"""
import hashlib
import json
from pathlib import Path
from pygments import lex
from pygments.lexers import CSharpLexer
from pygments.token import Comment, Error, String

root = Path(__file__).resolve().parents[1]
results = []
for path in sorted(list(root.glob('src/*.cs')) + list(root.glob('tests/*.cs'))):
    stack, issues = [], []
    for token, text in lex(path.read_text(encoding='utf-8'), CSharpLexer()):
        if token in Error:
            issues.append('Lexer error token: ' + repr(text))
        if token in Comment or token in String:
            continue
        for char in text:
            if char in '({[':
                stack.append(char)
            elif char in ')}]':
                if not stack or stack.pop() != {')': '(', '}': '{', ']': '['}[char]:
                    issues.append('Delimiter mismatch')
    if stack:
        issues.append('Unclosed delimiters: ' + ''.join(stack))
    results.append({'file': str(path.relative_to(root)), 'status': 'FAIL' if issues else 'PASS', 'issues': issues,
                    'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
report = {'scope': 'Pygments lexical/delimiter scan only; not C# parsing, type checking, compilation or execution.',
          'files': results, 'passed': sum(x['status'] == 'PASS' for x in results), 'failed': sum(x['status'] == 'FAIL' for x in results),
          'csharp_tests_executed': 0}
(root / 'docs/independent-lexical-check.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps({k: v for k, v in report.items() if k != 'files'}, indent=2))
raise SystemExit(bool(report['failed']))
