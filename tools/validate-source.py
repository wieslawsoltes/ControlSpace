#!/usr/bin/env python3
"""Structural checks only; this is not a C# compiler or a workflow execution."""
import argparse
import ast
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT/'docs/verification/static-checks.json')
args = parser.parse_args()
results = []

def record(name, callback):
    try:
        detail = callback()
        results.append({'check': name, 'passed': True, 'detail': detail})
    except Exception as exc:
        results.append({'check': name, 'passed': False, 'detail': str(exc)})

def xml():
    projects = list(ROOT.rglob('*.csproj'))
    for path in projects:
        tree = ET.parse(path)
        for reference in tree.findall('.//ProjectReference'):
            target = (path.parent/reference.attrib['Include']).resolve()
            if not target.is_file():
                raise ValueError(f'Missing project reference: {target}')
    solution = ET.parse(ROOT/'ControlSpace.slnx')
    for project in solution.findall('.//Project'):
        if not (ROOT/project.attrib['Path']).is_file():
            raise ValueError(f'Missing solution project: {project.attrib}')
    for path in [ROOT/'Directory.Build.props', ROOT/'src/ControlSpace.App/App.xaml']:
        ET.parse(path)
    return f'{len(projects)} project XML files, project references, solution, build props and App.xaml parse successfully; no compilation performed.'

def javascript():
    paths = sorted(list((ROOT/'prototype').rglob('*.js')) + list((ROOT/'tests').rglob('*.mjs')))
    for path in paths:
        subprocess.run(['node','--check',str(path)],check=True,capture_output=True,text=True)
    return f'{len(paths)} JavaScript modules parsed by Node.js.'

def python():
    paths = list((ROOT/'tools').rglob('*.py')) + list((ROOT/'tests').rglob('*.py'))
    for path in paths:
        ast.parse(path.read_text(), filename=str(path))
    return f'{len(paths)} Python sources parsed.'

def yaml():
    try:
        import yaml as parser
    except ImportError as exc:
        raise RuntimeError('PyYAML is required for this structural check: python -m pip install PyYAML') from exc
    paths = list((ROOT/'.github').rglob('*.yml'))
    for path in paths:
        data = parser.safe_load(path.read_text())
        if not isinstance(data,dict):
            raise ValueError(f'Expected mapping: {path}')
        if path.parent.name == 'workflows':
            if not data.get('jobs') or not (data.get('on') or data.get(True)):
                raise ValueError(f'Workflow missing trigger/jobs: {path}')
    return f'{len(paths)} YAML files parsed. GitHub workflow/schema/runtime validation remains pending.'

def staging_guard():
    with tempfile.TemporaryDirectory() as directory:
        run = subprocess.run([sys.executable, str(ROOT/'tools/prepare-pages.py'), str(ROOT/'prototype'), str(Path(directory)/'site')],text=True,capture_output=True)
        if run.returncode == 0 or 'prototype will not be substituted' not in run.stderr:
            raise ValueError('Expected prototype rejection; got: '+run.stdout+run.stderr)
        return run.stderr.strip()

record('Project structure and XML', xml)
record('JavaScript syntax', javascript)
record('Python syntax', python)
record('Workflow YAML structure', yaml)
record('Fail-closed Uno Pages staging', staging_guard)
report={'scope':'Structural validation only. Does not compile C#, execute WebGPU, or run GitHub Actions.','passed':sum(r['passed'] for r in results),'failed':sum(not r['passed'] for r in results),'checks':results}
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
sys.exit(1 if report['failed'] else 0)
