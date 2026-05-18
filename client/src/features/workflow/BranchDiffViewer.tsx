import { useEffect, useState } from 'react'
import { Box, Stack, Text, Group, Badge, Collapse, UnstyledButton, Loader, Alert, Anchor, Button } from '@mantine/core'
import { VscChevronDown, VscChevronRight, VscDiff, VscAdd, VscRemove } from 'react-icons/vsc'
import { TbAlertCircle, TbCheck, TbGitPullRequest, TbRefresh } from 'react-icons/tb'
import { useBranchDiff, type BranchDiffFileDto } from '../../api/projects'
import { useReviewedFiles } from '../../hooks/useReviewedFiles'

interface BranchDiffViewerProps {
  workflowId: string | undefined
}

function DiffLine({ line }: { line: string }) {
  const isAdded = line.startsWith('+') && !line.startsWith('+++')
  const isRemoved = line.startsWith('-') && !line.startsWith('---')
  const isHunk = line.startsWith('@@')

  return (
    <Box
      style={{
        fontFamily: 'monospace',
        fontSize: '0.72rem',
        lineHeight: 1.5,
        whiteSpace: 'pre',
        overflowX: 'auto',
        padding: '0 4px',
        backgroundColor: isAdded
          ? 'rgba(46, 160, 67, 0.15)'
          : isRemoved
            ? 'rgba(248, 81, 73, 0.15)'
            : isHunk
              ? 'rgba(100, 150, 255, 0.1)'
              : 'transparent',
        color: isAdded
          ? 'var(--mantine-color-green-4)'
          : isRemoved
            ? 'var(--mantine-color-red-4)'
            : isHunk
              ? 'var(--mantine-color-blue-4)'
              : 'var(--mantine-color-text)',
      }}
    >
      {line || ' '}
    </Box>
  )
}

function FileDiff({
  file,
  isReviewed,
  onMarkReviewed,
  onMarkUnreviewed,
}: {
  file: BranchDiffFileDto
  isReviewed: boolean
  onMarkReviewed: (file: BranchDiffFileDto) => void
  onMarkUnreviewed: (file: BranchDiffFileDto) => void
}) {
  const [open, setOpen] = useState(!isReviewed)

  useEffect(() => {
    setOpen(!isReviewed)
  }, [file.patch, isReviewed])

  return (
    <Box
      style={{
        border: '1px solid var(--mantine-color-dark-4)',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
      }}
    >
      <Group
        gap={4}
        wrap="nowrap"
        style={{
          width: '100%',
          backgroundColor: 'var(--mantine-color-dark-6)',
        }}
      >
        <UnstyledButton
          onClick={() => setOpen((o) => !o)}
          style={{
            flex: 1,
            minWidth: 0,
            padding: '6px 8px',
            display: 'flex',
            alignItems: 'center',
            gap: 6,
          }}
        >
          {open ? <VscChevronDown size={14} /> : <VscChevronRight size={14} />}
          <Text size="xs" fw={500} style={{ flex: 1, fontFamily: 'monospace', textAlign: 'left' }} truncate>
            {file.filename}
          </Text>
          <Group gap={4} wrap="nowrap">
            {isReviewed && (
              <Badge size="xs" color="blue" variant="light">
                Reviewed
              </Badge>
            )}
            {file.additions > 0 && (
              <Badge size="xs" color="green" variant="light" leftSection={<VscAdd size={10} />}>
                {file.additions}
              </Badge>
            )}
            {file.deletions > 0 && (
              <Badge size="xs" color="red" variant="light" leftSection={<VscRemove size={10} />}>
                {file.deletions}
              </Badge>
            )}
          </Group>
        </UnstyledButton>
        <Button
          size="compact-xs"
          variant={isReviewed ? 'subtle' : 'light'}
          color={isReviewed ? 'gray' : 'blue'}
          leftSection={isReviewed ? <TbRefresh size={12} /> : <TbCheck size={12} />}
          onClick={() => (isReviewed ? onMarkUnreviewed(file) : onMarkReviewed(file))}
          mr={6}
        >
          {isReviewed ? 'Unreview' : 'Mark reviewed'}
        </Button>
      </Group>
      <Collapse in={open}>
        <Box style={{ borderTop: '1px solid var(--mantine-color-dark-4)' }}>
          {file.patch
            .split('\n')
            .map((line, i) => (
              <DiffLine key={i} line={line} />
            ))}
        </Box>
      </Collapse>
    </Box>
  )
}

function ReviewedFilesSection({
  count,
  children,
}: {
  count: number
  children: React.ReactNode
}) {
  const [open, setOpen] = useState(true)

  return (
    <Box
      style={{
        border: '1px solid var(--mantine-color-dark-4)',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
      }}
    >
      <UnstyledButton
        onClick={() => setOpen((value) => !value)}
        style={{
          width: '100%',
          padding: '6px 8px',
          backgroundColor: 'var(--mantine-color-dark-7)',
          display: 'flex',
          alignItems: 'center',
          gap: 6,
        }}
      >
        {open ? <VscChevronDown size={14} /> : <VscChevronRight size={14} />}
        <Text size="xs" fw={600} style={{ flex: 1, textAlign: 'left' }}>
          Reviewed files
        </Text>
        <Badge size="xs" color="blue" variant="light">
          {count}
        </Badge>
      </UnstyledButton>
      <Collapse in={open}>
        <Stack gap="xs" p="xs">
          {children}
        </Stack>
      </Collapse>
    </Box>
  )
}

export function BranchDiffViewer({ workflowId }: BranchDiffViewerProps) {
  const { data, isLoading, error } = useBranchDiff(workflowId)
  const files = data?.files ?? []
  const reviewedFileState = useReviewedFiles(
    `workflow-branch-diff:${workflowId ?? 'missing'}:${data?.baseBranch ?? 'base'}:${data?.headBranch ?? 'head'}`,
    files,
  )

  if (!workflowId) return null

  if (isLoading) {
    return (
      <Stack align="center" justify="center" py="xl" gap="sm">
        <Loader size="sm" />
        <Text c="dimmed" size="sm" ta="center">
          Setting up repository...
        </Text>
        <Text c="dimmed" size="xs" ta="center" maw={240}>
          Cloning on first access may take a moment for large repositories.
        </Text>
      </Stack>
    )
  }

  if (error) {
    const status = (error as { status?: number })?.status
    const message =
      (error as { body?: { detail?: string } })?.body?.detail ??
      (error as Error)?.message ??
      'Unable to load branch diff.'

    // 503 = workspace not configured — not really an error, just not set up
    if (status === 503) {
      return (
        <Stack align="center" justify="center" py="xl" gap={4}>
          <VscDiff size={28} color="var(--mantine-color-dimmed)" />
          <Text c="dimmed" size="sm" ta="center">
            No workspace configured.
          </Text>
          <Text c="dimmed" size="xs" ta="center" maw={240}>
            Set a workspace path in Settings to enable automatic repository checkout.
          </Text>
        </Stack>
      )
    }

    // 404 = branch not yet created (workflow hasn't run any stages yet)
    if (status === 404) {
      return (
        <Stack align="center" justify="center" py="xl" gap={4}>
          <VscDiff size={28} color="var(--mantine-color-dimmed)" />
          <Text c="dimmed" size="sm" ta="center">
            Branch not yet initialized.
          </Text>
          <Text c="dimmed" size="xs" ta="center" maw={240}>
            The diff will appear once the first workflow stage has run.
          </Text>
        </Stack>
      )
    }

    return (
      <Alert color="gray" icon={<TbAlertCircle />} py="xs" mx="sm" mt="sm">
        <Text size="xs">{message}</Text>
      </Alert>
    )
  }

  if (!data || files.length === 0) {
    return (
      <Stack align="center" justify="center" py="xl">
        <VscDiff size={28} color="var(--mantine-color-dimmed)" />
        <Text c="dimmed" size="sm">
          No differences found between {data?.baseBranch ?? 'base'} and {data?.headBranch ?? 'head'}.
        </Text>
      </Stack>
    )
  }

  const totalAdditions = files.reduce((s, f) => s + f.additions, 0)
  const totalDeletions = files.reduce((s, f) => s + f.deletions, 0)

  const renderFileDiff = (file: BranchDiffFileDto, isReviewed: boolean) => (
    <FileDiff
      key={file.filename}
      file={file}
      isReviewed={isReviewed}
      onMarkReviewed={reviewedFileState.markReviewed}
      onMarkUnreviewed={reviewedFileState.markUnreviewed}
    />
  )

  return (
    <Stack gap="xs" p="xs">
      {/* Summary header */}
      <Group gap="xs" wrap="nowrap">
        <Text size="xs" c="dimmed" style={{ fontFamily: 'monospace' }}>
          {data.baseBranch}...{data.headBranch.split('/').pop()}
        </Text>
        <Group gap={4} ml="auto">
          {data.prUrl && (
            <Anchor href={data.prUrl} target="_blank" rel="noopener noreferrer" style={{ display: 'flex', alignItems: 'center' }}>
              <Badge
                size="xs"
                color={data.prState === 'merged' ? 'violet' : data.prState === 'closed' ? 'red' : 'blue'}
                variant="light"
                leftSection={<TbGitPullRequest size={10} />}
              >
                PR #{data.prNumber}
              </Badge>
            </Anchor>
          )}
          <Badge size="xs" color="green" variant="light">
            +{totalAdditions}
          </Badge>
          <Badge size="xs" color="red" variant="light">
            -{totalDeletions}
          </Badge>
          <Badge size="xs" color="gray" variant="outline">
            {files.length} file{files.length !== 1 ? 's' : ''}
          </Badge>
          <Badge size="xs" color="blue" variant="outline">
            {reviewedFileState.reviewedFiles.length} reviewed
          </Badge>
        </Group>
      </Group>

      {/* File diffs */}
      {reviewedFileState.unreviewedFiles.map((file) => renderFileDiff(file, false))}
      {reviewedFileState.reviewedFiles.length > 0 && (
        <ReviewedFilesSection count={reviewedFileState.reviewedFiles.length}>
          {reviewedFileState.reviewedFiles.map((file) => renderFileDiff(file, true))}
        </ReviewedFilesSection>
      )}
    </Stack>
  )
}
