#!/usr/bin/env python3
"""Actual Uno tag-table editing/clipboard/scale checks. Only pointer/keyboard/file-picker input mutates the app."""
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
    checks, errors, console = [], [], []
    failure = None
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox'])
        context = await browser.new_context(viewport={'width':1600,'height':1000}, permissions=['clipboard-read','clipboard-write'])
        page = await context.new_page()
        async def attach(page):
            await page.add_init_script("Object.defineProperty(window,'controlSpaceVerification',{get(){return JSON.parse(window.controlSpaceVerificationJson||'null')}})")
            page.on('pageerror', lambda e: errors.append(str(e)))
            page.on('console', lambda m: console.append({'type':m.type, 'text':m.text}))
        await attach(page)
        async def wait(expression):
            await page.wait_for_function('window.controlSpaceVerification && ('+expression+')', timeout=20000)
        async def state():
            return await page.evaluate('window.controlSpaceVerification')
        async def click(identifier):
            result = await page.wait_for_function('(id)=>window.controlSpaceVerification?.controls.find(c=>c.id===id && c.width>0 && c.height>0)', arg=identifier, timeout=20000)
            b = await result.json_value()
            await page.mouse.click(b['x']+b['width']/2, b['y']+b['height']/2)
            await page.wait_for_timeout(400)
        async def edit(name, column, text, commit=True):
            await click(f'tag-cell-{name}-{column}')
            await page.keyboard.press('F2')
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tag-cell-editor')")
            await page.keyboard.press('Control+A')
            await page.keyboard.type(text)
            await page.keyboard.press('Enter' if commit else 'Escape')
            await page.wait_for_timeout(400)
        def passed(name):
            checks.append(name); print('PASS',name,flush=True)
        try:
            await page.goto(args.url+'?verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function('!!window.controlSpaceVerification', timeout=120000)
            await click('tree-tags')
            await wait("window.controlSpaceVerification.view==='tags'")
            assert (await state())['tableRows']==11
            passed('compact tag grid displays existing model')
            await edit('Start_PB','Comment','Updated from the grid')
            await wait("window.controlSpaceVerification.tags.find(t=>t.Name==='Start_PB').Comment==='Updated from the grid'")
            await click('undo')
            await wait("window.controlSpaceVerification.tags.find(t=>t.Name==='Start_PB').Comment==='Start conveyor'")
            await click('redo')
            await wait("window.controlSpaceVerification.tags.find(t=>t.Name==='Start_PB').Comment==='Updated from the grid'")
            passed('inline edit participates in workspace undo redo')
            await edit('Start_PB','Comment','Discarded text',False)
            assert (await state())['tags'][0]['Comment']=='Updated from the grid'
            passed('Escape cancels cell edit without changing project')
            await edit('Speed_Setpoint','Name','Line_Setpoint')
            await wait("window.controlSpaceVerification.tags.some(t=>t.Name==='Line_Setpoint')")
            assert '"Line_Setpoint"' in (await state())['blockSources']['speed']
            passed('renaming a cell rewrites SCL symbol references')
            await edit('Start_PB','Name','Stop_PB')
            await wait("window.controlSpaceVerification.tableStatus.includes('duplicate')")
            assert (await state())['tags'][0]['Name']=='Start_PB'
            await page.keyboard.press('Escape')
            passed('invalid name remains editable without partial mutation')
            await click('tag-cell-Start_PB-Comment')
            await page.evaluate("navigator.clipboard.writeText('Batch one\\r\\nBatch two')")
            await click('tag-paste')
            await wait("window.controlSpaceVerification.tags[0].Comment==='Batch one' && window.controlSpaceVerification.tags[1].Comment==='Batch two'")
            await click('undo')
            await wait("window.controlSpaceVerification.tags[0].Comment==='Updated from the grid' && window.controlSpaceVerification.tags[1].Comment.includes('Stop conveyor')")
            passed('rectangular paste commits as one undo transaction')
            await click('tag-cell-Start_PB-Name')
            await page.evaluate("navigator.clipboard.writeText('Start_PB\\tBOOL\\t%I0.1')")
            await click('tag-paste')
            await wait("window.controlSpaceVerification.tableStatus.includes('overlap')")
            assert (await state())['tags'][0]['Address']=='%I0.0'
            passed('invalid paste rejects the complete rectangle')
            await click('tag-cell-Start_PB-Name'); await click('tag-copy')
            assert await page.evaluate('navigator.clipboard.readText()')=='Start_PB'
            passed('native clipboard copy exports selected cell')
            await click('tag-header-Name')
            await click('tag-cell-Delay_ET-Name'); await page.keyboard.press('Home')
            await wait("window.controlSpaceVerification.tableSelection==='Delay_ET'")
            await click('tag-filter'); await page.keyboard.type('Line_Setpoint')
            await wait('window.controlSpaceVerification.tableRows===1')
            await page.keyboard.press('Control+A'); await page.keyboard.press('Backspace')
            passed('sorting and filtering operate on the view')
            await click('tag-add'); await wait('window.controlSpaceVerification.tagCount===12')
            created=(await state())['tableSelection']
            assert created.startswith('Tag_')
            await click('tag-duplicate'); await wait('window.controlSpaceVerification.tagCount===13')
            await click('tag-delete'); await wait('window.controlSpaceVerification.tagCount===12')
            passed('add duplicate and delete allocate valid unique tags')
            await click('tag-cell-Motor_Run-Name'); await click('tag-delete')
            await wait("window.controlSpaceVerification.tableStatus.includes('references')")
            assert (await state())['tagCount']==12
            passed('used tags cannot be silently deleted')
            await click('tag-monitor')
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tag-header-MonitorValue')")
            await click('compile'); await wait("window.controlSpaceVerification.state==='Stopped'")
            await click('run'); await wait('window.controlSpaceVerification.cycle>0')
            await click('stop')
            await page.screenshot(path=str(args.output/'uno-tags.png'))
            passed('monitor column and simulation coexist with grid')
            await context.close()
            context = await browser.new_context(viewport={'width':1600,'height':1000})
            page = await context.new_page(); await attach(page)
            await page.goto(args.url+'?verify=1', wait_until='domcontentloaded', timeout=120000)
            await page.wait_for_function('!!window.controlSpaceVerification', timeout=120000)
            project=json.loads(Path('tests/fixtures/conveyor.controlspace.json').read_text())
            project.update(id='tag-table-scale',name='Tag_Table_Scale',revision=0,blocks=[],devices=[],links=[],screens=[])
            project['tags']=[{'name':f'Tag{i:05d}','type':'Bool','address':f'%M{i//8}.{i%8}','initialValue':0,'comment':'Scale fixture','retain':False} for i in range(10000)]
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click('open')
            await (await chooser.value).set_files({'name':'scale.controlspace.json','mimeType':'application/json','buffer':json.dumps(project).encode()})
            await wait('window.controlSpaceVerification.tagCount===10000')
            if (await state())['portal']: await click('project-view')
            await click('tree-tags'); await wait('window.controlSpaceVerification.tableRows>0')
            assert (await state())['tableRows'] < 50
            await click('tag-cell-Tag00000-Name'); await page.keyboard.press('End')
            await wait("window.controlSpaceVerification.controls.some(c=>c.id==='tag-cell-Tag09999-Name')")
            assert (await state())['tableSelection']=='Tag09999'
            assert (await state())['tableRows'] < 50
            await page.screenshot(path=str(args.output/'uno-tags-scale.png'))
            passed('real file import and last-row navigation recycle ten-thousand-tag grid')
            if errors: raise AssertionError('\n'.join(errors))
            if [m for m in console if m['type']=='error']: raise AssertionError('Browser console errors; inspect tag-table-logs.json')
        except Exception:
            failure=traceback.format_exc(); raise
        finally:
            await page.screenshot(path=str(args.output/'uno-tags-last.png'))
            (args.output/'tag-table-logs.json').write_text(json.dumps({'checks':checks,'errors':errors,'console':console,'failure':failure,'state':await state()},indent=2))
            print('Tag table workflows:',len(checks),'passed',flush=True)
            print('Failure:',failure,flush=True)
            await browser.close()
asyncio.run(main())
