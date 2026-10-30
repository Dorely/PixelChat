import { readFile, writeFile, mkdir, rename, unlink, rmdir } from 'node:fs/promises';
import { homedir } from 'node:os';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import * as TOML from '@iarna/toml';
import { guidanceDocuments, guidanceHash } from './guidance';
import skillText from '../../resources/skill/SKILL.md?raw';
import { installationSchema, WorkbenchError, type InstallationInput } from './workbench';

export { skillText };
export function skillFiles(): Record<string, string> {
  return { 'SKILL.md': skillText, ...Object.fromEntries(Object.entries(guidanceDocuments).map(([name, value]) => [`references/${name}`, value])), 'guidance.json': JSON.stringify({ version: 1, hash: guidanceHash() }, null, 2) + '\n' };
}
async function read(path: string) { try { return await readFile(path, 'utf8'); } catch (e) { if ((e as NodeJS.ErrnoException).code === 'ENOENT') return undefined; throw e; } }
export async function installIntegration(raw: InstallationInput, runtime: string, client: string) {
  const input = installationSchema.parse(raw);
  input.targets = [...new Set(input.targets)];
  if (!input.targets.length && !input.directory) throw new WorkbenchError('INVALID_ARGUMENTS', 'Choose at least one harness target or custom skills directory.');
  if (input.scope === 'project' && (!input.projectDirectory || !isAbsolute(input.projectDirectory))) throw new WorkbenchError('INVALID_PATH', 'Project scope requires an absolute projectDirectory.');
  if (input.directory && !isAbsolute(input.directory)) throw new WorkbenchError('INVALID_PATH', 'Custom skills directory must be absolute.');
  if (input.mcp && input.directory && !input.targets.length) throw new WorkbenchError('INVALID_ARGUMENTS', 'MCP registration requires a named harness target.');
  const home = homedir(), project = input.projectDirectory ? resolve(input.projectDirectory) : '';
  const roots = input.targets.map(target => ({ target, root: join(input.scope === 'project' ? project : home, target === 'codex' ? '.agents' : '.claude', 'skills', 'pixelchat') }));
  if (input.directory && !roots.some(entry => entry.root.toLowerCase() === join(resolve(input.directory!), 'pixelchat').toLowerCase())) roots.push({ target: 'codex', root: join(resolve(input.directory), 'pixelchat') });
  const plan: { path: string; old?: string; content?: string; status: string }[] = [];
  if (input.skill) for (const { root } of roots) for (const [relative, content] of Object.entries(skillFiles())) {
    const path = join(root, relative), old = await read(path);
    const conflict = old !== undefined && old !== content;
    if (conflict && !input.force && input.action !== 'status') throw new WorkbenchError('INSTALL_CONFLICT', `File differs: ${path}. Use force only to replace/remove this customization.`);
    plan.push({ path, old, content: input.action === 'uninstall' ? undefined : content, status: old === undefined ? 'missing' : conflict ? 'different' : 'up-to-date' });
  }
  const entry = { command: runtime, args: [client, 'mcp'] };
  // Codex ends tool calls after 60 seconds by default; review_wait may hold one for up to 1800.
  const CODEX_TOOL_TIMEOUT = 1900;
  if (input.mcp) for (const target of input.targets) {
    const path = target === 'codex' ? join(input.scope === 'project' ? join(project, '.codex') : (process.env.CODEX_HOME || join(home, '.codex')), 'config.toml')
      : input.scope === 'project' ? join(project, '.mcp.json') : join(home, '.claude.json');
    const old = await read(path);
    let content: string | undefined, existing: unknown;
    const expected = target === 'codex' ? { ...entry, tool_timeout_sec: CODEX_TOOL_TIMEOUT } : entry;
    if (target === 'codex') {
      const parsed = TOML.parse(old ?? '') as Record<string, any>;
      existing = parsed.mcp_servers?.pixelchat;
      const block = /^\[mcp_servers\.pixelchat(?:\.[^\]]+)?\][^\S\r\n]*\r?\n[\s\S]*?(?=^\[|(?![\s\S]))/gm;
      const text = old ?? '';
      if (existing && !/^\[mcp_servers\.pixelchat\][^\S\r\n]*\r?\n/m.test(text)) throw new WorkbenchError('INSTALL_CONFLICT', 'PixelChat MCP uses an inline/quoted TOML definition; edit that entry manually before using the installer.');
      content = text.replace(block, '');
      if (input.action !== 'uninstall') content = content.replace(/\s*$/, '') + '\n\n[mcp_servers.pixelchat]\ncommand = ' + JSON.stringify(runtime) + '\nargs = ' + JSON.stringify(entry.args) + '\ntool_timeout_sec = ' + CODEX_TOOL_TIMEOUT + '\n';
      TOML.parse(content);
    } else {
      const parsed = JSON.parse(old ?? '{}');
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed) || (parsed.mcpServers && (typeof parsed.mcpServers !== 'object' || Array.isArray(parsed.mcpServers)))) throw new WorkbenchError('INSTALL_CONFLICT', 'Invalid harness configuration shape.');
      existing = parsed.mcpServers?.pixelchat;
      parsed.mcpServers ??= {};
      if (input.action === 'uninstall') delete parsed.mcpServers.pixelchat; else parsed.mcpServers.pixelchat = entry;
      content = JSON.stringify(parsed, null, 2) + '\n';
    }
    const identical = existing && JSON.stringify(existing) === JSON.stringify(expected);
    if (existing && !identical && !input.force && input.action !== 'status') throw new WorkbenchError('INSTALL_CONFLICT', `MCP entry differs in ${path}; explicit force is required.`);
    if ((input.action !== 'uninstall' && identical) || (input.action === 'uninstall' && !existing)) content = old;
    plan.push({ path, old, content, status: existing ? identical ? 'up-to-date' : 'different' : 'missing' });
  }
  // Preflight every target before writing any. Check again at commit to preserve concurrent edits.
  const applied: { path: string; backup?: string }[] = [];
  if (!input.dryRun && input.action !== 'status') for (const item of plan) {
    if (item.old === item.content) continue;
    if (await read(item.path) !== item.old) throw new WorkbenchError('INSTALL_CONFLICT', `File changed during installation: ${item.path}`, { applied });
    await mkdir(dirname(item.path), { recursive: true });
    let backup: string | undefined;
    if (item.old !== undefined) { backup = item.path + `.pixelchat-backup-${randomUUID()}`; await writeFile(backup, item.old, { flag: 'wx' }); }
    if (item.content === undefined) await unlink(item.path);
    else { const temp = item.path + `.pixelchat-${randomUUID()}`; await writeFile(temp, item.content, { flag: 'wx' }); await rename(temp, item.path); }
    applied.push({ path: item.path, backup });
  }
  if (input.action === 'uninstall' && !input.dryRun) for (const { root } of roots) { await rmdir(join(root, 'references')).catch(() => {}); await rmdir(root).catch(() => {}); }
  return { action: input.action, dryRun: input.dryRun, files: plan.map(({ path, status }) => ({ path, status })), applied };
}
