import { build } from 'esbuild';
import { readFile, mkdir, writeFile, copyFile, access } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { resolve, join } from 'node:path';
const rawPlugin = { name: 'guidance-raw', setup(build) { build.onResolve({ filter: /\?raw$/ }, args => ({ path: resolve(args.resolveDir, args.path.slice(0, -4)), namespace: 'raw' })); build.onLoad({ filter: /.*/, namespace: 'raw' }, async args => ({ contents: await readFile(args.path, 'utf8'), loader: 'text' })); } };

const root = resolve('.'), output = join(root, 'out/agent');
await mkdir(output, { recursive: true });
await build({ entryPoints: ['src/cli/index.ts'], outfile: join(output, 'cli.cjs'), bundle: true, platform: 'node', format: 'cjs', target: 'node22', plugins: [rawPlugin] });
// Pin the redistributed runtime independently of the developer's Node installation.
const nodeVersion = 'v24.21.0';
const nodeHash = 'ba4e6d110e8c1592a1ecd390f6b05f3da124b13871a5be62b341a07a853c6c32';
const runtimePath = join(output, 'pixelchat-runtime.exe');
let runtime;
try { runtime = await readFile(runtimePath); } catch { /* first build */ }
if (!runtime || createHash('sha256').update(runtime).digest('hex') !== nodeHash) {
  const response = await fetch(`https://nodejs.org/dist/${nodeVersion}/win-x64/node.exe`);
  if (!response.ok) throw new Error('Could not download the pinned Node runtime.');
  runtime = Buffer.from(await response.arrayBuffer());
  if (createHash('sha256').update(runtime).digest('hex') !== nodeHash) throw new Error('Node runtime checksum mismatch.');
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
await writeFile(join(output, 'pixelchat.cmd'), '@echo off\r\n"%~dp0pixelchat-runtime.exe" "%~dp0cli.cjs" %*\r\n');
const manifest = { node: nodeVersion, runtimeHash: nodeHash, source: `https://nodejs.org/dist/${nodeVersion}/win-x64/node.exe` };
await writeFile(join(output, 'runtime.json'), JSON.stringify(manifest, null, 2) + '\n');

if (process.platform !== 'win32') throw new Error('Agent packaging currently requires Windows x64.');
let cmake = process.env.CMAKE || 'cmake';
try { execFileSync(cmake, ['--version'], { stdio: 'ignore' }); } catch {
  const vswhere = join(process.env['ProgramFiles(x86)'], 'Microsoft Visual Studio/Installer/vswhere.exe');
  const vs = execFileSync(vswhere, ['-latest', '-products', '*', '-property', 'installationPath'], { encoding: 'utf8' }).trim();
  cmake = join(vs, 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe');
}
execFileSync(cmake, ['-S', 'native', '-B', 'out/native-build', '-A', 'x64'], { stdio: 'inherit' });
execFileSync(cmake, ['--build', 'out/native-build', '--config', 'Release', '--target', 'pixelchat-helper', '--parallel'], { stdio: 'inherit' });
await copyFile('out/native-build/Release/pixelchat-helper.exe', join(output, 'pixelchat-helper.exe'));
