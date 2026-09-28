#!/usr/bin/env python3
"""Browser interaction tests. Supports HTTP builds or offline set_content fixtures.

Offline fixtures use an explicit in-memory Storage double. They do not qualify
real browser persistence, a secure-context WebGPU adapter, or the Uno build.
"""
import argparse
import asyncio
import json
import os
import sys
from pathlib import Path
from playwright.async_api import async_playwright
ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--url', default='')
parser.add_argument('--chromium', default=os.environ.get('CHROMIUM_PATH', ''))
parser.add_argument('--output', type=Path, default=ROOT/'artifacts'/'browser-tests.json')
args = parser.parse_args()

async def run():
    results=[]
    async with async_playwright() as pw:
        launch={'headless':True,'args':['--no-sandbox']}
        if args.chromium: launch['executable_path']=args.chromium
        browser=await pw.chromium.launch(**launch)
        context=await browser.new_context(viewport={'width':1600,'height':1000},device_scale_factor=1,accept_downloads=True)
        page=await context.new_page()
        errors=[]
        page.on('pageerror',lambda e: errors.append(str(e)))
        page.on('dialog',lambda dialog: asyncio.create_task(dialog.accept()))
        offline=not bool(args.url)
        async def fixture(page, recovery=None, denied=False):
            if not denied and offline:
                await page.evaluate('''initial => { const store = initial || {}; Object.defineProperty(window, 'localStorage', {configurable:true, value:{getItem:k=>store[k]??null,setItem:(k,v)=>store[k]=String(v),removeItem:k=>delete store[k],clear:()=>Object.keys(store).forEach(k=>delete store[k])}}); }''', recovery)
            if args.url: await page.goto(args.url)
            else: await page.set_content((ROOT/'artifacts/ControlSpace-prototype.html').read_text())
            await page.wait_for_function('!!window.ControlSpacePrototype')
            await page.wait_for_timeout(200)
        await fixture(page)
        async def check(name, callback):
            try:
                await callback()
                results.append({'test':name,'passed':True})
                print('PASS',name)
            except Exception as error:
                results.append({'test':name,'passed':False,'error':str(error)})
                print('FAIL',name,str(error)[:400])
        async def expect_js(expression):
            await page.wait_for_function(expression,timeout=4000)
        async def click_action(action):
            await page.locator(f'[data-action="{action}"]:visible').first.click()
            await page.wait_for_timeout(50)
        async def go(view):
            await page.locator(f'[data-view="{view}"]:visible').first.click()
            await expect_js(f'ControlSpacePrototype.view === {json.dumps(view)}')
        async def boot():
            assert await page.locator('.prototype-badge').inner_text()=='INTERACTION PROTOTYPE'
            assert await page.locator('#menubar > .menu').count()==7
            boxes=await page.locator('#menubar > .menu').evaluate_all('(xs)=>xs.map(x=>x.getBoundingClientRect().y)')
            assert len(set(boxes))==1, boxes
            await expect_js('ControlSpacePrototype.hits.length > 0')
        await check('boot, prototype label, horizontal menu layout and hit regions',boot)
        async def menu():
            await page.get_by_role('button',name='Project',exact=True).click()
            assert await page.get_by_role('menuitem',name='Open… Ctrl+O').is_visible()
            await page.locator('.editor-title').click()
            assert not await page.locator('.menu.open').count()
        await check('menus open and dismiss without overlapping the workspace',menu)
        async def search():
            await page.locator('#project-search').fill('Speed_Control')
            assert await page.locator('#tree .tree-row').count()==1
            await page.locator('#project-search').fill('')
            assert await page.locator('#tree .tree-row').count()>15
        await check('project tree search',search)
        async def compilation():
            await click_action('compile')
            await expect_js('ControlSpacePrototype.diagnostics.some(d=>d.code==="CS000")')
            assert '0 errors' in await page.locator('#compile-status').inner_text()
        await check('compile from toolbar produces real diagnostics',compilation)
        async def run_start():
            await click_action('run')
            await page.locator('[data-toggle-input="Start_PB"]').click()
            await expect_js('ControlSpacePrototype.snapshot.values[4]===1')
            await page.locator('[data-toggle-input="Start_PB"]').click()
            await expect_js('ControlSpacePrototype.snapshot.values[0]===0 && ControlSpacePrototype.snapshot.values[4]===1')
        await check('run, simulated input toggle and ladder seal-in',run_start)
        async def ready():
            await expect_js('ControlSpacePrototype.snapshot.values[5]===1 && ControlSpacePrototype.snapshot.values[7]===2000')
            assert await page.evaluate('ControlSpacePrototype.snapshot.values[10]')==65
        await check('two-second timer and SCL speed block',ready)
        async def counter():
            await page.locator('[data-toggle-input="Part_Sensor"]').click()
            await expect_js('ControlSpacePrototype.snapshot.values[8]===1')
            await page.locator('[data-toggle-input="Part_Sensor"]').click()
        await check('virtual product counter increments on rising edge',counter)
        async def watch():
            await go('watch')
            await expect_js('document.querySelector(\'[data-value="Motor_Run"]\').textContent==="TRUE"')
            assert await page.locator('[data-value="Speed_Actual"]').inner_text()=='65'
        await check('watch table displays live runtime values',watch)
        async def stop():
            await click_action('stop')
            await expect_js('ControlSpacePrototype.snapshot.state==="STOP" && ControlSpacePrototype.snapshot.values[4]===0')
        await check('Stop clears simulated output image',stop)
        async def source_edit():
            await go('block:speed')
            await page.locator('#scl-source').fill('IF "Motor_Run" THEN\n  "Speed_Actual" := 42;\nELSE\n  "Speed_Actual" := 0;\nEND_IF;')
            await click_action('apply-source')
            await expect_js('ControlSpacePrototype.project.blocks[1].source.includes(":= 42")')
        await check('SCL source editing commits model changes',source_edit)
        async def history():
            await click_action('undo')
            await expect_js('ControlSpacePrototype.project.blocks[1].source.includes(":= \\"Speed_Setpoint\\"")')
            await click_action('redo')
            await expect_js('ControlSpacePrototype.project.blocks[1].source.includes(":= 42")')
        await check('source edit undo and redo',history)
        async def compiler_error():
            await page.locator('#scl-source').fill('"Missing_Tag" := 42;')
            await click_action('compile')
            await expect_js('ControlSpacePrototype.diagnostics.some(d=>d.severity==="Error")')
            assert await page.evaluate('ControlSpacePrototype.snapshot') is None
            await click_action('undo')
            await click_action('compile')
        await check('invalid source blocks simulation and is recoverable with Undo',compiler_error)
        async def tag_edit():
            await go('tags')
            await page.locator('[data-tag="Speed_Setpoint"]').click()
            await page.locator('#properties-form [name="initialValue"]').fill('75')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('ControlSpacePrototype.project.tags[9].initialValue===75')
        await check('tag property editor applies typed initial value',tag_edit)
        async def invalid_tag():
            before=await page.evaluate('ControlSpacePrototype.undoCount')
            await page.locator('#properties-form [name="address"]').fill('%MD4')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('document.querySelector("#toast").textContent.includes("overlap")')
            assert await page.evaluate('ControlSpacePrototype.undoCount')==before
            assert await page.evaluate('ControlSpacePrototype.project.tags[9].address')=='%MD12'
            await page.locator('#properties-form [name="address"]').fill('%MD12')
        await check('overlapping tag memory edit is rejected atomically',invalid_tag)
        async def rename_tag():
            await page.locator('[data-tag="Motor_Run"]').click()
            await page.locator('#properties-form [name="name"]').fill('Motor_Enable')
            before=await page.evaluate('ControlSpacePrototype.undoCount')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('ControlSpacePrototype.project.tags[4].name==="Motor_Enable"')
            assert await page.evaluate('ControlSpacePrototype.undoCount')==before+1
            await click_action('compile')
            await expect_js('ControlSpacePrototype.diagnostics.some(d=>d.code==="CS000")')
            await click_action('undo')
        await check('atomic symbol rename propagates to LAD, SCL and HMI',rename_tag)
        async def ladder_edit():
            await go('block:main')
            await click_action('add-network')
            await expect_js('ControlSpacePrototype.project.blocks[0].networks.length===5')
            await page.locator('#properties-form [name="title"]').fill('Test network')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('ControlSpacePrototype.project.blocks[0].networks[4].title==="Test network"')
            await click_action('undo'); await click_action('undo')
        await check('add and edit ladder network with undo',ladder_edit)
        async def select_contact():
            await page.locator('[data-task="instructions"]').click()
            await page.get_by_text('Instruction list (keyboard access)',exact=True).click()
            await page.locator('[data-select="start"]').click()
            assert await page.locator('#properties-form [name="tag"]').input_value()=='Start_PB'
        await check('keyboard-accessible instruction selection mirrors canvas',select_contact)
        async def device_add():
            await go('devices')
            await click_action('add-device')
            await expect_js('ControlSpacePrototype.project.devices.length===4')
            await click_action('link-device')
            await expect_js('ControlSpacePrototype.project.links.length===3')
            await click_action('add-module')
            await expect_js('ControlSpacePrototype.project.devices[3].modules.length===3')
        await check('add device, subnet link and I/O module',device_add)
        async def invalid_ip():
            await page.locator('#properties-form [name="ipAddress"]').fill('999.0.0.1')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('document.querySelector("#toast").textContent.includes("IPv4")')
            assert await page.evaluate('ControlSpacePrototype.project.devices[3].ipAddress')!='999.0.0.1'
        await check('invalid device IP rejected',invalid_ip)
        async def hmi_drag():
            await go('hmi')
            await expect_js('ControlSpacePrototype.hits.some(h=>h.id==="motor-lamp")')
            hit=await page.evaluate('ControlSpacePrototype.hits.find(h=>h.id==="motor-lamp")')
            bounds=await page.locator('#viewport').bounding_box();x=bounds['x']+hit['x']+hit['width']/2;y=bounds['y']+hit['y']+hit['height']/2
            await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+30,y+20,steps=5);await page.mouse.up()
            await expect_js('ControlSpacePrototype.project.screens[0].objects.find(o=>o.id==="motor-lamp").x!==40')
            assert await page.evaluate('ControlSpacePrototype.project.screens[0].objects.find(o=>o.id==="motor-lamp").x%10')==0
            await click_action('undo')
        await check('HMI object drag snaps and creates an undoable transaction',hmi_drag)
        async def hmi_insert():
            await page.locator('[data-hmi="Lamp"]:visible').first.click()
            await expect_js('ControlSpacePrototype.project.screens[0].objects.length===9')
            await page.locator('#properties-form [name="text"]').fill('NEW LAMP')
            await page.locator('#properties-form button[type="submit"]').click()
            await expect_js('ControlSpacePrototype.project.screens[0].objects.at(-1).text==="NEW LAMP"')
            await click_action('undo');await click_action('undo')
        await check('insert and configure tag-bound HMI object',hmi_insert)
        async def runtime_buttons():
            await click_action('run');await click_action('runtime-preview')
            hit=await page.evaluate('ControlSpacePrototype.hits.find(h=>h.id==="start-button")')
            bounds=await page.locator('#viewport').bounding_box();x=bounds['x']+hit['x']+hit['width']/2;y=bounds['y']+hit['y']+hit['height']/2
            await page.mouse.move(x,y);await page.mouse.down();await page.wait_for_timeout(160)
            await expect_js('ControlSpacePrototype.snapshot.values[0]===1')
            await page.mouse.up();await expect_js('ControlSpacePrototype.snapshot.values[0]===0')
            await expect_js('ControlSpacePrototype.snapshot.values[4]===1')
        await check('HMI runtime buttons drive and release momentary virtual inputs',runtime_buttons)
        async def traces():
            await go('trace');await expect_js('ControlSpacePrototype.snapshot.cycle>2')
            assert await page.locator('#viewport').is_visible()
        await check('trace view renders acquired virtual samples',traces)
        async def stop_keyboard():
            await page.keyboard.press('Escape')
            await expect_js('ControlSpacePrototype.snapshot.state==="STOP"')
        await check('Escape stops simulation',stop_keyboard)
        async def splitters():
            box=await page.locator('.left-splitter').bounding_box();await page.mouse.move(box['x']+2,box['y']+120);await page.mouse.down();await page.mouse.move(box['x']+42,box['y']+120);await page.mouse.up()
            width=await page.locator('.project-pane').evaluate('(x)=>x.getBoundingClientRect().width')
            assert width>=280,width
        await check('workspace splitters resize panels',splitters)
        async def export():
            async with page.expect_download() as event: await click_action('save')
            file=await event.value
            assert file.suggested_filename.endswith('.controlspace.json')
            path=await file.path();payload=json.loads(Path(path).read_text())
            assert payload['format']=='controlspace.project'
        await check('project export downloads validated JSON',export)
        async def import_bad():
            await page.locator('#file-input').set_input_files({'name':'invalid.json','mimeType':'application/json','buffer':b'{"format":"bad"}'})
            await expect_js('document.querySelector("#toast").classList.contains("error")')
            assert await page.evaluate('ControlSpacePrototype.project.name')=='Conveyor_Line'
        await check('malformed project import does not replace current document',import_bad)
        async def recovery():
            await page.wait_for_timeout(500)
            saved=await page.evaluate('localStorage.getItem("controlspace.prototype.project.v1")')
            assert saved is not None
            second=await context.new_page()
            await fixture(second, {'controlspace.prototype.project.v1':saved})
            assert await second.evaluate('ControlSpacePrototype.project.tags[9].initialValue')==75
            await second.close()
        await check('recovery round trip through explicit Storage test double' if offline else 'browser localStorage recovery round trip',recovery)
        async def no_access():
            denied=await context.new_page();await fixture(denied,denied=True)
            assert await denied.evaluate('!!ControlSpacePrototype')
            if offline: assert 'Recovery was not loaded' in await denied.locator('#status-message').inner_text()
            await denied.close()
        await check('workspace remains usable when Storage access is denied',no_access)
        async def responsive():
            await page.set_viewport_size({'width':1024,'height':768});await page.wait_for_timeout(150)
            bounds=await page.locator('#viewport').bounding_box();assert bounds['width']>=300
            await page.set_viewport_size({'width':1600,'height':1000})
        await check('compact desktop layout remains usable at 1024 × 768',responsive)
        async def about():
            await click_action('about')
            assert await page.locator('#modal').is_visible()
            assert 'not Uno' in await page.locator('#modal-body').inner_text()
            await click_action('close-modal')
        await check('About discloses runtime and compatibility boundaries',about)
        async def no_errors(): assert errors==[],errors
        await check('no uncaught browser exceptions across workflows',no_errors)
        # Evidence screenshots are explicitly labelled as prototype captures.
        await go('block:main');await page.locator('[data-task="instructions"]').click();await page.locator('[data-bottom="messages"]').click();await click_action('compile')
        await page.evaluate('document.querySelector("#toast").hidden=true')
        await page.wait_for_timeout(150)
        (ROOT/'docs/images').mkdir(parents=True,exist_ok=True)
        await page.screenshot(path=str(ROOT/'docs/images/prototype-ladder.png'))
        await go('hmi');await page.locator('[data-task="instructions"]').click();await page.wait_for_timeout(150)
        await page.screenshot(path=str(ROOT/'docs/images/prototype-hmi.png'))
        await go('devices');await page.locator('[data-task="instructions"]').click();await page.wait_for_timeout(150)
        await page.screenshot(path=str(ROOT/'docs/images/prototype-devices.png'))
        report={'environment':'offline set_content + in-memory Storage double' if offline else args.url,'renderer':await page.evaluate('ControlSpacePrototype.renderer'),'unoTested':False,'physicalGpuTested':False,'passed':sum(r['passed'] for r in results),'failed':sum(not r['passed'] for r in results),'results':results,'browserErrors':errors}
        args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
        print(json.dumps({k:v for k,v in report.items() if k not in ['results','browserErrors']},indent=2))
        await browser.close()
        return report['failed']
sys.exit(asyncio.run(run()))
