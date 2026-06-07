import { Autocomplete, Mark, Stack, Switch, Text } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useEffect, useState } from 'react'
import { useDirectoryBrowse } from '../../api/filesystem'
import { matchRanges } from './matchRanges'

interface DirectoryAutocompleteProps {
  value: string
  onChange: (value: string) => void
  createIfMissing: boolean
  onCreateIfMissingChange: (value: boolean) => void
  /** Reports whether the current path is a non-existent directory. Pass a stable setter. */
  onPathMissingChange?: (missing: boolean) => void
  label?: string
}

/**
 * Working-directory picker: suggests drive roots when empty and matching child
 * directories as the user types a prefix. When the typed path doesn't exist it shows a
 * warning and a "Create this directory" toggle, and reports the missing state upward so
 * the parent can gate submission.
 */
export function DirectoryAutocomplete({
  value,
  onChange,
  createIfMissing,
  onCreateIfMissingChange,
  onPathMissingChange,
  label = 'Working directory',
}: DirectoryAutocompleteProps) {
  const [focused, setFocused] = useState(false)
  const [debounced] = useDebouncedValue(value, 200)
  const { data } = useDirectoryBrowse(debounced, focused || value.length > 0)

  // Only judge existence once typing has settled (data corresponds to the current input),
  // so a stale result doesn't flash an incorrect warning mid-keystroke.
  const settled = debounced === value
  const pathMissing = Boolean(
    data && settled && !data.isDrivesListing && !data.exists && value.trim().length > 0,
  )

  useEffect(() => {
    onPathMissingChange?.(pathMissing)
  }, [pathMissing, onPathMissingChange])

  return (
    <Stack gap="xs">
      <Autocomplete
        label={label}
        value={value}
        onChange={onChange}
        onFocus={() => setFocused(true)}
        data={data?.suggestions ?? []}
        // The server already returns normalized, fuzzy-ranked suggestions. Mantine's default
        // filter does a literal substring match of the raw input against each option, which drops
        // every suggestion when the two diverge — e.g. typing backslashes ("C:\src") against
        // forward-slash results ("C:/src"), or a fuzzy hit like "lea" → "torquay-leander". Show
        // the server's list verbatim and highlight the matched characters ourselves.
        filter={({ options }) => options}
        renderOption={({ option }) => <HighlightedPath path={option.value} query={value} />}
        placeholder="Start typing a path, e.g. C:/src"
      />
      {pathMissing && (
        <>
          <Text size="sm" c="orange">
            Directory does not exist
          </Text>
          <Switch
            label="Create this directory"
            checked={createIfMissing}
            onChange={(event) => onCreateIfMissingChange(event.currentTarget.checked)}
          />
        </>
      )}
    </Stack>
  )
}

/**
 * Renders a suggested path with the characters that matched the typed leaf segment marked. The
 * matched ranges are recomputed from the query rather than supplied by the server (see
 * {@link matchRanges}). Falls back to the plain path when nothing matches (e.g. drive roots).
 */
function HighlightedPath({ path, query }: { path: string; query: string }) {
  const ranges = matchRanges(query, path)
  if (ranges.length === 0) return <span>{path}</span>

  const parts: React.ReactNode[] = []
  let cursor = 0
  for (const [i, range] of ranges.entries()) {
    if (range.start > cursor) parts.push(path.slice(cursor, range.start))
    parts.push(<Mark key={i}>{path.slice(range.start, range.start + range.length)}</Mark>)
    cursor = range.start + range.length
  }
  if (cursor < path.length) parts.push(path.slice(cursor))
  return <span>{parts}</span>
}
