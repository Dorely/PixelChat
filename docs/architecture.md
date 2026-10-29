# PixelChat architecture

## Runtime and ownership

PixelChat uses Electron, React/TypeScript, electron-vite, and electron-builder with a
Windows x64 NSIS target. Exact versions live in the lockfile. This document describes
implementation; [verification](verification.md) records actual checks.
[VISION](../VISION.md) remains product intent.

The main process owns persistence, media, providers, credentials, jobs, and turns.
React owns presentation, selection, and forms. The sandboxed, isolated preload exposes
fixed typed operations. The renderer has no Node integration.

The profile lives at `%APPDATA%/PixelChat`.

## Verification policy

This project carries no automated tests. Verification is `npm run typecheck`, the
production build, and live/manual runs of the app, recorded in [verification](verification.md).
