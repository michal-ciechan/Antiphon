import {
  ActionIcon,
  Badge,
  Box,
  Group,
  SegmentedControl,
  Stack,
  Text,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState } from 'react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { TbCheck, TbChevronDown, TbChevronRight, TbMessagePlus } from 'react-icons/tb'
import {
  useMarkSectionReviews,
  useSectionReviews,
  type ReviewThreadDto,
} from '../../api/review'
import { getApiErrorMessage } from '../../api/client'
import {
  diffBlocks,
  diffSections,
  sectionLineRange,
  splitSections,
  subtreeEnd,
  type MarkdownSection,
  type SectionDiffEntry,
} from './markdownSections'

type ViewMode = 'clean' | 'inline' | 'side'

type SectionReviewState = 'none' | 'fresh' | 'stale'

const TINT: Record<'added' | 'removed' | 'changed', string> = {
  added: 'var(--mantine-color-green-light)',
  removed: 'var(--mantine-color-red-light)',
  changed: 'var(--mantine-color-yellow-light)',
}

/**
 * The sectioned rendered markdown view (feature 009): heading-delimited sections with
 * hash-anchored "reviewed" marks (stale on change), hierarchical collapse (reviewed-and-unchanged
 * subtrees fold away automatically), rendered diff vs the baseline in inline or side-by-side
 * form, and per-section comments that anchor ordinary line threads at the section heading.
 */
export function RenderedMarkdownReview({
  agentId,
  path,
  workText,
  baseText,
  threads,
  readOnly = false,
  onCommentAtLine,
}: {
  agentId: string
  path: string
  workText: string
  /** Baseline content when the file differs from it; null disables the diff modes. */
  baseText: string | null
  threads: ReviewThreadDto[]
  /** Context-only files (All-files browsing) are readable but not markable. */
  readOnly?: boolean
  onCommentAtLine: (line: number) => void
}) {
  const sections = useMemo(() => splitSections(workText), [workText])
  const reviews = useSectionReviews(agentId, path)
  const mark = useMarkSectionReviews(agentId, path)
  const [viewMode, setViewMode] = useState<ViewMode>('clean')
  // Manual collapse overrides; unset keys follow the automatic rule (reviewed+fresh folds).
  const [manual, setManual] = useState<Map<string, boolean>>(new Map())

  const storedByKey = useMemo(
    () => new Map((reviews.data ?? []).map((r) => [r.key, r.contentHash])),
    [reviews.data],
  )

  const stateOf = (section: MarkdownSection): SectionReviewState => {
    const stored = storedByKey.get(section.key)
    if (stored === undefined) return 'none'
    return stored === section.hash ? 'fresh' : 'stale'
  }

  /** A subtree auto-collapses only when every section in it is reviewed and unchanged. */
  const subtreeFresh = (index: number): boolean => {
    const end = subtreeEnd(sections, index)
    for (let i = index; i < end; i++) if (stateOf(sections[i]) !== 'fresh') return false
    return true
  }

  const isCollapsed = (index: number): boolean =>
    manual.get(sections[index].key) ?? subtreeFresh(index)

  const toggle = (key: string, collapsed: boolean) =>
    setManual((prev) => new Map(prev).set(key, collapsed))

  const threadCount = (index: number): number => {
    const range = sectionLineRange(sections, index)
    return threads.filter((t) => t.line >= range.start && t.line <= range.end).length
  }

  /** Mark (or clear) a section and everything under it — "this chapter is reviewed". */
  const markSubtree = (index: number, reviewed: boolean) => {
    const end = subtreeEnd(sections, index)
    const batch = sections
      .slice(index, end)
      .map((s) => ({ key: s.key, contentHash: reviewed ? s.hash : null }))
    mark.mutate(batch, {
      onError: (error) =>
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Marking failed') }),
    })
  }

  const diffAvailable = baseText !== null && baseText !== workText
  const entries = useMemo(
    () => (diffAvailable && viewMode !== 'clean' ? diffSections(splitSections(baseText!), sections) : null),
    [diffAvailable, viewMode, baseText, sections],
  )

  const indexByKey = useMemo(() => new Map(sections.map((s, i) => [s.key, i])), [sections])

  const controls = (index: number) => {
    const section = sections[index]
    const state = stateOf(section)
    const count = threadCount(index)
    return (
      <Group gap={2} wrap="nowrap" style={{ flexShrink: 0 }}>
        {state === 'stale' && (
          <Badge size="xs" variant="light" color="orange" data-testid={`stale-${section.key}`}>
            changed since review
          </Badge>
        )}
        {count > 0 && (
          <Badge size="xs" variant="light" color="orange">
            {count}💬
          </Badge>
        )}
        {!readOnly && (
          <Tooltip
            label={
              state === 'fresh'
                ? 'Reviewed — click to clear (applies to this section and its subsections)'
                : 'Mark this section and its subsections reviewed'
            }
            withArrow
            openDelay={300}
          >
            <ActionIcon
              size="sm"
              variant={state === 'fresh' ? 'light' : 'subtle'}
              color={state === 'fresh' ? 'green' : 'gray'}
              aria-label={`Mark section ${section.key} reviewed`}
              loading={mark.isPending}
              onClick={() => markSubtree(index, state !== 'fresh')}
            >
              <TbCheck size={14} />
            </ActionIcon>
          </Tooltip>
        )}
        <Tooltip label="Comment on this section" withArrow openDelay={300}>
          <ActionIcon
            size="sm"
            variant="subtle"
            color="gray"
            aria-label={`Comment on section ${section.key}`}
            onClick={() => onCommentAtLine(section.startLine)}
          >
            <TbMessagePlus size={14} />
          </ActionIcon>
        </Tooltip>
      </Group>
    )
  }

  const sectionRow = (index: number, collapsed: boolean) => {
    const section = sections[index]
    const hiddenCount = subtreeEnd(sections, index) - index
    return (
      <Box key={section.key} data-testid={`section-${section.key}`}>
        <Group gap={4} wrap="nowrap" align="flex-start">
          <ActionIcon
            size="sm"
            variant="subtle"
            color="gray"
            mt={2}
            aria-label={`${collapsed ? 'Expand' : 'Collapse'} section ${section.key}`}
            onClick={() => toggle(section.key, !collapsed)}
          >
            {collapsed ? <TbChevronRight size={14} /> : <TbChevronDown size={14} />}
          </ActionIcon>
          <Box style={{ flexGrow: 1, minWidth: 0 }}>
            {collapsed ? (
              <Group gap="xs" wrap="nowrap">
                <Text fw={600} size="sm" truncate>
                  {section.heading || '(intro)'}
                </Text>
                <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
                  {stateOf(section) === 'fresh' ? 'reviewed · ' : ''}
                  {hiddenCount === 1 ? 'collapsed' : `${hiddenCount} sections collapsed`}
                </Text>
                <Box style={{ flexGrow: 1 }} />
                {controls(index)}
              </Group>
            ) : (
              <Group gap={4} wrap="nowrap" align="flex-start">
                <Box style={{ flexGrow: 1, minWidth: 0 }} data-testid={`section-body-${section.key}`}>
                  <Markdown remarkPlugins={[remarkGfm]}>{section.content}</Markdown>
                </Box>
                {controls(index)}
              </Group>
            )}
          </Box>
        </Group>
      </Box>
    )
  }

  const cleanRows = () => {
    const rows = []
    let i = 0
    while (i < sections.length) {
      const collapsed = isCollapsed(i)
      rows.push(sectionRow(i, collapsed))
      i = collapsed ? subtreeEnd(sections, i) : i + 1
    }
    return rows
  }

  const removedBlock = (section: MarkdownSection) => (
    <Box
      key={`removed-${section.key}`}
      p="xs"
      data-testid={`removed-${section.key}`}
      style={{
        background: TINT.removed,
        borderLeft: '3px solid var(--mantine-color-red-6)',
        borderRadius: 4,
        opacity: 0.75,
        textDecoration: 'line-through',
      }}
    >
      <Markdown remarkPlugins={[remarkGfm]}>{section.content}</Markdown>
    </Box>
  )

  /** A changed section rendered block-by-block: removed blocks red, new blocks green. */
  const changedInline = (entry: SectionDiffEntry) => {
    const blocks = diffBlocks(entry.base!.content, entry.work!.content)
    const index = indexByKey.get(entry.key)!
    return (
      <Box
        key={entry.key}
        p="xs"
        data-testid={`changed-${entry.key}`}
        style={{ borderLeft: '3px solid var(--mantine-color-yellow-6)', borderRadius: 4 }}
      >
        <Group justify="flex-end">{controls(index)}</Group>
        {blocks.map((block, i) => (
          <Box
            key={i}
            style={
              block.status === 'unchanged'
                ? undefined
                : {
                    background: TINT[block.status],
                    borderRadius: 4,
                    padding: '0 8px',
                    textDecoration: block.status === 'removed' ? 'line-through' : undefined,
                    opacity: block.status === 'removed' ? 0.75 : 1,
                  }
            }
          >
            <Markdown remarkPlugins={[remarkGfm]}>{block.text}</Markdown>
          </Box>
        ))}
      </Box>
    )
  }

  const inlineRows = () =>
    entries!.map((entry) => {
      if (entry.status === 'removed') return removedBlock(entry.base!)
      if (entry.status === 'changed') return changedInline(entry)
      const index = indexByKey.get(entry.key)!
      if (entry.status === 'added') {
        return (
          <Box
            key={entry.key}
            data-testid={`added-${entry.key}`}
            style={{ borderLeft: '3px solid var(--mantine-color-green-6)', borderRadius: 4 }}
          >
            {sectionRow(index, false)}
          </Box>
        )
      }
      // Unchanged sections stay collapsible — the diff view is for reading what changed.
      return sectionRow(index, manual.get(entry.key) ?? stateOf(sections[index]) === 'fresh')
    })

  const sideRows = () =>
    entries!.map((entry) => {
      const leftBlocks =
        entry.status === 'changed'
          ? diffBlocks(entry.base!.content, entry.work!.content).filter((b) => b.status !== 'added')
          : null
      const rightBlocks =
        entry.status === 'changed'
          ? diffBlocks(entry.base!.content, entry.work!.content).filter((b) => b.status !== 'removed')
          : null
      const cell = (
        section: MarkdownSection | null,
        blocks: typeof leftBlocks,
        tint: 'added' | 'removed' | null,
      ) => (
        <Box
          p="xs"
          style={{
            background: tint ? TINT[tint] : undefined,
            borderRadius: 4,
            minWidth: 0,
          }}
        >
          {blocks
            ? blocks.map((block, i) => (
                <Box
                  key={i}
                  style={
                    block.status === 'unchanged'
                      ? undefined
                      : { background: TINT[block.status], borderRadius: 4, padding: '0 8px' }
                  }
                >
                  <Markdown remarkPlugins={[remarkGfm]}>{block.text}</Markdown>
                </Box>
              ))
            : section && <Markdown remarkPlugins={[remarkGfm]}>{section.content}</Markdown>}
        </Box>
      )
      const workIndex = entry.work ? indexByKey.get(entry.key) : undefined
      return (
        <Box key={entry.key} data-testid={`side-${entry.key}`}>
          {entry.work && workIndex !== undefined && (
            <Group justify="flex-end">{controls(workIndex)}</Group>
          )}
          <Box style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
            {cell(entry.base, leftBlocks, entry.status === 'removed' ? 'removed' : null)}
            {cell(entry.work, rightBlocks, entry.status === 'added' ? 'added' : null)}
          </Box>
        </Box>
      )
    })

  return (
    <Stack gap="xs">
      {diffAvailable && (
        <Group justify="flex-end">
          <SegmentedControl
            size="xs"
            value={viewMode}
            onChange={(value) => setViewMode(value as ViewMode)}
            data={[
              { label: 'Clean', value: 'clean' },
              { label: 'Inline diff', value: 'inline' },
              { label: 'Side by side', value: 'side' },
            ]}
          />
        </Group>
      )}
      {viewMode === 'clean' || !entries ? (
        <Stack gap={4}>{cleanRows()}</Stack>
      ) : viewMode === 'inline' ? (
        <Stack gap={4}>{inlineRows()}</Stack>
      ) : (
        <Stack gap={8}>{sideRows()}</Stack>
      )}
    </Stack>
  )
}
