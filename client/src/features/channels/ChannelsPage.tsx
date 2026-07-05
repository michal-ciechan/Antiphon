import {
  Badge,
  Group,
  Paper,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbBrandTelegram, TbBrandWhatsapp, TbBrandDiscord, TbMessageCircle } from 'react-icons/tb'
import { useChannels, useUpdateChannel, type ChatChannelDto } from '../../api/channels'
import { useAgentList } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'

const PROVIDER_META: Record<string, { icon: typeof TbMessageCircle; color: string; label: string }> = {
  telegram: { icon: TbBrandTelegram, color: 'blue', label: 'Telegram' },
  whatsapp: { icon: TbBrandWhatsapp, color: 'green', label: 'WhatsApp' },
  discord: { icon: TbBrandDiscord, color: 'violet', label: 'Discord' },
}

function ProviderBadge({ provider }: { provider: string }) {
  const meta = PROVIDER_META[provider] ?? { icon: TbMessageCircle, color: 'gray', label: provider }
  return (
    <Badge leftSection={<meta.icon size={13} />} color={meta.color} variant="light">
      {meta.label}
    </Badge>
  )
}

function relativeTime(iso: string | null): string {
  if (!iso) return '—'
  const deltaMs = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(deltaMs / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

function ChannelRow({ channel }: { channel: ChatChannelDto }) {
  const { data: agents } = useAgentList()
  const update = useUpdateChannel()

  const agentOptions = (agents ?? []).map((a) => ({ value: a.id, label: a.name }))

  const onAgentChange = (agentId: string | null) => {
    update.mutate(
      { id: channel.id, request: agentId ? { agentId } : { unbindAgent: true } },
      {
        onError: (e) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(e, 'Failed to update channel') }),
      },
    )
  }

  const onEnabledChange = (enabled: boolean) => {
    update.mutate(
      { id: channel.id, request: { enabled } },
      {
        onError: (e) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(e, 'Failed to update channel') }),
      },
    )
  }

  return (
    <Table.Tr>
      <Table.Td>
        <ProviderBadge provider={channel.provider} />
      </Table.Td>
      <Table.Td>
        <Stack gap={2}>
          <Text size="sm" fw={500}>
            {channel.title ?? channel.externalId}
          </Text>
          <Text size="xs" c="dimmed">
            {channel.kind}
            {channel.title ? ` · ${channel.externalId}` : ''}
          </Text>
        </Stack>
      </Table.Td>
      <Table.Td>
        <Stack gap={2} maw={340}>
          <Text size="sm" lineClamp={1}>
            {channel.lastMessagePreview ?? '—'}
          </Text>
          <Text size="xs" c="dimmed">
            {channel.lastAuthor ? `${channel.lastAuthor} · ` : ''}
            {relativeTime(channel.lastMessageAt)} · {channel.messageCount} msg
            {channel.messageCount === 1 ? '' : 's'}
          </Text>
        </Stack>
      </Table.Td>
      <Table.Td>
        <Select
          placeholder="No agent"
          data={agentOptions}
          value={channel.agentId}
          onChange={onAgentChange}
          clearable
          searchable
          size="xs"
          w={200}
          disabled={update.isPending}
        />
      </Table.Td>
      <Table.Td>
        <Tooltip label={channel.enabled ? 'Routing on' : 'Routing paused'} withArrow>
          <Switch
            checked={channel.enabled}
            onChange={(e) => onEnabledChange(e.currentTarget.checked)}
            disabled={update.isPending}
            size="sm"
          />
        </Tooltip>
      </Table.Td>
    </Table.Tr>
  )
}

export function ChannelsPage() {
  const { data: channels, isLoading } = useChannels()

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Channels</Title>
      </Group>
      <Text c="dimmed" size="sm">
        External conversations discovered from connected providers. Bind a channel to an agent to route
        its messages into that agent's session — the agent's answers flow back down the channel.
      </Text>

      <Paper withBorder radius="md" p={0}>
        <Table striped highlightOnHover verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={130}>Provider</Table.Th>
              <Table.Th>Conversation</Table.Th>
              <Table.Th>Last message</Table.Th>
              <Table.Th w={220}>Agent</Table.Th>
              <Table.Th w={90}>Enabled</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(channels ?? []).map((c) => (
              <ChannelRow key={c.id} channel={c} />
            ))}
            {!isLoading && (channels ?? []).length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={5}>
                  <Text c="dimmed" ta="center" py="lg" size="sm">
                    No channels yet — they appear automatically when a connected provider (e.g. the
                    Telegram bot) receives its first message.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Paper>
    </Stack>
  )
}
