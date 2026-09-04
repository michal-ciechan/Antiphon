import {
  Alert,
  Badge,
  Box,
  Button,
  Group,
  Loader,
  Paper,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import type { AgentTaskRole } from '../../api/agentTasks'
import { getApiErrorMessage } from '../../api/client'
import {
  useComplexityChainEffective,
  useComplexityChains,
  type ComplexityCandidateDto,
  type ComplexityChainDto,
  type TaskComplexity,
} from '../../api/complexityChains'
import {
  useClearModelAvailabilityHold,
  useModelAvailability,
  type ModelAvailabilityHoldDto,
} from '../../api/modelAvailability'
import { useRoutingPins, type RoutingPinDto } from '../../api/routingPins'
import {
  useSubscriptionUsage,
  type SubscriptionUsageObservationDto,
} from '../../api/subscriptionUsage'
import { ModelAvailabilityHoldForm } from '../orchestrator/ModelAvailabilityHoldForm'
import {
  CARD_PIN_SCOPE,
  COMPLEXITY_ROUTING_BOUNDARY,
  COMPLEXITY_ROUTING_BOUNDARY_TITLE,
  NON_COMPLEXITY_BOUNDARY,
  USAGE_UNKNOWN,
  anyRoleChains,
  candidateAvailabilityLabel,
  candidateLabel,
  cardPinCopy,
  cellByComplexity,
  complexitiesOf,
  effectiveResolvedFrom,
  groupPins,
  pinEffectCopy,
  resolvedFromLabel,
  stagePinsForRole,
  untilLabel,
} from './routingSettingsModel'

/**
 * Settings Routing tab. Each section owns its queries so SettingsPage
 * `keepMounted={false}` starts them only while this panel is open, and a failure
 * in one section cannot blank the others.
 */
export function RoutingSettingsTab() {
  return (
    <Stack gap="md" data-testid="routing-settings-tab">
      <div>
        <Title order={4}>Routing</Title>
        <Text size="sm" c="dimmed">
          Global model availability, subscription usage observations, routing pins, and the role ×
          complexity matrix.
        </Text>
      </div>
      <AvailabilitySection />
      <UsageSection />
      <MatrixSection />
    </Stack>
  )
}

function AvailabilitySection() {
  const snapshot = useModelAvailability()
  const clear = useClearModelAvailabilityHold()

  if (snapshot.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="routing-availability-section">
        <SectionHeading>Model availability</SectionHeading>
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (snapshot.error) {
    return (
      <Paper withBorder p="md" data-testid="routing-availability-section">
        <SectionHeading>Model availability</SectionHeading>
        <Text size="sm" c="dimmed">
          Could not load model availability.
        </Text>
      </Paper>
    )
  }

  const holds = snapshot.data?.holds ?? []
  const available = snapshot.data?.available ?? []

  const clearHold = (row: ModelAvailabilityHoldDto) => {
    clear.mutate(
      { kind: row.kind, alias: row.modelAlias },
      {
        onSuccess: () => notifications.show({ color: 'green', message: 'Hold cleared' }),
        onError: (error) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not clear the hold'),
          }),
      },
    )
  }

  return (
    <Paper withBorder p="md" data-testid="routing-availability-section">
      <Stack gap="sm">
        <SectionHeading>Model availability</SectionHeading>
        <Text size="sm" c="dimmed">
          Live hold state. The available list is the current snapshot, not a typed model catalog —
          a kind is not inferred from an alias.
        </Text>
        {available.length === 0 && holds.length === 0 ? (
          <Text size="sm" c="dimmed">
            This snapshot has no available aliases and no holds.
          </Text>
        ) : (
          <Table striped highlightOnHover withRowBorders={false} layout="fixed">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Alias</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Kind</Table.Th>
                <Table.Th>Source</Table.Th>
                <Table.Th>Reset</Table.Th>
                <Table.Th>Reason</Table.Th>
                <Table.Th>Observed</Table.Th>
                <Table.Th w={88} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {available.map((alias) => (
                <Table.Tr key={`available-${alias}`}>
                  <Table.Td>{alias}</Table.Td>
                  <Table.Td>
                    <Badge size="sm" color="teal" variant="light">
                      Available
                    </Badge>
                  </Table.Td>
                  <Table.Td />
                  <Table.Td />
                  <Table.Td />
                  <Table.Td />
                  <Table.Td />
                  <Table.Td />
                </Table.Tr>
              ))}
              {holds.map((row) => (
                <Table.Tr key={row.id}>
                  <Table.Td>{row.modelAlias}</Table.Td>
                  <Table.Td>
                    <Badge size="sm" color="red" variant="light">
                      Held
                    </Badge>
                  </Table.Td>
                  <Table.Td>{row.kind}</Table.Td>
                  <Table.Td>{row.source}</Table.Td>
                  <Table.Td>{untilLabel(row.disabledUntil)}</Table.Td>
                  <Table.Td>
                    <Text size="sm" lineClamp={2}>
                      {row.reason}
                    </Text>
                  </Table.Td>
                  <Table.Td>{row.hitAt}</Table.Td>
                  <Table.Td>
                    <Button
                      size="compact-xs"
                      variant="subtle"
                      onClick={() => clearHold(row)}
                    >
                      Clear
                    </Button>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
        <ModelAvailabilityHoldForm />
      </Stack>
    </Paper>
  )
}

function UsageSection() {
  const snapshot = useSubscriptionUsage()

  if (snapshot.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="routing-usage-section">
        <SectionHeading>Subscription usage observations (best effort)</SectionHeading>
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (snapshot.error) {
    return (
      <Paper withBorder p="md" data-testid="routing-usage-section">
        <SectionHeading>Subscription usage observations (best effort)</SectionHeading>
        <Text size="sm" c="dimmed">
          Could not load subscription usage observations.
        </Text>
      </Paper>
    )
  }

  const samples = snapshot.data ?? []

  return (
    <Paper withBorder p="md" data-testid="routing-usage-section">
      <Stack gap="sm">
        <SectionHeading>Subscription usage observations (best effort)</SectionHeading>
        <Text size="sm" c="dimmed">
          Provider or profile observations when the optional monitor has stored a sample — not a
          per-model quota, not current capacity, and never a manufactured 0% or 100%.
        </Text>
        {samples.length === 0 ? (
          <Text size="sm">{USAGE_UNKNOWN}</Text>
        ) : (
          <Table striped withRowBorders={false} layout="fixed">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Provider</Table.Th>
                <Table.Th>Plan</Table.Th>
                <Table.Th>Remaining</Table.Th>
                <Table.Th>Resets</Table.Th>
                <Table.Th>Observed at</Table.Th>
                <Table.Th>Age</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {samples.map((sample) => (
                <UsageRow key={`${sample.provider}-${sample.observedAt}`} sample={sample} />
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    </Paper>
  )
}

function UsageRow({ sample }: { sample: SubscriptionUsageObservationDto }) {
  const remaining =
    sample.remainingPercent == null ? '—' : `${sample.remainingPercent}% remaining`
  return (
    <Table.Tr>
      <Table.Td>{sample.provider}</Table.Td>
      <Table.Td>{sample.planLabel ?? '—'}</Table.Td>
      <Table.Td>{remaining}</Table.Td>
      <Table.Td>{sample.resetsAt ?? '—'}</Table.Td>
      <Table.Td>observed at {sample.observedAt}</Table.Td>
      <Table.Td>{sample.age}</Table.Td>
    </Table.Tr>
  )
}

function MatrixSection() {
  const list = useComplexityChains()
  const pinsQuery = useRoutingPins()

  if (list.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="routing-matrix-section">
        <SectionHeading>Role × complexity matrix</SectionHeading>
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (list.error) {
    return (
      <Paper withBorder p="md" data-testid="routing-matrix-section">
        <SectionHeading>Role × complexity matrix</SectionHeading>
        <Text size="sm" c="dimmed">
          Could not load complexity chains.
        </Text>
      </Paper>
    )
  }

  const complexities = complexitiesOf(list.data?.complexities)
  const roles = list.data?.roles ?? []
  const anyChains = anyRoleChains(list.data?.chains ?? [], complexities)
  const { stageWide, cardSpecific } = groupPins(pinsQuery.data?.pins ?? [])

  return (
    <Paper withBorder p="md" data-testid="routing-matrix-section">
      <Stack gap="sm">
        <SectionHeading>Role × complexity matrix</SectionHeading>
        <Alert
          color="gray"
          variant="light"
          title={COMPLEXITY_ROUTING_BOUNDARY_TITLE}
          data-testid="routing-boundary"
        >
          <Text size="sm">{COMPLEXITY_ROUTING_BOUNDARY}</Text>
          <Text size="sm" mt="xs">
            {NON_COMPLEXITY_BOUNDARY}
          </Text>
        </Alert>
        {pinsQuery.error ? (
          <Text size="sm" c="dimmed">
            Could not load routing pins. Matrix cells still reflect the chain service.
          </Text>
        ) : null}
        <Text size="sm" c="dimmed">
          Stage-wide pins are shown on the matching role row. A role cell replaces Any role as a
          whole; it does not append. Candidate availability is the server&apos;s verdict — this
          view does not pick a fallback.
        </Text>
        <Box style={{ overflowX: 'auto' }}>
          <Table striped withRowBorders highlightOnHover layout="fixed" miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={160}>Role</Table.Th>
                {complexities.map((complexity) => (
                  <Table.Th key={complexity}>{complexity}</Table.Th>
                ))}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              <Table.Tr data-testid="routing-matrix-row-any">
                <Table.Td>
                  <Text size="sm" fw={600}>
                    Any role
                  </Text>
                </Table.Td>
                {anyChains.map((chain) => (
                  <Table.Td key={`any-${chain.complexity}`}>
                    <MatrixCell chain={chain} isAnyRoleRow />
                  </Table.Td>
                ))}
              </Table.Tr>
              {roles.map((role) => (
                <RoleMatrixRow
                  key={role}
                  role={role}
                  complexities={complexities}
                  pins={stagePinsForRole(stageWide, role)}
                />
              ))}
            </Table.Tbody>
          </Table>
        </Box>
        <CardPinsList pins={cardSpecific} />
      </Stack>
    </Paper>
  )
}

function RoleMatrixRow({
  role,
  complexities,
  pins,
}: {
  role: AgentTaskRole
  complexities: TaskComplexity[]
  pins: RoutingPinDto[]
}) {
  const snapshot = useComplexityChainEffective(role)

  if (snapshot.isLoading) {
    return (
      <Table.Tr data-testid={`routing-matrix-row-${role}`}>
        <Table.Td>
          <Text size="sm" fw={600}>
            {role}
          </Text>
        </Table.Td>
        <Table.Td colSpan={complexities.length}>
          <Loader size="sm" />
        </Table.Td>
      </Table.Tr>
    )
  }

  if (snapshot.error) {
    return (
      <Table.Tr data-testid={`routing-matrix-row-${role}`}>
        <Table.Td>
          <Text size="sm" fw={600}>
            {role}
          </Text>
        </Table.Td>
        <Table.Td colSpan={complexities.length}>
          <Text size="sm" c="red">
            Could not load {role} effective cells.
          </Text>
        </Table.Td>
      </Table.Tr>
    )
  }

  return (
    <Table.Tr data-testid={`routing-matrix-row-${role}`}>
      <Table.Td>
        <Stack gap={6}>
          <Text size="sm" fw={600}>
            {role}
          </Text>
          {pins.map((pin) => (
            <Alert
              key={pin.id}
              color={pin.strength === 'Required' ? 'red' : 'blue'}
              variant="light"
              p="xs"
              data-testid={`routing-pin-banner-${role}`}
            >
              <Text size="xs">{pinEffectCopy(pin)}</Text>
            </Alert>
          ))}
        </Stack>
      </Table.Td>
      {complexities.map((complexity) => (
        <Table.Td key={`${role}-${complexity}`}>
          <MatrixCell
            chain={cellByComplexity(snapshot.data?.chains, complexity)}
            isAnyRoleRow={false}
            role={role}
            complexity={complexity}
          />
        </Table.Td>
      ))}
    </Table.Tr>
  )
}

function MatrixCell({
  chain,
  isAnyRoleRow,
  role,
  complexity,
}: {
  chain: ComplexityChainDto | undefined
  isAnyRoleRow: boolean
  role?: AgentTaskRole
  complexity?: TaskComplexity
}) {
  const resolved = chain ? effectiveResolvedFrom(chain) : 'none'
  const cellRole = chain?.role ?? role ?? 'any'
  const cellComplexity = chain?.complexity ?? complexity ?? 'Hard'
  return (
    <Stack gap={4} data-testid={`routing-matrix-cell-${cellRole}-${cellComplexity}`}>
      <Badge
        size="sm"
        variant="light"
        color={
          resolved === 'none' ? 'red' : resolved === 'config' ? 'yellow' : resolved === 'any' ? 'gray' : 'blue'
        }
      >
        {resolvedFromLabel(resolved, isAnyRoleRow)}
      </Badge>
      {chain?.provenance ? (
        <Badge size="xs" variant="outline">
          {chain.provenance}
        </Badge>
      ) : null}
      {chain?.notAfter ? (
        <Text size="xs" c="dimmed">
          Expires {chain.notAfter}
        </Text>
      ) : null}
      {chain?.reason ? (
        <Text size="xs" c="dimmed" lineClamp={2}>
          {chain.reason}
        </Text>
      ) : null}
      <CandidateList candidates={chain?.candidates ?? []} />
    </Stack>
  )
}

function CandidateList({ candidates }: { candidates: ComplexityCandidateDto[] }) {
  if (candidates.length === 0) {
    return (
      <Text size="xs" c="dimmed">
        (empty)
      </Text>
    )
  }
  return (
    <Stack gap={2}>
      {candidates.map((candidate, index) => (
        <Group key={`${candidate.agentKind}-${candidate.modelLevel}-${index}`} gap={6} wrap="nowrap">
          <Text size="xs">{candidateLabel(candidate, index)}</Text>
          <Text size="xs" c={candidate.availableNow ? 'teal' : 'red'}>
            {candidateAvailabilityLabel(candidate)}
          </Text>
        </Group>
      ))}
    </Stack>
  )
}

function CardPinsList({ pins }: { pins: RoutingPinDto[] }) {
  if (pins.length === 0) {
    return (
      <Stack gap={4} data-testid="routing-card-pins">
        <Text size="sm" fw={600}>
          Card-specific pins
        </Text>
        <Text size="sm" c="dimmed">
          No card-specific pins. {CARD_PIN_SCOPE}
        </Text>
      </Stack>
    )
  }

  return (
    <Stack gap="xs" data-testid="routing-card-pins">
      <Text size="sm" fw={600}>
        Card-specific pins
      </Text>
      <Text size="sm" c="dimmed">
        {CARD_PIN_SCOPE}
      </Text>
      {pins.map((pin) => (
        <Text key={pin.id} size="sm">
          {cardPinCopy(pin)}
          {pin.reason ? ` ${pin.reason}` : ''}
        </Text>
      ))}
    </Stack>
  )
}

function SectionHeading({ children }: { children: string }) {
  return (
    <Title order={5} mb={4}>
      {children}
    </Title>
  )
}
