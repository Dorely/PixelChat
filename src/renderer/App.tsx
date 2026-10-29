import { useCallback, useEffect, useRef, useState } from 'react'
import type { Project, Session, Snapshot } from '../shared/types'
import Icon from './Icon'
import { ConfirmDialog, Modal } from './Dialogs'
import LibraryView from './LibraryView'
import ReviewsView from './ReviewsView'
import { op } from './ops'
import { THEMES, applyTheme, storedTheme, type ThemeId } from './theme'

type View = 'library' | 'reviews'
type Deletion = { kind: 'project' | 'session'; id: string; projectId: string; name: string }
type Target = { reviewId?: string; itemId?: string }

const emptySnapshot: Snapshot = { projects: [], activeProjectId: '', sessions: [] }
const message = (error: unknown) => error instanceof Error ? error.message : 'Operation failed.'

function TitleInput({ value, className, ariaLabel, onCommit, onCancel }: { value: string; className: string; ariaLabel: string; onCommit: (value: string) => void; onCancel: () => void }) {
  const inputRef = useRef<HTMLInputElement>(null)
  useEffect(() => { inputRef.current?.focus(); inputRef.current?.select() }, [])
  const commit = (next: string) => { const trimmed = next.trim(); if (trimmed && trimmed !== value) onCommit(trimmed); else onCancel() }
  return <input ref={inputRef} className={className} defaultValue={value} aria-label={ariaLabel} maxLength={70}
    onKeyDown={event => { if (event.key === 'Enter') commit(event.currentTarget.value); if (event.key === 'Escape') onCancel() }}
    onBlur={event => commit(event.currentTarget.value)} onClick={event => event.stopPropagation()} />
}

function ProjectDialog({ mode, project, onClose, onSubmit }: { mode: 'create' | 'rename'; project: Project | null; onClose: () => void; onSubmit: (name: string) => void }) {
  const [name, setName] = useState(mode === 'rename' ? project?.name ?? '' : '')
  const submit = () => { const trimmed = name.trim(); if (trimmed) onSubmit(trimmed) }
  const title = mode === 'create' ? 'New project' : 'Rename project'
  return <Modal eyebrow="Project" title={title} label={title} className="project-dialog" onClose={onClose}>
    <div className="project-dialog-body">
      <label>Project name<input autoFocus value={name} onChange={event => setName(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') submit() }} maxLength={60} placeholder="Game art" aria-label="Project name" /></label>
      <div className="model-row"><button className="primary-button" onClick={submit} disabled={!name.trim()}>{mode === 'create' ? 'Create project' : 'Save name'}</button><button className="secondary-button" onClick={onClose}>Cancel</button></div>
    </div>
  </Modal>
}

function ProjectSwitcher({ snapshot, onSwitch, onCreate, onRename, onDelete }: { snapshot: Snapshot; onSwitch: (id: string) => void; onCreate: () => void; onRename: () => void; onDelete: () => void }) {
  return <span className="project-switcher">
    <select aria-label="Project" value={snapshot.activeProjectId} onChange={event => onSwitch(event.target.value)}>
      {snapshot.projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}
    </select>
    <button className="labeled-button" onClick={onCreate} aria-label="New project" title="New project"><Icon name="plus" /> New</button>
    <button className="icon-button" onClick={onRename} aria-label="Rename project" title="Rename project"><Icon name="edit" /></button>
    <button className="icon-button" onClick={onDelete} aria-label="Delete project" title="Delete project"><Icon name="trash" /></button>
  </span>
}

function ThemePicker({ theme, onChange }: { theme: ThemeId; onChange: (id: ThemeId) => void }) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLSpanElement>(null)
  useEffect(() => {
    if (!open) return
    const outside = (event: MouseEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false) }
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    window.addEventListener('mousedown', outside); window.addEventListener('keydown', escape)
    return () => { window.removeEventListener('mousedown', outside); window.removeEventListener('keydown', escape) }
  }, [open])
  return <span className="theme-picker" ref={root}>
    <button type="button" className="icon-button" onClick={() => setOpen(value => !value)} aria-label="Theme" aria-expanded={open} title="Theme"><Icon name="palette" /></button>
    {open && <div className="theme-popover"><p className="eyebrow">Theme</p><div className="theme-options" role="radiogroup" aria-label="Theme">
      {THEMES.map(entry => <button key={entry.id} type="button" role="radio" aria-checked={theme === entry.id} className={`theme-option${theme === entry.id ? ' theme-option-active' : ''}`} onClick={() => { onChange(entry.id); setOpen(false) }}>
        <span className="theme-swatch" style={{ background: entry.swatch[0] }}><i style={{ background: entry.swatch[1] }} /><b style={{ background: entry.swatch[2] }} /></span>
        <span className="theme-copy"><strong>{entry.label}</strong><small>{entry.note}</small></span>
        {theme === entry.id && <Icon name="check" />}
      </button>)}
    </div></div>}
  </span>
}

function ChatSidebar({ sessions, projectName, selectedId, onSelect, onNew, onRename, onDelete }: { sessions: Session[]; projectName: string; selectedId: string | null; onSelect: (id: string) => void; onNew: () => void; onRename: (id: string, title: string) => void; onDelete: () => void }) {
  const [renamingId, setRenamingId] = useState<string | null>(null)
  return <aside className="chat-sidebar">
    <div className="brand-row"><div className="brand-mark"><Icon name="mark" /></div><div><strong>PIXELCHAT</strong><span>art review workspace</span></div><span className="live-indicator" title="Local workspace ready" /></div>
    <div className="sidebar-heading"><span>Conversations <small>{projectName}</small></span><span className="sidebar-actions"><button className="icon-button" disabled={!selectedId} onClick={onDelete} aria-label="Delete selected conversation" title="Delete selected conversation"><Icon name="trash" /></button><button className="labeled-button" onClick={onNew} aria-label="New conversation" title="New conversation"><Icon name="plus" /> New</button></span></div>
    <div className="conversation-list">
      {sessions.length === 0 ? <div className="empty-conversations"><div className="empty-icon"><Icon name="chat" /></div><p>Conversations for this project will appear here.</p><button className="text-button" onClick={onNew}>Start a conversation <Icon name="arrow" /></button></div>
        : sessions.map(session => renamingId === session.id
          ? <div key={session.id} className="conversation-row conversation-active"><span className="conversation-dot" /><TitleInput value={session.title} className="conversation-rename" ariaLabel="Conversation title" onCommit={title => { setRenamingId(null); onRename(session.id, title) }} onCancel={() => setRenamingId(null)} /></div>
          : <button key={session.id} className={`conversation-row${session.id === selectedId ? ' conversation-active' : ''}`} onClick={() => onSelect(session.id)} onDoubleClick={() => setRenamingId(session.id)} title="Double-click to rename">
            <span className="conversation-dot" /><span className="conversation-copy"><strong>{session.title}</strong><small>{new Date(session.updatedAt).toLocaleString()}</small></span>
          </button>)}
    </div>
    <div className="sidebar-chat">
      <div className="chat-welcome"><Icon name="spark" /><p>The inline assistant is not configured yet. External agents can connect once the PixelChat MCP server is installed.</p></div>
    </div>
  </aside>
}

export default function App() {
  const [snapshot, setSnapshot] = useState<Snapshot>(emptySnapshot)
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [view, setView] = useState<View>('library')
  const [theme, setTheme] = useState<ThemeId>(storedTheme)
  const [uiError, setUiError] = useState('')
  const [projectDialog, setProjectDialog] = useState<'create' | 'rename' | null>(null)
  const [deletion, setDeletion] = useState<Deletion | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  const [sidebarWidth, setSidebarWidth] = useState(34)
  const [target, setTarget] = useState<Target | null>(null)
  const refreshSerial = useRef(0)

  const refresh = useCallback(async () => {
    const serial = ++refreshSerial.current
    try {
      const next = await window.pixel.snapshot()
      if (serial !== refreshSerial.current) return
      setSnapshot(next)
      setSessionId(current => current && next.sessions.some(session => session.id === current) ? current : next.sessions[0]?.id ?? null)
    } catch (error) { setUiError(message(error)) }
  }, [])
  useEffect(() => { void refresh(); return window.pixel.onChanged(() => void refresh()) }, [refresh])
  useEffect(() => {
    // The main process has already switched to the command's project; views pick the target up after the switch.
    const unsubscribe = window.pixel.onUiCommand(command => {
      setView(command.action === 'open-review' ? 'reviews' : 'library')
      setTarget({ reviewId: command.reviewId, itemId: command.itemId })
      void window.pixel.uiAcknowledge(command.id)
    })
    void window.pixel.uiReady()
    const disconnected = () => setUiError('Browser host disconnected. Restart npm run dev:browser and open its printed URL.')
    window.addEventListener('pixel-host-disconnected', disconnected)
    return () => { unsubscribe(); window.removeEventListener('pixel-host-disconnected', disconnected) }
  }, [])
  useEffect(() => applyTheme(theme), [theme])

  const startResize = (event: React.MouseEvent) => {
    event.preventDefault(); document.body.classList.add('resizing')
    const move = (moveEvent: MouseEvent) => setSidebarWidth(Math.min(48, Math.max(24, (moveEvent.clientX / window.innerWidth) * 100)))
    const stop = () => { document.body.classList.remove('resizing'); window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', stop) }
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', stop)
  }

  const clearTarget = useCallback(() => setTarget(null), [])
  const call = async (action: () => Promise<unknown>) => { try { await action(); void refresh(); return true } catch (error) { setUiError(message(error)); return false } }
  const projectId = snapshot.activeProjectId
  const activeProject = snapshot.projects.find(project => project.id === projectId) ?? null
  const session = snapshot.sessions.find(item => item.id === sessionId) ?? null
  const createSession = () => call(async () => setSessionId((await op<Session>('session_create', { projectId, title: 'New conversation' })).id))
  const saveProject = async (name: string) => {
    const saved = projectDialog === 'create'
      ? await call(async () => window.pixel.setActiveProject((await op<Project>('project_create', { name })).id))
      : await call(() => op('project_rename', { projectId, name }))
    if (saved) setProjectDialog(null)
  }
  const confirmDelete = async () => {
    if (!deletion || deleting) return
    setDeleting(true); setDeleteError('')
    try {
      await op(deletion.kind === 'project' ? 'project_delete' : 'session_delete', { projectId: deletion.projectId, ...(deletion.kind === 'session' ? { sessionId: deletion.id } : {}) })
      setDeletion(null); await refresh()
    } catch (error) { setDeleteError(message(error)) }
    finally { setDeleting(false) }
  }

  return <div className="app-shell" style={{ '--sidebar-width': `${sidebarWidth}%` } as React.CSSProperties}>
    <ChatSidebar sessions={snapshot.sessions} projectName={activeProject?.name ?? 'Project'} selectedId={sessionId} onSelect={setSessionId} onNew={() => void createSession()}
      onRename={(id, title) => void call(() => op('rename_session', { projectId, sessionId: id, title }))}
      onDelete={() => { if (session) { setDeleteError(''); setDeletion({ kind: 'session', id: session.id, projectId, name: session.title }) } }} />
    <div className="resize-handle" role="separator" aria-orientation="vertical" aria-label="Resize chat sidebar" aria-valuenow={Math.round(sidebarWidth)} aria-valuemin={24} aria-valuemax={48} tabIndex={0} onMouseDown={startResize}
      onKeyDown={event => { const step = event.key === 'ArrowLeft' ? -2 : event.key === 'ArrowRight' ? 2 : 0; if (step) { event.preventDefault(); setSidebarWidth(width => Math.min(48, Math.max(24, width + step))) } }} />
    <main className="main-workspace">
      <header className="topbar">
        <div className="breadcrumb"><ProjectSwitcher snapshot={snapshot} onSwitch={id => { if (id !== projectId) void call(() => window.pixel.setActiveProject(id)) }} onCreate={() => setProjectDialog('create')} onRename={() => setProjectDialog('rename')}
          onDelete={() => { if (activeProject) { setDeleteError(''); setDeletion({ kind: 'project', id: activeProject.id, projectId: activeProject.id, name: activeProject.name }) } }} /></div>
        <div className="topbar-actions"><ThemePicker theme={theme} onChange={setTheme} /><button className="icon-button" onClick={() => void call(() => window.pixel.openLogs())} aria-label="Open diagnostic logs" title="Open diagnostic logs"><Icon name="file" /></button></div>
      </header>
      <nav className="workspace-tabs" aria-label="Workspace views">
        <button className={view === 'library' ? 'tab-active' : ''} onClick={() => setView('library')}><Icon name="library" /> Library</button>
        <button className={view === 'reviews' ? 'tab-active' : ''} onClick={() => setView('reviews')}><Icon name="check" /> Reviews</button>
      </nav>
      {!projectId ? null : view === 'library'
        ? <LibraryView key={projectId} projectId={projectId} focusItemId={target?.reviewId ? undefined : target?.itemId} onFocusDone={clearTarget} onOpenReview={reviewId => { setTarget({ reviewId }); setView('reviews') }} onError={setUiError} />
        : <ReviewsView key={projectId} projectId={projectId} target={target?.reviewId ? target : null} onTargetDone={clearTarget} onError={setUiError} />}
    </main>
    {uiError && <div className="error-banner" role="alert"><Icon name="info" /><span>{uiError}</span><button className="icon-button" onClick={() => setUiError('')} aria-label="Dismiss error"><Icon name="close" /></button></div>}
    {projectDialog && <ProjectDialog mode={projectDialog} project={activeProject} onClose={() => setProjectDialog(null)} onSubmit={name => void saveProject(name)} />}
    {deletion && <ConfirmDialog eyebrow={deletion.kind === 'project' ? 'Delete project' : 'Delete conversation'} title={deletion.name} confirmLabel="Delete" busy={deleting} error={deleteError} onCancel={() => setDeletion(null)} onConfirm={() => void confirmDelete()}
      body={deletion.kind === 'project' ? 'The project, its reviews, notes, and stored image copies are removed permanently. Files in source folders are not touched.' : 'This conversation is removed permanently. Project art, reviews, and notes remain.'} />}
  </div>
}
