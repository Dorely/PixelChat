import { forwardRef, useEffect, useId, useRef, useState } from 'react'
import type { Item, MaskShape, NoteThread, Pin, Revision, Stroke } from '../shared/types'
import { strokeBounds, strokeStart } from '../shared/marks'
import Icon from './Icon'
import { ConfirmDialog } from './Dialogs'
import { op } from './ops'

const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback
const author = (origin: string) => origin === 'agent' ? 'Agent' : 'You'
export const pinLabel = (pin: Pin) => pin.width ? `${pin.x}, ${pin.y} · ${pin.width}×${pin.height}` : `${pin.x}, ${pin.y}`
/** Open threads with a pin or a drawing, in display order; their position is the number shown on the image. */
export const markedThreads = (notes: NoteThread[]) => notes.filter(thread => (thread.pin || thread.sketch) && !thread.resolvedAt)
/** Where a marked thread points: its pin, else the box around its drawing. */
export const markRegion = (thread: NoteThread) => thread.pin ?? strokeBounds(thread.sketch ?? [])

/** pin: click or drag a note pin. draw: freehand drawing for the next note. mask: paint the area an edit should change. */
export type Tool = 'pin' | 'draw' | 'mask'
/** Drawn note lines are this many screen pixels wide at the zoom they were drawn at. */
const PEN_WIDTH = 3
const round = (value: number) => Math.round(value * 10) / 10

function StrokeShape({ points, size, color, dashed }: { points: number[]; size: number; color: string; dashed?: boolean }) {
  if (points.length === 2) return <circle cx={points[0]} cy={points[1]} r={size / 2} fill={color} />
  return <polyline points={points.join(' ')} fill="none" stroke={color} strokeWidth={size} strokeLinecap="round" strokeLinejoin="round" strokeDasharray={dashed ? `${size * 2.5} ${size * 2}` : undefined} />
}
function Drawing({ strokes, dashed }: { strokes: Stroke[]; dashed?: boolean }) {
  // A dark outline under each line keeps it readable on any art.
  return <>
    {strokes.map((stroke, index) => <StrokeShape key={`o${index}`} points={stroke.points} size={stroke.size * 1.8} color="rgba(0, 0, 0, .6)" dashed={dashed} />)}
    {strokes.map((stroke, index) => <StrokeShape key={index} points={stroke.points} size={stroke.size} color={stroke.color} dashed={dashed} />)}
  </>
}
const MaskShapeView = ({ shape }: { shape: MaskShape }) => shape.kind === 'box'
  ? <rect x={shape.x} y={shape.y} width={shape.width} height={shape.height} fill={shape.erase ? '#000' : '#fff'} />
  : <StrokeShape points={shape.points} size={shape.size} color={shape.erase ? '#000' : '#fff'} />

type Point = { x: number; y: number }
type Gesture = { screen: Point; last: Point; points: number[]; box: boolean }

/**
 * Overlay that fills the drawn image. Shows open pins and drawings, the draft pin, drawing, and edit mask,
 * and turns pointer gestures into the active tool's marks. Marks are stored in image pixels of their revision;
 * ones from another version are drawn dashed and mapped proportionally.
 */
export function MarkLayer({ revision, revisions, notes, tool, scale, draft, sketch, mask, penColor, brush, erasing, activeNoteId, onDraft, onStroke, onMask, onActivate }: {
  revision: Revision; revisions: Revision[]; notes: NoteThread[]; tool: Tool; scale: number; draft: Pin | null; sketch: Stroke[]; mask: MaskShape[]
  penColor: string; brush: number; erasing: boolean; activeNoteId: string | null
  onDraft: (pin: Pin) => void; onStroke: (stroke: Stroke) => void; onMask: (shape: MaskShape) => void; onActivate: (id: string | null) => void
}) {
  const [gesture, setGesture] = useState<Gesture | null>(null)
  const [hover, setHover] = useState<Point | null>(null)
  const maskId = useId()
  const { width, height } = revision

  const point = (event: React.PointerEvent): Point => {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: (event.clientX - rect.left) / rect.width * width, y: (event.clientY - rect.top) / rect.height * height }
  }
  const pixel = (p: Point): Point => ({ x: Math.min(width - 1, Math.max(0, Math.floor(p.x))), y: Math.min(height - 1, Math.max(0, Math.floor(p.y))) })
  const box = (a: Point, b: Point): Required<Pin> => ({ x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(a.x - b.x) + 1, height: Math.abs(a.y - b.y) + 1 })
  const first = (points: number[]): Point => ({ x: points[0]!, y: points[1]! })
  const last = (points: number[]): Point => ({ x: points.at(-2)!, y: points.at(-1)! })
  const shape = (pin: Pin, source: { width: number; height: number }) => pin.width
    ? { left: `${pin.x / source.width * 100}%`, top: `${pin.y / source.height * 100}%`, width: `${pin.width / source.width * 100}%`, height: `${(pin.height ?? 1) / source.height * 100}%` }
    : { left: `${(pin.x + 0.5) / source.width * 100}%`, top: `${(pin.y + 0.5) / source.height * 100}%` }
  const sizes = new Map(revisions.map(entry => [entry.id, entry]))
  const penSize = round(Math.max(0.1, PEN_WIDTH / scale))

  const finish = (event: React.PointerEvent) => {
    if (!gesture) return
    const points = gesture.points.map(round)
    if (tool === 'pin') {
      const moved = Math.hypot(event.clientX - gesture.screen.x, event.clientY - gesture.screen.y) > 4
      onDraft(moved ? box(pixel(first(points)), pixel(last(points))) : pixel(first(points)))
    } else if (tool === 'draw') onStroke({ points, size: penSize, color: penColor })
    else if (gesture.box) onMask({ kind: 'box', ...box(pixel(first(points)), pixel(last(points))), ...(erasing ? { erase: true } : {}) })
    else onMask({ kind: 'stroke', points, size: brush, ...(erasing ? { erase: true } : {}) })
    setGesture(null)
  }
  // The live gesture is previewed as the mark it will become.
  const liveBox = gesture && (tool === 'pin' || gesture.box) && gesture.points.length === 4 ? box(pixel(first(gesture.points)), pixel(last(gesture.points))) : null
  const liveMask: MaskShape | null = gesture && tool === 'mask'
    ? liveBox ? { kind: 'box', ...liveBox, erase: erasing } : { kind: 'stroke', points: gesture.points, size: brush, erase: erasing }
    : null
  const shownPin: Pin | null = tool === 'pin' && gesture ? (liveBox ?? pixel(first(gesture.points))) : draft
  const maskShapes = liveMask ? [...mask, liveMask] : mask
  const marked = markedThreads(notes)

  return <div className={`mark-layer tool-${tool}`} title={tool === 'pin' ? 'Click to pin a point, drag to pin a region' : tool === 'draw' ? 'Draw on the image for the next note' : 'Paint the area to edit · Shift+drag for a box'}
    onPointerDown={event => {
      if (event.button !== 0) return
      event.currentTarget.setPointerCapture(event.pointerId)
      const p = point(event), screen = { x: event.clientX, y: event.clientY }
      setGesture({ screen, last: screen, points: [p.x, p.y], box: tool === 'mask' && event.shiftKey })
    }}
    onPointerMove={event => {
      const p = point(event)
      if (tool === 'mask') setHover(p)
      if (!gesture) return
      if (tool === 'pin' || gesture.box) setGesture({ ...gesture, points: [gesture.points[0]!, gesture.points[1]!, p.x, p.y] })
      // Freehand lines keep a point every couple of screen pixels.
      else if (Math.hypot(event.clientX - gesture.last.x, event.clientY - gesture.last.y) >= 2)
        setGesture({ ...gesture, last: { x: event.clientX, y: event.clientY }, points: [...gesture.points, p.x, p.y] })
    }}
    onPointerUp={finish} onPointerCancel={() => setGesture(null)} onPointerLeave={() => setHover(null)}>
    <svg className="mark-svg" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" aria-hidden="true">
      {maskShapes.length > 0 && <>
        <defs><mask id={maskId} maskUnits="userSpaceOnUse" x="0" y="0" width={width} height={height}>
          <rect width={width} height={height} fill="#000" />
          {maskShapes.map((entry, index) => <MaskShapeView key={index} shape={entry} />)}
        </mask></defs>
        <rect className="mask-fill" width={width} height={height} mask={`url(#${maskId})`} />
      </>}
      {marked.filter(thread => thread.sketch).map(thread => {
        const source = sizes.get(thread.revisionId) ?? revision
        return <svg key={thread.id} viewBox={`0 0 ${source.width} ${source.height}`} width={width} height={height} preserveAspectRatio="none"
          className={`note-drawing${thread.revisionId !== revision.id ? ' drawing-stale' : ''}`}>
          <Drawing strokes={thread.sketch!} dashed={thread.revisionId !== revision.id} />
        </svg>
      })}
      <Drawing strokes={tool === 'draw' && gesture ? [...sketch, { points: gesture.points, size: penSize, color: penColor }] : sketch} />
      {tool === 'mask' && hover && <>
        <circle cx={hover.x} cy={hover.y} r={brush / 2} fill="none" stroke="#000" strokeWidth={3 / scale} />
        <circle cx={hover.x} cy={hover.y} r={brush / 2} fill="none" stroke={erasing ? '#7dd3fc' : '#fff'} strokeWidth={1.5 / scale} />
      </>}
    </svg>
    {marked.map((thread, index) => {
      const source = sizes.get(thread.revisionId) ?? revision
      // A drawing without a pin is numbered where it starts.
      const start = thread.sketch && strokeStart(thread.sketch)
      const pin = thread.pin ?? { x: Math.floor(start!.x), y: Math.floor(start!.y) }
      return <button key={thread.id} className={`pin-mark${pin.width ? ' pin-box' : ''}${thread.revisionId !== revision.id ? ' pin-stale' : ''}${thread.id === activeNoteId ? ' pin-active' : ''}`}
        style={shape(pin, source)} onPointerDown={event => event.stopPropagation()} onClick={() => onActivate(thread.id === activeNoteId ? null : thread.id)}
        aria-label={`Note ${index + 1}`} title={thread.body}><span>{index + 1}</span></button>
    })}
    {shownPin && <span className={`pin-mark pin-draft${shownPin.width ? ' pin-box' : ''}`} style={shape(shownPin, revision)} />}
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

function Thread({ thread, pinNumber, revisionId, active, onActivate, onReply, onResolve, onDelete }: {
  thread: NoteThread; pinNumber: number | null; revisionId: string; active: boolean; onActivate: () => void
  onReply: (body: string) => Promise<boolean>; onResolve: (resolved: boolean) => void; onDelete: (id: string) => void
}) {
  const [replying, setReplying] = useState(false)
  const ref = useRef<HTMLElement>(null)
  useEffect(() => { if (active) ref.current?.scrollIntoView({ block: 'nearest' }) }, [active])
  const older = thread.revisionId !== revisionId
  return <article ref={ref} className={`note-thread${active ? ' note-active' : ''}${thread.resolvedAt ? ' note-resolved' : ''}`} onClick={onActivate}>
    <header>
      {pinNumber !== null && <span className="pin-number">{pinNumber}</span>}
      <strong className={`note-author author-${thread.author}`}>{author(thread.author)}</strong>
      <small>{new Date(thread.createdAt).toLocaleString()}</small>
      {older && <span className="badge" title="Written on another version of this item than the one shown">Other version</span>}
    </header>
    <p>{thread.body}</p>
    {thread.pin && pinNumber === null && <small className="quiet-text">Pinned {pinLabel(thread.pin)}</small>}
    {thread.sketch && <small className="quiet-text">Drawing {pinLabel(strokeBounds(thread.sketch))}</small>}
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
  projectId: string; item: Item; revisionId: string; reviewId?: string; locked?: boolean; draft: Pin | null; sketch: Stroke[]; activeNoteId: string | null
  onClearDraft: () => void; onClearSketch: () => void; onActivate: (id: string | null) => void; onError: (message: string) => void
}>(function NotesPanel({ projectId, item, revisionId, reviewId, locked, draft, sketch, activeNoteId, onClearDraft, onClearSketch, onActivate, onError }, composer) {
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  const draftRef = useRef(draft)
  const sketchRef = useRef(sketch)
  draftRef.current = draft
  sketchRef.current = sketch
  const pinned = markedThreads(item.notes)
  const open = item.notes.filter(thread => !thread.resolvedAt), resolved = item.notes.filter(thread => thread.resolvedAt)
  const run = async (action: () => Promise<unknown>, fallback: string) => { try { await action(); return true } catch (error) { onError(message(error, fallback)); return false } }
  const add = async () => {
    if (!body.trim() || busy) return
    setBusy(true)
    const submitted = body, pin = draft, drawing = sketch
    if (await run(() => op('note_add', { projectId, itemId: item.id, body: submitted, revisionId, ...(reviewId && !locked ? { reviewId } : {}), ...(pin ? { pin } : {}), ...(drawing.length ? { sketch: drawing } : {}) }), 'Could not add the note.')) {
      // Keep anything typed, pinned, or drawn while the note was saving.
      setBody(current => current === submitted ? '' : current)
      if (draftRef.current === pin) onClearDraft()
      if (sketchRef.current === drawing) onClearSketch()
    }
    setBusy(false)
  }
  const thread = (entry: NoteThread) => <Thread key={entry.id} thread={entry} revisionId={revisionId} pinNumber={pinned.includes(entry) ? pinned.indexOf(entry) + 1 : null} active={entry.id === activeNoteId}
    onActivate={() => onActivate(entry.id === activeNoteId ? null : entry.id)}
    onReply={body => run(() => op('note_add', { projectId, itemId: item.id, parentId: entry.id, body }), 'Could not add the reply.')}
    onResolve={resolved => void run(() => op('note_update', { projectId, noteId: entry.id, resolved }), 'Could not update the note.')}
    onDelete={setDeleting} />

  return <section className="viewer-section notes-section">
    <h3>Notes <span>{open.length}</span></h3>
    <div className="note-composer">
      <textarea ref={composer} rows={3} value={body} onChange={event => setBody(event.target.value)} aria-label="New note"
        placeholder={draft || sketch.length ? 'Describe what should change where you marked…' : 'Leave a note for the agent. Click or drag on the image to pin a region, or use Draw to mark it up.'}
        onKeyDown={event => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void add() } if (event.key === 'Escape') event.currentTarget.blur() }} />
      <div className="note-actions">
        {draft && <span className="badge badge-accent pin-chip">Pin {pinLabel(draft)}<button onClick={onClearDraft} aria-label="Remove pin" title="Remove pin"><Icon name="x" /></button></span>}
        {sketch.length > 0 && <span className="badge badge-accent pin-chip">Drawing · {sketch.length} {sketch.length === 1 ? 'line' : 'lines'}<button onClick={onClearSketch} aria-label="Remove drawing" title="Remove drawing"><Icon name="x" /></button></span>}
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
