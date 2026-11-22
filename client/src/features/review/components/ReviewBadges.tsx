import { Badge, Group, Stack, Text, Tooltip } from '@mantine/core'
import { TbCloudCheck, TbCloudOff, TbCloudUpload, TbDatabaseX, TbShieldLock, TbShieldOff } from 'react-icons/tb'
import type {
  FindingStatus,
  GateKind,
  ModelUsageDto,
  QueueItemStatus,
  ReviewRunStatus,
  ReviewStage,
  Severity,
  SyncState,
} from '../../../api/reviewEngine'
import {
  FINDING_STATUS_META,
  GATE_KIND_META,
  QUEUE_STATUS_META,
  RUN_STATUS_META,
  SEVERITY_META,
  STAGE_META,
  modelColor,
  modelShort,
} from '../reviewMeta'

type Size = 'xs' | 'sm' | 'md'

export function RunStatusBadge({ status, size = 'sm' }: { status: ReviewRunStatus; size?: Size }) {
  const m = RUN_STATUS_META[status]
  return (
    <Tooltip label={m.hint} withArrow openDelay={300}>
      <Badge color={m.color} variant="light" size={size} radius="sm">
        {m.label}
      </Badge>
    </Tooltip>
  )
}

export function StageText({ stage }: { stage: ReviewStage }) {
  return (
    <Text size="xs" c={`${STAGE_META[stage].color}.4`} fw={600} style={{ whiteSpace: 'nowrap' }}>
      {STAGE_META[stage].label}
    </Text>
  )
}

export function SeverityBadge({ severity, size = 'sm' }: { severity: Severity; size?: Size }) {
  const m = SEVERITY_META[severity]
  return (
    <Badge color={m.color} variant="filled" size={size} radius="sm" miw={size === 'xs' ? 52 : 64}>
      {m.label}
    </Badge>
  )
}

export function FindingStatusBadge({ status }: { status: FindingStatus }) {
  const m = FINDING_STATUS_META[status]
  return (
    <Badge size="xs" color={m.color} variant="dot">
      {m.label}
    </Badge>
  )
}

export function FlagBadges({ flags, bypassesRoundBar = false }: { flags: string[]; bypassesRoundBar?: boolean }) {
  if (flags.length === 0 && !bypassesRoundBar) return null
  return (
    <Group gap={4} wrap="nowrap">
      {flags.map((f) => (
        <Badge
          key={f}
          size="xs"
          color={f === 'security' ? 'red' : f === 'data-loss' ? 'grape' : 'gray'}
          variant="outline"
          radius="sm"
          leftSection={f === 'security' ? <TbShieldLock size={10} /> : f === 'data-loss' ? <TbDatabaseX size={10} /> : undefined}
        >
          {f}
        </Badge>
      ))}
      {bypassesRoundBar && (
        <Tooltip
          multiline
          w={300}
          label="A bypass category: it posts in every round regardless of the round's severity and confidence bar, and the adjudicator can't concede it alone."
        >
          <Badge size="xs" color="red" variant="light" leftSection={<TbShieldOff size={10} />} tt="none">
            bypasses round bar
          </Badge>
        </Tooltip>
      )}
    </Group>
  )
}

const SYNC_META: Record<Exclude<SyncState, 'NotApplicable'>, { label: string; color: string; hint: string }> = {
  Pending: { label: 'syncing', color: 'warning', hint: 'Saved in Antiphon; posting to the code host' },
  Synced: { label: 'on GitHub', color: 'success', hint: 'Posted to the code host' },
  Failed: { label: 'sync failed', color: 'danger', hint: 'Saved in Antiphon, not yet on the code host. Refresh retries.' },
}

export function SyncBadge({ state, quietWhenSynced = true }: { state: SyncState; quietWhenSynced?: boolean }) {
  if (state === 'NotApplicable' || (quietWhenSynced && state === 'Synced')) return null
  const m = SYNC_META[state]
  const Icon = state === 'Pending' ? TbCloudUpload : state === 'Failed' ? TbCloudOff : TbCloudCheck
  return (
    <Tooltip label={m.hint} withArrow>
      <Badge size="xs" color={m.color} variant="light" leftSection={<Icon size={11} />} tt="none">
        {m.label}
      </Badge>
    </Tooltip>
  )
}

export function GateKindBadge({ kind }: { kind: GateKind }) {
  const m = GATE_KIND_META[kind]
  return (
    <Badge size="xs" variant="light" color={m.color} radius="sm" tt="none">
      {m.label}
    </Badge>
  )
}

export function QueueStatusBadge({ status }: { status: QueueItemStatus }) {
  const m = QUEUE_STATUS_META[status]
  return (
    <Badge size="xs" variant="dot" color={m.color}>
      {m.label}
    </Badge>
  )
}

export function ModelBadge({ model, usage }: { model: string; usage?: ModelUsageDto }) {
  const badge = (
    <Badge size="xs" color={modelColor(model)} variant="light" radius="sm" tt="none" ff="monospace">
      {modelShort(model)}
    </Badge>
  )
  return (
    <Tooltip
      withArrow
      label={
        <Stack gap={0}>
          <Text size="xs" fw={600} ff="monospace">
            {model}
          </Text>
          {usage && (
            <Text size="xs">
              {usage.provider} · {usage.transport === 'Http' ? 'metered API' : usage.transport === 'Seat' ? 'subscription seat' : 'one-shot'}
            </Text>
          )}
        </Stack>
      }
    >
      {badge}
    </Tooltip>
  )
}

export function ModelBadges({ models, max = 4, usage }: { models: string[]; max?: number; usage?: ModelUsageDto[] }) {
  if (models.length === 0)
    return (
      <Text size="xs" c="dimmed">
        —
      </Text>
    )
  return (
    <Group gap={3} wrap="wrap">
      {models.slice(0, max).map((m) => (
        <ModelBadge key={m} model={m} usage={usage?.find((u) => u.model === m)} />
      ))}
      {models.length > max && (
        <Tooltip label={models.slice(max).join(', ')}>
          <Text size="xs" c="dimmed">
            +{models.length - max}
          </Text>
        </Tooltip>
      )}
    </Group>
  )
}
