import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ChatFocus, Decision, ItemSearchResult, ItemStatusFilter, Review, ReviewEntry, ReviewSummary } from '../shared/types'
import Icon from './Icon'
import ItemCard, { decisionLabel } from './ItemCard'
import ItemViewer, { type ViewerTarget } from './ItemViewer'
import { ConfirmDialog, Modal } from './Dialogs'
import { op } from './ops'

export interface ReviewFilter { query?: string; collection?: string; status?: ItemStatusFilter }
type EntryFilter = 'all' | Decision | 'newer'
const ALL = '\u0000all'
const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback

function Progress({ counts }: { counts: ReviewSummary['counts'] }) {
  const part = (value: number) => `${counts.total ? value / counts.total * 100 : 0}%`
  return <span className="review-progress" title={`${counts.accepted} accepted, ${counts.needs_changes} need changes, ${counts.rejected} rejected, ${counts.unreviewed} unreviewed`}>
    <i className="progress-accepted" style={{ width: part(counts.accepted) }} /><i className="progress-needs_changes" style={{ width: part(counts.needs_changes) }} /><i className="progress-rejected" style={{ width: part(counts.rejected) }} />
  </span>
}

/** Creates a review from library filters; the count updates as filters change. */
export function NewReviewDialog({ projectId, initial, onClose, onCreated }: { projectId: string; initial?: ReviewFilter; onClose: () => void; onCreated: (review: ReviewSummary) => void }) {
  const [title, setTitle] = useState('')
  const [instructions, setInstructions] = useState('')
  const [collection, setCollection] = useState(initial?.collection ?? ALL)
  const [status, setStatus] = useState<ItemStatusFilter>(initial?.status ?? 'all')
  const query = initial?.query
  const [match, setMatch] = useState<ItemSearchResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const filter = useMemo(() => ({ ...(query ? { query } : {}), ...(collection === ALL ? {} : { collection }), status }), [query, collection, status])
  useEffect(() => { void op<ItemSearchResult>('search_items', { projectId, ...filter, limit: 1 }).then(setMatch, () => setMatch(null)) }, [projectId, filter])
  const submit = async () => {
    setBusy(true); setError('')
    try { onCreated(await op<ReviewSummary>('review_create', { projectId, title, instructions, filter, open: false })) }
    catch (failure) { setError(message(failure, 'Could not create the review.')) }
    finally { setBusy(false) }
  }
  return <Modal eyebrow="Reviews" title="New review" label="New review" busy={busy} className="source-dialog" onClose={onClose}>
    <div className="project-dialog-body">
      <label>Title<input autoFocus value={title} onChange={event => setTitle(event.target.value)} maxLength={120} placeholder="Character pass" aria-label="Review title" /></label>
      <label>Instructions <small>Optional. What to look for; agents read this with the notes.</small><textarea rows={3} value={instructions} onChange={event => setInstructions(event.target.value)} /></label>
      <div className="dialog-filters">
        <label>Folder<select className="filter-select" value={collection} onChange={event => setCollection(event.target.value)} aria-label="Folder">
          <option value={ALL}>All folders</option>
          {match?.collections.map(entry => <option key={entry.name} value={entry.name}>{entry.name || '(root)'} · {entry.count}</option>)}
        </select></label>
        <label>Status<select className="filter-select" value={status} onChange={event => setStatus(event.target.value as ItemStatusFilter)} aria-label="Status">
          <option value="all">Any status</option><option value="pending_export">Pending export</option><option value="open_notes">Has open notes</option><option value="missing">Missing from source</option>
        </select></label>
      </div>
      <p className="quiet-text">{query ? `Matching “${query}”: ` : ''}{match ? `${Math.min(match.total, 5000)} items` : '…'}{match && match.total > 5000 ? ' (first 5000)' : ''}</p>
      {error && <p className="settings-error"><Icon name="info" /> {error}</p>}
      <div className="model-row"><button className="primary-button" onClick={() => void submit()} disabled={busy || !title.trim() || !match?.total}><Icon name="check" /> {busy ? 'Creating…' : 'Create review'}</button><button className="secondary-button" onClick={onClose} disabled={busy}>Cancel</button></div>
    </div>
  </Modal>
}

function ReviewBoard({ projectId, reviewId, focusItemId, onBack, onChatFocus, onError }: { projectId: string; reviewId: string; focusItemId?: string; onBack: () => void; onChatFocus: (focus: ChatFocus) => void; onError: (message: string) => void }) {
  const [review, setReview] = useState<Review | null>(null)
  const [filter, setFilter] = useState<EntryFilter>('all')
  const [viewer, setViewer] = useState<{ items: ViewerTarget[]; index: number } | null>(null)
  const viewedId = viewer?.items[viewer.index]?.item.id
  useEffect(() => onChatFocus(viewedId ? { reviewId, itemId: viewedId } : { reviewId }), [reviewId, viewedId, onChatFocus])
  const [confirm, setConfirm] = useState<'delete' | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirmError, setConfirmError] = useState('')
  const focused = useRef<string | undefined>(undefined)

  const load = useCallback(async () => {
    try { setReview(await op<Review>('review_get', { projectId, reviewId, includeResolved: true })) }
    catch (error) { onError(message(error, 'Could not load the review.')); onBack() }
  }, [projectId, reviewId, onError, onBack])
  useEffect(() => { void load() }, [load])
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | null = null
    const unsubscribe = window.pixel.onChanged(() => { if (!timer) timer = setTimeout(() => { timer = null; void load() }, 150) })
    return () => { unsubscribe(); if (timer) clearTimeout(timer) }
  }, [load])

  const entries = review?.entries ?? []
  const shown = entries.filter(entry => filter === 'all' || (filter === 'newer' ? entry.newerVersions > 0 : entry.decision === filter))
  const entryMap = useMemo(() => new Map(entries.map(entry => [entry.revisionId, entry])), [entries])
  const targets = (list: ReviewEntry[]) => list.map(entry => ({ item: entry.item, revisionId: entry.revisionId }))
  // The viewer walks a snapshot so deciding never reshuffles the list under it.
  const open = (list: ViewerTarget[], index: number) => setViewer({ items: list, index })
  useEffect(() => {
    if (!review || !focusItemId || focused.current === focusItemId) return
    focused.current = focusItemId
    const index = review.entries.map(entry => entry.itemId).lastIndexOf(focusItemId)
    if (index >= 0) { setFilter('all'); open(targets(review.entries), index) }
  }, [review, focusItemId])
  const decide = async (revisionId: string, decision: Decision) => {
    try { await op('review_decide', { projectId, reviewId, decisions: [{ revisionId, decision }] }) } catch (error) { onError(message(error, 'Could not record the decision.')) }
  }
  const setStatus = async (action: 'review_complete' | 'review_reopen') => {
    if (!review) return
    try { await op(action, { projectId, reviewId, expectedRevision: review.revision }) } catch (error) { onError(message(error, 'Could not update the review.')) }
  }
  const remove = async () => {
    setBusy(true); setConfirmError('')
    try { await op('review_delete', { projectId, reviewId }); setConfirm(null); onBack() }
    catch (error) { setConfirmError(message(error, 'Could not delete the review.')) }
    finally { setBusy(false) }
  }
  if (!review) return <section className="workspace-panel"><p className="quiet-text">Loading…</p></section>
  const completed = review.status === 'completed'
  const counts = review.counts
  const newerCount = entries.filter(entry => entry.newerVersions > 0).length
  const filters: { id: EntryFilter; label: string; count: number }[] = [
    { id: 'all', label: 'All', count: counts.total }, { id: 'unreviewed', label: 'Unreviewed', count: counts.unreviewed }, { id: 'accepted', label: 'Accepted', count: counts.accepted },
    { id: 'needs_changes', label: 'Needs changes', count: counts.needs_changes }, { id: 'rejected', label: 'Rejected', count: counts.rejected },
    ...(newerCount ? [{ id: 'newer' as const, label: 'Has newer versions', count: newerCount }] : []),
  ]
  const firstUnreviewed = entries.findIndex(entry => entry.decision === 'unreviewed')

  return <section className="workspace-panel library-panel">
    <div className="panel-header">
      <div>
        <button className="text-button back-link" onClick={onBack}><Icon name="arrow-left" /> Reviews</button>
        <h1>{review.title} {completed && <span className="badge">Completed</span>}</h1>
      </div>
      <div className="header-actions">
        {!completed && <button className="primary-button" onClick={() => open(targets(entries), Math.max(0, firstUnreviewed))} disabled={!entries.length}><Icon name="arrow" /> {firstUnreviewed >= 0 ? 'Continue reviewing' : 'Review again'}</button>}
        {completed ? <button className="secondary-button" onClick={() => void setStatus('review_reopen')}><Icon name="undo" /> Reopen</button>
          : <button className="secondary-button" onClick={() => void setStatus('review_complete')}><Icon name="check" /> Complete</button>}
        <button className="icon-button" onClick={() => { setConfirmError(''); setConfirm('delete') }} aria-label="Delete review" title="Delete review"><Icon name="trash" /></button>
      </div>
    </div>
    <div className="review-meta">
      <Progress counts={counts} />
      <small>{counts.total - counts.unreviewed} of {counts.total} decided · {review.openNotes} open {review.openNotes === 1 ? 'note' : 'notes'}{completed && review.completedAt ? ` · completed ${new Date(review.completedAt).toLocaleString()} by ${review.completedBy === 'agent' ? 'the agent' : 'you'}` : ''}</small>
      {review.instructions && <p className="review-instructions">{review.instructions}</p>}
    </div>
    <div className="library-toolbar">
      <span className="segmented" role="group" aria-label="Decision filter">
        {filters.map(entry => <button key={entry.id} className={filter === entry.id ? 'active' : ''} onClick={() => setFilter(entry.id)}>{entry.label} <span className="count-pill">{entry.count}</span></button>)}
      </span>
    </div>
    {shown.length === 0 ? <div className="empty-state"><div className="empty-icon"><Icon name="check" /></div><h2>Nothing here</h2><p>No entries match this filter.</p></div>
      : <div className="item-grid">{shown.map((entry, index) => <ItemCard key={entry.revisionId} item={entry.item} onOpen={() => open(targets(shown), index)}
        review={{ version: entry.revision, decision: entry.decision, openNotes: entry.openNotes, newerVersions: entry.newerVersions, locked: completed, onDecide: decision => void decide(entry.revisionId, decision) }} />)}</div>}
    {viewer && viewer.items[viewer.index] && <ItemViewer projectId={projectId} items={viewer.items} index={viewer.index} review={{ id: review.id, completed, entries: entryMap }}
      onIndex={index => setViewer(current => current && { ...current, index })} onClose={() => setViewer(null)} onError={onError} />}
    {confirm && <ConfirmDialog eyebrow="Delete review" title={review.title} confirmLabel="Delete" busy={busy} error={confirmError} onCancel={() => setConfirm(null)} onConfirm={() => void remove()}
      body="The review and its decisions are removed permanently. Notes stay on their items." />}
  </section>
}

/** Review launcher and boards. A target from the app (agent navigation) opens a review and item. */
export default function ReviewsView({ projectId, target, onTargetDone, onChatFocus, onError }: { projectId: string; target: { reviewId?: string; itemId?: string } | null; onTargetDone: () => void; onChatFocus: (focus: ChatFocus) => void; onError: (message: string) => void }) {
  const [reviews, setReviews] = useState<ReviewSummary[] | null>(null)
  const [status, setStatus] = useState<'active' | 'completed'>('active')
  const [openId, setOpenId] = useState<string | null>(null)
  const [focusItemId, setFocusItemId] = useState<string | undefined>()
  const [creating, setCreating] = useState(false)
  const load = useCallback(async () => {
    try { setReviews(await op<ReviewSummary[]>('review_list', { projectId, status })) } catch (error) { onError(message(error, 'Could not load reviews.')) }
  }, [projectId, status, onError])
  useEffect(() => { void load(); return window.pixel.onChanged(() => void load()) }, [load])
  useEffect(() => { if (target?.reviewId) { setOpenId(target.reviewId); setFocusItemId(target.itemId); onTargetDone() } }, [target, onTargetDone])
  const back = useCallback(() => { setOpenId(null); setFocusItemId(undefined) }, [])
  useEffect(() => { if (!openId) onChatFocus({}) }, [openId, onChatFocus])
  if (openId) return <ReviewBoard key={openId} projectId={projectId} reviewId={openId} focusItemId={focusItemId} onBack={back} onChatFocus={onChatFocus} onError={onError} />
  return <section className="workspace-panel">
    <div className="panel-header">
      <div><p className="eyebrow">Feedback for agents</p><h1>Reviews <span>{reviews?.length ?? 0}</span></h1></div>
      <div className="header-actions">
        <span className="segmented" role="group" aria-label="Review status">
          <button className={status === 'active' ? 'active' : ''} onClick={() => setStatus('active')}>Active</button>
          <button className={status === 'completed' ? 'active' : ''} onClick={() => setStatus('completed')}>Completed</button>
        </span>
        <button className="primary-button" onClick={() => setCreating(true)}><Icon name="plus" /> New review</button>
      </div>
    </div>
    {reviews && reviews.length === 0 ? <div className="empty-state"><div className="empty-icon"><Icon name="check" /></div>
      {status === 'active' ? <><h2>No active reviews</h2><p>A review collects artwork for accept, needs-changes, and reject decisions, with notes and region pins for agents to act on.</p><button className="primary-button" onClick={() => setCreating(true)}><Icon name="plus" /> New review</button></>
        : <><h2>No completed reviews</h2><p>Completed reviews are kept here with their decisions and notes.</p></>}
    </div>
      : <div className="review-list">{reviews?.map(review => <button key={review.id} className="review-card" onClick={() => setOpenId(review.id)}>
        <span className="review-card-copy"><strong>{review.title}</strong><small>{review.counts.total} versions · {review.counts.total - review.counts.unreviewed} decided · {review.openNotes} open notes · updated {new Date(review.updatedAt).toLocaleString()}</small></span>
        <Progress counts={review.counts} />
        <span className="review-card-counts">{(['accepted', 'needs_changes', 'rejected'] as const).map(decision => review.counts[decision] > 0 && <span key={decision} className={`decision-count decision-${decision}`}>{review.counts[decision]} {decisionLabel(decision).toLowerCase()}</span>)}</span>
      </button>)}</div>}
    {creating && <NewReviewDialog projectId={projectId} onClose={() => setCreating(false)} onCreated={review => { setCreating(false); setOpenId(review.id) }} />}
  </section>
}
