import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { Revision } from '../shared/types'

/** Draw the composited frame and report its index together, including after scrubbing. */
export function useAnimation(revision: Revision | null, onError: (message: string) => void) {
  const video = revision?.format === 'mp4' || revision?.format === 'webm'
  const enabled = !!revision && (video || revision.frames > 1)
  const [canvas, setCanvas] = useState<HTMLCanvasElement | null>(null)
  const [decoder, setDecoder] = useState<ImageDecoder | null>(null)
  const [requested, setRequested] = useState(0)
  const [shown, setShown] = useState<{ index: number; delay: number } | null>(null)
  const [playing, setPlaying] = useState(true)
  const deadline = useRef(0)
  const errorRef = useRef(onError)
  errorRef.current = onError
  const url = enabled ? window.pixel.mediaUrl(`${video ? 'playback' : 'original'}/${revision.id}`) : ''
  const type = video ? 'image/webp' : `image/${revision?.format}`
  useLayoutEffect(() => {
    deadline.current = 0
    setDecoder(null); setRequested(0); setShown(null); setPlaying(true)
    if (!url) return
    const abort = new AbortController()
    let source: ImageDecoder | undefined
    void (async () => {
      const response = await fetch(url, { signal: abort.signal })
      if (!response.ok) throw new Error('Could not load the animation.')
      const data = await response.arrayBuffer()
      if (abort.signal.aborted) return
      source = new ImageDecoder({ type, data, preferAnimation: true })
      await source.tracks.ready
      if (!abort.signal.aborted) setDecoder(source)
    })().catch(() => { if (!abort.signal.aborted) errorRef.current('Could not open the animation for frame review.') })
    return () => { abort.abort(); source?.close() }
  }, [url, type])

  useLayoutEffect(() => {
    if (!canvas || !decoder) return
    let cancelled = false
    void decoder.decode({ frameIndex: requested }).then(({ image }) => {
      try {
        if (cancelled) return
        canvas.width = image.displayWidth; canvas.height = image.displayHeight
        canvas.getContext('2d')!.drawImage(image, 0, 0)
        setShown({ index: requested, delay: Math.max(10, (image.duration ?? 100000) / 1000) })
      } finally { image.close() }
    }).catch(() => { if (!cancelled) { setPlaying(false); errorRef.current('Could not decode this frame.') } })
    return () => { cancelled = true }
  }, [canvas, decoder, requested])
  const count = revision?.frames ?? 1
  useEffect(() => {
    if (!playing || !shown || count < 2) return
    const now = performance.now()
    deadline.current = Math.max(now - shown.delay, deadline.current || now) + shown.delay
    const timer = window.setTimeout(() => setRequested((shown.index + 1) % count), deadline.current - now)
    return () => clearTimeout(timer)
  }, [playing, shown, count])
  const seek = (index: number) => { deadline.current = 0; setPlaying(false); setRequested(index) }
  const pause = () => { if (shown) seek(shown.index) }
  return { enabled, setCanvas, frame: shown?.index, playing, play: () => setPlaying(true), pause, seek }
}
