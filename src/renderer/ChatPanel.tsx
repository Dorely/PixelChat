import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { ChatFocus, ChatStatus, Message, Snapshot } from '../shared/types'
import Icon from './Icon'
import ChatContextMeter from './ChatContextMeter'

const roleLabel = { user: 'You', assistant: 'PixelChat', tool: 'Activity' } as const

function ChatModelPicker({ chat, onChange, onSettings }: { chat: ChatStatus; onChange: (modelId: string, effort: string) => void; onSettings: () => void }) {
  const current = chat.models.find(entry => entry.id === chat.active.modelId) ?? chat.models[0]
  if (!current) return <button type="button" className="model-picker-empty" onClick={onSettings}><Icon name="chip" /> Add a chat model</button>
  const effort = current.efforts.includes(chat.active.effort) ? chat.active.effort : current.efforts[0] ?? ''
  const providers = [...new Set(chat.models.map(entry => entry.providerLabel))]
  return <span className="model-picker">
    <label className="model-select" title={`${current.providerLabel} · ${current.slug}`}><Icon name="chip" />
      <select aria-label="Chat model" value={current.id} onChange={event => { const next = chat.models.find(entry => entry.id === event.target.value); if (next) onChange(next.id, next.efforts.includes(effort) ? effort : next.efforts[0] ?? '') }}>
        {providers.map(provider => <optgroup key={provider} label={provider}>{chat.models.filter(entry => entry.providerLabel === provider).map(entry => <option key={entry.id} value={entry.id}>{entry.label}</option>)}</optgroup>)}
      </select>
      <Icon name="chevron-down" />
    </label>
    {current.efforts.length > 0 && <label className="model-select effort-select" title="Reasoning effort">
      <select aria-label="Reasoning effort" value={effort} onChange={event => onChange(current.id, event.target.value)}>
        {current.efforts.map(value => <option key={value} value={value}>{value}</option>)}
      </select>
      <Icon name="chevron-down" />
    </label>}
  </span>
}

/** One conversation with the inline assistant. Messages are read from the main process on every change notice. */
export default function ChatPanel({ sessionId, snapshot, focus, onSettings, onError }: { sessionId: string; snapshot: Snapshot; focus: ChatFocus; onSettings: () => void; onError: (message: string) => void }) {
  const [messages, setMessages] = useState<Message[]>([])
  const [text, setText] = useState('')
  const historyRef = useRef<HTMLDivElement>(null)
  const followOutput = useRef(true)
  const active = snapshot.activeTurns.includes(sessionId)
  useEffect(() => {
    let current = true
    const load = () => void window.pixel.chatMessages(sessionId).then(next => { if (current) setMessages(next) }, () => undefined)
    load(); const unsubscribe = window.pixel.onChanged(load)
    return () => { current = false; unsubscribe() }
  }, [sessionId])
  useEffect(() => { followOutput.current = true }, [sessionId])
  useLayoutEffect(() => { const history = historyRef.current; if (history && followOutput.current) history.scrollTop = history.scrollHeight }, [messages, active])
  const fail = (error: unknown) => onError(error instanceof Error ? error.message : 'The chat request failed.')
  const submit = async () => {
    const draft = text.trim()
    if (!draft || active) return
    followOutput.current = true
    try { await window.pixel.sendMessage(sessionId, draft, focus); setText('') } catch (error) { fail(error) }
  }
  const changeModel = (modelId: string, effort: string) => void window.pixel.setActiveChat({ modelId, effort }).catch(fail)
  const focusLabel = focus.reviewId ? (focus.itemId ? 'Viewing an item in a review' : 'Viewing a review') : focus.itemId ? 'Viewing an item' : null
  return <>
    <div className="chat-history" ref={historyRef} aria-live="polite" onScroll={event => { const history = event.currentTarget; followOutput.current = history.scrollHeight - history.scrollTop - history.clientHeight <= 24 }}>
      {messages.length === 0 && <div className="chat-welcome"><Icon name="spark" /><p>Ask me to look over art, leave notes, start a review, or run an edit. I can see what you have open.</p></div>}
      {messages.map(message => <div key={message.id} className={`message message-${message.role}`}>
        <span className="message-role">{roleLabel[message.role]}</span>
        {message.text && <p>{message.text}</p>}
        {message.attachments.map(attachment => <figure key={attachment.revisionId} className="chat-attachment"><span className="checker"><img src={window.pixel.mediaUrl(`thumb/${attachment.revisionId}`)} alt={attachment.label} loading="lazy" /></span><figcaption>{attachment.label}</figcaption></figure>)}
        {message.status !== 'completed' && message.status !== 'running' && <span className={`message-status message-${message.status}`}>{message.status}</span>}
      </div>)}
      {active && <div className="typing-row"><span className="typing-dots"><i /><i /><i /></span> Working on it…</div>}
    </div>
    <div className="chat-composer">
      <ChatContextMeter sessionId={sessionId} draft={text} snapshot={snapshot} />
      {focusLabel && <span className="chat-focus"><Icon name="image" /> {focusLabel}</span>}
      <textarea value={text} onChange={event => setText(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void submit() } }}
        placeholder="Ask about this art…  (Enter to send · Shift+Enter for a new line)" aria-label="Message PixelChat" rows={3} />
      <div className="composer-footer"><ChatModelPicker chat={snapshot.chat} onChange={changeModel} onSettings={onSettings} />
        {active ? <button className="stop-button" onClick={() => void window.pixel.cancelTurn(sessionId).catch(fail)}><Icon name="stop" /> Stop</button>
          : <button className="send-button" onClick={() => void submit()} disabled={!text.trim()} aria-label="Send message"><Icon name="send" /></button>}
      </div>
    </div>
  </>
}
