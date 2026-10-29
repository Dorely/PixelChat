import { createContext, useState } from 'react'
import type { ProviderStatus } from '../shared/types'
import { Modal } from './Dialogs'
import Icon from './Icon'
import AgentSettings from './AgentSettings'

/** Provider readiness from the snapshot, for views that start image jobs. */
export const ProvidersContext = createContext<ProviderStatus | null>(null)

/** Provider credentials (Codex sign-in, Gemini key; both in OS-protected storage) and agent integration. */
export default function SettingsDialog({ providers, onClose, onError }: { providers: ProviderStatus; onClose: () => void; onError: (message: string) => void }) {
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    try { await action(); return true } catch (error) { onError(error instanceof Error ? error.message : 'The settings change failed.'); return false } finally { setBusy(false) }
  }
  const { codex, gemini } = providers
  return <Modal eyebrow="Settings" title="Providers and agents" label="Settings" onClose={onClose}>
    <div className="project-dialog-body settings-body">
      {!providers.secureStorage && <p className="settings-error"><Icon name="warn" /> Secure storage is unavailable on this device, so credentials cannot be saved.</p>}
      <section className="settings-provider">
        <header><strong>Codex</strong><span className={`badge ${codex.ready ? 'badge-accent' : ''}`}>{codex.ready ? 'Signed in' : codex.pending ? 'Waiting for browser' : 'Not signed in'}</span></header>
        <p>Image 2 and 2.5 models through your ChatGPT account. Each image draws on the plan's image allowance.</p>
        {codex.error && <p className="settings-error"><Icon name="info" /> {codex.error}</p>}
        <div className="model-row">
          {codex.pending
            ? <button className="secondary-button" disabled={busy} onClick={() => void run(() => window.pixel.codexCancelLogin())}>Cancel sign-in</button>
            : codex.ready
              ? <button className="secondary-button" disabled={busy} onClick={() => void run(() => window.pixel.codexLogout())}>Sign out</button>
              : <button className="primary-button" disabled={busy || !providers.secureStorage} onClick={() => void run(() => window.pixel.codexLogin())}><Icon name="key" /> Sign in with ChatGPT</button>}
        </div>
      </section>
      <section className="settings-provider">
        <header><strong>Gemini</strong><span className={`badge ${gemini.ready ? 'badge-accent' : ''}`}>{gemini.ready ? 'Key saved' : 'No key'}</span></header>
        <p>Nano Banana and Gemini image models through a Google AI Studio API key, billed to its project.</p>
        <label>API key<input type="password" value={key} autoComplete="off" spellCheck={false} placeholder={gemini.ready ? 'Enter a new key to replace the saved one' : 'Paste a Gemini API key'} onChange={event => setKey(event.target.value)} /></label>
        <div className="model-row">
          <button className="primary-button" disabled={busy || !key.trim() || !providers.secureStorage} onClick={() => void run(() => window.pixel.saveGeminiKey(key.trim())).then(saved => { if (saved) setKey('') })}>Save key</button>
          {gemini.ready && <button className="secondary-button" disabled={busy} onClick={() => void run(() => window.pixel.saveGeminiKey(null))}>Remove key</button>}
        </div>
      </section>
      <AgentSettings />
    </div>
  </Modal>
}
