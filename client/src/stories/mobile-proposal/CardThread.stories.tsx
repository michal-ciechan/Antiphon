import type { Meta, StoryObj } from '@storybook/react'
import {
  Badge,
  Blockquote,
  Box,
  Button,
  Divider,
  Group,
  Paper,
  Stack,
  Text,
  UnstyledButton,
} from '@mantine/core'
import { TbFileText, TbGitCommit, TbChevronRight } from 'react-icons/tb'

/**
 * PROPOSAL MOCK — not production code. Visual drawing for
 * `docs/superpowers/specs/2026-08-17-mobile-thread-and-plan-surfacing.md` §D2/§4: the card
 * thread — plan, tasks with check readings, commits and verdict as ONE scroll, correlated by
 * the CARD-nnnn citation convention.
 */

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box>
      <Text size="xs" fw={700} c="dimmed" tt="uppercase" mb={4} style={{ letterSpacing: 1 }}>
        {title}
      </Text>
      {children}
    </Box>
  )
}

function OpenThread() {
  return (
    <Box maw={390} mx="auto">
      <Group gap={8} mb={2}>
        <Text fw={700} size="lg">
          #35
        </Text>
        <Badge color="warning" variant="light">
          Review
        </Badge>
        <Badge color="red" variant="outline" size="xs">
          P0
        </Badge>
      </Group>
      <Text size="sm" mb="md">
        UX: a diagnostic view for work that is stuck
      </Text>

      <Stack gap="md">
        <Section title="Plan">
          <Paper withBorder radius="md" p="sm">
            <UnstyledButton w="100%">
              <Group gap={8} wrap="nowrap">
                <TbFileText size={16} style={{ flexShrink: 0 }} />
                <Box style={{ minWidth: 0, flex: 1 }}>
                  <Text size="sm" truncate>
                    Stuck-work view — nine conditions, one projection
                  </Text>
                  <Text size="xs" c="dimmed">
                    2026-08-16 · Planned · 6 slices
                  </Text>
                </Box>
                <TbChevronRight size={14} style={{ opacity: 0.5 }} />
              </Group>
            </UnstyledButton>
            <Group justify="flex-end" mt={8}>
              <Button size="compact-xs" variant="light" color="success">
                Approve → In Progress
              </Button>
            </Group>
          </Paper>
        </Section>

        <Section title="Work · 2 tasks · $3.87">
          <Paper withBorder radius="md" p="sm">
            <Group gap={6} mb={4}>
              <Badge size="xs" color="blue" variant="filled">
                working
              </Badge>
              <Text size="xs" c="dimmed">
                slices 4+6 · opus · check 13:02 · $1.12
              </Text>
            </Group>
            <Blockquote p="xs" color="gray" mb={0}>
              <Text size="xs">
                Wiring the parked-message verbs; BlockedReplyRow posts and clears in tests. Two
                slices in, no blockers, on the collision map’s safe side. Expect the home badge
                within the hour.
              </Text>
              <Text size="xs" c="dimmed" mt={4}>
                check #3 · 12:31
              </Text>
            </Blockquote>
          </Paper>
          <Paper withBorder radius="md" p="sm" mt="xs">
            <Group gap={6} mb={4}>
              <Badge size="xs" color="success" variant="filled">
                succeeded
              </Badge>
              <Text size="xs" c="dimmed">
                slices 1–3 · opus · settled 11:04 · $2.75
              </Text>
            </Group>
            <Text size="xs" lineClamp={2}>
              Shipped the projection and the diagnostic tab. GET /api/attention serves nine
              conditions; a working session is never listed. 34 tests green.
            </Text>
            <Text size="xs" c="blue.4" mt={2}>
              read full report
            </Text>
          </Paper>
        </Section>

        <Section title="Commits · 3">
          <Paper withBorder radius="md" px="sm" py={4}>
            {[
              ['1f2f2f1', 'feat(attention): CARD-0035 slice 3 - the diagnostic tab', '2h'],
              ['34f5638', 'feat(attention): CARD-0035 slice 2 - the runner, diffed', '3h'],
              ['b2a91c4', 'feat(attention): CARD-0035 slice 1 - the projection', '5h'],
            ].map(([hash, msg, age], i) => (
              <Box key={hash}>
                {i > 0 && <Divider />}
                <Group gap={8} py={6} wrap="nowrap">
                  <TbGitCommit size={14} style={{ flexShrink: 0, opacity: 0.6 }} />
                  <Text size="xs" ff="monospace" c="dimmed">
                    {hash}
                  </Text>
                  <Text size="xs" truncate style={{ flex: 1 }}>
                    {msg}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {age}
                  </Text>
                </Group>
              </Box>
            ))}
          </Paper>
        </Section>
      </Stack>
    </Box>
  )
}

function SettledThread() {
  return (
    <Box maw={390} mx="auto">
      <Group gap={8} mb={2}>
        <Text fw={700} size="lg">
          #67
        </Text>
        <Badge color="success" variant="light">
          Done
        </Badge>
        <Badge color="red" variant="outline" size="xs">
          P0
        </Badge>
      </Group>
      <Text size="sm" mb="md">
        A channel reply’s route out must be as durable as the message’s route in
      </Text>
      <Stack gap="md">
        <Section title="Verdict">
          <Paper withBorder radius="md" p="sm" style={{ borderColor: 'var(--mantine-color-success-8)' }}>
            <Text size="xs">
              Shipped c4df66b. The correlation map is deleted; the reply target is resolved at
              dispatch time from the durable row. Every correlation now ends in a published reply
              or a Critical ChannelReplyLost incident. Still open: the latestPromptSeq bail
              (CARD-0055 slice 7).
            </Text>
          </Paper>
        </Section>
        <Section title="Work · 1 task · $6.02">
          <Paper withBorder radius="md" p="sm">
            <Group gap={6}>
              <Badge size="xs" color="success" variant="filled">
                succeeded
              </Badge>
              <Text size="xs" c="dimmed">
                fable · settled 09:58 · 6 tests
              </Text>
            </Group>
          </Paper>
        </Section>
        <Section title="Commits · 2">
          <Paper withBorder radius="md" px="sm" py={4}>
            <Group gap={8} py={6} wrap="nowrap">
              <TbGitCommit size={14} style={{ opacity: 0.6 }} />
              <Text size="xs" ff="monospace" c="dimmed">
                eec2b73
              </Text>
              <Text size="xs" truncate style={{ flex: 1 }}>
                fix(channels): CARD-0067 - the reply route out…
              </Text>
            </Group>
            <Divider />
            <Group gap={8} py={6} wrap="nowrap">
              <TbGitCommit size={14} style={{ opacity: 0.6 }} />
              <Text size="xs" ff="monospace" c="dimmed">
                70d1e19
              </Text>
              <Text size="xs" truncate style={{ flex: 1 }}>
                docs(claude): CARD-0067 - the two-stores rule
              </Text>
            </Group>
          </Paper>
        </Section>
      </Stack>
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/Card thread',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** A live thread: plan (approvable), a working task with its latest check reading, commits. */
export const Open: Story = {
  render: () => <OpenThread />,
  globals: { viewport: { value: 'iphone12' } },
}

/** A settled thread: the verdict leads, then the report and the commits that carry the outcome. */
export const Settled: Story = {
  render: () => <SettledThread />,
  globals: { viewport: { value: 'iphone12' } },
}
