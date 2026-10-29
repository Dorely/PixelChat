import { spawn } from 'node:child_process';
export function native(helper: string, args: string[], input?: Buffer): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const child = spawn(helper, args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    const chunks: Buffer[] = []; let length = 0, error = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('Windows helper timed out.')); }, 10000);
    child.stdout.on('data', (chunk: Buffer) => { length += chunk.length; if (length > 65536) { child.kill(); reject(new Error('Invalid helper response size.')); } else chunks.push(chunk); });
    child.stderr.on('data', chunk => { error = (error + chunk.toString()).slice(0, 2000); });
    child.on('error', e => { clearTimeout(timer); reject(e); });
    child.on('close', code => { clearTimeout(timer); code === 0 ? resolve(Buffer.concat(chunks)) : reject(new Error(error || 'Windows helper failed.')); });
    child.stdin.on('error', () => {}); child.stdin.end(input);
  });
}
