import { useState, useMemo } from 'react'
import { Box, Group, SegmentedControl, Text, Stack, ScrollArea } from '@mantine/core'
import { diffLines, type Change } from 'diff'

type DiffViewMode = 'unified' | 'side-by-side'

interface ArtifactDiffViewerProps {
  /** Raw markdown source of the old version */
  oldContent: string
  /** Raw markdown source of the new version */
  newContent: string
  /** Label for old version (e.g., "v1") */
  oldLabel?: string
  /** Label for new version (e.g., "v2") */
  newLabel?: string
}

interface DiffLine {
  type: 'added' | 'removed' | 'unchanged'
  content: string
  oldLineNumber: number | null
  newLineNumber: number | null
}

function computeDiffLines(oldContent: string, newContent: string): DiffLine[] {
  const changes: Change[] = diffLines(oldContent, newContent)
  const lines: DiffLine[] = []
  let oldLine = 1
  let newLine = 1

  for (const change of changes) {
    const changeLines = change.value.replace(/\n$/, '').split('\n')

    for (const line of changeLines) {
      if (change.added) {
        lines.push({
          type: 'added',
          content: line,
          oldLineNumber: null,
          newLineNumber: newLine++,
        })
      } else if (change.removed) {
        lines.push({
          type: 'removed',
          content: line,
          oldLineNumber: oldLine++,
          newLineNumber: null,
        })
      } else {
        lines.push({
          type: 'unchanged',
          content: line,
          oldLineNumber: oldLine++,
          newLineNumber: newLine++,
        })
      }
    }
  }

  return lines
}

const LINE_STYLES = {
  added: {
    backgroundColor: 'rgba(46, 160, 67, 0.15)',
    color: 'var(--mantine-color-green-4)',
    prefix: '+',
  },
  removed: {
    backgroundColor: 'rgba(248, 81, 73, 0.15)',
    color: 'var(--mantine-color-red-4)',
    prefix: '-',
  },
  unchanged: {
    backgroundColor: 'transparent',
    color: 'var(--mantine-color-gray-5)',
    prefix: ' ',
  },
} as const

function UnifiedView({ lines }: { lines: DiffLine[] }) {
  return (
    <Box
      component="pre"
      style={{
        margin: 0,
        fontFamily: 'var(--mantine-font-family-monospace)',
        fontSize: '0.8rem',
        lineHeight: 1.6,
        overflow: 'visible',
      }}
    >
      {lines.map((line, idx) => {
        const style = LINE_STYLES[line.type]
        return (
          <Box
            key={idx}
            style={{
              display: 'flex',
              backgroundColor: style.backgroundColor,
              minHeight: '1.6em',
            }}
          >
            <Box
              component="span"
              style={{
                width: 48,
                textAlign: 'right',
                paddingRight: 8,
                color: 'var(--mantine-color-dark-3)',
                userSelect: 'none',
                flexShrink: 0,
              }}
            >
              {line.oldLineNumber ?? ''}
            </Box>
            <Box
              component="span"
              style={{
                width: 48,
                textAlign: 'right',
                paddingRight: 8,
                color: 'var(--mantine-color-dark-3)',
                userSelect: 'none',
                flexShrink: 0,
              }}
            >
              {line.newLineNumber ?? ''}
            </Box>
            <Box
              component="span"
              style={{
                width: 16,
                textAlign: 'center',
                color: style.color,
                fontWeight: 700,
                userSelect: 'none',
                flexShrink: 0,
              }}
            >
              {style.prefix}
            </Box>
            <Box
              component="span"
              style={{
                flex: 1,
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
                paddingLeft: 4,
                color: style.color,
              }}
            >
              {line.content}
            </Box>
          </Box>
        )
      })}
    </Box>
  )
}

function SideBySideView({ lines }: { lines: DiffLine[] }) {
  // Split into left (old) and right (new) panels
  const leftLines: DiffLine[] = []
  const rightLines: DiffLine[] = []

  let i = 0
  while (i < lines.length) {
    const line = lines[i]
    if (line.type === 'unchanged') {
      leftLines.push(line)
      rightLines.push(line)
      i++
    } else if (line.type === 'removed') {
      // Collect consecutive removed lines
      const removedStart = i
      while (i < lines.length && lines[i].type === 'removed') i++
      // Collect consecutive added lines that follow
      const addedStart = i
      while (i < lines.length && lines[i].type === 'added') i++

      const removedCount = addedStart - removedStart
      const addedCount = i - addedStart
      const maxCount = Math.max(removedCount, addedCount)

      for (let j = 0; j < maxCount; j++) {
        leftLines.push(
          j < removedCount
            ? lines[removedStart + j]
            : { type: 'unchanged', content: '', oldLineNumber: null, newLineNumber: null },
        )
        rightLines.push(
          j < addedCount
            ? lines[addedStart + j]
            : { type: 'unchanged', content: '', oldLineNumber: null, newLineNumber: null },
        )
      }
    } else if (line.type === 'added') {
      leftLines.push({
        type: 'unchanged',
        content: '',
        oldLineNumber: null,
        newLineNumber: null,
      })
      rightLines.push(line)
      i++
    }
  }

  const renderPanel = (panelLines: DiffLine[], side: 'old' | 'new') => (
    <Box
      component="pre"
      style={{
        margin: 0,
        fontFamily: 'var(--mantine-font-family-monospace)',
        fontSize: '0.8rem',
        lineHeight: 1.6,
        flex: 1,
        minWidth: 0,
        overflow: 'visible',
        borderRight: side === 'old' ? '1px solid var(--mantine-color-dark-4)' : undefined,
      }}
    >
      {panelLines.map((line, idx) => {
        const style = LINE_STYLES[line.type]
        const lineNum = side === 'old' ? line.oldLineNumber : line.newLineNumber
        return (
          <Box
            key={idx}
            style={{
              display: 'flex',
              backgroundColor: style.backgroundColor,
              minHeight: '1.6em',
            }}
          >
            <Box
              component="span"
              style={{
                width: 40,
                textAlign: 'right',
                paddingRight: 8,
                color: 'var(--mantine-color-dark-3)',
                userSelect: 'none',
                flexShrink: 0,
              }}
            >
              {lineNum ?? ''}
            </Box>
            <Box
              component="span"
              style={{
                flex: 1,
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
                paddingLeft: 4,
                color: style.color,
              }}
            >
              {line.content}
            </Box>
          </Box>
        )
      })}
    </Box>
  )

  return (
    <Box style={{ display: 'flex', minWidth: 0 }}>
      {renderPanel(leftLines, 'old')}
      {renderPanel(rightLines, 'new')}
    </Box>
  )
}

/**
 * Raw markdown source diff viewer (UX-DR18, FR36).
 * Shows raw markdown source diffs (not rendered HTML).
 * Supports unified and side-by-side views.
 * Additions in green, deletions in red.
 */
export function ArtifactDiffViewer({
  oldContent,
  newContent,
  oldLabel = 'Old',
  newLabel = 'New',
}: ArtifactDiffViewerProps) {
  const [viewMode, setViewMode] = useState<DiffViewMode>('unified')

  const diffLines = useMemo(
    () => computeDiffLines(oldContent, newContent),
    [oldContent, newContent],
  )

  const addedCount = diffLines.filter((l) => l.type === 'added').length
  const removedCount = diffLines.filter((l) => l.type === 'removed').length

  if (oldContent === newContent) {
    return (
      <Stack align="center" justify="center" p="xl">
        <Text c="dimmed" size="sm">
          No differences between versions.
        </Text>
      </Stack>
    )
  }

  return (
    <Stack gap="xs" style={{ height: '100%' }}>
      {/* Toolbar */}
      <Group justify="space-between" px="sm" pt="xs">
        <Group gap="xs">
          <Text size="xs" c="dimmed">
            {oldLabel}
          </Text>
          <Text size="xs" c="dimmed">
            vs
          </Text>
          <Text size="xs" c="dimmed">
            {newLabel}
          </Text>
          <Text size="xs" c="green">
            +{addedCount}
          </Text>
          <Text size="xs" c="red">
            -{removedCount}
          </Text>
        </Group>
        <SegmentedControl
          size="xs"
          value={viewMode}
          onChange={(v) => setViewMode(v as DiffViewMode)}
          data={[
            { label: 'Unified', value: 'unified' },
            { label: 'Side by side', value: 'side-by-side' },
          ]}
        />
      </Group>

      {/* Diff content */}
      <ScrollArea style={{ flex: 1 }} type="auto">
        <Box
          style={{
            backgroundColor: 'var(--mantine-color-dark-8)',
            borderRadius: 'var(--mantine-radius-sm)',
            padding: 'var(--mantine-spacing-xs) 0',
            minWidth: viewMode === 'side-by-side' ? 600 : undefined,
          }}
        >
          {viewMode === 'unified' ? (
            <UnifiedView lines={diffLines} />
          ) : (
            <SideBySideView lines={diffLines} />
          )}
        </Box>
      </ScrollArea>
    </Stack>
  )
}
