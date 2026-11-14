import { createContext, useContext } from 'react'
import Icon from './Icon'

/** available: a chat model is configured. open: the user's remembered choice, kept while chat is unavailable. */
export const ChatVisibility = createContext<{ available: boolean; open: boolean; toggle: () => void }>({ available: false, open: true, toggle: () => undefined })

/** Hides or shows the chat sidebar; the viewer covers the top bar, so it carries one too. Absent until a chat model is configured. */
export function ChatToggle() {
  const { available, open, toggle } = useContext(ChatVisibility)
  if (!available) return null
  const label = open ? 'Hide chat' : 'Show chat'
  return <button className="icon-button" onClick={toggle} aria-pressed={open} aria-label={label} title={label}><Icon name="panel" /></button>
}
