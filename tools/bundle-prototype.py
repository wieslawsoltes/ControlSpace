#!/usr/bin/env python3
"""Bundle the explicitly labelled JavaScript prototype; never substitutes for Uno."""
from pathlib import Path
import re
import argparse
ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'ControlSpace-prototype.html')
args = parser.parse_args()
files = ['engine/model.js','engine/languages.js','engine/runtime.js','engine/workspace.js','ui/renderer.js','ui/app.js']
parts=[]
for name in files:
    source=(ROOT/'prototype'/name).read_text()
    source=re.sub(r'^import .*?;\s*$', '', source, flags=re.M)
    source=re.sub(r'^export (?=(?:class|const|function)\b)', '', source, flags=re.M)
    parts.append(f'// ---- {name} ----\n{source}')
script='\n'.join(parts).replace('</script','<\\/script')
html=(ROOT/'prototype/index.html').read_text()
html=html.replace('<link rel="stylesheet" href="ui/styles.css">', '<style>'+ (ROOT/'prototype/ui/styles.css').read_text() + '</style>')
html=html.replace('<script type="module" src="ui/app.js"></script>', '<script type="module">\n'+script+'\n</script>')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(html)
print(f'{args.output}: {args.output.stat().st_size:,} bytes')
