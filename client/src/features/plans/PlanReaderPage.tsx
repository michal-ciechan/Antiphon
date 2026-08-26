import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Divider,
  Group,
  Loader,
  Paper,
  Stack,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { TbArrowLeft, TbChevronDown, TbChevronRight } from 'react-icons/tb'
import { ApiError, getApiErrorMessage } from '../../api/client'
import { usePlanCatalog, usePlanContent, type PlanSummaryDto } from '../../api/plans'
import { useAgentTasks, useMarkAgentTaskRead } from '../../api/agentTasks'
import { splitSections } from '../agents/markdownSections'
import { SelectionComposer, SelectionDelegate } from '../agents/SelectionDelegate'
import { RenderedMarkdown } from '../../shared/RenderedMarkdown'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { HandBackButton } from '../thread/CardThreadPanel'

/**
 * The plan reader (mobile-thread spec §4, M3): `/plans` lists the repo's plan files,
 * `/plans?file=` reads one. Reading is ToC-first — the section list is the landing view and each
 * section expands in place — because a 20,000-char plan on a phone is read section-at-a-time,
 * never as one scroll. Rendering goes through the shared `RenderedMarkdown` and the section split
 * through the review view's `splitSections`: same engine, third surface, zero new renderers.
 */
export function PlanReaderPage() {
  const [params, setParams] = useSearchParams()
  const path = params.get('path')
  const file = params.get('file')
  const ref = params.get('ref')
  const task = params.get('task')

  const openFile = (relativePath: string) => {
    const next = new URLSearchParams(params)
    next.set('file', relativePath)
    setParams(next)
  }

  const closeFile = () => {
    const next = new URLSearchParams(params)
    next.delete('file')
    setParams(next)
  }

  return file === null ? (
    <PlanCatalog path={path} onOpen={openFile} />
  ) : (
    <PlanReader path={path} file={file} refName={ref} taskId={task} onBack={closeFile} />
  )
}

/**
 * The two failure shapes the server draws deliberately, kept distinct here: 422 is a refusal
 * (the path resolves outside the plan roots — the server tells a prober nothing more), 404 is a
 * genuinely missing plan or an undeployed endpoint. Collapsing them would turn "you asked for
 * something forbidden" into "try another path", which is the exact hint the 422 exists to withhold.
 */
function PlanLoadError({ error }: { error: unknown }) {
  if (error instanceof ApiError && error.status === 422) {
    return (
      <Alert color="orange" title="Refused" data-testid="plan-error-refused">
        {getApiErrorMessage(error, 'The server refused this request — the path is outside the plan directories.')}
      </Alert>
    )
  }
  if (error instanceof ApiError && error.status === 404) {
    return (
      <Alert color="yellow" title="Not found" data-testid="plan-error-notfound">
        {getApiErrorMessage(error, 'No such plan — or the plans API is not deployed here yet.')}
      </Alert>
    )
  }
  return (
    <Alert color="red" title="Plans failed to load" data-testid="plan-error">
      {getApiErrorMessage(error, 'Plans failed to load')}
    </Alert>
  )
}

function PlanMeta({ plan }: { plan: PlanSummaryDto }) {
  return (
    <Group gap={6} wrap="nowrap" style={{ flexShrink: 0 }}>
      {plan.kind === 'Proposal' && (
        <Badge size="xs" variant="light" color="grape">
          proposal
        </Badge>
      )}
      {plan.kind === 'Plan' && (
        <Badge size="xs" variant="light" color="violet">
          plan
        </Badge>
      )}
      {plan.status && (
        <Badge size="xs" variant="light" color="blue" style={{ maxWidth: 160 }}>
          {plan.status}
        </Badge>
      )}
      {plan.date && (
        <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
          {plan.date}
        </Text>
      )}
    </Group>
  )
}

function PlanCatalog({ path, onOpen }: { path: string | null; onOpen: (file: string) => void }) {
  const catalog = usePlanCatalog(path)

  if (catalog.isPending) {
    return (
      <Group p="md">
        <Loader size="xs" />
        <Text size="sm" c="dimmed">
          Loading plans…
        </Text>
      </Group>
    )
  }
  if (catalog.isError) return <PlanLoadError error={catalog.error} />

  const { rootResolved, root, plans } = catalog.data
  if (!rootResolved) {
    // Absent, not empty — "no plans" and "nobody could find the repo" are different answers.
    return (
      <Alert color="yellow" title="No plans root" data-testid="plans-no-root">
        No repo root could be resolved{path ? ` from ${path}` : ''} — the list is absent, not empty.
      </Alert>
    )
  }

  return (
    <Stack gap="sm">
      <Box>
        <Title order={3}>Plans</Title>
        <Text size="xs" c="dimmed">
          {plans.length} {plans.length === 1 ? 'file' : 'files'} under {root}
        </Text>
      </Box>
      <Paper withBorder radius="md" px="xs">
        {plans.map((plan, i) => (
          <Box key={plan.relativePath}>
            {i > 0 && <Divider />}
            <UnstyledButton
              w="100%"
              py={10}
              px={4}
              onClick={() => onOpen(plan.relativePath)}
              data-testid={`plan-row-${plan.fileName}`}
            >
              <Group wrap="nowrap" gap={8} align="flex-start">
                <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5, marginTop: 3 }} />
                <Box style={{ minWidth: 0, flex: 1 }}>
                  <Group wrap="nowrap" gap={8}>
                    {plan.cards.length > 0 && (
                      <Text fw={700} size="sm" style={{ flexShrink: 0 }}>
                        {plan.cards.map(displayIdentifier).join(' ')}
                      </Text>
                    )}
                    <Text size="sm" truncate style={{ flex: 1 }}>
                      {plan.title}
                    </Text>
                  </Group>
                  <Text size="xs" c="dimmed" truncate>
                    {plan.relativePath}
                  </Text>
                </Box>
                <PlanMeta plan={plan} />
              </Group>
            </UnstyledButton>
          </Box>
        ))}
        {plans.length === 0 && (
          <Text size="sm" c="dimmed" p="sm">
            No plan files found.
          </Text>
        )}
      </Paper>
    </Stack>
  )
}

function PlanReader({
  path,
  file,
  refName,
  taskId,
  onBack,
}: {
  path: string | null
  file: string
  refName: string | null
  taskId: string | null
  onBack: () => void
}) {
  const content = usePlanContent(path, file, refName)
  const catalog = usePlanCatalog(path)
  const tasks = useAgentTasks()
  const markRead = useMarkAgentTaskRead()
  const stampedTask = useRef<string | null>(null)
  const [selection, setSelection] = useState<string | null>(null)
  // Open section keys. Everything starts collapsed: the ToC IS the landing view.
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())

  const sections = useMemo(
    () => (content.data ? splitSections(content.data.content) : []),
    [content.data],
  )

  const task = taskId ? (tasks.data ?? []).find((candidate) => candidate.id === taskId) : undefined
  const goalContext = taskId
    ? `Re ${content.data?.plan.cards[0] ?? 'this plan'} (task ${taskId.slice(0, 8)}):`
    : undefined

  useEffect(() => {
    if (!taskId || stampedTask.current === taskId) return
    stampedTask.current = taskId
    markRead.mutate(taskId)
  }, [taskId, markRead])

  if (content.isPending) {
    return (
      <Group p="md">
        <Loader size="xs" />
        <Text size="sm" c="dimmed">
          Loading plan…
        </Text>
      </Group>
    )
  }
  if (content.isError) {
    return (
      <Stack gap="sm">
        <BackButton onBack={onBack} />
        <PlanLoadError error={content.error} />
      </Stack>
    )
  }

  const plan = content.data.plan
  const toggle = (key: string) =>
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <Box>
      <Box
        pb="xs"
        mb="xs"
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 2,
          background: 'var(--mantine-color-body)',
          borderBottom: '1px solid var(--mantine-color-default-border)',
        }}
        data-testid="plan-header"
      >
        <Group gap={8} wrap="nowrap">
          <BackButton onBack={onBack} />
          {plan.cards.length > 0 && (
            <Text fw={700} style={{ flexShrink: 0 }}>
              {plan.cards.map(displayIdentifier).join(' ')}
            </Text>
          )}
          <Text size="sm" truncate style={{ flex: 1 }}>
            {plan.title}
          </Text>
          {plan.status && (
            <Badge color="blue" variant="light" size="sm" style={{ maxWidth: 180 }}>
              {plan.status}
            </Badge>
          )}
          {plan.cards[0] && (
            <HandBackButton
              identifier={plan.cards[0]}
              context={`plan ${plan.relativePath}`}
              directory={catalog.data?.root ?? path ?? undefined}
            />
          )}
        </Group>
        <Text size="xs" c="dimmed" truncate>
          {plan.relativePath}
          {plan.date ? ` · ${plan.date}` : ''}
        </Text>
      </Box>
      <SelectionDelegate onCompose={setSelection}>
        <Paper withBorder radius="md" px="xs">
          {sections.map((section, i) => {
          const isOpen = open.has(section.key)
          return (
            <Box key={section.key}>
              {i > 0 && <Divider />}
              <UnstyledButton
                w="100%"
                py={10}
                px={4}
                onClick={() => toggle(section.key)}
                aria-expanded={isOpen}
                aria-label={`${isOpen ? 'Collapse' : 'Expand'} section ${section.key}`}
                style={{ paddingLeft: Math.max(0, section.level - 1) * 12 }}
              >
                <Group wrap="nowrap" gap={8}>
                  {isOpen ? (
                    <TbChevronDown size={14} style={{ flexShrink: 0 }} />
                  ) : (
                    <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
                  )}
                  <Text size="sm" fw={600} truncate>
                    {section.heading || '(intro)'}
                  </Text>
                </Group>
              </UnstyledButton>
              {isOpen && (
                <Box px="sm" pb="sm" data-testid={`plan-section-body-${section.key}`}>
                  <RenderedMarkdown>{section.content}</RenderedMarkdown>
                </Box>
              )}
            </Box>
          )
          })}
          {sections.length === 0 && (
            <Text size="sm" c="dimmed" p="sm">
              This plan is empty.
            </Text>
          )}
        </Paper>
      </SelectionDelegate>
      {selection && (
        <Box mt="sm">
          <SelectionComposer
            filePath={file}
            workingDirectory={catalog.data?.root ?? path ?? ''}
            selection={selection}
            defaultRole={task?.role === 'Plan' ? 'Plan' : 'Docs'}
            goalContext={goalContext}
            onClose={() => setSelection(null)}
          />
        </Box>
      )}
    </Box>
  )
}

function BackButton({ onBack }: { onBack: () => void }) {
  return (
    <ActionIcon variant="subtle" color="gray" aria-label="Back to plans" onClick={onBack}>
      <TbArrowLeft size={16} />
    </ActionIcon>
  )
}
