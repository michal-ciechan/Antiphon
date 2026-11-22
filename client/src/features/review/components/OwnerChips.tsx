import { Badge, Group, Text, Tooltip } from '@mantine/core'
import { TbUser, TbUsers } from 'react-icons/tb'
import type { CodeOwnerDto } from '../../../api/reviewEngine'
import { ownerLabel } from '../reviewFormat'

/** One code owner: a team or a user, with its icon. */
export function OwnerChip({ owner, size = 'xs' }: { owner: CodeOwnerDto; size?: 'xs' | 'sm' }) {
  const team = owner.kind === 'Team'
  return (
    <Tooltip label={team ? `Team ${owner.name} owns changed paths` : `@${owner.name} owns changed paths`} openDelay={300}>
      <Badge
        size={size}
        variant="outline"
        color={team ? 'indigo' : 'teal'}
        radius="sm"
        tt="none"
        leftSection={team ? <TbUsers size={10} aria-label="team" /> : <TbUser size={10} aria-label="user" />}
      >
        {ownerLabel(owner)}
      </Badge>
    </Tooltip>
  )
}

/** The code owners of the changed paths (`subject.owners`), or "none resolved". */
export function OwnerChips({ owners, size = 'xs', max }: { owners: CodeOwnerDto[]; size?: 'xs' | 'sm'; max?: number }) {
  if (owners.length === 0)
    return (
      <Text size="xs" c="dimmed">
        none resolved
      </Text>
    )
  const shown = max === undefined ? owners : owners.slice(0, max)
  const rest = owners.length - shown.length
  return (
    <Group gap={4} wrap="wrap">
      {shown.map((o) => (
        <OwnerChip key={`${o.kind}:${o.name}`} owner={o} size={size} />
      ))}
      {rest > 0 && (
        <Tooltip label={owners.slice(shown.length).map(ownerLabel).join(', ')}>
          <Text size="xs" c="dimmed">
            +{rest}
          </Text>
        </Tooltip>
      )}
    </Group>
  )
}
