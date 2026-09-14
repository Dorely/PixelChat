// Isolated-host browser checks for large-image canvas navigation and inspector layout.
const { chromium } = require(process.env.PIXELCHAT_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');

async function main() {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.PIXELCHAT_TEST_URL || 'http://127.0.0.1:1465');
        await page.getByRole('button', { name: 'Sprites', exact: true }).click();
        const editor = page.locator('.native-editor');
        const viewport = page.getByLabel('Sprite viewport', { exact: true });
        const canvas = page.getByLabel('Sprite drawing canvas', { exact: true });
        const view = () => viewport.evaluate(v => ({ zoom: Number(v.dataset.zoom), x: Number(v.dataset.panX), y: Number(v.dataset.panY), width: v.clientWidth, height: v.clientHeight, fit: v.dataset.fit }));
        async function fits(width, height) {
            await page.waitForFunction(() => document.querySelector('.canvas-viewport')?.dataset.fit === 'true');
            const v = await view();
            assert.ok(width * v.zoom <= v.width + 1 && height * v.zoom <= v.height + 1, 'Whole image must fit');
            assert.ok(Math.abs(v.x - (v.width-width*v.zoom)/2) < 1, 'Image centered horizontally');
            assert.ok(Math.abs(v.y - (v.height-height*v.zoom)/2) < 1, 'Image centered vertically');
            assert.ok(v.width > 200 && v.height > 250, 'Canvas must retain useful viewport space');
            const overflow = await page.evaluate(() => ({ page: document.documentElement.scrollWidth > innerWidth, workspace: document.querySelector('.sprite-workspace').scrollHeight > document.querySelector('.sprite-workspace').clientHeight + 1 }));
            assert.deepEqual(overflow, { page: false, workspace: false });
        }
        for (const [width,height] of [[8,8],[1024,1536],[4096,2048]]) {
            await page.getByRole('button', { name: 'New sprite', exact: true }).click();
            const form = page.locator('.sprite-workspace > .sprite-form');
            const name = `Viewport ${width}x${height} ${Date.now()}`;
            await form.getByLabel('Name', { exact: true }).fill(name);
            await form.getByLabel('Width', { exact: true }).fill(String(width));
            await form.getByLabel('Height', { exact: true }).fill(String(height));
            await form.getByLabel('Art mode').selectOption('painted');
            await form.getByRole('button', { name: 'Create', exact: true }).click();
            await page.waitForFunction(name => document.querySelector('.document-identity strong')?.textContent === name, name);
            await page.waitForFunction(w => document.querySelector('[aria-label="Sprite drawing canvas"]')?.width === w && document.querySelector('[aria-label="Sprite drawing canvas"]')?.dataset.renderRevision === '0', width);
            await fits(width,height);
            if (width > 1000) assert.ok((await view()).zoom < 1, 'Large images start below 100%');
        }
        const rect = await viewport.boundingBox();
        const cursor = { x: Math.round(rect.x + rect.width*.43), y: Math.round(rect.y + rect.height*.42) };
        const before = await view();
        const logical = { x:(cursor.x-rect.x-before.x)/before.zoom, y:(cursor.y-rect.y-before.y)/before.zoom };
        await page.mouse.move(cursor.x,cursor.y); await page.mouse.wheel(0,-240);
        await page.waitForFunction(z => Number(document.querySelector('.canvas-viewport').dataset.zoom) > z, before.zoom);
        const zoomed = await view();
        assert.ok(Math.abs((cursor.x-rect.x-zoomed.x)/zoomed.zoom-logical.x) < .05, 'Wheel anchors x');
        assert.ok(Math.abs((cursor.y-rect.y-zoomed.y)/zoomed.zoom-logical.y) < .05, 'Wheel anchors y');
        await page.mouse.down({ button:'right' }); await page.mouse.move(cursor.x+70,cursor.y+40,{steps:8}); await page.mouse.up({button:'right'});
        const panned = await view();
        assert.ok(Math.abs(panned.x-zoomed.x-70)<1 && Math.abs(panned.y-zoomed.y-40)<1, 'Right drag pans by pointer delta');
        assert.equal(await editor.getAttribute('data-revision'),'0','Navigation must not create document edits');
        assert.equal(await viewport.evaluate(v=>v.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true}))),false,'Suppress native context menu');
        // Draw after navigation and verify the authoritative image coordinate, then undo without losing the view.
        const target = await canvas.evaluate((c,p)=>{const r=c.getBoundingClientRect();return {x:Math.floor((p.x-r.x)*c.width/r.width),y:Math.floor((p.y-r.y)*c.height/r.height)};},cursor);
        await page.mouse.click(cursor.x,cursor.y);
        await page.waitForFunction(() => document.querySelector('.native-editor').dataset.revision === '1' && document.querySelector('[aria-label="Sprite drawing canvas"]').dataset.renderRevision === '1');
        assert.deepEqual(await canvas.evaluate((c,p)=>[...c.getContext('2d').getImageData(p.x,p.y,1,1).data],target),[255,128,64,255]);
        assert.equal((await view()).zoom,panned.zoom);
        await editor.getByRole('button',{name:'Undo',exact:true}).click();
        await page.waitForFunction(() => document.querySelector('[aria-label="Sprite drawing canvas"]').dataset.renderRevision === '2');
        assert.equal((await view()).zoom,panned.zoom);
        const zoomInput=editor.getByLabel('Zoom percent',{exact:true});
        await zoomInput.fill('12800'); await zoomInput.press('Enter'); assert.equal((await view()).zoom,128);
        assert.equal(await canvas.evaluate(c=>c.width),4096,'Zoom never resizes the raster');
        await zoomInput.fill('0.1'); await zoomInput.press('Enter'); assert.equal((await view()).zoom,.001);
        await editor.getByRole('button',{name:'Fit',exact:true}).click(); await fits(4096,2048);
        await page.setViewportSize({width:1280,height:800});
        await page.waitForTimeout(150); await fits(4096,2048);
        await editor.getByRole('button',{name:'Toggle inspector',exact:true}).click();
        await page.waitForTimeout(150); await fits(4096,2048);
        await editor.getByRole('button',{name:'Toggle inspector',exact:true}).click();
        // Import an actual high-resolution PNG, not just a blank document.
        const encoded = await page.evaluate(() => {
            const image = document.createElement('canvas'); image.width = 4096; image.height = 2048;
            const c = image.getContext('2d');
            for (const [x,y,color] of [[64,64,'#446089'],[2112,64,'#5e547a'],[64,1088,'#477b7c'],[2112,1088,'#ae875e']]) {
                c.fillStyle=color; c.fillRect(x,y,1920,896);
            }
            c.fillStyle='#ffffff'; c.font='64px sans-serif'; c.fillText('4096 × 2048 · full-resolution import',160,200);
            return image.toDataURL('image/png').split(',')[1];
        });
        await page.getByRole('button',{name:'Import artwork',exact:true}).click();
        await page.getByLabel('Import image',{exact:true}).setInputFiles({name:'viewport-large.png',mimeType:'image/png',buffer:Buffer.from(encoded,'base64')});
        await page.locator('.source-preview').waitFor({state:'visible'});
        await page.getByRole('button',{name:'Import whole image',exact:true}).click();
        await page.waitForFunction(() => document.querySelector('[aria-label="Sprite drawing canvas"]').dataset.renderRevision === '0');
        await fits(4096,2048);
        assert.deepEqual(await canvas.evaluate(c=>[...c.getContext('2d').getImageData(100,100,1,1).data]),[68,96,137,255]);
        assert.equal(await editor.getAttribute('data-art-mode'),'painted');
        await editor.getByRole('button',{name:'AI',exact:true}).click();
        await editor.getByLabel('Sprite AI prompt').fill('Preserve the full resolution and silhouette.');
        await editor.getByRole('button',{name:'Layers',exact:true}).click();
        await page.waitForFunction(() => document.querySelector('.inspector-tabs button')?.classList.contains('active'));
        await editor.getByRole('button',{name:'AI',exact:true}).click();
        await editor.locator('.sprite-ai').waitFor({state:'visible'});
        assert.equal(await editor.getByLabel('Sprite AI prompt').inputValue(),'Preserve the full resolution and silhouette.');
        await fits(4096,2048);
        if(process.env.PIXELCHAT_TEST_SCREENSHOT) await page.screenshot({path:process.env.PIXELCHAT_TEST_SCREENSHOT,fullPage:true});
        assert.deepEqual(errors,[]);
        console.log('PASS: 8px, 1024px and 4096px fit; wheel anchoring; right-drag pan; no navigation edits; transformed drawing/undo; 0.1–12800% zoom; resize fit; full-resolution PNG import; inspector layout and retained AI drafts.');
    } finally { await browser.close(); }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
