# PixelChat

PixelChat is free to use. If it saves you time, you can [buy Dorely a coffee](https://buymeacoffee.com/dorely).

A desktop workspace for Windows and Linux for agent-aided art creation, editing, and review.
[MIT license](LICENSE).

PixelChat is built on the SfxChat stack (Electron, React/TypeScript, SQLite): an art
review workspace with an inline assistant and a CLI/MCP interface for external agents.
See [VISION](VISION.md) for direction and [architecture](docs/architecture.md) for what is
implemented. The earlier .NET/Blazor sprite workbench was removed; its history remains in git.

## Install

Download the latest build from [Releases](https://github.com/Dorely/PixelChat/releases).
Builds are x64 and unsigned.

- **Windows 10/11:** run `PixelChat-Setup-<version>.exe`. If SmartScreen warns, choose
  **More info → Run anyway**. The installer adds the `pixelchat` agent CLI to your PATH.
- **Debian/Ubuntu:** `sudo apt install ./pixelchat_<version>_amd64.deb`, then start PixelChat from
  the app menu. The `pixelchat` agent CLI is linked into `/usr/bin`.
- **Other Linux:** `chmod +x PixelChat-<version>-x86_64.AppImage` and run it (needs FUSE 2,
  `libfuse2`). Registering the agent MCP server needs the .deb or the Windows install.

Codex sign-in and the Gemini key need OS secure storage: on Linux, a Secret Service keyring
such as GNOME Keyring or KWallet. The library lives under `%APPDATA%\PixelChat` on Windows and
`~/.config/PixelChat` on Linux.

## Reviewing motion

Agents can import a GIF, animated WebP, MP4, or WebM and attach it as a review subject or reference. Open a clip to pause, scrub, or step through it. Click or drag to pin a region, or use Draw and add a note. Click a note's frame number to return to those pixels.

MP4 and WebM import requires `ffmpeg` on PixelChat's PATH with the libwebp encoder and `-fps_mode` support. PixelChat keeps the original file and prepares a silent visual preview for frame review. GIF and animated WebP work directly.

## Development

Requires Windows x64 or Linux x64 and Node.js 22.

```powershell
npm install
npm run typecheck
npm run build
npm run dev
npm run dev:browser
```

`npm run dev:browser` serves the built app at a printed loopback URL for browser-based checks.

`npm run build` also bundles the agent CLI and skill, and on Windows the native helper (needs
CMake or Visual Studio C++ build tools). `npm run package` builds the unsigned installers for
the current platform into `release/`: the NSIS installer on Windows, the AppImage and .deb on
Linux. See [agent workbench](docs/agent-workbench.md) for the CLI, MCP server, and skill install.

## Releases

Pushing a `v<version>` tag that matches `package.json` makes the Build workflow publish the
Windows and Linux packages as a GitHub release:

```powershell
npm version minor -m "Release %s"
git push origin main --follow-tags
```
