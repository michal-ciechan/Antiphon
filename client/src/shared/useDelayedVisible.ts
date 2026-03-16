import { useState, useEffect } from 'react'

/**
 * Returns false initially, then true after the specified delay (ms).
 * Used to implement the 200ms invisible delay before showing skeletons.
 */
export function useDelayedVisible(delayMs: number): boolean {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const timer = setTimeout(() => {
      setVisible(true)
    }, delayMs)

    return () => {
      clearTimeout(timer)
    }
  }, [delayMs])

  return visible
}
