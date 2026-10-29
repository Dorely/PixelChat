import type { PixelApi } from '../shared/types'

declare global {
  interface Window { pixel: PixelApi }
}
