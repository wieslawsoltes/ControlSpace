#!/usr/bin/env python3
"""Real Uno UI scheduling/retention regressions. Not a browser FPS or GPU benchmark."""
import argparse
import asyncio
import json
import traceback
from pathlib import Path
from playwright.async_api import async_playwright

parser = argparse.ArgumentParser()
parser.add_argument('url')
parser.add_argument('--output', type=Path, default=Path('artifacts/uno-verification'))
args = parser.parse_args()

async def main():
    args.output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width': 1600, 'height': 1000})
        await page.add_init_script("""Object.defineProperty(window, 'controlSpaceVerification', {
            get() { return JSON.parse(window.controlSpaceVerificationJson || 'null'); }
        });""")
        errors, logs, checks, samples = [], [], [], {}
        failure = None
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.on('console', lambda message: logs.append({'type': message.type, 'text': message.text}))
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && (' + expression + ')', timeout=15000)
        async def click(identifier):
            await wait(f"window.controlSpaceVerification.controls.some(c=>c.id==={json.dumps(identifier)})")
            previous = None
            for _ in range(15):
                item = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
                if item == previous:
                    break
                previous = item
                await page.wait_for_timeout(160)
            assert item is not None, identifier
            await page.mouse.click(item['x'] + item['width'] / 2, item['y'] + item['height'] / 2)
            await page.wait_for_timeout(350)
        def passed(name):
            checks.append(name)
            print('PASS', name, flush=True)
        try:
            await page.goto(args.url + '?verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function('window.controlSpaceVerification?.performance?.paintCount>0', timeout=120000)
            await wait("window.controlSpaceVerification.ladderFont.includes('Open Sans')")
            # Allow the one initial recovery write, then sample a second autosave interval.
            await wait('window.controlSpaceVerification.performance.recoverySerializations>0')
            await page.wait_for_timeout(500)
            before = await state()
            await page.wait_for_timeout(3400)
            after = await state()
            assert after['performance']['recoverySerializations'] == before['performance']['recoverySerializations']
            assert after['performance']['paintCount'] - before['performance']['paintCount'] <= 1
            samples['idle'] = {'before': before['performance'], 'after': after['performance']}
            passed('idle autosave skips serialization and the graphics scene stays cached')

            before = (await state())['performance']
            for _ in range(3):
                await click('compile')
            after = (await state())['performance']
            for key in ['editorBuilds', 'paletteBuilds', 'toolbarBuilds', 'tabCreations']:
                assert after[key] == before[key], (key, before, after)
            passed('recompilation retains editor toolbar palette and document tab controls')

            await click('run')
            await wait('window.controlSpaceVerification.cycle>=4')
            before = await state()
            # Count completed virtual scans; shared-runner wall-clock scheduling
            # is not the performance mechanism under test. Keep a bounded wait.
            await wait(f"window.controlSpaceVerification.cycle>={before['cycle'] + 10}")
            after = await state()
            assert after['cycle'] >= before['cycle'] + 10
            assert after['performance']['paintCount'] - before['performance']['paintCount'] <= 1
            assert after['performance']['snapshotCopies'] == 0
            samples['staticRun'] = {'before': before['performance'], 'after': after['performance'], 'cycles': after['cycle'] - before['cycle']}
            passed('unchanged running PLC image does not repaint or copy a snapshot')

            count = after['performance']['paintCount']
            await click('input-Start_PB')
            await wait('window.controlSpaceVerification.values.Motor_Run===1')
            await wait(f'window.controlSpaceVerification.performance.paintCount>{count}')
            assert (await state())['performance']['snapshotCopies'] == 0
            await click('stop')
            passed('a changed PLC input repaints current values without a snapshot copy')
            await page.screenshot(path=str(args.output / 'uno-performance-workspace.png'))

            await click('tree-tags')
            await wait("window.controlSpaceVerification.view==='tags'")
            await click('tab-block:main')
            await wait("window.controlSpaceVerification.view==='block:main'")
            before = (await state())['performance']
            for _ in range(3):
                await click('tab-tags')
                await click('tab-block:main')
            after = (await state())['performance']
            assert after['tabCreations'] == before['tabCreations']
            assert after['paletteBuilds'] == before['paletteBuilds']
            passed('tab switching reuses open tabs and context-independent instruction palette')

            await click('tree-watch')
            await wait("window.controlSpaceVerification.view==='watch'")
            await click('run')
            await wait('window.controlSpaceVerification.cycle>3')
            before = await state()
            await wait(f"window.controlSpaceVerification.cycle>={before['cycle'] + 10}")
            after = await state()
            assert after['cycle'] >= before['cycle'] + 10
            assert after['performance']['paintCount'] == before['performance']['paintCount']
            assert after['performance']['renderRequests'] == before['performance']['renderRequests']
            await click('stop')
            passed('watch-table monitoring never invalidates the hidden ladder canvas')

            await click('tree-block:speed')
            await wait("window.controlSpaceVerification.view==='block:speed'")
            await click('scl-source')
            before = (await state())['performance']['recoverySerializations']
            await page.keyboard.press('Control+End')
            await page.keyboard.press('Enter')
            await page.keyboard.type('// recovery dirty gate test', delay=25)
            await wait("window.controlSpaceVerification.source.includes('recovery dirty gate test')")
            await wait(f'window.controlSpaceVerification.performance.recoverySerializations>{before}')
            # A timer may have persisted a partial draft while keys were arriving.
            # Let the last dirty draft flush before measuring a full idle interval.
            await page.wait_for_timeout(3400)
            after = (await state())['performance']['recoverySerializations']
            await page.wait_for_timeout(3400)
            assert (await state())['performance']['recoverySerializations'] == after
            passed('uncommitted SCL drafts trigger recovery and settled drafts do not repeat writes')
            # Check both the page exception stream and logged .NET startup/runtime errors.
            assert not errors, errors
            assert not [message for message in logs if message['type'] == 'error'], logs
        except Exception:
            failure = traceback.format_exc()
            raise
        finally:
            await page.screenshot(path=str(args.output / 'uno-performance-last.png'))
            final = await state()
            (args.output / 'performance-ui.json').write_text(json.dumps({
                'checks': checks, 'errors': errors, 'console': logs, 'samples': samples,
                'failure': failure, 'finalPerformance': (final or {}).get('performance'),
                'finalCycle': (final or {}).get('cycle'), 'finalState': (final or {}).get('state')
            }, indent=2) + '\n')
            print(f'Uno performance mechanism workflows: {len(checks)} passed', flush=True)
            await browser.close()
asyncio.run(main())
