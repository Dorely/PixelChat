import { useCallback, useContext, useEffect, useState } from 'react'
import type { ImageJob, Item, Revision } from '../shared/types'
import { DEFAULT_IMAGE_MODEL, IMAGE_MODELS, MAX_IMAGE_COUNT, SIZE_PRESETS, imageModel, type ImageBackground } from '../shared/image-models'
import Icon from './Icon'
import { pinLabel, pinnedThreads } from './NotesPanel'
import { op } from './ops'
import { ProvidersContext } from './SettingsDialog'

const PREFERENCES = 'pixelchat.edit'
const VISIBLE_JOBS = 6
type Choices = { model: string; quality: string; background: ImageBackground; size: string; count: number }
const DEFAULTS: Choices = { model: DEFAULT_IMAGE_MODEL, quality: 'auto', background: 'auto', size: 'source', count: 1 }

function loadChoices(): Choices {
  try {
    const value = { ...DEFAULTS, ...JSON.parse(localStorage.getItem(PREFERENCES) ?? '{}') }
    const model = imageModel(value.model) ?? imageModel(DEFAULT_IMAGE_MODEL)!
    return {
      model: model.id, quality: model.qualities.includes(value.quality) ? value.quality : 'auto',
      background: ['auto', 'opaque', 'transparent'].includes(value.background) ? value.background : 'auto',
      size: value.size === 'source' || SIZE_PRESETS.includes(value.size) ? value.size : 'source',
      count: Math.min(MAX_IMAGE_COUNT, Math.max(1, Number(value.count) || 1)),
    }
  } catch { return DEFAULTS }
}
const STATUS_LABELS: Record<ImageJob['status'], string> = { queued: 'Queued', running: 'Running', completed: 'Done', failed: 'Failed', uncertain: 'Uncertain', cancelled: 'Cancelled' }
const STATUS_BADGES: Partial<Record<ImageJob['status'], string>> = { running: 'badge-accent', completed: 'badge-accent', failed: 'badge-danger', uncertain: 'badge-warn' }

/** Open note threads as prompt text, numbered like their pins. */
function notesText(item: Item) {
  const pinned = pinnedThreads(item.notes)
  const lines = item.notes.filter(thread => !thread.resolvedAt).map(thread => {
    const number = pinned.indexOf(thread) + 1
    return `- ${thread.pin ? `Pin ${number} at ${pinLabel(thread.pin)}: ` : ''}${thread.body}`
  })
  return lines.length ? `Address these review notes:\n${lines.join('\n')}` : ''
}

/**
 * Runs the user's own edit prompt on the viewed version. Results arrive as new versions of the item;
 * choosing one makes the item pending export.
 */
export default function EditPanel({ projectId, item, revision, onView, onError }: { projectId: string; item: Item; revision: Revision; onView: (revisionId: string) => void; onError: (message: string) => void }) {
  const providers = useContext(ProvidersContext)
  const [prompt, setPrompt] = useState('')
  const [choices, setChoices] = useState(loadChoices)
  const [jobs, setJobs] = useState<ImageJob[]>([])
  const [busy, setBusy] = useState(false)
  useEffect(() => { try { localStorage.setItem(PREFERENCES, JSON.stringify(choices)) } catch { /* storage unavailable */ } }, [choices])

  const loadJobs = useCallback(async () => {
    try { setJobs(await op<ImageJob[]>('jobs_get', { projectId, itemId: item.id })) }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not load image jobs.') }
  }, [projectId, item.id, onError])
  useEffect(() => { void loadJobs() }, [loadJobs])
  useEffect(() => window.pixel.onChanged(() => void loadJobs()), [loadJobs])

  const model = imageModel(choices.model)!
  const ready = (provider: 'codex' | 'gemini') => !!providers?.[provider].ready
  const sourceTransparent = revision.hasAlpha && !revision.opaque
  const needsAlpha = choices.background === 'transparent' || (choices.background === 'auto' && sourceTransparent)
  const problem = !ready(model.provider) ? `Sign in to ${model.provider === 'codex' ? 'Codex' : 'Gemini'} in Settings to use ${model.label}.`
    : needsAlpha && !model.transparency ? `${model.label} cannot ${sourceTransparent && choices.background === 'auto' ? 'keep this version\'s transparency' : 'make transparent images'}. Choose Image 2.5 Flare or Sunburst, or an opaque background.`
    : needsAlpha && revision.format === 'jpeg' ? 'JPEG cannot store transparency. Choose an opaque background.' : ''
  const set = (patch: Partial<Choices>) => setChoices(value => ({ ...value, ...patch }))

  const run = async () => {
    if (!prompt.trim() || problem || busy) return
    setBusy(true)
    try {
      await op('edit_image', {
        projectId, itemId: item.id, revisionId: revision.id, prompt: prompt.trim(), model: model.id, quality: choices.quality, background: choices.background,
        count: choices.count, ...(choices.size === 'source' ? {} : { size: choices.size }), requestId: crypto.randomUUID(),
      })
      await loadJobs()
    } catch (error) { onError(error instanceof Error ? error.message : 'Could not start the edit.') }
    finally { setBusy(false) }
  }
  const cancel = async (jobId: string) => {
    try { await op('job_cancel', { projectId, jobId }); await loadJobs() }
    catch (error) { onError(error instanceof Error ? error.message : 'Could not cancel the job.') }
  }
  const notes = notesText(item)

  return <section className="viewer-section edit-panel">
    <h3>Edit <span>{revision.id === item.selectedRevisionId ? 'selected version' : 'viewed version'}</span></h3>
    <textarea value={prompt} rows={4} maxLength={32000} placeholder="Describe the change to make to this version" aria-label="Edit prompt"
      onChange={event => setPrompt(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void run() } }} />
    {notes && <button className="text-button add-notes" onClick={() => setPrompt(value => value.trim() ? `${value.trim()}\n\n${notes}` : notes)}><Icon name="plus" /> Add open notes</button>}
    <div className="edit-options">
      <label>Model<select className="filter-select" value={model.id} onChange={event => { const next = imageModel(event.target.value)!; set({ model: next.id, quality: next.qualities.includes(choices.quality) ? choices.quality : 'auto' }) }}>
        {IMAGE_MODELS.map(entry => <option key={entry.id} value={entry.id}>{entry.label}{ready(entry.provider) ? '' : ' (not signed in)'}</option>)}
      </select></label>
      <label>Quality<select className="filter-select" value={choices.quality} disabled={model.qualities.length === 1} onChange={event => set({ quality: event.target.value })}>
        {model.qualities.map(entry => <option key={entry} value={entry}>{entry === 'auto' ? 'Auto' : entry === 'xhigh' ? 'Extra high' : entry[0]!.toUpperCase() + entry.slice(1)}</option>)}
      </select></label>
      <label>Background<select className="filter-select" value={choices.background} onChange={event => set({ background: event.target.value as ImageBackground })}>
        <option value="auto">{sourceTransparent ? 'Keep transparency' : 'Auto'}</option><option value="opaque">Opaque</option><option value="transparent">Transparent</option>
      </select></label>
      <label>Size<select className="filter-select" value={choices.size} onChange={event => set({ size: event.target.value })}>
        <option value="source">Match source ({revision.width}×{revision.height})</option>
        {SIZE_PRESETS.map(entry => <option key={entry} value={entry}>{entry[0]!.toUpperCase() + entry.slice(1)}</option>)}
      </select></label>
      <label>Images<select className="filter-select" value={choices.count} onChange={event => set({ count: Number(event.target.value) })}>
        {Array.from({ length: MAX_IMAGE_COUNT }, (_, index) => <option key={index} value={index + 1}>{index + 1}</option>)}
      </select></label>
    </div>
    {problem && <p className="settings-error"><Icon name="info" /> {problem}</p>}
    <button className="primary-button wide" disabled={!prompt.trim() || !!problem || busy} onClick={() => void run()} title="Run edit (Ctrl+Enter)">
      <Icon name="spark" /> {choices.count === 1 ? 'Run edit' : `Run ${choices.count} edits`}
    </button>
    <small className="quiet-text">Billed to your {model.provider === 'codex' ? 'ChatGPT plan' : 'Gemini project'}. Results become new versions; nothing changes on disk until you export.</small>
    {jobs.length > 0 && <ul className="job-list">
      {jobs.slice(0, VISIBLE_JOBS).map(job => <li key={job.id} className="job-row">
        <span className={`badge ${STATUS_BADGES[job.status] ?? ''}`}>{STATUS_LABELS[job.status]}</span>
        <span className="job-copy">
          {job.revisionId ? <button className="text-button" onClick={() => onView(job.revisionId!)} title="View this result">{job.input.prompt}</button> : <span title={job.input.prompt}>{job.input.prompt}</span>}
          <small>{imageModel(job.input.model)?.label ?? job.input.model} · {new Date(job.createdAt).toLocaleTimeString()}</small>
          {job.error && <small className="danger-text">{job.error}</small>}
          {job.warning && <small className="warn-text">{job.warning}</small>}
        </span>
        {job.status === 'queued' && <button className="icon-button" onClick={() => void cancel(job.id)} aria-label="Cancel job" title="Cancel"><Icon name="x" /></button>}
        {job.status === 'running' && <span className="spinner" aria-label="Running" />}
      </li>)}
    </ul>}
  </section>
}
