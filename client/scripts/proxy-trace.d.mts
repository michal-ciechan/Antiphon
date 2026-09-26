export function createWsProxyTrace(options?: {
  label?: string
  log?: (line: string) => void
  appendLine?: (line: string) => void
  now?: () => Date
}): (proxy: {
  on(event: string, listener: (...args: unknown[]) => void): void
}) => void
