import type { Meta, StoryObj } from '@storybook/react'
import {
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Group,
  List,
  Paper,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { TbChevronDown, TbChevronRight } from 'react-icons/tb'

/**
 * PROPOSAL MOCK — not production code. Visual drawing for
 * `docs/superpowers/specs/2026-08-17-mobile-thread-and-plan-surfacing.md` §4 (M3): the plan
 * reader. A 20,000-char plan is read ToC-first on a phone — section list, one section open at a
 * time — never as a single scroll.
 */

const SECTIONS: Array<[string, string]> = [
  ['0. What exists today (verified)', '9 signals inventoried'],
  ['1. Design decisions D1–D5', 'nine conditions · one projection'],
  ['2. Server design', '3 new files'],
  ['3. Client design', 'attention tab + visuals'],
  ['4. Slices 1–6', 'each independently landable'],
  ['5. Collision map', '4 files not touched'],
  ['6. What I could not determine', '5 open questions'],
]

function Header() {
  return (
    <Box
      pb="xs"
      mb="xs"
      style={{ position: 'sticky', top: 0, background: '#141517', zIndex: 2, borderBottom: '1px solid var(--mantine-color-dark-4)' }}
    >
      <Group gap={8}>
        <Text fw={700}>#35</Text>
        <Text size="sm" style={{ flex: 1 }} truncate>
          Stuck-work view
        </Text>
        <Badge color="blue" variant="light" size="sm">
          Planned
        </Badge>
      </Group>
      <Text size="xs" c="dimmed">
        docs/superpowers/specs/2026-08-16-card-0035-stuck-work-view.md · 2026-08-16
      </Text>
    </Box>
  )
}

function ApproveBar() {
  return (
    <Paper
      withBorder
      radius="md"
      p="xs"
      mt="md"
      style={{ position: 'sticky', bottom: 8, background: 'var(--mantine-color-dark-6)' }}
    >
      <Group justify="space-between">
        <Text size="xs" c="dimmed">
          Approving moves #35 → In Progress
        </Text>
        <Group gap="xs">
          <Button size="compact-sm" variant="default">
            Hand back…
          </Button>
          <Button size="compact-sm" color="success">
            Approve
          </Button>
        </Group>
      </Group>
    </Paper>
  )
}

function TocRow({ label, sub, open }: { label: string; sub: string; open?: boolean }) {
  return (
    <UnstyledButton w="100%" py={10} px={4}>
      <Group wrap="nowrap" gap={8}>
        {open ? (
          <TbChevronDown size={14} style={{ flexShrink: 0 }} />
        ) : (
          <TbChevronRight size={14} style={{ flexShrink: 0, opacity: 0.5 }} />
        )}
        <Box style={{ minWidth: 0, flex: 1 }}>
          <Text size="sm" truncate>
            {label}
          </Text>
          <Text size="xs" c="dimmed">
            {sub}
          </Text>
        </Box>
      </Group>
    </UnstyledButton>
  )
}

function TocFirst() {
  return (
    <Box maw={390} mx="auto">
      <Header />
      <Paper withBorder radius="md" px="xs">
        {SECTIONS.map(([label, sub], i) => (
          <Box key={label}>
            {i > 0 && <Divider />}
            <TocRow label={label} sub={sub} />
          </Box>
        ))}
      </Paper>
      <ApproveBar />
    </Box>
  )
}

function SectionOpen() {
  return (
    <Box maw={390} mx="auto">
      <Header />
      <Paper withBorder radius="md" px="xs">
        <TocRow label="1. Design decisions D1–D5" sub="nine conditions · one projection" open />
        <Box px="sm" pb="sm">
          <Title order={5} mb={4}>
            D1. “Stuck” is nine named, computable conditions
          </Title>
          <Text size="sm" mb="xs">
            One server projection computes them; every condition names its derivation. A session
            that is <Code>Working</Code> with a fresh transcript is <b>never</b> stuck —
            “genuinely slow, leave it alone” is an explicit non-member, which is what keeps the
            view trustworthy.
          </Text>
          <List size="sm" spacing={4} mb="xs">
            <List.Item>
              <Code>BlockedQuestion</Code> — task <Code>Status == Blocked</Code>
            </List.Item>
            <List.Item>
              <Code>ParkedMessage</Code> — attempts spent; Critical when channel-bound
            </List.Item>
            <List.Item>
              <Code>DeadSession</Code> — open task, session gone or ended
            </List.Item>
          </List>
          <Text size="xs" c="dimmed">
            … 6 more conditions · continue reading
          </Text>
        </Box>
        <Divider />
        <TocRow label="2. Server design" sub="3 new files" />
      </Paper>
      <ApproveBar />
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/Plan reader',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** Landing: the plan as a table of contents. Tap a section to read it; the approve bar is fixed. */
export const TocFirst_: Story = {
  name: 'ToC first',
  render: () => <TocFirst />,
  globals: { viewport: { value: 'iphone12' } },
}

/** One section open in place — the rest of the plan stays one tap away, never one scroll away. */
export const SectionOpen_: Story = {
  name: 'Section open',
  render: () => <SectionOpen />,
  globals: { viewport: { value: 'iphone12' } },
}
