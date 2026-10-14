import { Group, Loader, Paper, Stack, Table, Text, Title } from '@mantine/core'
import { useComplexityChainEffective } from '../../api/complexityChains'
import { useRolePolicy, type RolePolicyRowDto } from '../../api/rolePolicy'
import type { RoutingPinDto } from '../../api/routingPins'
import { nextCreateLabel, previewNextCreate } from './nextCreatePreview'
import {
  NO_PIN_CANDIDATE,
  defaultLabel,
  escalateLabel,
  pinCandidateAvailability,
  pinCandidateLabel,
  timeoutLabel,
} from './rolePolicyModel'

/**
 * CARD-0097. Read-only RolePolicy per role beside its active stage-wide pin. Rows and their order
 * come from GET /api/role-policy (C097 V08); a failure stays inside this paper (C097 V09).
 * The next-create column reads each role's default-tier row as well (CARD-1250).
 */
export function RolePolicySection() {
  const policy = useRolePolicy()

  if (policy.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="role-policy-section">
        <SectionHeading />
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (policy.error || !policy.data) {
    return (
      <Paper withBorder p="md" data-testid="role-policy-section">
        <SectionHeading />
        <Text size="sm" c="dimmed">
          Could not load role policy.
        </Text>
      </Paper>
    )
  }

  return (
    <Paper withBorder p="md" data-testid="role-policy-section">
      <Stack gap="sm">
        <SectionHeading />
        <Text size="sm" c="dimmed">
          The configured RolePolicy for each role beside its active stage-wide pin. Read-only here;
          pins are set with scripts/routing-pin.ps1. These Settings projections assume no card, explicit provider kind, agent or
          level. Card-bound modal previews withhold the target because card pins are absent from these reads. A single composed pin target bypasses the matrix; otherwise eligible Workers use the
          default-tier row. Walks filter stage forbids, kinds, holds and quota. Single targets can be
          refused, including a Required forbid-only pin with one default-row candidate. Unknown means the reads lack a routing fact. Snapshots refresh at 15-second intervals;
          admission and standing-agent details depend on the server.
        </Text>
        <Table striped withRowBorders={false} layout="fixed">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Role</Table.Th>
              <Table.Th>Default</Table.Th>
              <Table.Th>Escalate to</Table.Th>
              <Table.Th>Timeout</Table.Th>
              <Table.Th>Stage pin</Table.Th>
              <Table.Th>Effective next create</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {policy.data.roles.map((row, index) => (
              <RolePolicyRow key={`${index}-${row.role}`} row={row} />
            ))}
          </Table.Tbody>
        </Table>
      </Stack>
    </Paper>
  )
}

function RolePolicyRow({ row }: { row: RolePolicyRowDto }) {
  const cells = useComplexityChainEffective(row.role)
  const worker = nextCreateLabel(previewNextCreate({ kind: 'Worker', explicitLevel: null, row, cells: cells.data }))
  const orchestrator = nextCreateLabel(previewNextCreate({ kind: 'Orchestrator', explicitLevel: null, row, cells: cells.data }))
  return (
    <Table.Tr data-testid={`role-policy-row-${row.role}`}>
      <Table.Td>
        <Text size="sm" fw={600}>
          {row.role}
        </Text>
      </Table.Td>
      <Table.Td>
        <Text size="sm">{defaultLabel(row)}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="sm">{escalateLabel(row)}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="sm">{timeoutLabel(row)}</Text>
      </Table.Td>
      <Table.Td data-testid={`role-policy-pin-${row.role}`}>
        {row.stagePin ? <StagePinCell pin={row.stagePin} /> : <Text size="sm">—</Text>}
      </Table.Td>
      <Table.Td data-testid={`role-policy-effective-${row.role}`}>
        <Stack gap={2}>
          <Text size="xs" c="dimmed">
            Worker
          </Text>
          <Text size="sm" data-testid={`role-policy-next-worker-${row.role}`}>
            {worker}
          </Text>
          <Text size="xs" c="dimmed">
            Orchestrator
          </Text>
          <Text size="sm" data-testid={`role-policy-next-orchestrator-${row.role}`}>
            {orchestrator}
          </Text>
        </Stack>
      </Table.Td>
    </Table.Tr>
  )
}

function StagePinCell({ pin }: { pin: RoutingPinDto }) {
  const candidates = pin.candidates ?? []
  return (
    <Stack gap={4}>
      <Text size="sm" fw={600}>
        {`${pin.strength} ${pin.provenance} pin`}
      </Text>
      {candidates.length === 0 ? (
        <Text size="xs" c="dimmed">
          {NO_PIN_CANDIDATE}
        </Text>
      ) : (
        candidates.map((candidate, index) => (
          <Stack key={index} gap={0}>
            <Text size="xs">{pinCandidateLabel(candidate, index)}</Text>
            <Text size="xs" c={candidate.availableNow ? 'teal' : 'red'}>
              {pinCandidateAvailability(candidate)}
            </Text>
          </Stack>
        ))
      )}
      <Text size="xs" c="dimmed">
        {pin.reason}
      </Text>
      <Text size="xs" c="dimmed">
        updated {pin.updatedAt}
      </Text>
    </Stack>
  )
}

function SectionHeading() {
  return (
    <Title order={5} mb={4}>
      Role policy
    </Title>
  )
}
