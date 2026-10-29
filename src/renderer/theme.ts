/** Renderer-only appearance preference. It holds no secrets, so per-browser storage is enough. */
export const THEMES = [
  { id: 'studio', label: 'Studio', note: 'Graphite with lime', swatch: ['#151716', '#1c1f1d', '#c4f264'] },
  { id: 'midnight', label: 'Midnight', note: 'Deep navy with blue', swatch: ['#10131a', '#171c27', '#8ab4ff'] },
  { id: 'ember', label: 'Ember', note: 'Warm umber with amber', swatch: ['#16120f', '#1f1915', '#ffb454'] },
  { id: 'daylight', label: 'Daylight', note: 'Light paper with green', swatch: ['#f3f2ec', '#fbfaf6', '#4a7612'] },
] as const

export type ThemeId = typeof THEMES[number]['id']
const KEY = 'pixelchat.theme'

export function storedTheme(): ThemeId {
  try { const value = localStorage.getItem(KEY); if (THEMES.some(theme => theme.id === value)) return value as ThemeId } catch { /* storage unavailable */ }
  return 'studio'
}

export function applyTheme(id: ThemeId) {
  document.documentElement.dataset.theme = id
  const meta = document.querySelector('meta[name="theme-color"]')
  meta?.setAttribute('content', THEMES.find(theme => theme.id === id)!.swatch[0])
  try { localStorage.setItem(KEY, id) } catch { /* storage unavailable */ }
}
