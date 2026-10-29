import { forwardRef, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { Item, NoteThread, Pin, Revision } from '../shared/types'
import Icon from './Icon'
import { ConfirmDialog } from './Dialogs'
import { op } from './ops'

const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback
const author = (origin: string) => origin === 'agent' ? 'Agent' : 'You'
export const pinLabel = (pin: Pin) => pin.width ? `${pin.x}, ${pin.y} · ${pin.width}×${pin.height}` : `${pin.x}, ${pin.y}`
/** Open pinned threads in display order; their position is the pin number. */
export const pinnedThreads = (notes: NoteThread[]) => notes.filter(thread => thread.pin && !thread.resolvedAt)

type Frame = { left: number; top: number; width: number; height: number }

/**
 * Overlay over the viewed image: shows open pins and turns click or drag into a draft pin.
 * Pins are stored in the noted revision's pixels; ones from another version are drawn dashed.
 */
export function PinLayer({ image, stage, revision, revisions, notes, draft, activeNoteId, onDraft, onActivate }: {
  image: HTMLImageElement | null; stage: HTMLElement | null; revision: Revision; revisions: Revision[]; notes: NoteThread[]; draft: Pin | null; activeNoteId: string | null
  onDraft: (pin: Pin) => void; onActivate: (id: string | null) => void
}) {
  const [frame, setFrame] = useState<Frame | null>(null)
  const [drag, setDrag] = useState<{ start: { x: number; y: number }; screen: { x: number; y: number }; current: Pin } | null>(null)
  useLayoutEffect(() => {
    if (!image || !stage) return
    // The image may letterbox inside its box under object-fit, so map to the drawn content.
    const measure = () => {
      if (!image.naturalWidth || !image.clientWidth) return setFrame(null)
      const scale = Math.min(image.clientWidth / image.naturalWidth, image.clientHeight / image.naturalHeight)
      const width = image.naturalWidth * scale, height = image.naturalHeight * scale
      setFrame({ left: image.offsetLeft + (image.clientWidth - width) / 2, top: image.offsetTop + (image.clientHeight - height) / 2, width, height })
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(image); observer.observe(stage)
    image.addEventListener('load', measure)
    return () => { observer.disconnect(); image.removeEventListener('load', measure) }
  }, [image, stage])
  if (!frame) return null

  const toImage = (event: React.PointerEvent) => {
    const rect = event.currentTarget.getBoundingClientRect()
    const clamp = (value: number, max: number) => Math.min(max - 1, Math.max(0, Math.floor(value)))
    return { x: clamp((event.clientX - rect.left) / rect.width * revision.width, revision.width), y: clamp((event.clientY - rect.top) / rect.height * revision.height, revision.height) }
  }
  const box = (a: { x: number; y: number }, b: { x: number; y: number }): Pin => ({ x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(a.x - b.x) + 1, height: Math.abs(a.y - b.y) + 1 })
  const sizes = new Map(revisions.map(entry => [entry.id, entry]))
  const shape = (pin: Pin, source: { width: number; height: number }) => pin.width
    ? { left: `${pin.x / source.width * 100}%`, top: `${pin.y / source.height * 100}%`, width: `${pin.width / source.width * 100}%`, height: `${(pin.height ?? 1) / source.height * 100}%` }
    : { left: `${(pin.x + 0.5) / source.width * 100}%`, top: `${(pin.y + 0.5) / source.height * 100}%` }
  const shown = drag?.current ?? draft

  return <div className="pin-layer" style={frame} title="Click to pin a point, drag to pin a region"
    onPointerDown={event => { if (event.button !== 0) return; event.currentTarget.setPointerCapture(event.pointerId); const start = toImage(event); setDrag({ start, screen: { x: event.clientX, y: event.clientY }, current: start }) }}
    onPointerMove={event => { if (drag) setDrag({ ...drag, current: box(drag.start, toImage(event)) }) }}
    onPointerUp={event => {
      if (!drag) return
      const moved = Math.hypot(event.clientX - drag.screen.x, event.clientY - drag.screen.y) > 4
      onDraft(moved ? box(drag.start, toImage(event)) : drag.start); setDrag(null)
    }}
    onPointerCancel={() => setDrag(null)}>
    {pinnedThreads(notes).map((thread, index) => {
      const source = sizes.get(thread.revisionId) ?? revision
      return <button key={thread.id} className={`pin-mark${thread.pin!.width ? ' pin-box' : ''}${thread.revisionId !== revision.id ? ' pin-stale' : ''}${thread.id === activeNoteId ? ' pin-active' : ''}`}
        style={shape(thread.pin!, source)} onPointerDown={event => event.stopPropagation()} onClick={() => onActivate(thread.id === activeNoteId ? null : thread.id)}
        aria-label={`Pin ${index + 1}`} title={thread.body}><span>{index + 1}</span></button>
    })}
    {shown && <span className={`pin-mark pin-draft${shown.width ? ' pin-box' : ''}`} style={shape(shown, revision)} />}
  </div>
}

function ReplyBox({ onSubmit, onCancel }: { onSubmit: (body: string) => Promise<boolean>; onCancel: () => void }) {
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)
  const submit = async () => { if (!body.trim() || busy) return; setBusy(true); if (await onSubmit(body)) setBody(''); setBusy(false) }
  return <div className="reply-box">
    <textarea autoFocus rows={2} value={body} onChange={event => setBody(event.target.value)} placeholder="Reply…" aria-label="Reply"
      onKeyDown={event => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void submit() } if (event.key === 'Escape') onCancel() }} />
    <div className="note-actions"><button className="secondary-button" onClick={() => void submit()} disabled={busy || !body.trim()}>Reply</button><button className="text-button" onClick={onCancel}>Cancel</button></div>
  </div>
}

function Thread({ thread, pinNumber, item, active, onActivate, onReply, onResolve, onDelete }: {
  thread: NoteThread; pinNumber: number | null; item: Item; active: boolean; onActivate: () => void
  onReply: (body: string) => Promise<boolean>; onResolve: (resolved: boolean) => void; onDelete: (id: string) => void
}) {
  const [replying, setReplying] = useState(false)
  const ref = useRef<HTMLElement>(null)
  useEffect(() => { if (active) ref.current?.scrollIntoView({ block: 'nearest' }) }, [active])
  const older = thread.revisionId !== item.selectedRevisionId
  return <article ref={ref} className={`note-thread${active ? ' note-active' : ''}${thread.resolvedAt ? ' note-resolved' : ''}`} onClick={onActivate}>
    <header>
      {pinNumber !== null && <span className="pin-number">{pinNumber}</span>}
      <strong className={`note-author author-${thread.author}`}>{author(thread.author)}</strong>
      <small>{new Date(thread.createdAt).toLocaleString()}</small>
      {older && <span className="badge" title="Written against another version of this item">Other version</span>}
    </header>
    <p>{thread.body}</p>
    {thread.pin && pinNumber === null && <small className="quiet-text">Pinned {pinLabel(thread.pin)}</small>}
    {thread.replies.map(reply => <div key={reply.id} className="note-reply">
      <header><strong className={`note-author author-${reply.author}`}>{author(reply.author)}</strong><small>{new Date(reply.createdAt).toLocaleString()}</small>
        <button className="icon-button" onClick={event => { event.stopPropagation(); onDelete(reply.id) }} aria-label="Delete reply" title="Delete reply"><Icon name="trash" /></button></header>
      <p>{reply.body}</p>
    </div>)}
    {thread.resolvedAt && <small className="quiet-text">Resolved by {thread.resolvedBy === 'agent' ? 'the agent' : 'you'} {new Date(thread.resolvedAt).toLocaleString()}</small>}
    {replying ? <ReplyBox onSubmit={async body => { const ok = await onReply(body); if (ok) setReplying(false); return ok }} onCancel={() => setReplying(false)} />
      : <div className="note-actions" onClick={event => event.stopPropagation()}>
        <button className="text-button" onClick={() => setReplying(true)}>Reply</button>
        <button className="text-button" onClick={() => onResolve(!thread.resolvedAt)}>{thread.resolvedAt ? 'Reopen' : 'Resolve'}</button>
        <button className="text-button danger-text" onClick={() => onDelete(thread.id)}>Delete</button>
      </div>}
  </article>
}

/** Item note threads with a composer. Notes target the viewed revision and, in a review, that review. */
export const NotesPanel = forwardRef<HTMLTextAreaElement, {
  projectId: string; item: Item; revisionId: string; reviewId?: string; locked?: boolean; draft: Pin | null; activeNoteId: string | null
  onClearDraft: () => void; onActivate: (id: string | null) => void; onError: (message: string) => void
}>(function NotesPanel({ projectId, item, revisionId, reviewId, locked, draft, activeNoteId, onClearDraft, onActivate, onError }, composer) {
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  const draftRef = useRef(draft)
  draftRef.current = draft
  const pinned = pinnedThreads(item.notes)
  const open = item.notes.filter(thread => !thread.resolvedAt), resolved = item.notes.filter(thread => thread.resolvedAt)
  const run = async (action: () => Promise<unknown>, fallback: string) => { try { await action(); return true } catch (error) { onError(message(error, fallback)); return false } }
  const add = async () => {
    if (!body.trim() || busy) return
    setBusy(true)
    const submitted = body, pin = draft
    if (await run(() => op('note_add', { projectId, itemId: item.id, body: submitted, revisionId, ...(reviewId && !locked ? { reviewId } : {}), ...(pin ? { pin } : {}) }), 'Could not add the note.')) {
      // Keep anything typed or pinned while the note was saving.
      setBody(current => current === submitted ? '' : current)
      if (draftRef.current === pin) onClearDraft()
    }
    setBusy(false)
  }
  const thread = (entry: NoteThread) => <Thread key={entry.id} thread={entry} item={item} pinNumber={pinned.includes(entry) ? pinned.indexOf(entry) + 1 : null} active={entry.id === activeNoteId}
    onActivate={() => onActivate(entry.id === activeNoteId ? null : entry.id)}
    onReply={body => run(() => op('note_add', { projectId, itemId: item.id, parentId: entry.id, body }), 'Could not add the reply.')}
    onResolve={resolved => void run(() => op('note_update', { projectId, noteId: entry.id, resolved }), 'Could not update the note.')}
    onDelete={setDeleting} />

  return <section className="viewer-section notes-section">
    <h3>Notes <span>{open.length}</span></h3>
    <div className="note-composer">
      <textarea ref={composer} rows={3} value={body} onChange={event => setBody(event.target.value)} aria-label="New note"
        placeholder={draft ? 'Describe what should change in this region…' : 'Leave a note for the agent. Click or drag on the image to pin a region.'}
        onKeyDown={event => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void add() } if (event.key === 'Escape') event.currentTarget.blur() }} />
      <div className="note-actions">
        {draft && <span className="badge badge-accent pin-chip">Pin {pinLabel(draft)}<button onClick={onClearDraft} aria-label="Remove pin" title="Remove pin"><Icon name="x" /></button></span>}
        <button className="primary-button" onClick={() => void add()} disabled={busy || !body.trim()} title="Add note (Ctrl+Enter)">Add note</button>
      </div>
      {locked && <small className="quiet-text">This review is completed, so new notes stay on the item only.</small>}
    </div>
    <div className="note-list">
      {open.length === 0 && <p className="quiet-text">No open notes.</p>}
      {open.map(thread)}
      {resolved.length > 0 && <details className="resolved-notes"><summary>Resolved ({resolved.length})</summary>{resolved.map(thread)}</details>}
    </div>
    {deleting && <ConfirmDialog eyebrow="Delete note" title="Delete this note?" confirmLabel="Delete" body="The note and its replies are removed permanently." onCancel={() => setDeleting(null)}
      onConfirm={() => { const id = deleting; setDeleting(null); void run(() => op('note_delete', { projectId, noteId: id }), 'Could not delete the note.') }} />}
  </section>
})
