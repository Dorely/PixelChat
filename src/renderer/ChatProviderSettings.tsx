import { useState } from 'react'
import type { ChatStatus, GenericProviderKind, ModelCapabilityResult } from '../shared/types'
import { isDirectGemini } from '../shared/chat-models'
import Icon from './Icon'

const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback
const effortList = (value: string) => value.split(',').map(entry => entry.trim()).filter(Boolean)

/** Chat models: built-in Codex models plus custom endpoints whose models must pass a capability test before saving. */
export default function ChatProviderSettings({ chat }: { chat: ChatStatus }) {
  const [label, setLabel] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [kind, setKind] = useState<GenericProviderKind>('chat-completions')
  const [apiKey, setApiKey] = useState('')
  const [providerError, setProviderError] = useState<string | null>(null)
  const [providerId, setProviderId] = useState('')
  const [slug, setSlug] = useState('')
  const [efforts, setEfforts] = useState('')
  const [testResult, setTestResult] = useState<ModelCapabilityResult | null>(null)
  const [testError, setTestError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const activeProvider = chat.providers.some(entry => entry.id === providerId) ? providerId : chat.providers[0]?.id ?? ''
  const resetTest = () => { setTestResult(null); setTestError(null) }
  const saveProvider = async () => {
    setProviderError(null)
    try {
      const saved = await window.pixel.saveChatProvider({ label, baseUrl, kind, ...(isDirectGemini(baseUrl) || !apiKey.trim() ? {} : { apiKey }) })
      setProviderId(saved.id); setLabel(''); setBaseUrl(''); setApiKey('')
    } catch (error) { setProviderError(message(error, 'Unable to save the provider.')) }
  }
  const runTest = async () => {
    if (!activeProvider || !slug.trim()) { setTestError('Select a provider and enter a model name first.'); return }
    setTesting(true); setTestError(null)
    try {
      const result = await window.pixel.testChatModel({ providerId: activeProvider, slug: slug.trim(), efforts: effortList(efforts) })
      setTestResult(result)
      if (result.failedEfforts.length) setTestError(`Failed efforts: ${result.failedEfforts.join(', ')}${result.error ? ` — ${result.error}` : ''}`)
    } catch (error) { setTestError(message(error, 'Model test failed.')); setTestResult(null) }
    finally { setTesting(false) }
  }
  const saveModel = async () => {
    setTestError(null)
    try { await window.pixel.saveChatModel({ providerId: activeProvider, slug: slug.trim(), efforts: effortList(efforts) }); setSlug(''); setEfforts(''); resetTest() }
    catch (error) { setTestError(message(error, 'Unable to save the model.')) }
  }
  const removeProvider = async (id: string, name: string) => {
    if (!window.confirm(`Remove ${name} and its saved models?`)) return
    setProviderError(null)
    try { await window.pixel.deleteChatProvider(id) } catch (error) { setProviderError(message(error, 'Unable to remove the provider.')) }
  }
  const removeModel = async (id: string) => {
    setTestError(null)
    try { await window.pixel.deleteChatModel(id) } catch (error) { setTestError(message(error, 'Unable to remove the model.')) }
  }
  return <section className="settings-provider chat-providers">
    <header><strong>Chat models</strong></header>
    <p>Built-in models use the Codex sign-in above. Pick the model for a conversation from the chat composer.</p>
    <div className="provider-list">
      {chat.models.map(entry => <div key={entry.id} className={`provider-row${entry.id === chat.active.modelId ? ' provider-active' : ''}`}>
        <span className="provider-choice"><strong>{entry.label}</strong><small>{entry.providerLabel} · {entry.slug}{entry.efforts.length ? ` · ${entry.efforts.join(', ')}` : ''}</small></span>
        {entry.id === chat.active.modelId && <span className="badge badge-accent">In use</span>}
        {!entry.builtIn && <button className="icon-button" onClick={() => void removeModel(entry.id)} aria-label={`Remove ${entry.label}`} title="Remove model"><Icon name="trash" /></button>}
      </div>)}
    </div>
    {chat.providers.length > 0 && <div className="settings-subsection"><h4>Custom endpoints</h4><div className="provider-list">
      {chat.providers.map(entry => <div key={entry.id} className="provider-row">
        <span className="provider-choice"><strong>{entry.label}</strong><small>{entry.kind} · {entry.baseUrl}</small></span>
        <button className="icon-button" onClick={() => void removeProvider(entry.id, entry.label)} aria-label={`Remove ${entry.label}`} title="Remove endpoint"><Icon name="trash" /></button>
      </div>)}
    </div></div>}
    <div className="settings-subsection"><h4>Add an endpoint</h4><div className="provider-form">
      <label>Provider name<input value={label} onChange={event => setLabel(event.target.value)} placeholder="Local Ollama" /></label>
      <label>Base URL<input value={baseUrl} onChange={event => setBaseUrl(event.target.value)} placeholder="http://localhost:11434/v1" /></label>
      <label>Endpoint type<select value={kind} onChange={event => setKind(event.target.value as GenericProviderKind)}><option value="chat-completions">Chat Completions</option><option value="responses">Responses</option><option value="anthropic">Anthropic Messages</option></select></label>
      {isDirectGemini(baseUrl) ? <p className="settings-note">Uses the Gemini API key above.</p>
        : <label>API key (optional)<input type="password" value={apiKey} autoComplete="off" onChange={event => setApiKey(event.target.value)} placeholder="Leave empty for local providers" /></label>}
      <div className="model-row"><button className="secondary-button" onClick={() => void saveProvider()} disabled={!label.trim() || !baseUrl.trim()}>Save endpoint</button></div>
      {providerError && <p className="settings-error"><Icon name="info" /> {providerError}</p>}
    </div></div>
    {chat.providers.length > 0 && <div className="settings-subsection"><h4>Add a model</h4><div className="provider-form">
      <label>Endpoint<select value={activeProvider} onChange={event => { setProviderId(event.target.value); resetTest() }}>{chat.providers.map(entry => <option key={entry.id} value={entry.id}>{entry.label} ({entry.kind})</option>)}</select></label>
      <label>Model name<input value={slug} onChange={event => { setSlug(event.target.value); resetTest() }} placeholder="qwen3-vl" /></label>
      <label>Efforts (comma separated, blank for default)<input value={efforts} onChange={event => { setEfforts(event.target.value); resetTest() }} placeholder="low, high" /></label>
      <div className="model-row"><button className="secondary-button" onClick={() => void runTest()} disabled={testing}>{testing ? 'Testing…' : 'Test model'}</button><button className="secondary-button" onClick={() => void saveModel()} disabled={!testResult || testResult.failedEfforts.length > 0}>Save model</button></div>
      {testResult && <p className="settings-note">Efforts: {Object.entries(testResult.perEffort).map(([effort, result]) => `${effort || '(default)'}: ${result}`).join(' · ')} · Sees images: {String(testResult.vision)}</p>}
      {testError && <p className="settings-error"><Icon name="info" /> {testError}</p>}
    </div></div>}
  </section>
}
