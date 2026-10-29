import { useCallback, useEffect, useState } from 'react'
import type { Item, ItemSummary, Revision } from '../shared/types'
import Icon from './Icon'
import { op } from './ops'

type Backdrop = 'checker' | 'dark' | 'light'
type Zoom = 'fit' | number
const ZOOMS = [0.25, 0.5, 1, 2, 4, 8]
const PREFERENCES = 'pixelchat.viewer'

function loadPreferences(): { backdrop: Backdrop; pixelated: boolean } {
  try { const value = JSON.parse(localStorage.getItem(PREFERENCES) ?? '{}'); return { backdrop: ['checker', 'dark', 'light'].includes(value.backdrop) ? value.backdrop : 'checker', pixelated: value.pixelated === true } }
  catch { return { backdrop: 'checker', pixelated: false } }
}
const formatBytes = (bytes: number) => bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(2)} MB`
const alphaLabel = (revision: Revision) => !revision.hasAlpha ? 'No alpha channel' : revision.opaque ? 'Alpha channel, fully opaque' : 'Transparent pixels'
const typing = (target: EventTarget | null) => target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))

/** Full-size inspection of one item at a time with zoom, backdrops, and its revision history. */
export default function ItemViewer({ projectId, items, index, onIndex, onClose, onError }: { projectId: string; items: ItemSummary[]; index: number; onIndex: (index: number) => void; onClose: () => void; onError: (message: string) => void }) {
  const summary = items[index]!
  const [item, setItem] = useState<Item | null>(null)
  const [viewingId, setViewingId] = useState(summary.selectedRevisionId)
  const [zoom, setZoom] = useState<Zoom>('fit')
  const [preferences, setPreferences] = useState(loadPreferences)

  const load = useCallback(async () => {
    try { setItem(await op<Item>('item_get', { projectId, itemId: summary.id })) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not load the item.') }
  }, [projectId, summary.id, onError])
  useEffect(() => { setItem(null); setViewingId(summary.selectedRevisionId); setZoom('fit'); void load() }, [summary.id])
  useEffect(() => window.pixel.onChanged(() => void load()), [load])
  useEffect(() => { try { localStorage.setItem(PREFERENCES, JSON.stringify(preferences)) } catch { /* storage unavailable */ } }, [preferences])

  const step = (direction: 1 | -1) => { const next = index + direction; if (next >= 0 && next < items.length) onIndex(next) }
  const stepZoom = (direction: 1 | -1) => setZoom(current => {
    const value = current === 'fit' ? 1 : current
    return ZOOMS[Math.min(ZOOMS.length - 1, Math.max(0, ZOOMS.findIndex(entry => entry >= value) + direction))]!
  })
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if (typing(event.target) || event.ctrlKey || event.metaKey || event.altKey) return
      const actions: Record<string, () => void> = { Escape: onClose, ArrowLeft: () => step(-1), ArrowRight: () => step(1), '0': () => setZoom('fit'), '1': () => setZoom(1), '+': () => stepZoom(1), '=': () => stepZoom(1), '-': () => stepZoom(-1) }
      const action = actions[event.key]
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
  const width = revision && zoom !== 'fit' ? revision.width * zoom : undefined

  return <div className="item-viewer" role="dialog" aria-label={`Viewing ${summary.name}`}>
    <header className="viewer-header">
      <button className="icon-button" onClick={onClose} aria-label="Close viewer" title="Close (Esc)"><Icon name="close" /></button>
      <div className="viewer-title"><strong>{summary.name}</strong><small>{summary.sourcePath ?? summary.collection}</small></div>
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
        </div>
        <div className={`viewer-stage backdrop-${preferences.backdrop}${zoom === 'fit' ? ' stage-fit' : ''}`} onWheel={event => { if (event.ctrlKey) { event.preventDefault(); stepZoom(event.deltaY < 0 ? 1 : -1) } }}>
          {revision && <img key={revision.id} src={window.pixel.mediaUrl(`original/${revision.id}`)} alt={summary.name} draggable={false}
            className={preferences.pixelated ? 'pixelated' : ''} style={width ? { width, maxWidth: 'none', maxHeight: 'none' } : undefined} />}
        </div>
      </div>
      <aside className="viewer-side">
        {item && revision ? <>
          <section className="viewer-section">
            <div className="status-row">
              {item.pendingExport && <span className="badge badge-accent">Pending export</span>}
              {item.missing && <span className="badge badge-warn">Missing from source</span>}
              {!item.sourceId && <span className="badge">Unlinked</span>}
            </div>
            <dl className="facts">
              <dt>Size</dt><dd>{revision.width} × {revision.height}</dd>
              <dt>Format</dt><dd>{revision.format.toUpperCase()} · {formatBytes(revision.bytes)}</dd>
              <dt>Alpha</dt><dd>{alphaLabel(revision)}</dd>
              <dt>SHA-256</dt><dd><code title={revision.sha256}>{revision.sha256.slice(0, 16)}…</code></dd>
              {item.sourceFile && <><dt>Source</dt><dd className="break"><code>{item.sourceFile}</code></dd></>}
            </dl>
          </section>
          <section className="viewer-section">
            <h3>Versions <span>{item.revisions.length}</span></h3>
            <div className="revision-strip">
              {[...item.revisions].reverse().map(entry => <button key={entry.id} className={`revision-card${entry.id === viewingId ? ' revision-viewing' : ''}`} onClick={() => setViewingId(entry.id)}>
                <span className="revision-thumb checker"><img src={window.pixel.mediaUrl(`thumb/${entry.id}`)} alt="" loading="lazy" /></span>
                <span className="revision-copy">
                  <strong>{entry.origin === 'imported' ? 'Imported' : entry.origin === 'generated' ? 'Generated' : 'Edited'}</strong>
                  <small>{new Date(entry.createdAt).toLocaleString()}</small>
                  <span className="revision-tags">
                    {entry.id === item.selectedRevisionId && <span className="badge badge-accent">Selected</span>}
                    {entry.id === item.syncedRevisionId && <span className="badge">On disk</span>}
                  </span>
                </span>
              </button>)}
            </div>
            {revision.id !== item.selectedRevisionId && <button className="primary-button wide" onClick={() => void select(revision.id)}><Icon name="check" /> Use this version</button>}
          </section>
        </> : <p className="quiet-text">Loading…</p>}
      </aside>
    </div>
  </div>
}
