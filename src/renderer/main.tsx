import React from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './themes.css'
import './styles.css'
import { connectBrowserApi } from './browser-api'
import { applyTheme, storedTheme } from './theme'

if (!window.pixel) window.pixel = connectBrowserApi()
applyTheme(storedTheme())

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
