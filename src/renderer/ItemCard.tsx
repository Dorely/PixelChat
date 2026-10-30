import type { Decision, ItemSummary, RevisionMeta } from '../shared/types'

export const DECISIONS: { id: Decision; label: string; key: string; badge: string }[] = [
  { id: 'accepted', label: 'Accept', key: 'A', badge: 'badge-accent' },
  { id: 'needs_changes', label: 'Needs changes', key: 'C', badge: 'badge-warn' },
  { id: 'rejected', label: 'Reject', key: 'R', badge: 'badge-danger' },
]
export const decisionLabel = (decision: Decision) => ({ unreviewed: 'Unreviewed', accepted: 'Accepted', needs_changes: 'Needs changes', rejected: 'Rejected' })[decision]
export const decisionBadge = (decision: Decision) => DECISIONS.find(entry => entry.id === decision)?.badge ?? ''

export function Thumbnail({ revisionId, alt }: { revisionId: string; alt: string }) {
  return <img className="thumb-image" src={window.pixel.mediaUrl(`thumb/${revisionId}`)} alt={alt} loading="lazy" decoding="async" draggable={false} />
}

/** What a review shows on a card: the entry's exact version and its decision, decidable in place unless locked. */
export interface CardReview { version: RevisionMeta; decision: Decision; openNotes: number; newerVersions: number; locked: boolean; onDecide: (decision: Decision) => void }

/** Grid card for an item, or for one reviewed version of it. */
export default function ItemCard({ item, review, onOpen }: { item: ItemSummary; review?: CardReview; onOpen: () => void }) {
  const version = review?.version ?? item.selected
  const notes = review ? review.openNotes : item.openNotes
  const decision = review?.decision
  const badges = [
    decision && decision !== 'unreviewed' && <span key="decision" className={`badge ${decisionBadge(decision)}`}>{decisionLabel(decision)}</span>,
    review && review.newerVersions > 0 && <span key="newer" className="badge" title="The item has versions made after this one">{review.newerVersions} newer</span>,
    notes > 0 && <span key="notes" className="badge" title="Open notes">{notes} {notes === 1 ? 'note' : 'notes'}</span>,
    !review && item.pendingExport && <span key="export" className="badge badge-accent" title="Selected version differs from the source file">Pending export</span>,
    !review && item.missing && <span key="missing" className="badge badge-warn" title="Not found in the source folder at the last scan">Missing</span>,
  ].filter(Boolean)
  return <div className={`item-card${decision ? ` decision-${decision}` : ''}`}>
    <button className="item-open" onClick={onOpen} title={item.sourcePath ?? item.name}>
      <span className="item-thumb checker"><Thumbnail revisionId={version.id} alt={item.name} /></span>
      <span className="item-copy"><strong>{item.name}</strong><small>{version.width}×{version.height} · {version.format}</small></span>
    </button>
    {badges.length > 0 && <span className="item-badges">{badges}</span>}
    {review && <span className="card-decisions" role="group" aria-label={`Decision for ${item.name}`}>
      {DECISIONS.map(option => <button key={option.id} className={`decision-button decision-${option.id}${decision === option.id ? ' active' : ''}`} disabled={review.locked}
        onClick={() => review.onDecide(decision === option.id ? 'unreviewed' : option.id)} title={decision === option.id ? `Clear ${option.label.toLowerCase()}` : option.label}>
        {option.id === 'needs_changes' ? 'Changes' : option.label}</button>)}
    </span>}
  </div>
}
