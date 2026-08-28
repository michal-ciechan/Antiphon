import { Text } from '@mantine/core'

/**
 * The client-side limit, shown as a count rather than enforced with `maxLength`: silently
 * truncating a paste is how a correction becomes a new mistake. The server's 422 is the backstop
 * and its message is printed verbatim, because these limits are constants that can drift.
 */
export function LimitCounter({ value, limit }: { value: number; limit: number }) {
  const over = value > limit
  return (
    <Text component="span" size="xs" c={over ? 'red' : 'dimmed'} fw={over ? 700 : undefined}>
      {value.toLocaleString()} / {limit.toLocaleString()}
    </Text>
  )
}
