import { chmod, mkdir, readdir, readFile, rename, unlink, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { randomBytes } from 'node:crypto';
import { native } from './native';

/**
 * Bridge discovery for the agent CLI. Windows: a random named pipe, published in a file encrypted
 * with DPAPI inside a directory whose ACL admits only the user and SYSTEM (both via the native
 * helper). Linux: a Unix socket and a plain discovery file, both inside an owner-only (0700)
 * directory, the file itself 0600.
 */
export interface Discovery { version: 1; pipe: string; token: string; pid: number }

const windows = process.platform === 'win32';
const SOCKET = /^pixelchat-[a-f0-9]{32}\.sock$/;

/** The profile Electron uses: `%APPDATA%\PixelChat`, or `$XDG_CONFIG_HOME/PixelChat` (default `~/.config`). */
export function profileRoot() {
  return join(windows ? process.env.APPDATA || '' : process.env.XDG_CONFIG_HOME || join(homedir(), '.config'), 'PixelChat');
}
const directoryOf = (root: string) => join(root, 'bridge');
const fileOf = (root: string) => join(directoryOf(root), windows ? 'endpoint.bin' : 'endpoint.json');

/** Secures the bridge directory and returns a fresh endpoint to listen on. */
export async function prepareEndpoint(root: string, helper: string) {
  const directory = directoryOf(root);
  if (windows) {
    await mkdir(directory, { recursive: true });
    await native(helper, ['secure-directory', directory]);
    return `\\\\.\\pipe\\pixelchat-${randomBytes(32).toString('hex')}`;
  }
  await mkdir(directory, { recursive: true, mode: 0o700 }); await chmod(directory, 0o700);
  // The single-instance lock means any socket left here belongs to an app that is gone.
  for (const name of await readdir(directory)) if (SOCKET.test(name)) await unlink(join(directory, name)).catch(() => {});
  // Shorter than the pipe name: Unix socket paths are limited to 107 bytes.
  return join(directory, `pixelchat-${randomBytes(16).toString('hex')}.sock`);
}

export async function publishDiscovery(root: string, helper: string, value: Discovery) {
  const path = fileOf(root), plain = Buffer.from(JSON.stringify(value));
  const bytes = windows ? await native(helper, ['protect'], plain) : plain;
  await writeFile(path + '.partial', bytes, { mode: 0o600 }); await rename(path + '.partial', path);
}

export async function removeDiscovery(root: string) { await unlink(fileOf(root)).catch(() => {}); }

export async function readDiscovery(root: string, helper: string): Promise<Discovery> {
  const bytes = await readFile(fileOf(root));
  const value = JSON.parse((windows ? await native(helper, ['unprotect'], bytes) : bytes).toString());
  const endpoint = typeof value.pipe === 'string' && (windows ? /^\\\\\.\\pipe\\pixelchat-[a-f0-9]{64}$/.test(value.pipe)
    : dirname(value.pipe) === directoryOf(root) && SOCKET.test(basename(value.pipe)));
  if (value.version !== 1 || !endpoint || !/^[a-f0-9]{64}$/.test(value.token) || !Number.isInteger(value.pid)) throw new Error('Invalid bridge discovery.');
  return value as Discovery;
}
