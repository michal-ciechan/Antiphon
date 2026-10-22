import { useCallback, useEffect, useRef } from 'react'
import type { MouseEvent as ReactMouseEvent } from 'react'

export interface UseLongPressOptions {
  /** Milliseconds the press must be held before `onLongPress` fires. Default 500. */
  threshold?: number
  /** Called at the start of every press, mouse or touch, before the primary-button gate. */
  onPressStart?: () => void
}

export interface LongPressHandlers {
  onMouseDown: (event: ReactMouseEvent) => void
  onMouseUp: () => void
  onMouseLeave: () => void
  onTouchStart: () => void
  onTouchMove: () => void
  onTouchEnd: () => void
  onTouchCancel: () => void
}

/**
 * A press-and-hold gesture for the primary mouse button or a touch (CARD-0105).
 *
 * Replaces `useLongPress` from `@mantine/hooks` 8.3.17, which starts its timer on any mouse button
 * and ends it only on mouseup, mouseleave and touchend: a held right button (no click follows) or
 * a touch ended by scrolling or `touchcancel` fired the callback with nothing left to consume the
 * result (Review 74b6b98b D-1). This hook starts only for `button === 0` or a touch, cancels on
 * release, leave, touchmove, touchcancel and unmount, and exposes `cancel` for events the element
 * handles itself, such as `contextmenu`. No new dependency (plan D-7 fallback).
 */
export function useLongPress(onLongPress: () => void, { threshold = 500, onPressStart }: UseLongPressOptions = {}) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const cancel = useCallback(() => {
    if (timer.current !== null) {
      clearTimeout(timer.current)
      timer.current = null
    }
  }, [])

  const start = useCallback(() => {
    cancel()
    timer.current = setTimeout(() => {
      timer.current = null
      onLongPress()
    }, threshold)
  }, [cancel, onLongPress, threshold])

  // A press still held when the element unmounts must not fire into a component that is gone.
  useEffect(() => cancel, [cancel])

  const handlers: LongPressHandlers = {
    onMouseDown: (event) => {
      onPressStart?.()
      // Only the primary button is a press; a secondary button is the browser's own context-menu
      // gesture and releasing it produces no click.
      if (event.button === 0) start()
    },
    onMouseUp: cancel,
    onMouseLeave: cancel,
    onTouchStart: () => {
      onPressStart?.()
      start()
    },
    onTouchMove: cancel,
    onTouchEnd: cancel,
    onTouchCancel: cancel,
  }

  return { handlers, cancel }
}
