#!/usr/bin/env python3
"""Real Uno shape authoring and transparent-region hit tests. No mutation probe."""
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
    checks, errors, console, actions = [], [], [], []
    failure = None
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        context = await browser.new_context(viewport={'width':1600,'height':1000}, permissions=['clipboard-read','clipboard-write'])
        page = await context.new_page()
        await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: console.append({'type':m.type,'text':m.text}))
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && ('+expression+')', timeout=20000)
        async def click(identifier):
            await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.some(c=>c.id===id)', arg=identifier, timeout=20000)
            previous = None
            for _ in range(35):
                b = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
                position = tuple(round(b[k],2) for k in ('x','y','width','height')) if b else None
                if b and position == previous and 0 <= b['x']+b['width']/2 < 1600 and 0 <= b['y']+b['height']/2 < 1000:
                    break
                previous = position
                await page.wait_for_timeout(180)
            else:
                raise AssertionError('Control did not settle: '+identifier)
            actions.append(identifier)
            await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
            await page.wait_for_timeout(350)
        async def fill(identifier,text):
            await click(identifier)
            await page.keyboard.press('Control+A')
            await page.keyboard.press('Backspace')
            await page.keyboard.type(text,delay=20)
            await page.wait_for_function('([id,text])=>window.controlSpaceVerification?.controls.find(c=>c.id===id)?.text===text',arg=[identifier,text],timeout=15000)
        async def screen_click(x,y,selected):
            v=(await state())['hmiView']
            await page.mouse.click(v['x']+x*v['scale'],v['y']+y*v['scale'])
            await wait('JSON.stringify(window.controlSpaceVerification.hmiSelection)==='+json.dumps(json.dumps(selected,separators=(',',':'))))
            await page.wait_for_timeout(250)
        def passed(name):
            checks.append(name)
            print('PASS',name,flush=True)
        try:
            await page.goto(args.url+'?verify=1',wait_until='domcontentloaded',timeout=120000)
            await page.wait_for_function("window.controlSpaceVerification?.performance.paintCount>0 && window.controlSpaceVerification.ladderFont.includes('Open Sans')",timeout=120000)
            await click('tree-hmi:overview')
            await click('hmi-screen-new')
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-screen-name','BasicShapes')
            await click('program-dialog-apply')
            await wait("!window.controlSpaceVerification.dialogOpen && window.controlSpaceVerification.hmiScreens.some(s=>s.Name==='BasicShapes')")
            ids=[]
            specs=[('Rectangle',40,40,'#DEA536'),('Rectangle',260,40,'#ADD8E6'),('Ellipse',260,40,'#005A9C'),('Line',480,40,'#008C95')]
            for index,(kind,x,y,color) in enumerate(specs):
                await click('hmi-insert-'+kind)
                await wait(f'window.controlSpaceVerification.hmiObjects.length==={index+1}')
                identifier=(await state())['hmiSelection'][0]
                ids.append(identifier)
                await click('hmi-properties')
                await wait('window.controlSpaceVerification.dialogOpen')
                for field,value in [('x',str(x)),('y',str(y)),('width','140'),('height','100'),('color',color)]:
                    await fill('hmi-object-'+field,value)
                await click('program-dialog-apply')
                await wait('!window.controlSpaceVerification.dialogOpen')
                o=next(o for o in (await state())['hmiObjects'] if o['Id']==identifier)
                assert (o['X'],o['Y'],o['Width'],o['Height'],o['Color'],o['Tag'])==(x,y,140,100,color,'')
            passed('rectangle ellipse and line insert with editable validated geometry and color')

            # An ellipse's rectangular selection box must not hide objects beneath
            # transparent corners. Lines must not hit their whole rectangular bounds.
            await screen_click(900,500,[])
            await screen_click(264,44,[ids[1]])
            await screen_click(330,90,[ids[2]])
            await screen_click(484,132,[])
            await screen_click(550,90,[ids[3]])
            passed('ellipse corners select underlying objects and lines use stroke hit testing')

            # The selection handles only appear after selection, keeping this first
            # corner hit an actual shape test rather than a resize-handle interaction.
            await click('hmi-properties')
            await wait('window.controlSpaceVerification.dialogOpen')
            revision=(await state())['revision']
            await fill('hmi-object-tag','Start_PB')
            await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.dialogError.length>0')
            assert (await state())['revision']==revision
            await fill('hmi-object-tag','')
            await fill('hmi-object-text','Line separator')
            await click('program-dialog-apply')
            await wait('!window.controlSpaceVerification.dialogOpen')
            assert next(o for o in (await state())['hmiObjects'] if o['Id']==ids[3])['Tag']==''
            passed('basic shapes reject tag binding without partial property changes')

            await screen_click(550,90,[ids[3]])
            await page.keyboard.press('Control+c')
            await page.wait_for_timeout(350)
            await page.keyboard.press('Control+v')
            await wait('window.controlSpaceVerification.hmiObjects.length===5')
            copied=(await state())['hmiSelection'][0]
            assert copied not in ids
            await click('undo')
            await wait('window.controlSpaceVerification.hmiObjects.length===4')
            await screen_click(550,90,[ids[3]])
            await page.keyboard.press('ArrowRight')
            await wait(f'window.controlSpaceVerification.hmiObjects.find(o=>o.Id==={json.dumps(ids[3])}).X===481')
            await click('undo')
            await wait(f'window.controlSpaceVerification.hmiObjects.find(o=>o.Id==={json.dumps(ids[3])}).X===480')
            passed('new shapes use native clipboard fresh IDs keyboard nudging and single-step undo')

            await click('hmi-100')
            await wait('!window.controlSpaceVerification.hmiView.fit && window.controlSpaceVerification.hmiView.scale===1')
            await screen_click(900,500,[])
            await screen_click(330,90,[ids[2]])
            await click('hmi-fit')
            await wait('window.controlSpaceVerification.hmiView.fit')
            await screen_click(900,500,[])
            await page.screenshot(path=str(args.output/'uno-hmi-shapes.png'))
            await click('hmi-runtime')
            await wait('window.controlSpaceVerification.hmiRuntime')
            before=await state()
            v=before['hmiView']
            await page.mouse.click(v['x']+550*v['scale'],v['y']+90*v['scale'])
            await page.wait_for_timeout(400)
            after=await state()
            assert after['revision']==before['revision'] and after['values']['Start_PB']==before['values']['Start_PB']
            await click('hmi-design')
            await wait('!window.controlSpaceVerification.hmiRuntime')
            passed('fit/manual zoom and runtime rendering keep shape coordinates and inputs intact')
            assert not errors,errors
            assert not [m for m in console if m['type']=='error'],console
        except Exception:
            failure=traceback.format_exc()
            raise
        finally:
            await page.screenshot(path=str(args.output/'uno-hmi-shapes-last.png'))
            (args.output/'hmi-shapes.json').write_text(json.dumps({'checks':checks,'errors':errors,'console':console,'actions':actions,'failure':failure,'state':await state()},indent=2)+'\n')
            print(f'Uno HMI shape workflows: {len(checks)} passed',flush=True)
            await browser.close()
asyncio.run(main())
