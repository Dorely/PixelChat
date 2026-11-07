import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { Decision, Item, ItemSummary, MaskShape, Pin, ReviewEntry, ReviewMode, Revision, Stroke } from '../shared/types'
import { ConfirmDialog } from './Dialogs'
import EditPanel from './EditPanel'
import Icon from './Icon'
import { decisionBadge, decisionLabel, decisionOptions } from './ItemCard'
import { MarkLayer, NotesPanel, type Tool } from './NotesPanel'
import { ChatToggle } from './ChatToggle'
import { OperationError, op } from './ops'

type Backdrop = 'checker' | 'dark' | 'light'
type Zoom = 'fit' | number
/** Zoom steps for the buttons and keys; Ctrl+wheel zooms smoothly between them. */
const ZOOMS = [0.1, 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64]
/** Displayed edges stay under this many CSS pixels so a large image is never enlarged past what the browser can draw. */
const MAX_EDGE = 32768
const STAGE_PADDING = 24
const PEN_COLORS = ['#ff3b5c', '#ffd60a', '#32d74b', '#40c8ff', '#ffffff']
const PREFERENCES = 'pixelchat.viewer'

/** Review context for the viewer: entries by version, locked when the review is completed. */
export interface ViewerReview { id: string; completed: boolean; mode: ReviewMode; entries: Map<string, ReviewEntry> }
/** One stop in the viewer: an item, opened on a given version (a review entry's) or its selected one. */
export interface ViewerTarget { item: ItemSummary; revisionId?: string }

function loadPreferences(): { backdrop: Backdrop; pixelated: boolean; penColor: string } {
  try {
    const value = JSON.parse(localStorage.getItem(PREFERENCES) ?? '{}')
    return { backdrop: ['checker', 'dark', 'light'].includes(value.backdrop) ? value.backdrop : 'checker', pixelated: value.pixelated === true, penColor: PEN_COLORS.includes(value.penColor) ? value.penColor : PEN_COLORS[0]! }
  } catch { return { backdrop: 'checker', pixelated: false, penColor: PEN_COLORS[0]! } }
}
/** Mask brush width in image pixels: a sixteenth of the long edge to start, at most half of it. */
const defaultBrush = (revision: Revision) => Math.max(1, Math.round(Math.max(revision.width, revision.height) / 16))
const maxBrush = (revision: Revision) => Math.max(8, Math.round(Math.max(revision.width, revision.height) / 2))
const formatBytes = (bytes: number) => bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(2)} MB`
const alphaLabel = (revision: Revision) => !revision.hasAlpha ? 'No alpha channel' : revision.opaque ? 'Alpha channel, fully opaque' : 'Transparent pixels'
const ORIGIN_LABELS: Record<Revision['origin'], string> = { imported: 'Imported', generated: 'Generated', edited: 'Edited' }
/** Model and prompt excerpt for versions an image job made. */
const provenanceLine = (revision: Revision) => {
  const { model, prompt } = revision.provenance as { model?: string; prompt?: string }
  return model ? `${model}${prompt ? ` · ${prompt.length > 70 ? `${prompt.slice(0, 70)}…` : prompt}` : ''}` : ''
}
const typing = (target: EventTarget | null) => target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))

/**
 * Full-size inspection of one item at a time: zoom (Ctrl+wheel at the cursor) and panning, backdrops, versions,
 * note threads with region pins and drawings, edit masks, and, inside a review, keyboard decisions.
 */
export default function ItemViewer({ projectId, items, index, review, onIndex, onClose, onError }: { projectId: string; items: ViewerTarget[]; index: number; review?: ViewerReview; onIndex: (index: number) => void; onClose: () => void; onError: (message: string) => void }) {
  const { item: summary, revisionId: targetId } = items[index]!
  const [item, setItem] = useState<Item | null>(null)
  const [viewingId, setViewingId] = useState('')
  const [zoom, setZoom] = useState<Zoom>('fit')
  const [preferences, setPreferences] = useState(loadPreferences)
  const [draft, setDraft] = useState<Pin | null>(null)
  const [activeNoteId, setActiveNoteId] = useState<string | null>(null)
  const [stage, setStage] = useState<HTMLDivElement | null>(null)
  const canvas = useRef<HTMLDivElement>(null)
  const [stageSize, setStageSize] = useState({ width: 0, height: 0 })
  const [tool, setTool] = useState<Tool>('pin')
  // The drawing for the next note and the edit mask belong to the viewed version's pixels.
  const [sketch, setSketch] = useState<Stroke[]>([])
  const [mask, setMask] = useState<MaskShape[]>([])
  const [brush, setBrush] = useState(8)
  const [erasing, setErasing] = useState(false)
  const [spaceHeld, setSpaceHeld] = useState(false)
  const [panning, setPanning] = useState(false)
  const pan = useRef<{ x: number; y: number; left: number; top: number } | null>(null)
  const composer = useRef<HTMLTextAreaElement>(null)
  // Focus requests wait for the notes panel, which mounts once the item loads.
  const [noteFocus, setNoteFocus] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [conflict, setConflict] = useState<'' | 'changed' | 'missing'>('')
  useEffect(() => { if (noteFocus && item) { composer.current?.focus(); setNoteFocus(false) } }, [noteFocus, item])
  const entry = targetId ? review?.entries.get(targetId) : undefined
  // Reference entries are context: notes only, no decision.
  const decidable = entry?.role === 'subject'

  const load = useCallback(async () => {
    try {
      const loaded = await op<Item>('item_get', { projectId, itemId: summary.id })
      // Opens on the target version, else the selected version as loaded; the list summary may predate a selection change.
      setItem(loaded); setViewingId(current => current || targetId || loaded.selectedRevisionId)
    }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not load the item.') }
  }, [projectId, summary.id, targetId, onError])
  useEffect(() => { setItem(null); setViewingId(''); setZoom('fit'); setDraft(null); setActiveNoteId(null); void load() }, [summary.id, targetId])
  useEffect(() => window.pixel.onChanged(() => void load()), [load])
  useEffect(() => { try { localStorage.setItem(PREFERENCES, JSON.stringify(preferences)) } catch { /* storage unavailable */ } }, [preferences])

  // Keys can arrive faster than renders; the ref keeps rapid decisions on the right item.
  const position = useRef(index)
  useEffect(() => { position.current = index }, [index])
  const step = (direction: 1 | -1) => { const next = position.current + direction; if (next >= 0 && next < items.length) { position.current = next; onIndex(next) } }

  const revision = item?.revisions.find(entry => entry.id === viewingId) ?? null
  useEffect(() => { setSketch([]); setMask([]); setErasing(false); if (revision) setBrush(defaultBrush(revision)) }, [revision?.id])
  useLayoutEffect(() => {
    if (!stage) return
    const measure = () => setStageSize({ width: stage.clientWidth, height: stage.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    return () => observer.disconnect()
  }, [stage])
  // Fit scales art up or down to the stage inside its padding.
  const fitScale = revision && stageSize.width ? Math.max(0.001, Math.min((stageSize.width - 2 * STAGE_PADDING - 1) / revision.width, (stageSize.height - 2 * STAGE_PADDING - 1) / revision.height)) : 0
  const scale = zoom === 'fit' ? fitScale : zoom
  const minZoom = Math.min(ZOOMS[0]!, fitScale || ZOOMS[0]!)
  const maxZoom = revision ? Math.max(1, Math.min(ZOOMS.at(-1)!, MAX_EDGE / Math.max(revision.width, revision.height))) : 1
  const scaleRef = useRef(scale)
  scaleRef.current = scale
  // The image point under the cursor (or the stage center) stays put across a zoom change.
  const anchor = useRef<{ x: number; y: number; clientX: number; clientY: number } | null>(null)
  const zoomAt = (next: number, clientX?: number, clientY?: number) => {
    const box = canvas.current?.getBoundingClientRect(), view = stage?.getBoundingClientRect(), current = scaleRef.current
    if (!box || !view || !current) return
    const target = Math.min(maxZoom, Math.max(minZoom, next))
    const x = clientX ?? view.left + view.width / 2, y = clientY ?? view.top + view.height / 2
    anchor.current = target === current ? null : { x: (x - box.left) / current, y: (y - box.top) / current, clientX: x, clientY: y }
    setZoom(target)
  }
  useLayoutEffect(() => {
    const point = anchor.current, box = canvas.current?.getBoundingClientRect()
    anchor.current = null
    if (!point || !box || !stage) return
    stage.scrollLeft += box.left + point.x * scale - point.clientX
    stage.scrollTop += box.top + point.y * scale - point.clientY
  }, [scale, stage])
  const zoomAtRef = useRef(zoomAt)
  zoomAtRef.current = zoomAt
  // React wheel listeners are passive, so Ctrl+wheel needs a native listener to stop the page from zooming.
  useEffect(() => {
    if (!stage) return
    const wheel = (event: WheelEvent) => {
      if (!event.ctrlKey && !event.metaKey) return
      event.preventDefault()
      const delta = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaMode === 2 ? event.deltaY * 400 : event.deltaY
      zoomAtRef.current(scaleRef.current * Math.pow(2, -delta / 300), event.clientX, event.clientY)
    }
    stage.addEventListener('wheel', wheel, { passive: false })
    return () => stage.removeEventListener('wheel', wheel)
  }, [stage])
  const stepZoom = (direction: 1 | -1) => {
    const current = scaleRef.current
    zoomAt(direction > 0 ? ZOOMS.find(entry => entry > current * 1.001) ?? maxZoom : [...ZOOMS].reverse().find(entry => entry < current / 1.001) ?? minZoom)
  }
  const decide = async (decision: Decision) => {
    const revisionId = items[position.current]!.revisionId
    if (!review || review.completed || !revisionId) return
    if (decision === 'needs_changes') setNoteFocus(true)
    // A contest pick stays on the candidate; the others need no walk-through.
    else if (decision !== 'unreviewed' && review.mode !== 'contest') step(1)
    try { await op('review_decide', { projectId, reviewId: review.id, decisions: [{ revisionId, decision }] }) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not record the decision.') }
  }
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if (typing(event.target) || event.altKey || document.querySelector('[aria-modal="true"]')) return
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
        if (tool === 'mask' && mask.length) { event.preventDefault(); setMask(mask.slice(0, -1)) }
        else if (tool === 'draw' && sketch.length) { event.preventDefault(); setSketch(sketch.slice(0, -1)) }
        return
      }
      if (event.ctrlKey || event.metaKey) return
      if (event.key === ' ') { event.preventDefault(); setSpaceHeld(true); return }
      const actions: Record<string, () => void> = {
        Escape: () => draft ? setDraft(null) : tool !== 'pin' ? setTool('pin') : onClose(), ArrowLeft: () => step(-1), ArrowRight: () => step(1),
        '0': () => setZoom('fit'), '1': () => zoomAt(1), '+': () => stepZoom(1), '=': () => stepZoom(1), '-': () => stepZoom(-1),
        n: () => setNoteFocus(true), p: () => setTool('pin'), d: () => setTool('draw'), m: () => setTool('mask'),
        ...(tool === 'mask' && revision ? { e: () => setErasing(value => !value), '[': () => setBrush(value => Math.max(1, Math.round(value / 1.25))), ']': () => setBrush(value => Math.min(maxBrush(revision), Math.max(value + 1, Math.round(value * 1.25)))) } : {}),
        ...(decidable && review && !review.completed ? { a: () => void decide('accepted'), u: () => void decide('unreviewed'), ...(review.mode === 'contest' ? {} : { c: () => void decide('needs_changes'), r: () => void decide('rejected') }) } : {}),
      }
      const action = actions[event.key.length === 1 ? event.key.toLowerCase() : event.key]
      if (action) { event.preventDefault(); action() }
    }
    const release = (event: KeyboardEvent) => { if (event.key === ' ') setSpaceHeld(false) }
    const blur = () => setSpaceHeld(false)
    window.addEventListener('keydown', handle)
    window.addEventListener('keyup', release)
    window.addEventListener('blur', blur)
    return () => { window.removeEventListener('keydown', handle); window.removeEventListener('keyup', release); window.removeEventListener('blur', blur) }
  })
  const select = async (revisionId: string) => {
    try { setItem(await op<Item>('item_update', { projectId, itemId: summary.id, selectedRevisionId: revisionId })) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not select the version.') }
  }
  const exportItem = async (force = false) => {
    setExporting(true)
    try { await op('export_item', { projectId, itemId: summary.id, ...(force ? { force } : {}) }); setConflict('') }
    catch (error) {
      if (!force && error instanceof OperationError && error.code === 'SOURCE_CHANGED') setConflict((error.details as { state?: string } | undefined)?.state === 'missing' ? 'missing' : 'changed')
      else { setConflict(''); onError(error instanceof Error ? error.message : 'Could not export the item.') }
    } finally { setExporting(false) }
  }
  const endPan = () => { pan.current = null; setPanning(false) }
  const hint = tool === 'draw' ? 'Draw for the next note · Ctrl+Z undo · Esc done'
    : tool === 'mask' ? 'Paint the area to edit · Shift+drag box · E erase · [ ] brush · Ctrl+Z undo'
    : entry && viewingId !== entry.revisionId ? 'Not the reviewed version · decisions apply to the reviewed one'
    : decidable && !review?.completed ? (review?.mode === 'contest' ? 'A pick · U clear · N note' : 'A accept · C needs changes · R reject · N note')
    : entry && !decidable ? 'Reference · N note · click or drag to pin' : 'N note · click or drag to pin'

  return <div className="item-viewer" role="dialog" aria-label={`Viewing ${summary.name}`}>
    <header className="viewer-header">
      <button className="icon-button" onClick={onClose} aria-label="Close viewer" title="Close (Esc)"><Icon name="close" /></button>
      <ChatToggle />
      <div className="viewer-title"><strong>{summary.name}</strong><small>{summary.sourcePath ?? summary.collection}</small></div>
      {review && entry && !decidable && <div className="decision-bar"><span className="badge" title="Context for the decision; takes no decision">Reference</span></div>}
      {review && entry && decidable && <div className="decision-bar" role="group" aria-label="Decision">
        {entry.decision !== 'unreviewed' && <span className={`badge ${decisionBadge(entry.decision)}`}>{review.mode === 'contest' && entry.decision === 'accepted' ? 'Picked' : decisionLabel(entry.decision)}</span>}
        {entry.newerVersions > 0 && <span className="badge" title="The item has versions made after the reviewed one">{entry.newerVersions} newer</span>}
        {decisionOptions(review.mode).map(option => <button key={option.id} className={`decision-button decision-${option.id}${entry.decision === option.id ? ' active' : ''}`} disabled={review.completed}
          onClick={() => void decide(entry.decision === option.id ? 'unreviewed' : option.id)} title={`${option.label} the reviewed version (${option.key})`}>{option.label} <kbd>{option.key}</kbd></button>)}
      </div>}
      <div className="viewer-nav">
        <button className="icon-button" onClick={() => step(-1)} disabled={index === 0} aria-label="Previous item" title="Previous (←)"><Icon name="back" /></button>
        <span>{index + 1} / {items.length}</span>
        <button className="icon-button" onClick={() => step(1)} disabled={index === items.length - 1} aria-label="Next item" title="Next (→)"><Icon name="next" /></button>
      </div>
    </header>
    <div className="viewer-body">
      <div className="viewer-main">
        <div className="viewer-toolbar">
          <span className="segmented" role="group" aria-label="Zoom">
            <button className={zoom === 'fit' ? 'active' : ''} onClick={() => setZoom('fit')} title="Fit (0)"><Icon name="fit" /> Fit</button>
            <button onClick={() => stepZoom(-1)} aria-label="Zoom out" title="Zoom out (-)"><Icon name="zoom-out" /></button>
            <span className="zoom-value" title="Ctrl+wheel zooms at the cursor · Space+drag or middle-drag pans">{scale ? `${+(scale * 100).toFixed(scale < 0.1 ? 1 : 0)}%` : '–'}</span>
            <button onClick={() => stepZoom(1)} aria-label="Zoom in" title="Zoom in (+)"><Icon name="zoom-in" /></button>
            <button className={zoom === 1 ? 'active' : ''} onClick={() => zoomAt(1)} title="Actual size (1)">1:1</button>
          </span>
          <span className="segmented" role="group" aria-label="Tool">
            <button className={tool === 'pin' ? 'active' : ''} onClick={() => setTool('pin')} title="Pin a point or region for a note (P)"><Icon name="dot" /> Pin</button>
            <button className={tool === 'draw' ? 'active' : ''} onClick={() => setTool('draw')} title="Draw on the image for a note (D)"><Icon name="edit" /> Draw</button>
            <button className={tool === 'mask' ? 'active' : ''} onClick={() => setTool('mask')} title="Paint the area an edit should change (M)"><Icon name="spark" /> Mask</button>
          </span>
          {tool === 'draw' && <span className="tool-options" role="group" aria-label="Drawing">
            {PEN_COLORS.map(color => <button key={color} className={`swatch${preferences.penColor === color ? ' active' : ''}`} style={{ background: color }} onClick={() => setPreferences(value => ({ ...value, penColor: color }))} aria-label={`Pen color ${color}`} title={color} />)}
            <button className="icon-button" disabled={!sketch.length} onClick={() => setSketch(sketch.slice(0, -1))} aria-label="Undo line" title="Undo line (Ctrl+Z)"><Icon name="undo" /></button>
            <button className="text-button" disabled={!sketch.length} onClick={() => setSketch([])}>Clear</button>
          </span>}
          {tool === 'mask' && revision && <span className="tool-options" role="group" aria-label="Mask">
            <span className="segmented">
              <button className={!erasing ? 'active' : ''} onClick={() => setErasing(false)} title="Paint the area to edit">Paint</button>
              <button className={erasing ? 'active' : ''} onClick={() => setErasing(true)} title="Erase painted area (E)">Erase</button>
            </span>
            <label className="brush-size" title="Brush width in image pixels ([ and ])">Brush<input type="range" min={1} max={maxBrush(revision)} value={brush} onChange={event => setBrush(Number(event.target.value))} /><span>{brush}px</span></label>
            <button className="icon-button" disabled={!mask.length} onClick={() => setMask(mask.slice(0, -1))} aria-label="Undo mask stroke" title="Undo (Ctrl+Z)"><Icon name="undo" /></button>
            <button className="text-button" disabled={!mask.length} onClick={() => setMask([])}>Clear</button>
          </span>}
          <span className="segmented" role="group" aria-label="Backdrop">
            {(['checker', 'dark', 'light'] as const).map(entry => <button key={entry} className={preferences.backdrop === entry ? 'active' : ''} onClick={() => setPreferences(value => ({ ...value, backdrop: entry }))}>{entry === 'checker' ? 'Checker' : entry === 'dark' ? 'Dark' : 'Light'}</button>)}
          </span>
          <label className="toggle"><input type="checkbox" checked={preferences.pixelated} onChange={event => setPreferences(value => ({ ...value, pixelated: event.target.checked }))} /> Pixelated</label>
          <span className="toolbar-hint">{hint}</span>
        </div>
        <div ref={setStage} className={`viewer-stage backdrop-${preferences.backdrop}${spaceHeld || panning ? ' stage-pan' : ''}${panning ? ' stage-panning' : ''}`}
          onPointerDownCapture={event => {
            // Middle-drag, or Space+drag, pans instead of reaching the active tool.
            if (!stage || !(event.button === 1 || (event.button === 0 && spaceHeld))) return
            event.preventDefault(); event.stopPropagation()
            stage.setPointerCapture(event.pointerId)
            pan.current = { x: event.clientX, y: event.clientY, left: stage.scrollLeft, top: stage.scrollTop }; setPanning(true)
          }}
          onPointerMove={event => { if (pan.current && stage) { stage.scrollLeft = pan.current.left - (event.clientX - pan.current.x); stage.scrollTop = pan.current.top - (event.clientY - pan.current.y) } }}
          onPointerUp={endPan} onPointerCancel={endPan} onMouseDown={event => { if (event.button === 1) event.preventDefault() }}>
          {revision && scale > 0 && <div ref={canvas} className="viewer-canvas" style={{ width: revision.width * scale, height: revision.height * scale }}>
            <img key={revision.id} src={window.pixel.mediaUrl(`original/${revision.id}`)} alt={summary.name} draggable={false} className={preferences.pixelated ? 'pixelated' : ''} />
            {item && <MarkLayer revision={revision} revisions={item.revisions} notes={item.notes} tool={tool} scale={scale} draft={draft} sketch={sketch} mask={mask}
              penColor={preferences.penColor} brush={brush} erasing={erasing} activeNoteId={activeNoteId}
              onDraft={pin => { setDraft(pin); setNoteFocus(true) }} onStroke={stroke => { setSketch(value => [...value, stroke]); setNoteFocus(true) }}
              onMask={shape => setMask(value => [...value, shape])} onActivate={setActiveNoteId} />}
          </div>}
        </div>
      </div>
      <aside className="viewer-side">
        {item && revision ? <>
          <NotesPanel ref={composer} projectId={projectId} item={item} revisionId={revision.id} reviewId={entry && revision.id === entry.revisionId ? review?.id : undefined} locked={review?.completed} draft={draft} sketch={sketch} activeNoteId={activeNoteId}
            onClearDraft={() => setDraft(null)} onClearSketch={() => setSketch([])} onActivate={setActiveNoteId} onError={onError} />
          <EditPanel projectId={projectId} item={item} revision={revision} mask={mask} onPaintMask={() => setTool('mask')} onClearMask={() => setMask([])} onView={setViewingId} onError={onError} />
          <section className="viewer-section">
            <h3>Versions <span>{item.revisions.length}</span></h3>
            <div className="revision-strip">
              {[...item.revisions].reverse().map(entry => <button key={entry.id} className={`revision-card${entry.id === viewingId ? ' revision-viewing' : ''}`} onClick={() => setViewingId(entry.id)}>
                <span className="revision-thumb checker"><img src={window.pixel.mediaUrl(`thumb/${entry.id}`)} alt="" loading="lazy" /></span>
                <span className="revision-copy">
                  <strong>{ORIGIN_LABELS[entry.origin]}</strong>
                  <small>{new Date(entry.createdAt).toLocaleString()}</small>
                  {provenanceLine(entry) && <small className="revision-prompt" title={String(entry.provenance.prompt ?? '')}>{provenanceLine(entry)}</small>}
                  <span className="revision-tags">
                    {entry.id === targetId && review && <span className="badge">Reviewed</span>}
                    {entry.id === item.selectedRevisionId && <span className="badge badge-accent">Selected</span>}
                    {entry.id === item.syncedRevisionId && <span className="badge">On disk</span>}
                  </span>
                </span>
              </button>)}
            </div>
            {revision.id !== item.selectedRevisionId && <button className="primary-button wide" onClick={() => void select(revision.id)}><Icon name="check" /> Use this version</button>}
          </section>
          <section className="viewer-section">
            <div className="status-row">
              {item.pendingExport && <span className="badge badge-accent">Pending export</span>}
              {item.missing && <span className="badge badge-warn">Missing from source</span>}
              {!item.sourceId && <span className="badge">Unlinked</span>}
            </div>
            {item.pendingExport && item.sourceId && <button className="secondary-button wide export-button" disabled={exporting} onClick={() => void exportItem()} title="Write the selected version to the source file">
              <Icon name="upload" /> {exporting ? 'Exporting…' : 'Export to source'}
            </button>}
            <dl className="facts">
              <dt>Size</dt><dd>{revision.width} × {revision.height}</dd>
              <dt>Format</dt><dd>{revision.format.toUpperCase()}{revision.frames > 1 ? ` · ${revision.frames} frames` : ''} · {formatBytes(revision.bytes)}</dd>
              <dt>Alpha</dt><dd>{alphaLabel(revision)}</dd>
              <dt>SHA-256</dt><dd><code title={revision.sha256}>{revision.sha256.slice(0, 16)}…</code></dd>
              {item.sourceFile && <><dt>Source</dt><dd className="break"><code>{item.sourceFile}</code></dd></>}
            </dl>
          </section>
        </> : <p className="quiet-text">Loading…</p>}
      </aside>
    </div>
    {conflict && <ConfirmDialog eyebrow="Source changed" title={summary.name} confirmLabel={conflict === 'missing' ? 'Write file' : 'Overwrite file'} busy={exporting} onCancel={() => setConflict('')} onConfirm={() => void exportItem(true)}
      body={conflict === 'missing'
        ? 'The source file is missing. Rescan the source to record that, or write the selected version to the file again.'
        : 'The file on disk changed since PixelChat last read it. Rescan the source to bring the change in as a version, or overwrite the file with the selected version and lose the change.'} />}
  </div>
}
