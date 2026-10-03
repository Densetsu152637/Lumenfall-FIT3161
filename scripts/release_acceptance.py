#!/usr/bin/env python3
"""Create/check explicit licensed-player evidence; never infer readiness from offline checks."""
import argparse
from datetime import datetime
import json
import math
from pathlib import Path
import re
import sys

MATRIX = Path(__file__).resolve().parents[1] / 'docs/release/chapter-one-acceptance.json'
STATUSES = {'pending', 'pass', 'fail', 'blocked'}


def load_matrix(path=MATRIX):
    matrix = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    ids = [c['id'] for c in matrix['checks']]
    if matrix.get('schema_version') != 1 or len(ids) != len(set(ids)) or not ids:
        raise ValueError('Invalid acceptance matrix schema or duplicate/empty checks')
    return matrix


def template(matrix, platform, commit, build_id, slot_id):
    return {
        'schema_version': 1,
        'environment': {'unity_version': matrix['unity_version'], 'platform': platform,
                        'commit': commit, 'build_id': build_id, 'slot_id': slot_id,
                        'tester': '', 'tested_at_utc': '', 'device_os': ''},
        'checks': [{'id': c['id'], 'status': 'pending', 'evidence': ''} for c in matrix['checks']],
        'performance': {'hardware': '', 'scenario': '', 'captures': [],
                        'metrics': [{'name': name, 'diagnostics': mode, 'samples': None,
                                     'mean_ms': None, 'max_ms': None, 'budget_mean_ms': None,
                                     'budget_max_ms': None, 'budget_approval': ''}
                                    for name in matrix['required_metrics']
                                    for mode in ('disabled', 'enabled')]},
    }


def nonblank(value):
    return isinstance(value, str) and bool(value.strip())


def number(value, positive=False):
    return type(value) in (int, float) and math.isfinite(value) and (value > 0 if positive else value >= 0)


def validate(evidence, matrix):
    """Return all blockers. A successful gate requires every manual player check with evidence."""
    errors = []
    if not isinstance(evidence, dict) or evidence.get('schema_version') != 1:
        return ['Evidence schema_version must be 1']
    env = evidence.get('environment', {})
    if not isinstance(env, dict):
        return ['environment must be an object']
    for key in ('platform', 'commit', 'build_id', 'slot_id', 'tester', 'tested_at_utc', 'device_os'):
        if not nonblank(env.get(key)):
            errors.append(f'environment.{key} is required')
    if env.get('unity_version') != matrix['unity_version']:
        errors.append(f"Unity must match {matrix['unity_version']}")
    if not isinstance(env.get('commit'), str) or not re.fullmatch(r'[0-9a-fA-F]{40}', env['commit']):
        errors.append('commit must be the full 40-character Git SHA')
    try:
        date = datetime.fromisoformat(env.get('tested_at_utc', '').replace('Z', '+00:00'))
        if date.utcoffset() is None or date.utcoffset().total_seconds() != 0:
            errors.append('tested_at_utc must include UTC timezone')
    except (ValueError, TypeError, AttributeError):
        errors.append('tested_at_utc must be an ISO 8601 UTC timestamp')
    checks = evidence.get('checks')
    if not isinstance(checks, list) or any(not isinstance(c, dict) for c in checks):
        return errors + ['checks must be an array of objects']
    expected = {c['id'] for c in matrix['checks']}
    seen = set()
    for check in checks:
        check_id = check.get('id')
        if not isinstance(check_id, str) or check_id not in expected:
            errors.append(f'Unknown check {check_id!r}')
            continue
        if check_id in seen:
            errors.append(f'Duplicate check {check_id}')
        seen.add(check_id)
        status = check.get('status')
        if not isinstance(status, str) or status not in STATUSES:
            errors.append(f'{check_id}: invalid status {status!r}')
        elif status != 'pass':
            errors.append(f'{check_id}: {status}')
        if status == 'pass' and not nonblank(check.get('evidence')):
            errors.append(f'{check_id}: pass requires evidence path/log and observed result')
    errors.extend(f'{i}: missing check' for i in sorted(expected-seen))
    perf = next((c for c in checks if c.get('id') == 'performance'), {})
    if perf.get('status') == 'pass':
        errors.extend(validate_performance(evidence.get('performance'), matrix))
    return errors


def validate_performance(perf, matrix):
    if not isinstance(perf, dict):
        return ['performance evidence object is required']
    errors = []
    for key in ('hardware', 'scenario'):
        if not nonblank(perf.get(key)):
            errors.append(f'performance.{key} is required')
    captures = perf.get('captures')
    if not isinstance(captures, list) or not captures or not all(nonblank(p) for p in captures):
        errors.append('performance.captures must list actual disabled/enabled captures')
    metrics = perf.get('metrics')
    if not isinstance(metrics, list) or any(not isinstance(m, dict) for m in metrics):
        return errors + ['performance.metrics must be an array of objects']
    expected = {(metric, mode) for metric in matrix['required_metrics'] for mode in ('disabled', 'enabled')}
    seen = set()
    for metric in metrics:
        name, mode = metric.get('name'), metric.get('diagnostics')
        if not isinstance(name, str) or not isinstance(mode, str):
            errors.append('metric name/diagnostics must be strings')
            continue
        key = (name, mode)
        if key not in expected or key in seen:
            errors.append(f'Unexpected/duplicate metric {key}')
        seen.add(key)
        if type(metric.get('samples')) is not int or metric['samples'] < 1:
            errors.append(f'{key}: positive sample count required')
        for field in ('mean_ms', 'max_ms', 'budget_mean_ms', 'budget_max_ms'):
            if not number(metric.get(field), field.startswith('budget_')):
                errors.append(f'{key}: invalid {field}')
        if number(metric.get('mean_ms')) and number(metric.get('max_ms')) and metric['mean_ms'] > metric['max_ms']:
            errors.append(f'{key}: mean exceeds maximum')
        for value, budget in (('mean_ms','budget_mean_ms'), ('max_ms','budget_max_ms')):
            if number(metric.get(value)) and number(metric.get(budget), True) and metric[value] > metric[budget]:
                errors.append(f'{key}: {value} exceeds approved budget')
        if not nonblank(metric.get('budget_approval')):
            errors.append(f'{key}: budget_approval reference is required')
    errors.extend(f'{key}: missing performance metric' for key in sorted(expected-seen))
    return errors


def report(evidence, matrix, errors):
    env = evidence.get('environment', {})
    if not isinstance(env, dict):
        env = {'invalid_environment': repr(env)}
    lines = ['# Chapter 1 acceptance evidence', '',
             'Gate: ' + ('PASS (recorded licensed-player evidence)' if not errors else 'INCOMPLETE / FAILED'), '',
             'This gate validates recorded evidence. It does not execute Unity or independently verify human observations.', '',
             '| Environment | Value |', '|---|---|']
    lines.extend(f'| {key} | {value} |' for key, value in env.items())
    lines += ['', '| Check | Status | Evidence |', '|---|---|---|']
    for check in evidence.get('checks', []):
        lines.append('| ' + ' | '.join(str(check.get(k, '')).replace('|', '\\|').replace('\n',' ') for k in ('id','status','evidence')) + ' |')
    if errors:
        lines += ['', 'Blockers:', ''] + ['- '+error for error in errors]
    return '\n'.join(lines)+'\n'


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    init = sub.add_parser('init', help='Create pending evidence for one platform/build/slot')
    for key in ('platform', 'commit', 'build-id', 'slot-id', 'output'):
        init.add_argument('--'+key, required=True)
    check = sub.add_parser('check', help='Validate explicit player evidence; nonzero if incomplete')
    check.add_argument('evidence', type=Path)
    check.add_argument('--report', type=Path)
    args = parser.parse_args(argv)
    try:
        matrix = load_matrix()
        if args.command == 'init':
            path = Path(args.output)
            path.parent.mkdir(parents=True, exist_ok=True)
            # Refuse overwrite so new runs cannot erase collected evidence.
            with path.open('x', encoding='utf-8') as output:
                json.dump(template(matrix, args.platform, args.commit, args.build_id, args.slot_id), output, indent=2)
                output.write('\n')
            print(f'Pending acceptance evidence: {path}')
            return 0
        evidence = json.loads(args.evidence.read_text(encoding='utf-8-sig'))
        errors = validate(evidence, matrix)
        if args.report and isinstance(evidence, dict) and isinstance(evidence.get('checks'), list) and all(isinstance(c, dict) for c in evidence['checks']):
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(report(evidence, matrix, errors), encoding='utf-8')
        for error in errors:
            print(error, file=sys.stderr)
        if not errors:
            print('PASS: all recorded licensed-player acceptance evidence is complete')
        return 1 if errors else 0
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f'Invalid acceptance input: {exc}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
