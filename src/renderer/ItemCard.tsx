import type { Decision, ItemSummary } from '../shared/types'

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

/** Grid card for an item; a review passes its decision for the badge row. */
export default function ItemCard({ item, decision, stale, onOpen }: { item: ItemSummary; decision?: Decision; stale?: boolean; onOpen: () => void }) {
  const badges = [
    decision && decision !== 'unreviewed' && <span key="decision" className={`badge ${decisionBadge(decision)}`}>{decisionLabel(decision)}</span>,
    stale && <span key="stale" className="badge" title="The selected version changed after this decision">Changed since</span>,
    item.openNotes > 0 && <span key="notes" className="badge" title="Open notes">{item.openNotes} {item.openNotes === 1 ? 'note' : 'notes'}</span>,
    item.pendingExport && <span key="export" className="badge badge-accent" title="Selected version differs from the source file">Pending export</span>,
    item.missing && <span key="missing" className="badge badge-warn" title="Not found in the source folder at the last scan">Missing</span>,
  ].filter(Boolean)
  return <button className={`item-card${decision ? ` decision-${decision}` : ''}`} onClick={onOpen} title={item.sourcePath ?? item.name}>
    <span className="item-thumb checker"><Thumbnail revisionId={item.selectedRevisionId} alt={item.name} /></span>
    <span className="item-copy"><strong>{item.name}</strong><small>{item.selected.width}×{item.selected.height} · {item.selected.format}</small></span>
    {badges.length > 0 && <span className="item-badges">{badges}</span>}
  </button>
}
