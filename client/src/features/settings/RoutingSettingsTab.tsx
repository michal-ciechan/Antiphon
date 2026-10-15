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
import { useState } from 'react'
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
  ComplexityChainCellEditor,
  type ComplexityChainEditorTarget,
} from './ComplexityChainCellEditor'
import { RolePolicySection } from './RolePolicySection'
import { RunnerDefaultsSection } from './RunnerDefaultsSection'
import {
  CARD_PIN_SCOPE,
  COMPLEXITY_ROUTING_BOUNDARY,
  COMPLEXITY_ROUTING_BOUNDARY_TITLE,
  USAGE_UNKNOWN,
  anyRoleChains,
  candidateAvailabilityLabel,
  candidateAvailabilityUnknown,
  candidateLabel,
  cardPinCopy,
  cardPinCountLabel,
  cellByComplexity,
  cellEditorTitle,
  cellMetadataUnknownLabel,
  complexitiesOf,
  effectiveResolvedFrom,
  effectiveCellLabel,
  fallbackResolvedFromForRoleCell,
  groupPins,
  nonComplexityBoundary,
  pendingReadLabel,
  pinEffectCopy,
  effectiveRoutingUnknown,
  failedReadSegment,
  resolvedFromLabel,
  routingPinsReadLabel,
  stagePinsForRole,
  untilLabel,
  unsetCellLabel,
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
          Global model availability, subscription usage observations, runner defaults, role policy,
          routing pins, and the role × complexity matrix.
        </Text>
      </div>
      <AvailabilitySection />
      <UsageSection />
      <RunnerDefaultsSection />
      <RolePolicySection />
      <MatrixSection />
    </Stack>
  )
}

function AvailabilitySection() {
  const snapshot = useModelAvailability()
  const clear = useClearModelAvailabilityHold()
  const pending = pendingReadLabel(snapshot, 'Model availability')

  if (pending) {
    return (
      <Paper withBorder p="md" data-testid="routing-availability-section">
        <SectionHeading>Model availability</SectionHeading>
        <PendingRead label={pending} />
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
  const pending = pendingReadLabel(snapshot, 'Subscription usage')

  if (pending) {
    return (
      <Paper withBorder p="md" data-testid="routing-usage-section">
        <SectionHeading>Subscription usage observations (best effort)</SectionHeading>
        <PendingRead label={pending} />
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
  const [editor, setEditor] = useState<ComplexityChainEditorTarget | null>(null)
  // CARD-1275: the open editor sits at one tree position whatever the list read shows, so a
  // failed refresh neither drops its draft nor reopens it from the old snapshot afterwards.
  const editorNode = editor?.role ? (
    <RoleCellEditor target={editor} role={editor.role} onClose={() => setEditor(null)} />
  ) : editor ? (
    // The Any role cell comes from the list read, which this section has loaded.
    <ComplexityChainCellEditor
      opened
      onClose={() => setEditor(null)}
      role={editor.role}
      complexity={editor.complexity}
      chain={editor.chain}
      isAnyRoleRow={editor.isAnyRoleRow}
      fallbackResolvedFrom={editor.fallbackResolvedFrom}
    />
  ) : null

  return (
    <>
      <MatrixSectionBody list={list} pinsQuery={pinsQuery} onConfigure={setEditor} />
      {editorNode}
    </>
  )
}

function MatrixSectionBody({
  list,
  pinsQuery,
  onConfigure,
}: {
  list: ReturnType<typeof useComplexityChains>
  pinsQuery: ReturnType<typeof useRoutingPins>
  onConfigure: (target: ComplexityChainEditorTarget) => void
}) {
  // Without the list there is no Any role row to synthesize and no cell to open in the editor.
  if (list.isPending) {
    return (
      <Paper withBorder p="md" data-testid="routing-matrix-section">
        <SectionHeading>Role × complexity matrix</SectionHeading>
        <PendingRead label={pendingReadLabel(list, 'Complexity chains') ?? 'Complexity chains loading.'} />
      </Paper>
    )
  }

  if (list.isError) {
    return (
      <Paper withBorder p="md" data-testid="routing-matrix-section">
        <SectionHeading>Role × complexity matrix</SectionHeading>
        <Text size="sm" c="dimmed">
          Could not load complexity chains.
        </Text>
      </Paper>
    )
  }

  const complexities = complexitiesOf(list.data.complexities)
  const defaultComplexity = list.data.defaultComplexity
  const roles = list.data.roles ?? []
  const anyChains = anyRoleChains(list.data.chains ?? [], complexities)
  const pinsRead = routingPinsReadLabel(pinsQuery, getApiErrorMessage(pinsQuery.error, 'request failed'))
  // A failed refetch keeps the previous pins in data: they are stale, so none is shown as current.
  const { stageWide, cardSpecific } = groupPins(pinsRead ? [] : (pinsQuery.data?.pins ?? []))

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
            {nonComplexityBoundary(defaultComplexity)}
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
                  <Table.Th key={complexity}>
                    {complexity}{complexity === defaultComplexity
                      ? ' (default for an ordinary Worker dispatch without -Complexity)'
                      : ''}
                  </Table.Th>
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
                    <MatrixCell
                      chain={chain}
                      isAnyRoleRow
                      onConfigure={() =>
                        onConfigure({
                          role: null,
                          complexity: chain.complexity,
                          chain,
                          isAnyRoleRow: true,
                          fallbackResolvedFrom: 'none',
                        })
                      }
                    />
                  </Table.Td>
                ))}
              </Table.Tr>
              {roles.map((role) => (
                <RoleMatrixRow
                  key={role}
                  role={role}
                  complexities={complexities}
                  pins={stagePinsForRole(stageWide, role)}
                  cardPins={cardSpecific}
                  pinsUnknown={pinsRead?.row ?? null}
                  anyChains={anyChains}
                  onConfigure={onConfigure}
                />
              ))}
            </Table.Tbody>
          </Table>
        </Box>
        <CardPinsList pins={cardSpecific} unknown={pinsRead?.list ?? null} />
      </Stack>
    </Paper>
  )
}

/** A role cell editor follows its row read: Save and Clear wait while that read is pending again. */
function RoleCellEditor({
  target,
  role,
  onClose,
}: {
  target: ComplexityChainEditorTarget
  role: AgentTaskRole
  onClose: () => void
}) {
  const snapshot = useComplexityChainEffective(role)
  return (
    <ComplexityChainCellEditor
      opened
      onClose={onClose}
      role={role}
      complexity={target.complexity}
      chain={target.chain}
      isAnyRoleRow={target.isAnyRoleRow}
      fallbackResolvedFrom={target.fallbackResolvedFrom}
      readPending={pendingReadLabel(snapshot, `${role} effective cells`)}
    />
  )
}

function RoleMatrixRow({
  role,
  complexities,
  pins,
  cardPins,
  pinsUnknown,
  anyChains,
  onConfigure,
}: {
  role: AgentTaskRole
  complexities: TaskComplexity[]
  pins: RoutingPinDto[]
  cardPins: RoutingPinDto[]
  pinsUnknown: string | null
  anyChains: ComplexityChainDto[]
  onConfigure: (target: ComplexityChainEditorTarget) => void
}) {
  const snapshot = useComplexityChainEffective(role)
  const cardPinCount = cardPinCountLabel(role, cardPins)

  // A pending row (loading, or paused offline) has no cells and no Configure.
  if (snapshot.isPending) {
    return (
      <Table.Tr data-testid={`routing-matrix-row-${role}`}>
        <Table.Td>
          <Text size="sm" fw={600}>
            {role}
          </Text>
        </Table.Td>
        <Table.Td colSpan={complexities.length}>
          <Group gap="xs" wrap="nowrap">
            <Loader size="sm" />
            <Text size="xs" c="dimmed">
              {pendingReadLabel(snapshot, `${role} effective cells`)}
            </Text>
          </Group>
        </Table.Td>
      </Table.Tr>
    )
  }

  if (snapshot.isError) {
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
          {cardPinCount ? <Text size="xs" c="dimmed">{cardPinCount}</Text> : null}
          {pinsUnknown ? <Text size="xs" c="dimmed">{pinsUnknown}</Text> : null}
        </Stack>
      </Table.Td>
      {complexities.map((complexity) => {
        const chain = cellByComplexity(snapshot.data.chains, complexity)
        return (
          <Table.Td key={`${role}-${complexity}`}>
            <MatrixCell
              chain={chain}
              isAnyRoleRow={false}
              role={role}
              complexity={complexity}
              onConfigure={() =>
                onConfigure({
                  role,
                  complexity,
                  chain,
                  isAnyRoleRow: false,
                  fallbackResolvedFrom: fallbackResolvedFromForRoleCell(
                    cellByComplexity(anyChains, complexity),
                  ),
                })
              }
            />
          </Table.Td>
        )
      })}
    </Table.Tr>
  )
}

function MatrixCell({
  chain,
  isAnyRoleRow,
  role,
  complexity,
  onConfigure,
}: {
  chain: ComplexityChainDto | undefined
  isAnyRoleRow: boolean
  role?: AgentTaskRole
  complexity?: TaskComplexity
  onConfigure: () => void
}) {
  const resolved = chain ? effectiveResolvedFrom(chain) : 'none'
  const cellRole = chain?.role ?? role ?? 'any'
  const cellComplexity = chain?.complexity ?? complexity ?? 'Hard'
  const configureLabel = cellEditorTitle(isAnyRoleRow ? null : (role ?? null), cellComplexity)
  const effective = isAnyRoleRow ? null : chain?.effective
  const chainReadFailed = effective != null && failedReadSegment(effective, 'chain') != null
  const routingUnknown = effective != null && effectiveRoutingUnknown(effective)
  const metadataUnknown = effective != null ? cellMetadataUnknownLabel(effective) : null
  // RolePolicy describes the ordinary default create. A non-default -Complexity dispatch
  // blocks on a known empty chain only when the stage pin read succeeded too.
  const showEffective = effective != null && (
    effective.source !== 'rolePolicy' || effective.governsDefault || routingUnknown
  )
  const showStored = !showEffective || (
    effective.source === 'pin' || effective.source === 'pin+chain'
    || effective.source === 'rolePolicy' || effective.source === 'agent'
  )
  const storedLabel = chainReadFailed
    ? 'Stored cell: unknown (read failed)'
    : routingUnknown && resolved !== 'none'
    ? null
    : resolved !== 'none'
    ? resolvedFromLabel(resolved, isAnyRoleRow)
    : showEffective
      ? 'Stored cell: unset'
      : unsetCellLabel(cellComplexity, effective?.governsDefault ?? false)
  return (
    <Stack gap={4} data-testid={`routing-matrix-cell-${cellRole}-${cellComplexity}`}>
      {showEffective ? (
        <>
          <Badge
            size="sm"
            variant="light"
            styles={{ root: { height: 'auto', maxWidth: '100%', textTransform: 'none' }, label: { whiteSpace: 'normal', textAlign: 'left' } }}
            color={routingUnknown || (effective.withheldReason && effective.source !== 'agent') ? 'red'
              : effective.source === 'pin' ? 'red'
                : effective.source === 'rolePolicy' ? 'gray' : 'blue'}
          >
            {effectiveCellLabel(effective)}
          </Badge>
          {effective.candidates.length > 0 && !routingUnknown
            ? <CandidateList candidates={effective.candidates} /> : null}
        </>
      ) : null}
      {effective?.withheldReason ? (
        <Alert color={effective.source === 'agent' && !effective.withheldReason.includes('read failed') ? 'blue' : 'red'} p="xs">
          <Text size="xs">{effective.withheldReason}</Text>
        </Alert>
      ) : null}
      {showStored && storedLabel ? (
        <Badge
          size="sm"
          variant="light"
          styles={{ root: { height: 'auto', maxWidth: '100%', textTransform: 'none' }, label: { whiteSpace: 'normal', textAlign: 'left' } }}
          color={showEffective ? 'gray'
            : resolved === 'none' ? 'red' : resolved === 'config' ? 'yellow' : resolved === 'any' ? 'gray' : 'blue'}
        >
          {storedLabel}
        </Badge>
      ) : null}
      {chain?.provenance ? (
        <Badge size="xs" variant="outline">
          {chain.provenance}
        </Badge>
      ) : null}
      {metadataUnknown ? (
        <Text size="xs" c="red">
          {metadataUnknown}
        </Text>
      ) : null}
      {chain?.notAfter && !metadataUnknown ? (
        <Text size="xs" c="dimmed">
          Expires {chain.notAfter}
        </Text>
      ) : null}
      {chain?.reason && !metadataUnknown ? (
        <Text size="xs" c="dimmed" lineClamp={2}>
          {chain.reason}
        </Text>
      ) : null}
      {showStored && !chainReadFailed && !routingUnknown ? <CandidateList candidates={chain?.candidates ?? []} /> : null}
      <Button size="compact-xs" variant="light" onClick={onConfigure} aria-label={configureLabel}>
        Configure
      </Button>
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
          <Text size="xs" c={candidate.availableNow ? 'teal' : candidateAvailabilityUnknown(candidate) ? 'dimmed' : 'red'}>
            {candidateAvailabilityLabel(candidate)}
          </Text>
        </Group>
      ))}
    </Stack>
  )
}

function CardPinsList({ pins, unknown }: { pins: RoutingPinDto[]; unknown: string | null }) {
  if (unknown) {
    return (
      <Stack gap={4} data-testid="routing-card-pins">
        <Text size="sm" fw={600}>
          Card-specific pins
        </Text>
        <Text size="sm" c="dimmed">
          {unknown}
        </Text>
      </Stack>
    )
  }

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

function PendingRead({ label }: { label: string }) {
  return (
    <Group justify="center" gap="xs" py="sm">
      <Loader size="sm" />
      <Text size="sm" c="dimmed">
        {label}
      </Text>
    </Group>
  )
}

function SectionHeading({ children }: { children: string }) {
  return (
    <Title order={5} mb={4}>
      {children}
    </Title>
  )
}
