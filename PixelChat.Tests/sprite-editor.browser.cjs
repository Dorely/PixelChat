// Run against an isolated PixelChat host. Requires Playwright and a local Edge installation.
const { chromium } = require(process.env.PIXELCHAT_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
async function main() {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        const connected = page.waitForEvent('websocket', { predicate: socket => socket.url().includes('/_blazor') });
        await page.goto(process.env.PIXELCHAT_TEST_URL || 'http://127.0.0.1:1465');
        const socket = await connected;
        await socket.waitForEvent('framereceived');
        await page.waitForTimeout(500);
        await page.getByRole('button', { name: 'Sprites', exact: true }).click();
        await page.getByRole('button', { name: 'New sprite', exact: true }).click();
        const form = page.locator('.sprite-form').first();
        const name = `Browser fixture ${Date.now()}`;
        await form.getByLabel('Name', { exact: true }).fill(name);
        await form.getByLabel('Width', { exact: true }).fill('8');
        await form.getByLabel('Height', { exact: true }).fill('8');
        await form.getByRole('button', { name: 'Create', exact: true }).click();
        const editor = page.locator('.native-editor'); const canvas = page.getByLabel('Sprite drawing canvas');
        await page.waitForFunction(name => document.querySelector('.native-toolbar strong')?.textContent === name, name);
        await page.waitForFunction(() => document.querySelector('.native-editor canvas')?.dataset.renderRevision === '0');
        async function revision(number) { await page.waitForFunction(n => document.querySelector('.native-editor')?.dataset.revision === String(n), number); }
        async function pixel(x, y) { return await canvas.evaluate((c, p) => [...c.getContext('2d').getImageData(p.x, p.y, 1, 1).data], { x, y }); }
        const rect = await canvas.boundingBox();
        await page.mouse.click(rect.x + 2.5 * rect.width / 8, rect.y + 3.5 * rect.height / 8);
        await revision(1); await page.waitForFunction(() => document.querySelector('.native-editor canvas').getContext('2d').getImageData(2,3,1,1).data[3] === 255);
        assert.deepEqual(await pixel(2, 3), [255,128,64,255]); assert.equal((await pixel(3, 3))[3], 0);
        await editor.getByRole('button', { name: 'Undo', exact: true }).click(); await revision(2);
        await page.waitForFunction(() => document.querySelector('.native-editor canvas').getContext('2d').getImageData(2,3,1,1).data[3] === 0);
        await editor.getByRole('button', { name: 'Redo', exact: true }).click(); await revision(3);
        await editor.getByRole('button', { name: 'Add', exact: true }).click(); await revision(4);
        assert.equal(await editor.locator('.layer-row').count(), 2);
        await editor.getByRole('button', { name: 'Duplicate frame', exact: true }).click(); await revision(5);
        assert.equal(await editor.locator('.frame-card').count(), 2);
        await editor.getByLabel('Duration (ms)', { exact: true }).fill('175');
        await editor.getByLabel('Duration (ms)', { exact: true }).press('Tab'); await revision(6);
        await editor.getByRole('button', { name: 'History', exact: true }).click();
        await editor.locator('.history details').nth(6).waitFor();
        assert.equal(await editor.locator('.history details').count(), 7);
        await page.reload(); await page.getByRole('button', { name: 'Sprites', exact: true }).click();
        await revision(6); assert.equal(await editor.locator('.frame-card').count(), 2);
        await page.waitForFunction(() => document.querySelector('.native-editor canvas')?.dataset.renderRevision === '6');
        assert.deepEqual(await pixel(2, 3), [255,128,64,255]);
        if (process.env.PIXELCHAT_TEST_SCREENSHOT) await page.screenshot({ path: process.env.PIXELCHAT_TEST_SCREENSHOT, fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS: drawing coordinates, visible pixels, undo/redo, layers, frame duplication, timing, history, reopen.');
    } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
