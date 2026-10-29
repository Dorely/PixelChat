import { useState } from 'react'
import type { InstallationInput } from '../shared/workbench'
import Icon from './Icon'

/** Installs the PixelChat skill (and optionally the MCP server) for Codex or Claude Code through the bundled CLI. */
export default function AgentSettings() {
  const [scope, setScope] = useState<'project' | 'user'>('project')
  const [project, setProject] = useState('')
  const [directory, setDirectory] = useState('')
  const [codex, setCodex] = useState(true)
  const [claude, setClaude] = useState(false)
  const [mcp, setMcp] = useState(true)
  const [force, setForce] = useState(false)
  const [result, setResult] = useState('')
  const [busy, setBusy] = useState(false)
  const show = (response: { ok: boolean; result?: unknown; error?: { message: string } }) => setResult(response.ok ? JSON.stringify(response.result, null, 2) : response.error?.message ?? 'The request failed.')
  const run = async (action: InstallationInput['action'], dryRun = false) => {
    setBusy(true)
    try {
      show(await window.pixel.installIntegration({ action, scope, targets: [...(codex ? ['codex' as const] : []), ...(claude ? ['claude' as const] : [])], projectDirectory: project || undefined, directory: directory || undefined, skill: true, mcp, force, dryRun }))
    } catch (error) { setResult(error instanceof Error ? error.message : 'Installation failed.') } finally { setBusy(false) }
  }
  return <section className="settings-provider agent-settings">
    <header><strong>Agent integration</strong></header>
    <p>Lets Codex or Claude Code review, edit, and export art through the bundled CLI, which starts PixelChat when an agent needs it.</p>
    <label>Scope<select className="filter-select" value={scope} onChange={event => setScope(event.target.value as typeof scope)}><option value="project">This project directory</option><option value="user">Harness-wide for this user</option></select></label>
    {scope === 'project' && <label>Project directory<span className="model-row"><input aria-label="Integration project directory" value={project} onChange={event => setProject(event.target.value)} placeholder="Absolute project directory" /><button className="secondary-button" onClick={() => void window.pixel.chooseDirectory().then(path => { if (path) setProject(path) })}><Icon name="folder" /> Browse</button></span></label>}
    <label>Custom skills directory (optional)<input value={directory} onChange={event => setDirectory(event.target.value)} placeholder="Absolute skills parent directory" /></label>
    <div className="check-grid">
      <label className="toggle"><input type="checkbox" checked={codex} onChange={event => setCodex(event.target.checked)} /> Codex</label>
      <label className="toggle"><input type="checkbox" checked={claude} onChange={event => setClaude(event.target.checked)} /> Claude Code</label>
      <label className="toggle"><input type="checkbox" checked={mcp} onChange={event => setMcp(event.target.checked)} /> Also register MCP</label>
      <label className="toggle"><input type="checkbox" checked={force} onChange={event => setForce(event.target.checked)} /> Replace conflicting files (backup kept)</label>
    </div>
    <div className="model-row wrap">
      <button className="primary-button" disabled={busy} onClick={() => void run('install')}>Install / update</button>
      <button className="secondary-button" disabled={busy} onClick={() => void run('install', true)}>Preview changes</button>
      <button className="secondary-button" disabled={busy} onClick={() => void run('status')}>Installation status</button>
      <button className="secondary-button" disabled={busy} onClick={() => void window.pixel.operation('status', {}).then(show)}>Connection status</button>
      <button className="text-button danger-text" disabled={busy} onClick={() => void run('uninstall')}>Uninstall</button>
    </div>
    {result && <pre className="integration-result" role="status">{result}</pre>}
  </section>
}
