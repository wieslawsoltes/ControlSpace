#!/usr/bin/env python3
"""Pointer-driven tests of the compiled Uno UI. Probe exposes read-only bounds/state only."""
import argparse
import asyncio
import json
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
        # Parse the .NET-owned JSON string without exposing any mutation endpoint.
        await page.add_init_script("""Object.defineProperty(window, 'controlSpaceVerification', {
            get() { return JSON.parse(window.controlSpaceVerificationJson || 'null'); }
        });""")
        logs, errors, checks = [], [], []
        page.on('console', lambda message: logs.append({'type': message.type, 'text': message.text}))
        page.on('pageerror', lambda error: errors.append(str(error)))
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && (' + expression + ')', timeout=15000)
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def click(identifier):
            await wait(f"window.controlSpaceVerification.controls.some(c=>c.id==={json.dumps(identifier)})")
            bounds = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
            await page.mouse.click(bounds['x'] + bounds['width'] / 2, bounds['y'] + bounds['height'] / 2)
            await page.wait_for_timeout(250)
        def passed(name):
            checks.append(name)
            print('PASS', name, flush=True)
        try:
            await page.goto(args.url + ('&' if '?' in args.url else '?') + 'verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function('!!window.controlSpaceVerification', timeout=120000)
            assert not await page.evaluate('!!window.ControlSpacePrototype')
            await wait("window.controlSpaceVerification.view==='block:main'")
            passed('genuine Uno startup and initial editor')
            await click('tree-tags'); await wait("window.controlSpaceVerification.view==='tags'")
            await click('tree-block:speed'); await wait("window.controlSpaceVerification.view==='block:speed'")
            passed('project navigation opens real document tabs')
            await click('scl-source'); await page.keyboard.press('Control+End'); await page.keyboard.type('\n// UI draft survives navigation')
            await click('tab-tags'); await click('tab-block:speed')
            await wait("window.controlSpaceVerification.source.includes('UI draft survives navigation')")
            passed('SCL draft commits and survives document switching')
            await click('close-tags'); await wait("window.controlSpaceVerification.documents.every(d=>d.Id!=='tags')")
            await wait("window.controlSpaceVerification.source.includes('UI draft survives navigation')")
            passed('closing inactive editor preserves active source')
            await click('close-block:speed'); await wait("window.controlSpaceVerification.view==='block:main'")
            assert 'UI draft' not in (await state())['blockSources']['main']
            passed('closing active source never overwrites neighboring block')
            await click('tree-blocks'); await wait("!window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            await click('tree-blocks'); await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            passed('project folders collapse and expand')
            await click('project-search'); await page.keyboard.type('Speed'); await page.wait_for_timeout(300)
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tree-blocks') && !window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            await page.keyboard.press('Control+A'); await page.keyboard.press('Backspace')
            passed('project search retains ancestors and filters siblings')
            before = (await state())['layout']['ProjectWidth']
            await click('project-splitter'); await page.keyboard.press('ArrowRight')
            await wait(f'window.controlSpaceVerification.layout.ProjectWidth>{before}')
            await page.keyboard.press('Home')
            passed('keyboard splitter resize and reset')
            await click('maximize-editor'); await wait('window.controlSpaceVerification.maximized')
            await click('maximize-editor'); await wait('!window.controlSpaceVerification.maximized')
            passed('maximize and restore work area')
            await click('task-card-Libraries'); await wait("window.controlSpaceVerification.layout.TaskCard==='Libraries'")
            await click('task-card-Testing'); await wait("window.controlSpaceVerification.layout.TaskCard==='Testing'")
            await click('task-card-Instructions')
            await click('inspector-Properties'); await wait("window.controlSpaceVerification.layout.InspectorTab==='Properties'")
            await click('inspector-Info')
            passed('task cards and inspector tabs')
            await click('compile'); await wait("window.controlSpaceVerification.state==='Stopped'")
            await click('run'); await wait('window.controlSpaceVerification.cycle>0')
            await click('stop'); await wait("window.controlSpaceVerification.state==='Stopped'")
            passed('compile run and stop through real toolbar')
            await click('portal-view'); await wait('window.controlSpaceVerification.portal')
            await page.screenshot(path=str(args.output / 'uno-portal.png'))
            await click('project-view'); await wait('!window.controlSpaceVerification.portal')
            passed('Portal view returns to active project editor')
            await click('tree-tags'); await click('tab-block:main')
            await page.screenshot(path=str(args.output / 'uno-workspace.png'))
            await page.set_viewport_size({'width': 700, 'height': 860}); await page.wait_for_timeout(400)
            await click('project-pane-toggle'); await wait('window.controlSpaceVerification.projectDrawer')
            await click('tree-tags'); await wait("window.controlSpaceVerification.view==='tags'")
            await click('task-card-Libraries'); await wait('window.controlSpaceVerification.taskDrawer')
            await page.screenshot(path=str(args.output / 'uno-compact.png'))
            passed('compact viewport has usable project and task drawers')
            await page.set_viewport_size({'width':1600,'height':1000}); await page.wait_for_timeout(500)
            await click('task-card-Testing'); await page.wait_for_timeout(900)
            await page.reload(wait_until='domcontentloaded')
            await page.wait_for_function('!!window.controlSpaceVerification', timeout=120000)
            await wait("window.controlSpaceVerification.layout.TaskCard==='Testing'")
            passed('window layout persists across reload independently of project')
            if errors: raise AssertionError('\n'.join(errors))
        finally:
            await page.screenshot(path=str(args.output / 'uno-last-state.png'))
            final_state = await state()
            (args.output / 'logs.json').write_text(json.dumps({'checks': checks, 'console': logs, 'errors': errors, 'state': final_state}, indent=2) + '\n')
            print('UI evidence:', json.dumps({'checks': checks, 'errors': errors, 'consoleTail': logs[-10:], 'state': final_state}), flush=True)
            print(f'Uno UI workflows: {len(checks)} passed', flush=True)
            await browser.close()
asyncio.run(main())
