import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChatFocus, Item, ItemSearchResult, ItemStatusFilter, ItemSummary, ReviewSummary, Source } from '../shared/types'
import Icon from './Icon'
import ItemCard from './ItemCard'
import ItemViewer from './ItemViewer'
import { NewReviewDialog } from './ReviewsView'
import { ConfirmDialog, Modal } from './Dialogs'
import { op } from './ops'

const PAGE = 2000
const ALL = '\u0000all'
const lines = (text: string) => text.split('\n').map(line => line.trim()).filter(Boolean)

function SourceRow({ source, onRescan, onRemove }: { source: Source; onRescan: () => void; onRemove: () => void }) {
  const scan = source.lastScan
  return <div className="source-row">
    <div className="source-copy">
      <strong title={source.root}>{source.root}</strong>
      <small>{source.itemCount} items{source.exclude.length ? ` · excludes ${source.exclude.join(', ')}` : ''}</small>
      {source.scanning ? <span className="scan-progress"><i style={{ width: `${source.scanning.total ? (source.scanning.done / source.scanning.total) * 100 : 0}%` }} /><small>Scanning {source.scanning.done} / {source.scanning.total || '…'}</small></span>
        : scan && <small>Last scan {new Date(scan.finishedAt).toLocaleString()}: {scan.added} new, {scan.changed} changed, {scan.unchanged} unchanged, {scan.missing} missing{scan.failed.length ? `, ${scan.failed.length} failed` : ''}</small>}
      {!source.scanning && scan && scan.failed.length > 0 && <details className="scan-failures"><summary>Failed files</summary>{scan.failed.slice(0, 50).map(entry => <div key={entry.path}><code>{entry.path}</code> {entry.error}</div>)}</details>}
    </div>
    <button className="icon-button" onClick={onRescan} disabled={!!source.scanning} aria-label="Rescan source" title="Rescan"><Icon name="refresh" /></button>
    <button className="icon-button" onClick={onRemove} disabled={!!source.scanning} aria-label="Remove source" title="Remove source"><Icon name="trash" /></button>
  </div>
}

function AddSourceDialog({ projectId, onClose, onError }: { projectId: string; onClose: (added: boolean) => void; onError: (message: string) => void }) {
  const [root, setRoot] = useState('')
  const [include, setInclude] = useState('')
  const [exclude, setExclude] = useState('')
  const [busy, setBusy] = useState(false)
  const browse = async () => { const chosen = await window.pixel.chooseDirectory(); if (chosen) setRoot(chosen) }
  const submit = async () => {
    setBusy(true)
    try { await op('source_add', { projectId, root: root.trim(), include: lines(include), exclude: lines(exclude) }); onClose(true) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not add the folder.') }
    finally { setBusy(false) }
  }
  return <Modal eyebrow="Library" title="Add source folder" label="Add source folder" busy={busy} className="source-dialog" onClose={() => onClose(false)}>
    <div className="project-dialog-body">
      <p>PixelChat stores a copy of each PNG, WebP, and JPEG file and remembers where it came from. Rescan picks up later changes; export writes approved versions back.</p>
      <label>Folder<span className="path-row"><input value={root} onChange={event => setRoot(event.target.value)} placeholder="C:\path\to\game\assets" aria-label="Folder path" /><button className="secondary-button" onClick={() => void browse()}><Icon name="folder" /> Browse</button></span></label>
      <label>Include globs <small>One per line, relative to the folder. Empty includes everything.</small><textarea rows={2} value={include} onChange={event => setInclude(event.target.value)} placeholder="characters/**" /></label>
      <label>Exclude globs <small>Dot-folders such as .godot are always skipped.</small><textarea rows={2} value={exclude} onChange={event => setExclude(event.target.value)} placeholder="concepts/**" /></label>
      <div className="model-row"><button className="primary-button" onClick={() => void submit()} disabled={busy || !root.trim()}><Icon name="import" /> {busy ? 'Adding…' : 'Add and scan'}</button><button className="secondary-button" onClick={() => onClose(false)} disabled={busy}>Cancel</button></div>
    </div>
  </Modal>
}

/** Thumbnail grid over the project library with folder, status, and text filters. */
export default function LibraryView({ projectId, focusItemId, onFocusDone, onOpenReview, onChatFocus, onError }: { projectId: string; focusItemId?: string; onFocusDone: () => void; onOpenReview: (reviewId: string) => void; onChatFocus: (focus: ChatFocus) => void; onError: (message: string) => void }) {
  const [result, setResult] = useState<ItemSearchResult | null>(null)
  const [sources, setSources] = useState<Source[]>([])
  const [query, setQuery] = useState('')
  const [collection, setCollection] = useState(ALL)
  const [status, setStatus] = useState<ItemStatusFilter>('all')
  const [showSources, setShowSources] = useState(false)
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<Source | null>(null)
  const [removeBusy, setRemoveBusy] = useState(false)
  const [removeError, setRemoveError] = useState('')
  const [viewer, setViewer] = useState<{ items: ItemSummary[]; index: number } | null>(null)
  const viewedId = viewer?.items[viewer.index]?.id
  useEffect(() => onChatFocus(viewedId ? { itemId: viewedId } : {}), [viewedId, onChatFocus])
  const [reviewing, setReviewing] = useState(false)
  const serial = useRef(0)

  const load = useCallback(async () => {
    const current = ++serial.current
    try {
      const [items, nextSources] = await Promise.all([
        op<ItemSearchResult>('search_items', { projectId, query: query || undefined, collection: collection === ALL ? undefined : collection, status, limit: PAGE }),
        op<Source[]>('sources_list', { projectId }),
      ])
      if (current === serial.current) { setResult(items); setSources(nextSources) }
    } catch (error) { onError(error instanceof Error ? error.message : 'Could not load the library.') }
  }, [projectId, query, collection, status, onError])
  useEffect(() => { const timer = setTimeout(() => void load(), query ? 180 : 0); return () => clearTimeout(timer) }, [load, query])
  useEffect(() => {
    // Scans emit many changes; coalesce them so the grid refreshes a few times a second at most.
    let timer: ReturnType<typeof setTimeout> | null = null
    const unsubscribe = window.pixel.onChanged(() => { if (!timer) timer = setTimeout(() => { timer = null; void load() }, 400) })
    return () => { unsubscribe(); if (timer) clearTimeout(timer) }
  }, [load])

  useEffect(() => {
    // Agent navigation opens one item, even when the current filters hide it.
    if (!focusItemId || !result) return
    onFocusDone()
    const index = result?.items.findIndex(entry => entry.id === focusItemId) ?? -1
    if (index >= 0) { setViewer({ items: result!.items, index }); return }
    void op<Item>('item_get', { projectId, itemId: focusItemId }).then(item => setViewer({ items: [item], index: 0 }), error => onError(error instanceof Error ? error.message : 'Could not open the item.'))
  }, [focusItemId, result])

  const scanning = sources.some(source => source.scanning)
  const removeSource = async () => {
    if (!removing) return
    setRemoveBusy(true); setRemoveError('')
    try { await op('source_remove', { projectId, sourceId: removing.id }); setRemoving(null) }
    catch (error) { setRemoveError(error instanceof Error ? error.message : 'Could not remove the source.') }
    finally { setRemoveBusy(false) }
  }
  const items = result?.items ?? []

  return <section className="workspace-panel library-panel">
    <div className="panel-header">
      <div><p className="eyebrow">Project art</p><h1>Library <span>{result?.total ?? 0}</span></h1></div>
      <div className="header-actions">
        <button className={`secondary-button${showSources ? ' button-active' : ''}`} onClick={() => setShowSources(value => !value)} aria-expanded={showSources}><Icon name="folder" /> Sources {sources.length > 0 && <span className="count-pill">{sources.length}</span>}{scanning && <span className="spinner" aria-label="Scanning" />}</button>
        <button className="secondary-button" onClick={() => setReviewing(true)} disabled={!result?.total} title="Start a review of the items matching the current filters"><Icon name="check" /> Review these</button>
        <button className="primary-button" onClick={() => setAdding(true)}><Icon name="plus" /> Add folder</button>
      </div>
    </div>
    {showSources && <div className="sources-panel">
      {sources.length === 0 ? <p className="quiet-text">No source folders yet.</p> : sources.map(source => <SourceRow key={source.id} source={source}
        onRescan={() => void op('source_rescan', { projectId, sourceId: source.id }).catch(error => onError(error instanceof Error ? error.message : 'Rescan failed.'))}
        onRemove={() => { setRemoveError(''); setRemoving(source) }} />)}
    </div>}
    <div className="library-toolbar">
      <label className="search-field"><Icon name="search" /><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Search names, paths, tags…" aria-label="Search library" /></label>
      <select className="filter-select" value={collection} onChange={event => setCollection(event.target.value)} aria-label="Folder">
        <option value={ALL}>All folders</option>
        {result?.collections.map(entry => <option key={entry.name} value={entry.name}>{entry.name || '(root)'} · {entry.count}</option>)}
      </select>
      <select className="filter-select" value={status} onChange={event => setStatus(event.target.value as ItemStatusFilter)} aria-label="Status">
        <option value="all">Any status</option><option value="pending_export">Pending export</option><option value="open_notes">Has open notes</option><option value="missing">Missing from source</option>
      </select>
    </div>
    {result && result.total > items.length && <p className="quiet-text">Showing the first {items.length} of {result.total}. Narrow the filters to see the rest.</p>}
    {result && items.length === 0 ? <div className="empty-state"><div className="empty-icon"><Icon name="image" /></div>
      {result.total === 0 && !query && collection === ALL && status === 'all'
        ? <><h2>Your art library is empty</h2><p>Add a source folder to snapshot its artwork for review. Source files stay where they are.</p><button className="primary-button" onClick={() => setAdding(true)}><Icon name="plus" /> Add folder</button></>
        : <><h2>No matching art</h2><p>Try another search or filter.</p></>}
    </div>
      : <div className="item-grid">{items.map((item, index) => <ItemCard key={item.id} item={item} onOpen={() => setViewer({ items, index })} />)}</div>}
    {viewer && viewer.items[viewer.index] && <ItemViewer projectId={projectId} items={viewer.items} index={viewer.index} onIndex={index => setViewer(current => current && { ...current, index })} onClose={() => setViewer(null)} onError={onError} />}
    {reviewing && <NewReviewDialog projectId={projectId} initial={{ query: query || undefined, collection: collection === ALL ? undefined : collection, status }} onClose={() => setReviewing(false)}
      onCreated={(review: ReviewSummary) => { setReviewing(false); onOpenReview(review.id) }} />}
    {adding && <AddSourceDialog projectId={projectId} onClose={added => { setAdding(false); if (added) setShowSources(true) }} onError={onError} />}
    {removing && <ConfirmDialog eyebrow="Remove source" title={removing.root} confirmLabel="Remove" busy={removeBusy} error={removeError} onCancel={() => setRemoving(null)} onConfirm={() => void removeSource()}
      body={`Removes ${removing.itemCount} items with their stored copies, reviews, and notes from this project. Files in the folder are not touched.`} />}
  </section>
}
