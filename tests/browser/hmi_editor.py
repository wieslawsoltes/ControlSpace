#!/usr/bin/env python3
"""Real pointer, keyboard, clipboard and file-picker HMI workflows; telemetry is read-only."""
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
    checks, errors, logs, actions, samples = [], [], [], [], {}
    failure = None
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        context = await browser.new_context(viewport={'width':1600,'height':1000}, permissions=['clipboard-read','clipboard-write'])
        page = await context.new_page()
        await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: logs.append({'type':m.type,'text':m.text}))
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && ('+expression+')', timeout=20000)
        async def bounds(identifier):
            await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.some(c=>c.id===id)',arg=identifier,timeout=20000)
            previous = None
            for _ in range(35):
                b = await page.evaluate('(id)=>window.controlSpaceVerification.controls.find(c=>c.id===id)', identifier)
                geo = tuple(round(b[k],2) for k in ('x','y','width','height')) if b else None
                if b and geo == previous and 0 <= b['x']+b['width']/2 < 1600 and 0 <= b['y']+b['height']/2 < 1000:
                    return b
                previous = geo
                await page.wait_for_timeout(180)
            raise AssertionError(f'Control did not settle in viewport: {identifier}: {b}')
        async def click(identifier):
            b = await bounds(identifier); actions.append(identifier)
            await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
            await page.wait_for_timeout(350)
        async def fill(identifier,text):
            await click(identifier); await page.keyboard.press('Control+A'); await page.keyboard.press('Backspace')
            for index,line in enumerate(text.split('\n')):
                if index: await page.keyboard.press('Enter')
                await page.keyboard.type(line,delay=15)
            await page.wait_for_function("([id,text])=>{const f=window.controlSpaceVerification?.controls.find(c=>c.id===id);return f && (f.text??'').replace(/\\r\\n|\\r/g,'\\n')===text}",arg=[identifier,text],timeout=15000)
        async def hit(identifier):
            await wait(f'window.controlSpaceVerification.hmiHits.some(h=>h.id==={json.dumps(identifier)})')
            await page.wait_for_timeout(250)
            h = next(h for h in (await state())['hmiHits'] if h['id']==identifier)
            return h['x']+h['width']/2,h['y']+h['height']/2
        async def select(identifier,add=False):
            x,y=await hit(identifier)
            if add: await page.keyboard.down('Control')
            await page.mouse.click(x,y)
            if add: await page.keyboard.up('Control')
            await page.wait_for_timeout(400)
        async def point(x,y):
            v=(await state())['hmiView']; return v['x']+x*v['scale'],v['y']+y*v['scale']
        async def background():
            x,y=await point(915,515); await page.mouse.click(x,y); await wait('window.controlSpaceVerification.hmiSelection.length===0')
        async def drag(start,delta,cancel=False):
            x,y=start; dx,dy=delta
            await page.mouse.move(x,y); await page.mouse.down(); await page.wait_for_timeout(100)
            for fraction in [.25,.5,.75,1]:
                await page.mouse.move(x+dx*fraction,y+dy*fraction); await page.wait_for_timeout(100)
            await wait('window.controlSpaceVerification.hmiView.preview!==null || window.controlSpaceVerification.hmiView.gesture')
            if cancel: await page.keyboard.press('Escape')
        async def undo():
            await click('undo'); await page.wait_for_timeout(400)
        def passed(name):
            checks.append(name); print('PASS',name,flush=True)
        try:
            await page.goto(args.url+'?verify=1',wait_until='domcontentloaded',timeout=120000)
            await page.wait_for_function("window.controlSpaceVerification?.performance.paintCount>0 && window.controlSpaceVerification.ladderFont.includes('Open Sans')",timeout=120000)
            await click('tree-hmi:overview')
            await wait("window.controlSpaceVerification.view==='hmi:overview' && window.controlSpaceVerification.hmiHits.length===8")
            original=(await state())['revision']
            await click('hmi-100'); await wait('!window.controlSpaceVerification.hmiView.fit && window.controlSpaceVerification.hmiView.scale===1')
            await click('hmi-fit'); await wait('window.controlSpaceVerification.hmiView.fit')
            assert (await state())['revision']==original
            passed('real HMI viewport, rulers and zoom do not mutate the project')

            await click('hmi-screen-new'); await wait('window.controlSpaceVerification.dialogOpen')
            await fill('hmi-screen-name','Overview'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.includes('already exists')")
            await fill('hmi-screen-name','OperatorPanel'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.hmiScreens.some(s=>s.Name==='OperatorPanel') && !window.controlSpaceVerification.dialogOpen")
            screen=next(s for s in (await state())['hmiScreens'] if s['Name']=='OperatorPanel')
            assert (await state())['view']=='hmi:'+screen['Id']
            passed('screen creation rejects duplicate names and opens a new screen')

            for index,kind in enumerate(['Label','Button','Lamp','Tank','Numeric','Gauge']):
                await click('hmi-insert-'+kind)
                await wait(f'window.controlSpaceVerification.hmiObjects.length==={index+1}')
            assert len((await state())['hmiSelection'])==1
            gauge=(await state())['hmiSelection'][0]
            passed('all six supported HMI object types insert with selection and compatible bindings')

            await select(gauge); await page.keyboard.press('F2'); await wait('window.controlSpaceVerification.dialogOpen')
            revision=(await state())['revision']
            await fill('hmi-object-text','Conveyor speed'); await fill('hmi-object-color','red'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.length>0")
            assert (await state())['revision']==revision
            await fill('hmi-object-color','#005A9C'); await fill('hmi-object-tag','Motor_Run'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.includes('compatible')")
            await fill('hmi-object-tag','Speed_Actual'); await fill('hmi-object-x','500'); await fill('hmi-object-y','160'); await fill('hmi-object-width','300'); await fill('hmi-object-height','180')
            await page.screenshot(path=str(args.output/'uno-hmi-properties.png'))
            await click('program-dialog-apply'); await wait('!window.controlSpaceVerification.dialogOpen')
            obj=next(o for o in (await state())['hmiObjects'] if o['Id']==gauge)
            assert obj['Text']=='Conveyor speed' and obj['Tag']=='Speed_Actual' and obj['X']==500 and obj['Color']=='#005A9C'
            passed('property dialog validates color and typed tags before applying text and geometry atomically')

            await select(gauge); await page.keyboard.press('Control+d'); await wait('window.controlSpaceVerification.hmiObjects.length===7')
            copy=(await state())['hmiSelection'][0]; assert copy!=gauge
            await undo(); await wait('window.controlSpaceVerification.hmiObjects.length===6')
            await select(gauge); await page.keyboard.press('Control+c'); await page.wait_for_timeout(400); await page.keyboard.press('Control+v')
            await wait('window.controlSpaceVerification.hmiObjects.length===7')
            pasted=(await state())['hmiSelection'][0]; assert pasted!=gauge
            await select(pasted); await page.keyboard.press('Control+x'); await wait('window.controlSpaceVerification.hmiObjects.length===6')
            await undo(); await wait('window.controlSpaceVerification.hmiObjects.length===7')
            passed('native clipboard copy paste cut and duplication preserve fresh IDs and one-step undo')

            await click('tab-hmi:overview'); await wait("window.controlSpaceVerification.view==='hmi:overview' && window.controlSpaceVerification.hmiHits.length===8")
            await click('hmi-snap'); await wait('!window.controlSpaceVerification.hmiView.snap')
            revision=(await state())['revision']
            await select('motor-lamp'); await select('ready-lamp',True)
            await wait('window.controlSpaceVerification.hmiSelection.length===2')
            assert (await state())['revision']==revision
            await click('hmi-inspector-Left')
            await wait("window.controlSpaceVerification.hmiObjects.find(o=>o.Id==='ready-lamp').X===40")
            await undo(); await wait("window.controlSpaceVerification.hmiObjects.find(o=>o.Id==='ready-lamp').X===270")
            passed('multi-selection and reference alignment are undoable without selection edits')

            before=await state(); scale=before['hmiView']['scale']; xy=await hit('motor-lamp')
            await drag(xy,(30*scale,20*scale))
            preview=await state(); assert preview['revision']==before['revision'] and preview['hmiView']['preview']
            await page.screenshot(path=str(args.output/'uno-hmi-drag.png'))
            await page.mouse.up(); await wait(f"window.controlSpaceVerification.revision==={before['revision']+1}")
            after=await state(); motor=next(o for o in after['hmiObjects'] if o['Id']=='motor-lamp'); ready=next(o for o in after['hmiObjects'] if o['Id']=='ready-lamp')
            assert abs(motor['X']-70)<.1 and abs(ready['X']-300)<.1 and abs(motor['Y']-180)<.1
            await undo()
            passed('group drag previews without mutations and commits one coherent transform')

            before=await state(); xy=await hit('motor-lamp'); await drag(xy,(40,25),True); await page.mouse.up(); await page.wait_for_timeout(500)
            assert (await state())['revision']==before['revision'] and (await state())['hmiView']['preview'] is None
            passed('Escape cancels captured drag without a history entry')

            await background(); await select('motor-lamp'); await wait('window.controlSpaceVerification.hmiHandles.length===8')
            before=await state(); handle=next(h for h in before['hmiHandles'] if h['handle']=='se'); scale=before['hmiView']['scale']
            await drag((handle['x']+5,handle['y']+5),(20*scale,15*scale)); await page.mouse.up()
            await wait(f"window.controlSpaceVerification.revision==={before['revision']+1}")
            obj=next(o for o in (await state())['hmiObjects'] if o['Id']=='motor-lamp'); assert abs(obj['Width']-210)<.1 and abs(obj['Height']-103)<.1
            await undo()
            passed('constant-size resize handles transform the object and support undo')

            await background(); start=await point(20,140); end=await point(485,265)
            await drag(start,(end[0]-start[0],end[1]-start[1])); await page.mouse.up()
            await wait('window.controlSpaceVerification.hmiSelection.length===2')
            assert set((await state())['hmiSelection'])=={'motor-lamp','ready-lamp'}
            await click('hmi-object-card'); await click('hmi-front')
            await wait("window.controlSpaceVerification.hmiObjects.slice(-2).every(o=>['motor-lamp','ready-lamp'].includes(o.Id))")
            await undo()
            passed('marquee selection and object-list paint ordering preserve grouped order')

            await background(); await select('motor-lamp'); before=next(o for o in (await state())['hmiObjects'] if o['Id']=='motor-lamp')
            await page.keyboard.press('ArrowRight'); await wait("window.controlSpaceVerification.hmiObjects.find(o=>o.Id==='motor-lamp').X===41")
            await page.keyboard.press('Shift+ArrowDown'); await wait("window.controlSpaceVerification.hmiObjects.find(o=>o.Id==='motor-lamp').Y===170")
            await undo(); await undo()
            passed('keyboard nudging uses one pixel or ten pixels with Shift')

            await click('hmi-runtime'); await wait('window.controlSpaceVerification.hmiRuntime')
            await click('run'); await wait("window.controlSpaceVerification.state==='Running'")
            x,y=await hit('start-button'); await page.mouse.move(x,y); await page.mouse.down()
            await wait('window.controlSpaceVerification.values.Start_PB===1 && window.controlSpaceVerification.values.Motor_Run===1')
            await page.mouse.move(10,10); await page.mouse.up(); await wait('window.controlSpaceVerification.values.Start_PB===0')
            passed('runtime momentary inputs release after pointer capture outside the HMI')
            x,y=await hit('start-button'); await page.mouse.move(x,y); await page.mouse.down(); await wait('window.controlSpaceVerification.values.Start_PB===1')
            await page.keyboard.press('Escape'); await page.mouse.up(); await wait("window.controlSpaceVerification.values.Start_PB===0 && window.controlSpaceVerification.state==='Stopped'")
            await click('hmi-design'); await wait('!window.controlSpaceVerification.hmiRuntime')
            await page.screenshot(path=str(args.output/'uno-hmi-designer.png'))
            passed('stopping runtime cancels held inputs and returns to editable design')

            await click('hmi-screens'); await wait("window.controlSpaceVerification.view==='screens'")
            await click('hmi-screen-row-'+screen['Id']); await click('hmi-directory-screen-duplicate')
            await wait('window.controlSpaceVerification.hmiScreens.length===3')
            copied=next(s for s in (await state())['hmiScreens'] if s['Name']=='OperatorPanel_copy')
            await click('hmi-screens'); await click('hmi-screen-row-'+copied['Id']); await click('hmi-directory-screen-delete')
            await wait('window.controlSpaceVerification.dialogOpen'); await click('program-dialog-cancel'); assert len((await state())['hmiScreens'])==3
            await click('hmi-directory-screen-delete'); await wait('window.controlSpaceVerification.dialogOpen'); await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.hmiScreens.length===2')
            await undo(); await wait('window.controlSpaceVerification.hmiScreens.length===3')
            passed('screen directory supports deep duplication, confirmed deletion and undo')
            await page.screenshot(path=str(args.output/'uno-hmi-screens.png'))

            # Use a new context so Open does not require discarding this edited project.
            await context.close(); context=await browser.new_context(viewport={'width':1600,'height':1000}); page=await context.new_page()
            await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
            page.on('pageerror', lambda e: errors.append(str(e))); page.on('console', lambda m: logs.append({'type':m.type,'text':m.text}))
            await page.goto(args.url+'?verify=1',wait_until='domcontentloaded',timeout=120000)
            await page.wait_for_function("window.controlSpaceVerification?.performance.paintCount>0 && window.controlSpaceVerification.ladderFont.includes('Open Sans')",timeout=120000)
            fixture={'format':'controlspace.project','version':1,'id':'hmi-scale','name':'HMI scale','revision':0,'tags':[],
                'blocks':[{'id':'empty','name':'Empty','number':1,'language':'SCL','cyclic':False,'source':'','networks':[]}], 'devices':[], 'links':[],
                'screens':[{'id':'large','name':'Large HMI','width':8192,'height':540,'objects':[{'id':f'o{i}','kind':'Label','text':f'Object {i}','tag':'','x':10 if i==0 else 5000,'y':30,'width':100,'height':30,'color':'#008C95'} for i in range(10000)]}]}
            path=args.output/'hmi-scale.json'; path.write_text(json.dumps(fixture))
            async with page.expect_file_chooser() as chooser: await click('open')
            await (await chooser.value).set_files(str(path)); await wait("window.controlSpaceVerification.view==='block:empty'")
            await click('tree-hmi:large'); await click('hmi-100'); await wait('window.controlSpaceVerification.hmiView.drawnObjects===1')
            await click('hmi-object-card'); await wait("window.controlSpaceVerification.controls.some(c=>c.id==='hmi-object-list')")
            rows=[c for c in (await state())['controls'] if c['id'].startswith('hmi-object-row-')]
            assert len(rows)<50, len(rows)
            samples['scale']={'totalObjects':10000,'drawnObjects':(await state())['hmiView']['drawnObjects'],'realizedRows':len(rows)}
            await page.screenshot(path=str(args.output/'uno-hmi-scale.png'))
            passed('10000-object file import renders only the viewport and virtualizes the object list')
            assert not errors, errors
            assert not [m for m in logs if m['type']=='error'], logs
        except Exception:
            failure=traceback.format_exc(); raise
        finally:
            await page.screenshot(path=str(args.output/'uno-hmi-last.png'))
            final=await state()
            (args.output/'hmi-editor.json').write_text(json.dumps({'checks':checks,'errors':errors,'console':logs,'actions':actions,'samples':samples,'failure':failure,'state':final},indent=2)+'\n')
            print(f'HMI browser workflows: {len(checks)} passed',flush=True)
            print('Failure:',failure,flush=True)
            await browser.close()
asyncio.run(main())
