#!/usr/bin/env python3
"""Candidate runtime UI regressions. Real file-picker/pointer/keyboard input only."""
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
    checks, errors, logs, actions = [], [], [], []
    failure = None
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width':1600, 'height':1000})
        await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: logs.append({'type':m.type, 'text':m.text}))
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && (' + expression + ')', timeout=20000)
        async def click(identifier):
            await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.some(c=>c.id===id)', arg=identifier, timeout=20000)
            previous = None
            for _ in range(35):
                box = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
                current = tuple(round(box[k], 2) for k in ('x','y','width','height')) if box else None
                if box and current == previous and 0 <= box['x']+box['width']/2 < 1600 and 0 <= box['y']+box['height']/2 < 1000:
                    break
                previous = current
                await page.wait_for_timeout(180)
            else:
                raise AssertionError('Control failed to settle: ' + identifier)
            actions.append(identifier)
            await page.mouse.click(box['x']+box['width']/2, box['y']+box['height']/2)
            await page.wait_for_timeout(350)
        async def fill(identifier, text):
            await click(identifier)
            await page.keyboard.press('Control+A')
            await page.keyboard.press('Backspace')
            await page.keyboard.type(text, delay=20)
            await page.wait_for_function('([id,text])=>window.controlSpaceVerification?.controls.find(c=>c.id===id)?.text===text', arg=[identifier,text], timeout=15000)
        async def point(identifier):
            await wait(f'window.controlSpaceVerification.hmiHits.some(h=>h.id==={json.dumps(identifier)})')
            await page.wait_for_timeout(250)
            h = next(h for h in (await state())['hmiHits'] if h['id'] == identifier)
            return h['x']+h['width']/2, h['y']+h['height']/2
        async def hit(identifier):
            await page.mouse.click(*(await point(identifier)))
            await page.wait_for_timeout(300)
        def passed(name):
            checks.append(name)
            print('PASS', name, flush=True)
        try:
            await page.goto(args.url+'?verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function("window.controlSpaceVerification?.ladderFont.includes('Open Sans')", timeout=120000)
            async with page.expect_file_chooser() as chooser:
                await click('open')
            fixture = Path(__file__).resolve().parents[2] / 'samples/hmi-runtime.controlspace.json'
            await (await chooser.value).set_files(str(fixture))
            await wait("window.controlSpaceVerification.hmiScreens.some(s=>s.Id==='operator')")
            await click('tree-hmi:operator')
            await wait("window.controlSpaceVerification.view==='hmi:operator'")
            passed('configured operator screens load through normal project import')

            await hit('setpoint')
            await wait("window.controlSpaceVerification.hmiSelection[0]==='setpoint'")
            await click('hmi-runtime-settings')
            await wait('window.controlSpaceVerification.dialogOpen')
            revision = (await state())['revision']
            await fill('hmi-options-max','0')
            await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.dialogError.length>0')
            assert (await state())['revision'] == revision
            await fill('hmi-options-max','120')
            await click('program-dialog-apply')
            await wait("!window.controlSpaceVerification.dialogOpen && window.controlSpaceVerification.hmiObjects.find(o=>o.Id==='setpoint').Runtime.Maximum===120")
            passed('runtime settings reject invalid limits and commit valid configuration')

            await click('hmi-runtime')
            await wait('window.controlSpaceVerification.hmiRuntime')
            revision = (await state())['revision']
            await hit('setpoint')
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-runtime-value','121')
            await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.dialogError.length>0')
            assert (await state())['values']['Speed_Setpoint'] == 65
            await fill('hmi-runtime-value','90.5')
            await page.keyboard.press('Enter')
            await wait('!window.controlSpaceVerification.dialogOpen && window.controlSpaceVerification.values.Speed_Setpoint===90.5')
            assert (await state())['revision'] == revision
            passed('numeric validation and Enter write affect values but not project revision')

            await hit('setpoint')
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-runtime-value','20')
            await page.keyboard.press('Escape')
            await wait('!window.controlSpaceVerification.dialogOpen')
            assert (await state())['values']['Speed_Setpoint'] == 90.5
            passed('Escape cancels an uncommitted numeric value')

            x,y = await point('preset')
            await page.mouse.move(x,y)
            await page.mouse.down()
            await page.mouse.move(x+240,y-100,steps=8)
            await page.mouse.up()
            await page.wait_for_timeout(450)
            assert (await state())['values']['Speed_Setpoint'] == 90.5
            await hit('preset')
            await wait('window.controlSpaceVerification.values.Speed_Setpoint===75')
            passed('action buttons require release over the pressed object and write once')

            await hit('details')
            await wait("window.controlSpaceVerification.hmiRuntime && window.controlSpaceVerification.view==='hmi:operator-details'")
            assert (await state())['values']['Speed_Setpoint'] == 75
            await hit('toggle')
            await wait('window.controlSpaceVerification.values.Manual_Enable===1')
            await hit('toggle')
            await wait('window.controlSpaceVerification.values.Manual_Enable===0')
            await hit('set-manual')
            await wait('window.controlSpaceVerification.values.Manual_Enable===1')
            await hit('reset-manual')
            await wait('window.controlSpaceVerification.values.Manual_Enable===0')
            await hit('back')
            await wait("window.controlSpaceVerification.hmiRuntime && window.controlSpaceVerification.view==='hmi:operator'")
            assert (await state())['revision'] == revision
            passed('navigation history and bit actions operate without rebuilding the project')

            await click('run')
            await wait("window.controlSpaceVerification.state==='Running'")
            await page.mouse.move(*(await point('hmi-start')))
            await page.mouse.down()
            await wait('window.controlSpaceVerification.values.Motor_Run===1')
            await page.mouse.up()
            await wait('window.controlSpaceVerification.values.Start_PB===0')
            await hit('setpoint')
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-runtime-value','42')
            await click('program-dialog-apply')
            await wait('!window.controlSpaceVerification.dialogOpen && window.controlSpaceVerification.values.Speed_Actual===42')
            assert (await state())['revision'] == revision
            await hit('setpoint')
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-runtime-value','99')
            await page.keyboard.press('Escape')
            await wait('!window.controlSpaceVerification.dialogOpen')
            after_cancel = await state()
            assert after_cancel['state'] == 'Running' and after_cancel['values']['Speed_Setpoint'] == 42
            passed('numeric writes work across scans; Escape cancels without stopping the controller')
            await page.screenshot(path=str(args.output/'uno-hmi-runtime.png'))
            await click('stop')
            await click('hmi-design')
            await wait('!window.controlSpaceVerification.hmiRuntime')

            async with page.expect_download() as download:
                await click('save')
            saved = await download.value
            path = args.output/'runtime-export.controlspace.json'
            await saved.save_as(str(path))
            exported = json.loads(path.read_text())
            item = next(o for s in exported['screens'] if s['id']=='operator' for o in s['objects'] if o['id']=='setpoint')
            assert item['runtime']['maximum'] == 120 and item['runtime']['ioMode'] == 'InputOutput'
            assert exported['revision'] == revision
            passed('project export retains configured behavior without exporting runtime values')
            assert not errors, errors
            assert not [m for m in logs if m['type']=='error'], logs
        except Exception:
            failure=traceback.format_exc()
            raise
        finally:
            await page.screenshot(path=str(args.output/'uno-hmi-runtime-last.png'))
            (args.output/'hmi-runtime.json').write_text(json.dumps({'checks':checks,'errors':errors,'console':logs,'actions':actions,'failure':failure,'state':await state()},indent=2)+'\n')
            print(f'Uno HMI runtime workflows: {len(checks)} passed',flush=True)
            await browser.close()

asyncio.run(main())
