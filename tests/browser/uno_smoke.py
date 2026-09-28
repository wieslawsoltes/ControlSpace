#!/usr/bin/env python3
"""Verify that a published Uno application starts, not that the JS prototype starts."""
import argparse
import asyncio
import json
from pathlib import Path
from playwright.async_api import async_playwright
parser=argparse.ArgumentParser()
parser.add_argument('url')
parser.add_argument('--output',type=Path,default=Path('artifacts/uno-verification'))
args=parser.parse_args()
async def main():
    async with async_playwright() as p:
        browser=await p.chromium.launch(headless=True,args=['--no-sandbox'])
        page=await browser.new_page(viewport={'width':1600,'height':1000})
        logs=[];errors=[];ready=asyncio.Event()
        def log(message):
            logs.append({'type':message.type,'text':message.text})
            if '[ControlSpace] Uno workspace ready' in message.text: ready.set()
        page.on('console',log);page.on('pageerror',lambda error:errors.append(str(error)))
        await page.goto(args.url,wait_until='domcontentloaded',timeout=120000)
        args.output.mkdir(parents=True,exist_ok=True)
        try:
            await asyncio.wait_for(ready.wait(),timeout=120)
            if await page.evaluate('!!window.ControlSpacePrototype'): raise AssertionError('The page is the JS prototype, not Uno.')
            if errors: raise AssertionError('\n'.join(errors))
            # User-level shortcut reaches the C# workbench handler when focused.
            await page.mouse.click(650,360)
            await page.keyboard.press('F7')
            await page.wait_for_timeout(500)
            await page.screenshot(path=str(args.output/'uno-workspace.png'))
        finally:
            (args.output/'logs.json').write_text(json.dumps({'console':logs,'errors':errors},indent=2)+'\n')
            await browser.close()
asyncio.run(main())
