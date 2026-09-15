// Explicit opt-in: uses the host's saved OpenAI account and creates a validation project.
const { chromium } = require(process.env.PIXELCHAT_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');

async function main() {
    assert.equal(process.env.PIXELCHAT_LIVE_ACCOUNT_TEST, '1', 'Set PIXELCHAT_LIVE_ACCOUNT_TEST=1 to authorize real account usage.');
    const browser = await chromium.launch({ channel:'msedge', headless:true });
    let page, previousProject, previousModel;
    try {
        page = await browser.newPage({ viewport:{width:1600,height:1000} });
        await page.goto(process.env.PIXELCHAT_TEST_URL || 'http://127.0.0.1:1465');
        await page.waitForFunction(() => document.querySelector('.chat-scroll')?.dataset.chatAutoFollow !== undefined);
        const projects = page.locator('.project-controls select').first();
        await projects.waitFor(); previousProject = await projects.inputValue();
        const model = page.locator('.chat-pane select').first();
        previousModel = await model.inputValue();
        const astra = await model.locator('option').evaluateAll(options => options.find(o => o.value.endsWith(':gpt-6-astra'))?.value);
        assert.ok(astra, 'A saved OpenAI account with Astra is required.');
        if (previousModel !== astra) await model.selectOption(astra);
        const name = `Account tool validation ${Date.now()}`;
        await page.getByPlaceholder('New project', {exact:true}).fill(name);
        await page.getByPlaceholder('New project', {exact:true}).press('Tab');
        await page.getByRole('button',{name:'New',exact:true}).click();
        await page.waitForFunction(name => document.querySelector('.project-controls select')?.selectedOptions[0]?.textContent.trim() === name, name);
        console.log(`Created ${name}; model gpt-6-astra.`);
        await page.getByRole('button',{name:'Sprites',exact:true}).click();
        await page.getByRole('button',{name:'New sprite',exact:true}).click();
        const form = page.locator('.sprite-workspace > .sprite-form');
        await form.getByLabel('Name',{exact:true}).fill('Account schema fixture');
        await form.getByLabel('Width',{exact:true}).fill('8');
        await form.getByLabel('Height',{exact:true}).fill('8');
        await form.getByRole('button',{name:'Create',exact:true}).click();
        await page.waitForFunction(()=>document.querySelector('[aria-label="Sprite drawing canvas"]')?.dataset.renderRevision === '0');
        await page.locator('.chat-input').fill('Integration check on the active 8x8 sprite: use sprite_read to get its current frame and layer IDs, then use sprite_apply in one batch to pencil exactly one opaque red pixel (#ff0000ff) at x=2,y=3 and set that frame duration to 125ms. Use sprite_apply, not sprite_script or image generation. Inspect the resulting PNG with sprite_render. Then reply ACCOUNT_TOOL_OK. Do not modify anything else.');
        await page.locator('[data-chat-send]').click();
        await page.locator('.chat-composer').getByRole('button',{name:'Stop',exact:true}).waitFor({state:'visible'});
        console.log('Assistant request submitted through browser composer.');
        await page.waitForFunction(() => document.querySelector('.chat-error') || document.querySelector('.chat-scroll')?.textContent.includes('ACCOUNT_TOOL_OK') && !Array.from(document.querySelectorAll('.chat-composer button')).some(b=>b.textContent==='Stop'), { }, { timeout:240000 });
        const error = await page.locator('.chat-error').allTextContents();
        assert.deepEqual(error,[],`Assistant error: ${error.join(' ')}`);
        const canvas = page.getByLabel('Sprite drawing canvas',{exact:true});
        await page.waitForFunction(()=>Number(document.querySelector('[aria-label="Sprite drawing canvas"]')?.dataset.renderRevision)>0);
        assert.deepEqual(await canvas.evaluate(c=>[...c.getContext('2d').getImageData(2,3,1,1).data]),[255,0,0,255]);
        assert.equal(await page.getByLabel('Duration (ms)',{exact:true}).inputValue(),'125');
        const transcript = await page.locator('.chat-scroll').innerText();
        console.log(transcript);
        if (process.env.PIXELCHAT_TEST_SCREENSHOT) await page.screenshot({path:process.env.PIXELCHAT_TEST_SCREENSHOT,fullPage:true});
        console.log('PASS: real Astra assistant request, sprite_apply batch, authoritative pixels/timing, and image inspection through browser UI.');
    } finally {
        try {
            if (page) {
                const stop = page.locator('.chat-composer').getByRole('button',{name:'Stop',exact:true});
                if (await stop.count()) { await stop.click(); await stop.waitFor({state:'hidden',timeout:30000}); }
                if (previousModel) await page.locator('.chat-pane select').first().selectOption(previousModel);
                if (previousProject) await page.locator('.project-controls select').first().selectOption(previousProject);
            }
        } finally { await browser.close(); }
    }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
