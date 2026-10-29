import { useCallback, useEffect, useRef, useState } from 'react'
import type { Decision, Item, ItemSummary, Pin, ReviewEntry, Revision } from '../shared/types'
import { ConfirmDialog } from './Dialogs'
import EditPanel from './EditPanel'
import Icon from './Icon'
import { DECISIONS, decisionBadge, decisionLabel } from './ItemCard'
import { NotesPanel, PinLayer } from './NotesPanel'
import { OperationError, op } from './ops'

type Backdrop = 'checker' | 'dark' | 'light'
type Zoom = 'fit' | number
const ZOOMS = [0.25, 0.5, 1, 2, 4, 8]
const PREFERENCES = 'pixelchat.viewer'

/** Review context for the viewer: decisions per item, locked when the review is completed. */
export interface ViewerReview { id: string; completed: boolean; entries: Map<string, ReviewEntry> }

function loadPreferences(): { backdrop: Backdrop; pixelated: boolean } {
  try { const value = JSON.parse(localStorage.getItem(PREFERENCES) ?? '{}'); return { backdrop: ['checker', 'dark', 'light'].includes(value.backdrop) ? value.backdrop : 'checker', pixelated: value.pixelated === true } }
  catch { return { backdrop: 'checker', pixelated: false } }
}
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
 * Full-size inspection of one item at a time: zoom, backdrops, versions, note threads with region pins,
 * and, inside a review, keyboard decisions.
 */
export default function ItemViewer({ projectId, items, index, review, onIndex, onClose, onError }: { projectId: string; items: ItemSummary[]; index: number; review?: ViewerReview; onIndex: (index: number) => void; onClose: () => void; onError: (message: string) => void }) {
  const summary = items[index]!
  const [item, setItem] = useState<Item | null>(null)
  const [viewingId, setViewingId] = useState('')
  const [zoom, setZoom] = useState<Zoom>('fit')
  const [preferences, setPreferences] = useState(loadPreferences)
  const [draft, setDraft] = useState<Pin | null>(null)
  const [activeNoteId, setActiveNoteId] = useState<string | null>(null)
  const [image, setImage] = useState<HTMLImageElement | null>(null)
  const [stage, setStage] = useState<HTMLDivElement | null>(null)
  const composer = useRef<HTMLTextAreaElement>(null)
  // Focus requests wait for the notes panel, which mounts once the item loads.
  const [noteFocus, setNoteFocus] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [conflict, setConflict] = useState<'' | 'changed' | 'missing'>('')
  useEffect(() => { if (noteFocus && item) { composer.current?.focus(); setNoteFocus(false) } }, [noteFocus, item])
  const entry = review?.entries.get(summary.id)

  const load = useCallback(async () => {
    try {
      const loaded = await op<Item>('item_get', { projectId, itemId: summary.id })
      // Opens on the selected version as loaded; the list summary may predate a selection change.
      setItem(loaded); setViewingId(current => current || loaded.selectedRevisionId)
    }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not load the item.') }
  }, [projectId, summary.id, onError])
  useEffect(() => { setItem(null); setViewingId(''); setZoom('fit'); setDraft(null); setActiveNoteId(null); void load() }, [summary.id])
  useEffect(() => window.pixel.onChanged(() => void load()), [load])
  useEffect(() => { try { localStorage.setItem(PREFERENCES, JSON.stringify(preferences)) } catch { /* storage unavailable */ } }, [preferences])

  // Keys can arrive faster than renders; the ref keeps rapid decisions on the right item.
  const position = useRef(index)
  useEffect(() => { position.current = index }, [index])
  const step = (direction: 1 | -1) => { const next = position.current + direction; if (next >= 0 && next < items.length) { position.current = next; onIndex(next) } }
  const stepZoom = (direction: 1 | -1) => setZoom(current => {
    const value = current === 'fit' ? 1 : current
    return ZOOMS[Math.min(ZOOMS.length - 1, Math.max(0, ZOOMS.findIndex(entry => entry >= value) + direction))]!
  })
  const decide = async (decision: Decision) => {
    if (!review || review.completed) return
    const itemId = items[position.current]!.id
    if (decision === 'needs_changes') setNoteFocus(true)
    else if (decision !== 'unreviewed') step(1)
    try { await op('review_decide', { projectId, reviewId: review.id, decisions: [{ itemId, decision }] }) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not record the decision.') }
  }
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if (typing(event.target) || event.ctrlKey || event.metaKey || event.altKey || document.querySelector('[aria-modal="true"]')) return
      const actions: Record<string, () => void> = {
        Escape: () => draft ? setDraft(null) : onClose(), ArrowLeft: () => step(-1), ArrowRight: () => step(1),
        '0': () => setZoom('fit'), '1': () => setZoom(1), '+': () => stepZoom(1), '=': () => stepZoom(1), '-': () => stepZoom(-1),
        n: () => setNoteFocus(true),
        ...(review && !review.completed ? { a: () => void decide('accepted'), c: () => void decide('needs_changes'), r: () => void decide('rejected'), u: () => void decide('unreviewed') } : {}),
      }
      const action = actions[event.key.length === 1 ? event.key.toLowerCase() : event.key]
      if (action) { event.preventDefault(); action() }
    }
    window.addEventListener('keydown', handle)
    return () => window.removeEventListener('keydown', handle)
  })

  const revision = item?.revisions.find(entry => entry.id === viewingId) ?? null
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
  const width = revision && zoom !== 'fit' ? revision.width * zoom : undefined

  return <div className="item-viewer" role="dialog" aria-label={`Viewing ${summary.name}`}>
    <header className="viewer-header">
      <button className="icon-button" onClick={onClose} aria-label="Close viewer" title="Close (Esc)"><Icon name="close" /></button>
      <div className="viewer-title"><strong>{summary.name}</strong><small>{summary.sourcePath ?? summary.collection}</small></div>
      {review && <div className="decision-bar" role="group" aria-label="Decision">
        {entry && entry.decision !== 'unreviewed' && <span className={`badge ${decisionBadge(entry.decision)}`}>{decisionLabel(entry.decision)}{entry.stale ? ' · changed since' : ''}</span>}
        {DECISIONS.map(option => <button key={option.id} className={`decision-button decision-${option.id}${entry?.decision === option.id ? ' active' : ''}`} disabled={review.completed}
          onClick={() => void decide(entry?.decision === option.id ? 'unreviewed' : option.id)} title={`${option.label} (${option.key})`}>{option.label} <kbd>{option.key}</kbd></button>)}
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
            <span className="zoom-value">{zoom === 'fit' ? 'Fit' : `${zoom * 100}%`}</span>
            <button onClick={() => stepZoom(1)} aria-label="Zoom in" title="Zoom in (+)"><Icon name="zoom-in" /></button>
            <button className={zoom === 1 ? 'active' : ''} onClick={() => setZoom(1)} title="Actual size (1)">1:1</button>
          </span>
          <span className="segmented" role="group" aria-label="Backdrop">
            {(['checker', 'dark', 'light'] as const).map(entry => <button key={entry} className={preferences.backdrop === entry ? 'active' : ''} onClick={() => setPreferences(value => ({ ...value, backdrop: entry }))}>{entry === 'checker' ? 'Checker' : entry === 'dark' ? 'Dark' : 'Light'}</button>)}
          </span>
          <label className="toggle"><input type="checkbox" checked={preferences.pixelated} onChange={event => setPreferences(value => ({ ...value, pixelated: event.target.checked }))} /> Pixelated</label>
          <span className="toolbar-hint">{review && !review.completed ? 'A accept · C needs changes · R reject · N note' : 'N note · click or drag to pin'}</span>
        </div>
        <div ref={setStage} className={`viewer-stage backdrop-${preferences.backdrop}${zoom === 'fit' ? ' stage-fit' : ''}`} onWheel={event => { if (event.ctrlKey) { event.preventDefault(); stepZoom(event.deltaY < 0 ? 1 : -1) } }}>
          {revision && <img ref={setImage} key={revision.id} src={window.pixel.mediaUrl(`original/${revision.id}`)} alt={summary.name} draggable={false}
            className={preferences.pixelated ? 'pixelated' : ''} style={width ? { width, maxWidth: 'none', maxHeight: 'none' } : undefined} />}
          {item && revision && <PinLayer image={image} stage={stage} revision={revision} revisions={item.revisions} notes={item.notes} draft={draft} activeNoteId={activeNoteId}
            onDraft={pin => { setDraft(pin); setNoteFocus(true) }} onActivate={setActiveNoteId} />}
        </div>
      </div>
      <aside className="viewer-side">
        {item && revision ? <>
          <NotesPanel ref={composer} projectId={projectId} item={item} revisionId={revision.id} reviewId={review?.id} locked={review?.completed} draft={draft} activeNoteId={activeNoteId}
            onClearDraft={() => setDraft(null)} onActivate={setActiveNoteId} onError={onError} />
          <EditPanel projectId={projectId} item={item} revision={revision} onView={setViewingId} onError={onError} />
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
              <dt>Format</dt><dd>{revision.format.toUpperCase()} · {formatBytes(revision.bytes)}</dd>
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
