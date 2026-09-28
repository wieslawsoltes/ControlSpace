#!/usr/bin/env python3
"""Stage only a genuine Uno publish output at the site root; fail closed otherwise."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
from datetime import datetime, timezone
ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('publish', type=Path)
parser.add_argument('destination', type=Path)
args=parser.parse_args()
source=args.publish.resolve();destination=args.destination.resolve()
if not source.is_dir(): raise SystemExit('Uno publish directory does not exist.')
if source==destination or source in destination.parents: raise SystemExit('Staging destination must be outside the publish directory.')
candidates=[]
for index in source.rglob('index.html'):
    content=index.read_text(errors='replace').lower()
    if ('uno' in content or 'dotnet' in content) and 'interaction prototype' not in content:
        candidates.append(index)
if not candidates: raise SystemExit('No Uno index.html found. The prototype will not be substituted.')
index=min(candidates,key=lambda p:len(p.parts))
webroot=index.parent
if not any('.wasm' in path.name for path in webroot.rglob('*') if path.is_file()):
    raise SystemExit('No WebAssembly binary in the Uno webroot. Deployment refused.')
if destination.exists(): shutil.rmtree(destination)
shutil.copytree(webroot,destination)
shutil.copytree(ROOT/'prototype',destination/'prototype')
shutil.copytree(ROOT/'docs',destination/'docs')
(destination/'.nojekyll').write_text('')
commit=os.environ.get('GITHUB_SHA','')
if not commit:
    try: commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    except (OSError,subprocess.CalledProcessError): commit='local-uncommitted'
info={'application':'ControlSpace','runtime':'Uno Platform / Skia / .NET WebAssembly','commit':commit,'builtAt':datetime.now(timezone.utc).isoformat(),'prototypePath':'prototype/'}
(destination/'build-info.json').write_text(json.dumps(info,indent=2)+'\n')
print(f'Uno webroot: {webroot}\nStaged: {destination}')
