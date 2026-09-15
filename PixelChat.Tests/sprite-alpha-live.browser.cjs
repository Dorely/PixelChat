// Opt-in account/browser regression; uploads a fixture into a new validation project.
const { chromium } = require(process.env.PIXELCHAT_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { createHash } = require('node:crypto');
const { deflateSync } = require('node:zlib');

function fixturePng() {
    // Encode RGBA directly: browser canvas encoders may erase the hidden RGB being tested.
    function chunk(type, data) {
        const body = Buffer.concat([Buffer.from(type), data]);
        let crc = 0xffffffff;
        for (const byte of body) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
        const length = Buffer.alloc(4), checksum = Buffer.alloc(4);
        length.writeUInt32BE(data.length); checksum.writeUInt32BE((crc ^ 0xffffffff) >>> 0);
        return Buffer.concat([length, body, checksum]);
    }
    const size = 64, header = Buffer.alloc(13), rows = Buffer.alloc(size * (size * 4 + 1));
    header.writeUInt32BE(size, 0); header.writeUInt32BE(size, 4); header[8] = 8; header[9] = 6;
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
        const offset = y * (size * 4 + 1) + 1 + x * 4;
        rows.set([53, 114, 16, 0], offset);
        if (x >= 16 && x < 48 && y >= 16 && y < 48) rows.set([30, 200, 40, x === 16 || x === 47 || y === 16 || y === 47 ? 128 : 255], offset);
    }
    return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
}

async function main() {
    assert.equal(process.env.PIXELCHAT_LIVE_ACCOUNT_TEST, '1', 'Set PIXELCHAT_LIVE_ACCOUNT_TEST=1 to authorize account usage.');
    const bytes = process.env.PIXELCHAT_ALPHA_FIXTURE ? readFileSync(process.env.PIXELCHAT_ALPHA_FIXTURE) : fixturePng();
    const hash = buffer => createHash('sha256').update(buffer).digest('hex');
    const browser = await chromium.launch({ channel:'msedge', headless:true });
    let page, previousProject, previousModel, validationProject, requested = false;
    try {
        page = await browser.newPage({ viewport:{width:1600,height:1000} });
        await page.goto(process.env.PIXELCHAT_TEST_URL || 'http://127.0.0.1:1465');
        await page.waitForFunction(() => document.querySelector('.chat-scroll')?.dataset.chatAutoFollow !== undefined);
        const projects = page.locator('.project-controls select').first(); previousProject = await projects.inputValue();
        const model = page.locator('.chat-pane select').first(); previousModel = await model.inputValue();
        const astra = await model.locator('option').evaluateAll(options => options.find(o => o.value.endsWith(':gpt-6-astra'))?.value);
        assert.ok(astra, 'A saved OpenAI account with Astra is required.');
        if (previousModel !== astra) await model.selectOption(astra);
        const name = `Alpha vision validation ${Date.now()}`;
        await page.getByPlaceholder('New project',{exact:true}).fill(name);
        await page.getByPlaceholder('New project',{exact:true}).press('Tab');
        await page.getByRole('button',{name:'New',exact:true}).click();
        await page.waitForFunction(name => document.querySelector('.project-controls select')?.selectedOptions[0]?.textContent.trim() === name, name);
        console.log(`Created ${name}; model gpt-6-astra.`);
        await page.getByRole('button',{name:'Sprites',exact:true}).click();
        await page.getByRole('button',{name:'Import artwork',exact:true}).click();
        await page.getByLabel('Import image',{exact:true}).setInputFiles({name:'alpha-vision-fixture.png',mimeType:'image/png',buffer:bytes});
        const source = page.getByLabel('Source image',{exact:true});
        await page.waitForFunction(() => document.querySelector('[aria-label="Source image"]')?.value.length > 0);
        const assetId = await source.inputValue(), projectId = await projects.inputValue();
        validationProject = projectId;
        await page.getByRole('button',{name:'Import whole image',exact:true}).click();
        await page.getByLabel('Sprite drawing canvas',{exact:true}).waitFor();
        await page.locator('.chat-input').fill(`Inspect asset ${assetId} for suspected green background haze. This is a read-only vision integration check: do not edit, clean up, or generate anything. First read_asset on its default background, then read_asset with backgroundColor #FF00FF. Read the active sprite, inspect_frame with backgroundColor #FFFFFF and scale 1, then sprite_render its frame with backgroundColor #FF00FF and scale 1. Report measured SOURCE transparency counts and whether a background-removal regeneration is justified by the composited evidence. End with ALPHA_VISION_OK.`);
        await page.locator('[data-chat-send]').click();
        requested = true;
        await page.locator('.chat-composer').getByRole('button',{name:'Stop',exact:true}).waitFor({state:'visible'});
        console.log('Submitted alpha inspection through browser composer.');
        await page.waitForFunction(() => document.querySelector('.chat-error') || document.querySelector('.chat-scroll')?.textContent.includes('ALPHA_VISION_OK') && !Array.from(document.querySelectorAll('.chat-composer button')).some(b=>b.textContent==='Stop'), {}, {timeout:300000});
        assert.deepEqual(await page.locator('.chat-error').allTextContents(), []);
        const captions = await page.locator('.chat-visual-caption').allTextContents();
        assert.ok(captions.filter(c => c.includes('"hasTransparency":true')).length >= 4, 'All four tool views must carry source alpha metadata.');
        for (const color of ['#808080','#FF00FF','#FFFFFF']) assert.ok(captions.some(c=>c.includes(`Inspection background ${color}`)), `Missing ${color} preview`);
        const cards = page.locator('.chat-visual-card');
        let checked = 0;
        for (let index=0; index<await cards.count(); index++) {
            const card=cards.nth(index), caption=await card.locator('.chat-visual-caption').innerText();
            if (!caption.includes('Inspection background #FF00FF')) continue;
            await card.scrollIntoViewIfNeeded();
            await page.waitForFunction(index => !!document.querySelectorAll('.chat-visual-card')[index]?.querySelector('img')?.src, index);
            const src=await card.locator('img').getAttribute('src');
            const pixel=await page.evaluate(async ({url,y}) => {
                const image=new Image(); image.src=url.replace('/preview','/full'); await image.decode();
                const canvas=document.createElement('canvas'); canvas.width=image.width; canvas.height=image.height;
                const context=canvas.getContext('2d'); context.drawImage(image,0,0);
                return [...context.getImageData(0,y,1,1).data];
            },{url:src,y:caption.includes('sprite-inspection-') ? 24 : 0});
            assert.deepEqual(pixel,[255,0,255,255], 'Visible stored inspection must contain the chosen background in its pixels.'); checked++;
        }
        assert.ok(checked>=2);
        const sourceResponse=await page.request.get(new URL(`/media/projects/${projectId}/assets/${assetId}/full`,page.url()).href);
        assert.equal(hash(await sourceResponse.body()),hash(bytes),'Source bytes must remain unchanged.');
        console.log(await page.locator('.chat-scroll').innerText());
        await cards.filter({hasText:'Inspection background #FF00FF'}).first().click();
        const modal=page.getByRole('dialog');
        await modal.getByText(/Source alpha:/).waitFor();
        assert.equal(await modal.locator('.alpha-controls').count(),0,'A saved opaque composite must not show source transparency controls.');
        await modal.getByRole('button',{name:'Close',exact:true}).waitFor({state:'visible'});
        await modal.locator('img').evaluate(image=>image.decode());
        const bounds=await modal.boundingBox();
        assert.ok(bounds.x>=0 && bounds.x+bounds.width<=1600 && bounds.y>=0 && bounds.y+bounds.height<=1000,'Inspection modal must fit the viewport.');
        if (process.env.PIXELCHAT_TEST_SCREENSHOT) await page.screenshot({path:process.env.PIXELCHAT_TEST_SCREENSHOT,fullPage:true});
        await page.reload();
        await page.waitForFunction(() => document.querySelector('.chat-scroll')?.dataset.chatAutoFollow !== undefined);
        await page.locator('.project-controls select').first().selectOption(projectId.toLowerCase());
        await page.waitForFunction(id => document.querySelector('.chat-scroll')?.textContent.toLowerCase().includes(id.toLowerCase()) && document.querySelectorAll('.chat-visual-caption').length >= 4,assetId);
        assert.ok((await page.locator('.chat-visual-caption').allTextContents()).some(c=>c.includes('Inspection background #FF00FF')),'Inspection evidence survives reload.');
        console.log('PASS: live account alpha metadata, all three background-capable tools, actual composite pixels in chat, original bytes preserved, and persisted history.');
    } finally {
        try {
            if (page) {
                const stop=page.locator('.chat-composer').getByRole('button',{name:'Stop',exact:true});
                if (requested && await page.locator('.project-controls select').first().inputValue() === validationProject && await stop.count()) {
                    await stop.click(); await stop.waitFor({state:'hidden',timeout:30000});
                }
                if (previousModel) await page.locator('.chat-pane select').first().selectOption(previousModel);
                if (previousProject) await page.locator('.project-controls select').first().selectOption(previousProject);
            }
        } finally { await browser.close(); }
    }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
