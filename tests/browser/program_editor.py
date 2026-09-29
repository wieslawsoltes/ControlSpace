#!/usr/bin/env python3
"""Pointer/keyboard tests of real Uno block dialogs and vector ladder editing; no test-only mutation APIs."""
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
        page = await browser.new_page(viewport={'width':1600,'height':1000})
        await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: logs.append({'type':m.type,'text':m.text}))
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && ('+expression+')',timeout=20000)
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def bounds(identifier):
            result=await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.find(c=>c.id===id && c.width>0 && c.height>0)',arg=identifier,timeout=20000)
            return await result.json_value()
        async def click(identifier):
            b=await bounds(identifier); actions.append(identifier)
            await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
            await page.wait_for_timeout(350)
        async def fill(identifier,text):
            await click(identifier); await page.keyboard.press('Control+A'); await page.keyboard.press('Backspace')
            for index, line in enumerate(text.split('\n')):
                if index: await page.keyboard.press('Enter')
                await page.keyboard.type(line)
        async def hit(identifier,kind='instruction',double=False):
            result=await page.wait_for_function('([id,kind])=>window.controlSpaceVerification?.ladderHits.find(h=>h.id===id && h.kind===kind)',arg=[identifier,kind],timeout=20000)
            h=await result.json_value()
            x=h['x']+(80 if kind=='network' else h['width']/2)
            y=h['y']+(13 if kind=='network' else h['height']/2)
            actions.append({'hit':identifier,'kind':kind,'x':x,'y':y})
            if double: await page.mouse.dblclick(x,y,delay=110)
            else: await page.mouse.click(x,y)
            await page.wait_for_timeout(400)
        def passed(name):
            checks.append(name); print('PASS',name,flush=True)
        try:
            await page.goto(args.url+'?verify=1',wait_until='domcontentloaded',timeout=120000)
            await page.wait_for_function('!!window.controlSpaceVerification',timeout=120000)
            await wait("window.controlSpaceVerification.ladderFont.includes('Open Sans')")
            assert len((await state())['programBlocks'])==2
            passed('packaged renderer font loads in genuine Uno workbench')
            await click('tree-add-block'); await wait('window.controlSpaceVerification.dialogOpen')
            await fill('block-name','Main'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.includes('already exists')")
            assert len((await state())['programBlocks'])==2
            await fill('block-name','Motor_Logic'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.programBlocks.some(b=>b.Name==='Motor_Logic') && !window.controlSpaceVerification.dialogOpen")
            block=next(b for b in (await state())['programBlocks'] if b['Name']=='Motor_Logic')
            assert not block['Cyclic'] and block['networks']==0
            passed('create dialog rejects duplicate names then opens a valid offline LAD block')
            await click('add-network'); await wait('window.controlSpaceVerification.ladderNetworks.length===1')
            n=(await state())['ladderNetworks'][0]
            await hit(n['Id'],'network',True)
            await wait('window.controlSpaceVerification.dialogOpen')
            await fill('network-title','Motor permissive')
            await fill('network-comment','Start command and permissive\nVerified authoring workflow')
            await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.ladderNetworks[0].Title==='Motor permissive' && !window.controlSpaceVerification.dialogOpen")
            assert 'Verified authoring workflow' in (await state())['ladderNetworks'][0]['Comment']
            passed('insert network and edit title and multiline comment')
            revision=(await state())['revision']
            await click('ladder-collapse-all')
            await wait(f"window.controlSpaceVerification.ladderCollapsed.includes({json.dumps(n['Id'])})")
            assert not any(h['kind']=='instruction' for h in (await state())['ladderHits'])
            await hit(n['Id'],'network-toggle')
            await wait(f"!window.controlSpaceVerification.ladderCollapsed.includes({json.dumps(n['Id'])})")
            assert (await state())['revision']==revision
            passed('collapse and expand change only view state')
            n=(await state())['ladderNetworks'][0]; first=n['Branches'][0][0]['Id']
            await hit(first); await page.keyboard.press('F9')
            await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===2')
            second=(await state())['ladderSelection']
            await page.keyboard.press('F10')
            await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===3')
            assert (await state())['ladderNetworks'][0]['Branches'][0][1]['Id']==second
            passed('F9 and F10 insert contacts in the selected path')
            selected=(await state())['ladderSelection']
            await hit(selected,double=True); await wait('window.controlSpaceVerification.dialogOpen')
            await fill('instruction-operand','Speed_Actual')
            await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.includes('incompatible')")
            await fill('instruction-operand','Stop_PB')
            await page.screenshot(path=str(args.output/'uno-instruction-dialog.png'))
            await click('program-dialog-apply'); await wait('!window.controlSpaceVerification.dialogOpen')
            assert (await state())['ladderNetworks'][0]['Branches'][0][-1]['Tag']=='Stop_PB'
            passed('typed operand dialog rejects incompatible tag and applies BOOL operand')
            await fill('task-search','Normally open contact')
            source=await bounds('palette-Normally open contact')
            target=next(h for h in (await state())['ladderHits'] if h['id']==first and h['kind']=='instruction')
            await page.mouse.move(source['x']+source['width']/2,source['y']+source['height']/2)
            await page.mouse.down(); await page.wait_for_timeout(150)
            await page.mouse.move(source['x']-20,source['y']+source['height']/2,steps=5)
            await page.wait_for_timeout(200)
            await page.mouse.move(target['x']+target['width']/2,target['y']+target['height']/2,steps=15)
            await page.wait_for_timeout(400); await page.mouse.up()
            await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===4')
            await click('undo'); await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===3')
            await fill('task-search','')
            passed('native palette drag inserts at the dropped contact and is undoable')
            await hit(selected); await page.keyboard.press('Shift+F8')
            await wait('window.controlSpaceVerification.ladderNetworks[0].Branches.length===2')
            await click('branch-delete'); await wait('window.controlSpaceVerification.ladderNetworks[0].Branches.length===1')
            passed('add and remove selected parallel branch')
            await hit(first); await click('contact-right')
            await wait(f"window.controlSpaceVerification.ladderNetworks[0].Branches[0][1].Id==={json.dumps(first)}")
            await hit(first); await page.keyboard.press('Delete')
            await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===2')
            await click('undo'); await wait('window.controlSpaceVerification.ladderNetworks[0].Branches[0].length===3')
            passed('contact reorder deletion and undo preserve the network')
            await hit(n['Id'],'network'); await click('network-duplicate')
            await wait('window.controlSpaceVerification.ladderNetworks.length===2')
            original,copy=(await state())['ladderNetworks']
            assert original['Id']!=copy['Id'] and original['Output']['Id']!=copy['Output']['Id']
            await click('ladder-network-up')
            await wait(f"window.controlSpaceVerification.ladderNetworks[0].Id==={json.dumps(copy['Id'])}")
            await click('network-delete'); await wait('window.controlSpaceVerification.dialogOpen'); await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.ladderNetworks.length===1 && !window.controlSpaceVerification.dialogOpen')
            await click('undo'); await wait('window.controlSpaceVerification.ladderNetworks.length===2')
            passed('duplicate move and delete network are undoable with fresh IDs')
            await click('block-properties'); await wait('window.controlSpaceVerification.dialogOpen')
            await fill('block-name','Motor_Program'); await fill('block-number','10')
            await click('block-cyclic'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.programBlocks.some(b=>b.Name==='Motor_Program' && b.Number===10 && b.Cyclic) && !window.controlSpaceVerification.dialogOpen")
            await click('tree-block-overview'); await wait("window.controlSpaceVerification.view==='blocks'")
            await click('block-row-'+block['Id']); await click('blocks-duplicate')
            await wait("window.controlSpaceVerification.programBlocks.length===4")
            copied=next(b for b in (await state())['programBlocks'] if b['Name'].startswith('Motor_Program_copy'))
            assert not copied['Cyclic']
            passed('block properties and duplication preserve explicit execution settings')
            await click('tree-block-overview'); await click('block-row-'+copied['Id'])
            await click('blocks-up')
            s=await state(); assert next(i for i,b in enumerate(s['programBlocks']) if b['Id']==copied['Id'])<next(i for i,b in enumerate(s['programBlocks']) if b['Id']==block['Id'])
            await page.screenshot(path=str(args.output/'uno-program-blocks.png'))
            await click('blocks-delete'); await wait('window.controlSpaceVerification.dialogOpen'); await click('program-dialog-apply')
            await wait('window.controlSpaceVerification.programBlocks.length===3 && !window.controlSpaceVerification.dialogOpen')
            await click('undo'); await wait('window.controlSpaceVerification.programBlocks.length===4')
            passed('block directory reorders scan sequence and confirms undoable deletion')
            await click('tree-block:'+block['Id']); await wait("window.controlSpaceVerification.ladderNetworks.length===2")
            network=(await state())['ladderNetworks'][0]
            await hit(network['Id'],'network'); await fill('task-search','TON'); await click('palette-TON · On-delay')
            await wait('window.controlSpaceVerification.ladderNetworks[0].Output.Parameter===1000')
            await click('compile'); await wait("window.controlSpaceVerification.state==='Stopped'")
            await click('run'); await wait('window.controlSpaceVerification.cycle>0')
            await click('block-properties'); await wait('window.controlSpaceVerification.dialogOpen')
            await fill('block-name','Running_edit'); await click('program-dialog-apply')
            await wait("window.controlSpaceVerification.dialogError.includes('Stop simulation')")
            assert not any(b['Name']=='Running_edit' for b in (await state())['programBlocks'])
            await click('program-dialog-cancel'); await click('stop')
            passed('output palette compiles and RUN blocks declaration changes')
            await fill('task-search','')
            await click('tree-block:main'); await wait("window.controlSpaceVerification.view==='block:main'")
            first=(await state())['ladderNetworks'][0]['Branches'][0][0]['Id']
            await hit(first)
            for _ in range(8): await page.keyboard.press('F9'); await page.wait_for_timeout(150)
            await wait('window.controlSpaceVerification.ladderWidth>1500 && window.controlSpaceVerification.ladderHorizontal>0')
            horizontal=await bounds('ladder-scroll-horizontal'); assert horizontal['width']>200
            await page.keyboard.press('Home'); await page.wait_for_timeout(300)
            await page.screenshot(path=str(args.output/'uno-program-ladder.png'))
            passed('long rungs scroll horizontally without compressing contact cells')
            if errors: raise AssertionError('\n'.join(errors))
        except Exception:
            failure=traceback.format_exc(); raise
        finally:
            await page.screenshot(path=str(args.output/'uno-program-last.png'))
            data={'checks':checks,'errors':errors,'console':logs,'actions':actions,'failure':failure,'state':await state()}
            (args.output/'program-report.json').write_text(json.dumps(data,indent=2)+'\n')
            print('Program UI workflows:',len(checks),'passed; failure:',failure,flush=True)
            await browser.close()
asyncio.run(main())
