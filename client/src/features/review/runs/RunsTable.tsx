import { Anchor, Badge, Group, Paper, Table, Text, Tooltip } from '@mantine/core'
import { Link, useNavigate } from 'react-router'
import type { ReviewRunSummaryDto } from '../../../api/reviewEngine'
import { CostText } from '../components/CostText'
import { PersonChip } from '../components/PersonChip'
import { ModelBadges, RunStatusBadge, StageText } from '../components/ReviewBadges'
import { RoundPips } from '../components/RoundPips'
import { BOT_ROUND_LIMIT, WAITING_ON_META } from '../reviewMeta'
import { fmtAgo, prRef } from '../reviewFormat'

export function OpenFindingsCell({ run }: { run: ReviewRunSummaryDto }) {
  if (run.openFindings === 0)
    return (
      <Text size="xs" c="dimmed">
        none open
      </Text>
    )
  return (
    <Group gap={4} wrap="nowrap">
      <Text size="sm" fw={700} c={run.openBlockers > 0 ? 'danger.4' : undefined}>
        {run.openFindings}
      </Text>
      <Text size="xs" c="dimmed">
        open
      </Text>
      {run.openBlockers > 0 && (
        <Badge size="xs" color="danger" variant="light">
          {run.openBlockers} blocker{run.openBlockers === 1 ? '' : 's'}
        </Badge>
      )}
    </Group>
  )
}

export function AuthorLine({ run }: { run: ReviewRunSummaryDto }) {
  const s = run.subject
  return (
    <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
      <PersonChip login={s.authorLogin} displayName={s.authorDisplayName} size="xs" />
      {s.authorResolvedBy && (
        <Tooltip multiline w={300} label={`Resolved author: ${s.authorResolvedBy}`}>
          <Badge size="xs" variant="light" color="grape" tt="none">
            resolved
          </Badge>
        </Tooltip>
      )}
    </Group>
  )
}

/** The dense runs table (the operator's default view). A row opens the run. */
export function RunsTable({ runs, now }: { runs: ReviewRunSummaryDto[]; now: number }) {
  const navigate = useNavigate()
  return (
    <Paper withBorder radius="md">
      <Table.ScrollContainer minWidth={1180} type="native">
        <Table verticalSpacing={6} horizontalSpacing="sm" fz="sm" highlightOnHover stickyHeader>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Pull request</Table.Th>
              <Table.Th w={170}>Stage · status</Table.Th>
              <Table.Th w={120}>Round</Table.Th>
              <Table.Th w={100}>Waiting on</Table.Th>
              <Table.Th w={140}>Findings</Table.Th>
              <Table.Th w={180}>Models</Table.Th>
              <Table.Th w={70} ta="right">
                Cost
              </Table.Th>
              <Table.Th w={90} ta="right">
                Updated
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {runs.map((r) => (
              <Table.Tr
                key={r.id}
                onClick={() => navigate(`/reviews/${r.id}`)}
                style={{ cursor: 'pointer', opacity: r.status === 'Merged' || r.status === 'Closed' ? 0.6 : 1 }}
              >
                <Table.Td maw={460}>
                  <Group gap={6} wrap="nowrap">
                    <Text size="xs" ff="monospace" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                      {prRef(r.subject)}
                    </Text>
                    <Anchor component={Link} to={`/reviews/${r.id}`} size="sm" fw={500} c="gray.1" truncate onClick={(e) => e.stopPropagation()}>
                      {r.subject.title}
                    </Anchor>
                  </Group>
                  <Group gap={8} wrap="nowrap" mt={2}>
                    <AuthorLine run={r} />
                    <Text size="xs" c="dimmed" ff="monospace" truncate>
                      {r.subject.headRef}
                    </Text>
                  </Group>
                </Table.Td>
                <Table.Td>
                  <StageText stage={r.stage} />
                  <RunStatusBadge status={r.status} size="xs" />
                </Table.Td>
                <Table.Td>
                  <RoundPips round={r.round} botOnly={r.botOnlyRounds} compact />
                  <Text size="xs" c={r.botOnlyRounds >= BOT_ROUND_LIMIT ? 'pink.4' : 'dimmed'} mt={2} style={{ whiteSpace: 'nowrap' }}>
                    R{r.round} · bot {r.botOnlyRounds}/{BOT_ROUND_LIMIT}
                  </Text>
                </Table.Td>
                <Table.Td>
                  {r.waitingOn ? (
                    <Badge size="sm" variant="light" color={WAITING_ON_META[r.waitingOn].color}>
                      {WAITING_ON_META[r.waitingOn].label}
                    </Badge>
                  ) : (
                    <Text size="xs" c="dimmed">
                      —
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <OpenFindingsCell run={r} />
                </Table.Td>
                <Table.Td>
                  <ModelBadges models={r.models} max={4} />
                </Table.Td>
                <Table.Td ta="right">
                  <CostText usd={r.costUsd} />
                </Table.Td>
                <Table.Td ta="right">
                  <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                    {fmtAgo(r.updatedAt, now)}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ))}
            {runs.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={8}>
                  <Text size="sm" c="dimmed" ta="center" py="md">
                    No runs match these filters.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Paper>
  )
}
