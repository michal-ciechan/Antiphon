import { useEffect, useState } from 'react'

/**
 * Browser fullscreen for the whole document rather than one element, so Mantine's portals
 * (tooltips, popovers, notifications) stay visible. Requests can be refused (an iframe without
 * allowfullscreen, e.g. Storybook); the in-app fullscreen view still works then.
 */
export function useDocumentFullscreen() {
  const [active, setActive] = useState(() => typeof document !== 'undefined' && !!document.fullscreenElement)
  useEffect(() => {
    const sync = () => setActive(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', sync)
    return () => document.removeEventListener('fullscreenchange', sync)
  }, [])
  const enter = () => {
    if (document.fullscreenElement || !document.fullscreenEnabled) return
    document.documentElement.requestFullscreen?.()?.catch(() => undefined)
  }
  const exit = () => {
    if (!document.fullscreenElement) return
    document.exitFullscreen?.()?.catch(() => undefined)
  }
  return { active, enter, exit }
}
