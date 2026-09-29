#!/usr/bin/env python3
"""Report same-runner warm medians; timing changes are evidence, not flaky CI assertions."""
import json
import sys
from pathlib import Path
before, after = (json.loads(Path(p).read_text()) for p in sys.argv[1:3])
old = {v['name']:v for v in before['results']}
lines = ['# ControlSpace performance comparison', '',
    f"Baseline: `{before['revision']}`. Candidate: `{after['revision']}`.",
    '', after['scenario'], '',
    '| Scenario | Before ms/op | After ms/op | Speed ratio | Before bytes/op | After bytes/op |',
    '| --- | ---: | ---: | ---: | ---: | ---: |']
for new in after['results']:
    previous = old[new['name']]
    ratio = previous['milliseconds'] / max(new['milliseconds'], 1e-9)
    lines.append(f"| {new['name']} | {previous['milliseconds']:.6f} | {new['milliseconds']:.6f} | {ratio:.2f}× | {previous['allocatedBytes']:.0f} | {new['allocatedBytes']:.0f} |")
lines += ['', 'The harness is identical in both checkouts. Seven warmed batches use the same runner and SDK. Allocation counts cover the calling thread; medians are reported independently. These are native .NET CPU-raster/engine microbenchmarks, not browser FPS, GPU time, native TIA measurements, or an overall application speedup. Cold-start/network costs are excluded.', '',
    f"Environment: {after['runtime']}; {after['os']}; {after['architecture']}; {after['processors']} logical processors."]
text='\n'.join(lines)+'\n'
Path(sys.argv[3]).write_text(text)
print(text)
