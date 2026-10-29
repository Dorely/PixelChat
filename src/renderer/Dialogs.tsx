import type { ReactNode } from 'react'
import Icon from './Icon'

/** Modal shell with the shared header; closes on backdrop click unless busy. */
export function Modal({ eyebrow, title, label, busy = false, className = '', onClose, children }: { eyebrow: string; title: string; label: string; busy?: boolean; className?: string; onClose: () => void; children: ReactNode }) {
  return <div className="modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose() }} onKeyDown={event => { if (event.key === 'Escape' && !busy) onClose() }}>
    <section className={`settings-dialog ${className}`} role="dialog" aria-modal="true" aria-label={label}>
      <div className="drawer-header"><div><p className="eyebrow">{eyebrow}</p><h2>{title}</h2></div><button className="icon-button" onClick={onClose} disabled={busy} aria-label="Close dialog"><Icon name="close" /></button></div>
      {children}
    </section>
  </div>
}

/** Confirmation for destructive actions; the caller performs the action and reports its error. */
export function ConfirmDialog({ eyebrow, title, body, confirmLabel, busy = false, error = '', onCancel, onConfirm }: { eyebrow: string; title: string; body: string; confirmLabel: string; busy?: boolean; error?: string; onCancel: () => void; onConfirm: () => void }) {
  return <Modal eyebrow={eyebrow} title={title} label={eyebrow} busy={busy} className="project-dialog" onClose={onCancel}>
    <div className="project-dialog-body">
      <p>{body}</p>
      {error && <p className="settings-error"><Icon name="info" /> {error}</p>}
      <div className="model-row"><button className="danger-button" onClick={onConfirm} disabled={busy}><Icon name="trash" /> {busy ? 'Working…' : confirmLabel}</button><button className="secondary-button" onClick={onCancel} disabled={busy}>Cancel</button></div>
    </div>
  </Modal>
}
