import { useRef, useEffect, useCallback } from 'react'

/**
 * Auto-scroll hook for ConversationTimeline.
 * - Auto-scrolls to bottom during streaming (when user is near bottom).
 * - Preserves scroll position when user has scrolled up.
 */
export function useAutoScroll(isStreaming: boolean) {
  const containerRef = useRef<HTMLDivElement>(null)
  const userScrolledUpRef = useRef(false)

  const handleScroll = useCallback(() => {
    const el = containerRef.current
    if (!el) return

    // Consider "near bottom" if within 100px of the bottom
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 100
    userScrolledUpRef.current = !nearBottom
  }, [])

  useEffect(() => {
    if (!isStreaming) return
    if (userScrolledUpRef.current) return

    const el = containerRef.current
    if (!el) return

    el.scrollTop = el.scrollHeight
  })

  const scrollToBottom = useCallback(() => {
    const el = containerRef.current
    if (!el) return
    el.scrollTop = el.scrollHeight
    userScrolledUpRef.current = false
  }, [])

  return { containerRef, handleScroll, scrollToBottom }
}
