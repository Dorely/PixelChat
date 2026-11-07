import { createContext, useContext } from 'react'
import Icon from './Icon'

export const ChatVisibility = createContext<{ open: boolean; toggle: () => void }>({ open: true, toggle: () => undefined })

/** Hides or shows the chat sidebar; the viewer covers the top bar, so it carries one too. */
export function ChatToggle() {
  const { open, toggle } = useContext(ChatVisibility)
  const label = open ? 'Hide chat' : 'Show chat'
  return <button className="icon-button" onClick={toggle} aria-pressed={open} aria-label={label} title={label}><Icon name="panel" /></button>
}
