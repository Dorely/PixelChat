import { build } from 'esbuild';
import { readFile, mkdir, mkdtemp, writeFile, copyFile, access, rm, chmod } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
const rawPlugin = { name: 'guidance-raw', setup(build) { build.onResolve({ filter: /\?raw$/ }, args => ({ path: resolve(args.resolveDir, args.path.slice(0, -4)), namespace: 'raw' })); build.onLoad({ filter: /.*/, namespace: 'raw' }, async args => ({ contents: await readFile(args.path, 'utf8'), loader: 'text' })); } };

const windows = process.platform === 'win32';
if (!(windows || process.platform === 'linux') || process.arch !== 'x64') throw new Error('Agent packaging requires Windows x64 or Linux x64.');
const root = resolve('.'), output = join(root, 'out/agent');
await mkdir(output, { recursive: true });
await build({ entryPoints: ['src/cli/index.ts'], outfile: join(output, 'cli.cjs'), bundle: true, platform: 'node', format: 'cjs', target: 'node22', plugins: [rawPlugin] });
// Pin the redistributed runtime independently of the developer's Node installation.
// Linux ships the official archive's bin/node: the archive and the extracted binary are both checked.
const nodeVersion = 'v24.21.0';
const runtimes = {
  win32: { file: 'pixelchat-runtime.exe', source: `https://nodejs.org/dist/${nodeVersion}/win-x64/node.exe`, hash: 'ba4e6d110e8c1592a1ecd390f6b05f3da124b13871a5be62b341a07a853c6c32' },
  linux: { file: 'pixelchat-runtime', source: `https://nodejs.org/dist/${nodeVersion}/node-${nodeVersion}-linux-x64.tar.xz`, archiveHash: 'fd8e59d5a511510f6a298afb548f18c7d2b1be404d8b4a27d94fbe49f56cb2d6', member: `node-${nodeVersion}-linux-x64/bin/node`, hash: '7fde7b8afa198da66257f42ee2001d874c7355631e6d1579a5fb5ef1f246df4c' },
};
const pinned = runtimes[process.platform];
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const runtimePath = join(output, pinned.file);
let runtime;
try { runtime = await readFile(runtimePath); } catch { /* first build */ }
if (!runtime || sha256(runtime) !== pinned.hash) {
  const response = await fetch(pinned.source);
  if (!response.ok) throw new Error('Could not download the pinned Node runtime.');
  const download = Buffer.from(await response.arrayBuffer());
  if (sha256(download) !== (pinned.archiveHash ?? pinned.hash)) throw new Error('Node runtime checksum mismatch.');
  if (pinned.member) {
    const scratch = await mkdtemp(join(tmpdir(), 'pixelchat-node-'));
    try {
      await writeFile(join(scratch, 'node.tar.xz'), download);
      execFileSync('tar', ['-xJf', join(scratch, 'node.tar.xz'), '-C', scratch, pinned.member]);
      runtime = await readFile(join(scratch, pinned.member));
    } finally { await rm(scratch, { recursive: true, force: true }); }
    if (sha256(runtime) !== pinned.hash) throw new Error('Node runtime checksum mismatch.');
  } else runtime = download;
  await writeFile(runtimePath, runtime);
}
const skillOutput = join(root, 'out/skill');
await mkdir(join(skillOutput, 'references'), { recursive: true });
// The runtime manifest is also the packaging/install/hash manifest; no second file list.
const guidanceBundle = await build({ entryPoints: ['src/shared/guidance.ts'], bundle: true, platform: 'node', format: 'esm', write: false, plugins: [rawPlugin] });
const { guidanceDocuments: documents, guidanceHash } = await import(`data:text/javascript;base64,${Buffer.from(guidanceBundle.outputFiles[0].text).toString('base64')}`);
for (const name of Object.keys(documents)) {
  await writeFile(join(skillOutput, 'references', name), documents[name]);
}
await copyFile('resources/skill/SKILL.md', join(skillOutput, 'SKILL.md'));
await writeFile(join(skillOutput, 'guidance.json'), JSON.stringify({ version: 1, hash: guidanceHash() }, null, 2) + '\n');
await access(join(root, 'third-party/node-LICENSE.txt'));
const manifest = { node: nodeVersion, runtimeHash: pinned.hash, source: pinned.source, ...(pinned.archiveHash ? { archiveHash: pinned.archiveHash } : {}) };
await writeFile(join(output, 'runtime.json'), JSON.stringify(manifest, null, 2) + '\n');

if (!windows) {
  // The .deb links /usr/bin/pixelchat here, so resolve the link to find the runtime beside it.
  await writeFile(join(output, 'pixelchat'), '#!/bin/sh\ndir=$(dirname "$(readlink -f "$0")")\nexec "$dir/pixelchat-runtime" "$dir/cli.cjs" "$@"\n');
  // writeFile's mode applies only to new files; packaging copies these bits.
  for (const file of ['pixelchat', pinned.file]) await chmod(join(output, file), 0o755);
  process.exit(0);
}
await writeFile(join(output, 'pixelchat.cmd'), '@echo off\r\n"%~dp0pixelchat-runtime.exe" "%~dp0cli.cjs" %*\r\n');
let cmake = process.env.CMAKE || 'cmake';
try { execFileSync(cmake, ['--version'], { stdio: 'ignore' }); } catch {
  const vswhere = join(process.env['ProgramFiles(x86)'], 'Microsoft Visual Studio/Installer/vswhere.exe');
  const vs = execFileSync(vswhere, ['-latest', '-products', '*', '-property', 'installationPath'], { encoding: 'utf8' }).trim();
  cmake = join(vs, 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe');
}
execFileSync(cmake, ['-S', 'native', '-B', 'out/native-build', '-A', 'x64'], { stdio: 'inherit' });
execFileSync(cmake, ['--build', 'out/native-build', '--config', 'Release', '--target', 'pixelchat-helper', '--parallel'], { stdio: 'inherit' });
await copyFile('out/native-build/Release/pixelchat-helper.exe', join(output, 'pixelchat-helper.exe'));
