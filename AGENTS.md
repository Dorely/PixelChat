# PixelChat agent guidelines

## First read

- Read [VISION.md](VISION.md), [FILEMAP.md](FILEMAP.md), and [docs/architecture.md](docs/architecture.md) before substantive work.
- Before provider, prompting, image, or credential work, read the relevant [image-provider knowledge](docs/knowledge/README.md).
- Verify implementation from files and relevant checks. Vision and proposed architecture are intent, not evidence of working features.
- Keep this file focused on durable working practices. Product direction belongs in VISION, technical decisions in architecture, and navigation in FILEMAP.

## Repository workflow

- Inspect branch, working tree, index, and remotes before starting. Continue on the current branch.
- Start new work from a clean commit. Review existing changes first; commit coherent completed work after verification. If changes are incomplete or ownership is unclear, ask for direction rather than overwriting or mixing them with new work.
- Preserve unrelated user changes. Do not discard work, rewrite history, publish, or create remotes without authorization.
- Finish each coherent task with a reviewed, focused commit and verify a clean working tree.

## Implementation practices

- Inspect callers and downstream effects before editing. Use `rg` for file and text discovery.
- Keep UI interaction state separate from library operations, assistant orchestration, provider transports, and background jobs. Manual UI actions, the in-app assistant, and CLI/MCP agents use the same owning operations in the shared operation registry.
- Preserve provider boundaries; do not spread provider-specific payloads or model names through the UI.
- Implement only what the current milestone needs. The desktop stack is Electron with React/TypeScript; keep privileged operations in the main process and the renderer isolated.
- Replace superseded runtime code and documentation coherently. Do not keep compatibility shims, legacy aliases, or dual paths unless the user asks for a transition.
- This project deliberately carries no automated tests. Do not add or reintroduce
  unit, integration, or end-to-end suites (for example Vitest, Jest, or Playwright);
  `*.test.ts`, `*.spec.ts`, or any other test-only files; test runners, test
  dependencies, or CI test steps; test-only fixtures, mock providers, deterministic
  test modes, or harnesses built to exercise the app. Delete such scaffolding when you
  encounter it rather than extending it.
- Verify work with `npm run typecheck`, the production build, and live/manual checks
  instead, and record what was actually run. Temporary local helpers are allowed, but
  keep them outside the repository and delete them when the check is done.
- For manual UI verification, use `npm run dev:browser` and control the printed
  loopback URL with browser tools. This runs the real renderer against Electron's
  main-process operations, actual library, and OS-backed credentials; it is not a
  mock. Keep paid or account-metered image calls within existing authorization.
  Use the native desktop only for desktop-specific integration checks. Stop the
  browser development host after validation and record any coverage gaps.
- Never point verification exports at a real game repository. Use scratch copies.
- Use UTF-8 and LF for authored text.

## Credentials and provider operations

- App-configured API keys and OAuth tokens must use OS-backed secure storage through a narrow privileged boundary. Never use plaintext database storage, browser local storage, committed configuration, prompts, or logs for secret values.
- The UI may accept a credential for saving; it should receive only readiness/masked status afterward. Resolve stored credentials inside provider operations, not assistant tool arguments.
- Never copy sibling-app databases, auth caches, real credentials, or account tokens into this repository.
- Revalidate provider models, capabilities, request fields, limits, and pricing against official sources when implementing or invoking them. Bundled notes are dated handoff material.
- Keep paid work within the user's authorized scope. Never silently retry an uncertain billed generation or expand a batch indefinitely.

## Art evidence

- Preserve original imported and returned image bytes, exact prompts, provider/model details, request identifiers when available, hashes, and revision lineage. Preview backgrounds, zoom, and overlays never change stored bytes.
- Distinguish technical measurements (dimensions, alpha coverage), model observations, and the user's review decisions. An agent viewing an image is an observation, not acceptance.
- Preserve notes, pins, rejections, and uncertainty. Agent completion of a review is not human acceptance.
- Export back to a source folder only through the export operation, which refuses to overwrite a source file that changed since it was last synced.

## Documentation and verification

- Update FILEMAP whenever files are added, removed, renamed, or materially repurposed. Keep entries concise and exclude generated output.
- Keep architecture and README synchronized with implemented behavior and verified setup commands. Change VISION when accepted product direction changes.
- For implementation, run checks appropriate to the affected behavior and record actual evidence in [docs/verification.md](docs/verification.md). Do not claim provider, login, secure-storage, or platform integration from compilation alone.
- Plan cleanup before starting temporary hosts or helpers and terminate those you start after validation. Never terminate a user-owned process without authorization.
