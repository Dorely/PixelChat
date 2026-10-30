# PixelChat vision

PixelChat is a local workspace for agent-aided art creation, editing, and review. It brings
artwork from a game or other project into one place where a person can look at it properly,
decide what is good, say precisely what is not, and hand that feedback to an agent that does
the follow-up work.

The desktop is the distribution target. Development also runs the real workspace in a local
browser so agents can verify ordinary UI behavior with the same privileged operations,
library, and secure credentials.

## Review first

The first use case is bulk review of existing game art. Users import a folder tree from
their game, browse it by folder and status, and move quickly through it: accept, mark as
needing changes, or reject, leaving notes for the whole image or pinned to a region of it.
Viewing must make art easy to judge: zoom, crisp pixel scaling, and transparency shown
against checkerboard, light, dark, or custom backgrounds.

Imports are snapshots, and stored art is never changed in place. Every image loaded or
made is a new asset; lineage groups the versions of one artwork. The library keeps the
exact bytes that were reviewed together with a link to the source file, so decisions and
notes always refer to the version the user saw. Rescanning a source picks up changed files
as new versions, which arrive unreviewed. A completed review stays exactly as it was
completed.

## Agents as collaborators

An inline chat assistant can operate every function of the workspace. The same operations
are offered to agents in other harnesses (Claude Code, Codex) through a bundled CLI and
stdio MCP server, with installable skills. Agents read review decisions and notes, view the
artwork, reply to or resolve notes, and can hold a connection on a review to respond as
soon as the user decides, writes a note, or completes it. The workspace is where humans review; agents never need to build their own
viewers.

## Creating and editing

PixelChat calls image models itself and also accepts art produced elsewhere. Users can run
their own edit prompts directly from the review workspace; results appear as candidate
revisions beside the original. Choosing a version marks the item as pending export, which
agents can see and export back to the source file. Export never silently overwrites a
source that changed since it was last synced.

Providers sit behind a neutral contract: an OpenAI account (Codex image endpoint) shares
the chat login, and Gemini uses an API key configured in the app and stored securely.
Native alpha transparency, model and quality choice, and exact prompts are preserved as
provenance.

## Projects

Work is organized into projects. A project holds its source folders, library, reviews, and
conversations. Provider configuration and credentials stay app-level.

## Later

Local art-editing tools, sprite-sheet and animation workspaces, reusable style recipes, and
additional providers are expected to return later. They are deliberately out of the first
milestone.

## Product character

PixelChat is a focused tool for looking at art and acting on feedback. It should make review
fast and unambiguous, keep enough provenance to trust what changed and why, and make the
handoff between the person and the agent visible in both directions.
