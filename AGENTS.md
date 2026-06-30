# PixelChat - Project Guidelines

## First Read
- Read `VISION.md` immediately before doing any substantive work in this repo.
- Read `FILEMAP.md` at the start of every session to understand the full codebase layout.
- Treat `VISION.md` as the high-level direction for the project.
- Do not assume everything in `VISION.md` is implemented or currently part of the implementation plan. Use the codebase to confirm current behavior.
- Keep this file (`AGENTS.md`) stable and high level. Do not add notes here that are likely to become stale during normal development.

## File Map Maintenance
- After adding, deleting, or renaming any source file, update `FILEMAP.md` to reflect the change.
- When refactoring moves code between files or changes a file's responsibility, update the description in `FILEMAP.md`.
- Keep `FILEMAP.md` entries concise - one to two lines per file maximum. If a file is doing more than can be reasonably construed within that limit, consider refactoring it.

## Tech Stack
- .NET 10 Blazor Interactive Server for the application UI and local app host.
- Electron.NET for the desktop shell on Windows, Linux, and macOS.
- EF Core with SQLite for local persistence.
- `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, and the OpenAI .NET SDK for chat-provider abstraction and OpenAI-compatible endpoints.
- Bootstrap is vendored by the Blazor template for first-slice UI styling.

## Architectural Overview
- PixelChat is desktop-first: normal user operation is through Electron, while browser-hosted development remains available for local startup and debugging when needed.
- The app runs a local-only ASP.NET Core host and stores local state in SQLite.

## Code Style
- Use dependency injection for services and repositories.
- Configuration belongs in `appsettings.json` and environment variables.
- Keep UI components focused on interaction state; put persistence, provider resolution, and chat behavior in services.
- Do not store new secrets directly on feature entities. Add them through `ISecretStore`.

## Build & Run
```bash
dotnet build PixelChat.sln
dotnet run --project PixelChat
dotnet run --project PixelChat -- --electron
```

## Verification
- Do not add test projects or automated tests unless the user explicitly requests them.
- Verify normal changes with `dotnet build PixelChat.sln`.
- After a successful build, run `dotnet run --project PixelChat`, confirm the app host starts without startup exceptions, and terminate it.
- VS Code F5 is configured to build and debug the Electron desktop shell by default.
- Do not run Playwright, screenshots, browser UI checks, Electron window checks, or manual UI validation unless the user explicitly requests them.
- When Electron startup itself must be smoke-checked, start `dotnet run --project PixelChat -- --electron`, confirm startup, and terminate the app. Never leave the app running.

## Conventions
- When you need to understand current wiring, start with `FILEMAP.md`, then the relevant feature area.
- Trace each requested change through its full impact area before considering the work complete. Changes to models, contracts, or core concepts should include all affected layers such as persistence, services, UI, and documentation.
- Remove superseded code and concepts when replacing them. Do not leave deprecated pages, components, handlers, prompts, queries, or other logic in place just because the new path works; clean out obsolete implementations and reduce unnecessary complexity.
- Before any work you must ALWAYS look to see how this code should be properly incorporated into the existing codebase to avoid code or concept duplication. Always think about how your maintainable and editable your implementation will be and aim for code that does not need to be refactored later.
- This is a local development project. When a requested change replaces a concept, remove the superseded implementation outright; do not add or retain compatibility shims, legacy handlers/fallbacks, deprecated tool aliases, or dual paths unless the user explicitly asks for a transition path.
