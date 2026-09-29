#!/usr/bin/env python3
"""Real Uno project navigation: pointer, keyboard and ordinary project-file import.
Counters verify bounded control creation, not browser FPS or original TIA speed."""
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
    checks, errors, logs, samples = [], [], [], {}
    failure = None
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width': 1600, 'height': 1000})
        await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: logs.append({'type': m.type, 'text': m.text}))
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && (' + expression + ')', timeout=20000)
        async def click(identifier):
            await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.some(c=>c.id===id && c.width>0 && c.height>0)', arg=identifier, timeout=20000)
            previous = None
            for _ in range(25):
                b = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
                coords = tuple(round(b[k], 2) for k in ('x', 'y', 'width', 'height')) if b else None
                if coords is not None and coords == previous:
                    break
                previous = coords
                await page.wait_for_timeout(180)
            assert b and 0 <= b['y'] < 1000, (identifier, b)
            await page.mouse.click(b['x'] + b['width']/2, b['y'] + b['height']/2)
            await page.wait_for_timeout(350)
        async def search(text):
            await click('project-search')
            await page.keyboard.press('Control+A')
            await page.keyboard.press('Backspace')
            await page.keyboard.type(text, delay=25)
            await wait('window.controlSpaceVerification.navigation.filter===' + json.dumps(text))
        def passed(name):
            checks.append(name)
            print('PASS', name, flush=True)
        try:
            await page.goto(args.url+'?verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function('!!window.controlSpaceVerification?.navigation', timeout=120000)
            await click('tree-block:main')
            before = (await state())['navigation']
            for _ in range(3):
                await click('compile')
            after = (await state())['navigation']
            assert after['indexBuilds'] == before['indexBuilds']
            assert after['projectionBuilds'] == before['projectionBuilds']
            assert after['creations'] == before['creations']
            passed('recompilation retains navigation index and row controls')

            await click('tree-blocks')
            await wait("!window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            await search('Speed')
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:speed')")
            assert (await state())['navigation']['visible'] == 4
            await page.keyboard.press('Escape')
            await wait("window.controlSpaceVerification.navigation.filter===''")
            assert not any(c['id'] == 'tree-block:main' for c in (await state())['controls'])
            passed('filtered ancestors do not overwrite collapsed folder state')

            await click('tree-reveal-active')
            await wait("window.controlSpaceVerification.navigation.selected==='block:main'")
            await page.keyboard.press('ArrowLeft')
            await wait("window.controlSpaceVerification.navigation.selected==='blocks'")
            await page.keyboard.press('ArrowLeft')
            await wait("!window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            await page.keyboard.press('ArrowRight')
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tree-block:main')")
            await page.keyboard.press('ArrowRight')
            await wait("window.controlSpaceVerification.navigation.selected==='add-block'")
            assert (await state())['view'] == 'block:main'
            passed('parent child keys expand collapse and move without opening editors')

            await search('no such project object')
            assert (await state())['navigation']['visible'] == 0
            assert (await state())['navigation']['rows'] == 0
            await click('tree-clear-search')
            await wait("window.controlSpaceVerification.navigation.filter===''")
            await page.keyboard.type('Speed', delay=25)
            await wait("window.controlSpaceVerification.navigation.filter==='Speed'")
            passed('empty search has feedback and clearing keeps search keyboard focus')
            await click('tree-clear-search')

            fixture = {'format':'controlspace.project','version':1,'id':'navigation-scale', 'name':'Navigation scale', 'revision':0,
                'tags':[], 'blocks':[{'id':f'b{i}','name':f'Program_{i:04d}','number':i+1,'language':'SCL','cyclic':False,'networks':[],'source':''} for i in range(1000)],
                'devices':[], 'links':[], 'screens':[{'id':f's{i}','name':f'Screen_{i:04d}','width':800,'height':480,'objects':[]} for i in range(1000)]}
            path = args.output/'navigation-scale.json'
            path.write_text(json.dumps(fixture))
            async with page.expect_file_chooser() as chooser:
                await click('open')
            await (await chooser.value).set_files(str(path))
            await wait("window.controlSpaceVerification.view==='block:b0' && window.controlSpaceVerification.navigation.visible>2000")
            nav = (await state())['navigation']
            assert nav['rows'] <= 40, nav
            samples['largeImport'] = nav
            passed('1000 blocks and 1000 screens import with a bounded visible-row pool')

            await click('tree-project') # collapse and restore root without constructing descendants
            await wait('window.controlSpaceVerification.navigation.visible===1')
            await click('tree-project')
            await wait('window.controlSpaceVerification.navigation.visible>2000')
            await page.keyboard.press('End')
            await wait("window.controlSpaceVerification.navigation.selected==='library'")
            await page.keyboard.press('ArrowUp')
            await wait("window.controlSpaceVerification.navigation.selected==='hmi:s999'")
            await page.keyboard.press('Enter')
            await wait("window.controlSpaceVerification.view==='hmi:s999'")
            nav = (await state())['navigation']
            assert nav['rows'] <= 40
            samples['endOfOutline'] = nav
            passed('keyboard navigation reaches and opens the final offscreen HMI screen')

            await search('Program_0999')
            await wait('window.controlSpaceVerification.navigation.visible===4')
            await click('tree-block:b999')
            await wait("window.controlSpaceVerification.view==='block:b999'")
            await click('tree-clear-search')
            await click('tree-reveal-active')
            await wait("window.controlSpaceVerification.navigation.selected==='block:b999'")
            await page.screenshot(path=str(args.output/'uno-navigation-scale.png'))
            nav = (await state())['navigation']
            assert nav['rows'] <= 40
            samples['revealActive'] = nav
            passed('search and explicit reveal navigate to a block outside the viewport')

            await click('scl-source')
            before = (await state())['navigation']
            await page.keyboard.type('// navigation retains source-independent index', delay=15)
            await click('compile')
            await wait("window.controlSpaceVerification.blockSources.b999.includes('retains source-independent')")
            after = (await state())['navigation']
            assert after['indexBuilds'] == before['indexBuilds']
            assert after['projectionBuilds'] == before['projectionBuilds']
            assert after['creations'] == before['creations']
            samples['sourceCommit'] = after
            passed('committing SCL text leaves the large navigation index and row controls intact')
            assert not errors, errors
            assert not [m for m in logs if m['type']=='error'], logs
        except Exception:
            failure = traceback.format_exc()
            raise
        finally:
            await page.screenshot(path=str(args.output/'uno-navigation-last.png'))
            final = await state()
            (args.output/'navigation-ui.json').write_text(json.dumps({'checks':checks,'errors':errors,'console':logs,'samples':samples,'failure':failure,'navigation':(final or {}).get('navigation')},indent=2)+'\n')
            print(f'Uno navigation workflows: {len(checks)} passed',flush=True)
            await browser.close()
asyncio.run(main())
