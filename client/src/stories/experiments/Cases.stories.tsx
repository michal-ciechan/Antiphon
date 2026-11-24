import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import { Badge, Box, Button, Code, Divider, Group, Paper, Stack, Table, Tabs, Text, Textarea } from '@mantine/core'
import { TbBookmarkPlus, TbExternalLink, TbFlask, TbGitPullRequest, TbMessage2, TbPlus, TbTargetArrow } from 'react-icons/tb'
import { CASES, HUMAN_COMMENTS, ORIGIN_META, SUITE, TRIAL_HISTORY, VARIANTS, WORKSPACE_RECORD, type ExpectationKind } from './mockData'
import { ByoaButton, Fact, GraphPreview, PageHeader, ResultBadge, SectionLabel } from './shared'

/**
 * PROPOSAL MOCK — not production code. Cases, suites and the graph entry points of
 * `docs/features/013-experiments/plan.md`.
 *   A — a step's panel on the run graph: Inputs, Output, Prompt, Calls, Experiments, and the actions
 *   B — a case: why it exists, what it recorded, what to expect, every trial run on it
 *   C — a suite: where runs start, the judge, tuning and held-out cases
 *   D — human comments the bot did not match, each one click from becoming a case
 */

const KIND_COLOR: Record<ExpectationKind, string> = {
  unchanged: 'gray',
  'finding-present': 'success',
  'finding-absent': 'orange',
  field: 'cyan',
  budget: 'yellow',
  script: 'violet',
  prompt: 'grape',
}

function Mono({ children }: { children: string }) {
  return (
    <Text span ff="monospace" size="xs" c="gray.4">
      {children}
    </Text>
  )
}

// ─────────────────────────────────── A · Step panel on the graph ───────────────────────────────────

function StepPanel() {
  const [tab, setTab] = useState<string | null>('inputs')
  return (
    <Box>
      <PageHeader title="antiphon-demo#1 · Add card priority" subtitle="Run page, Workflow tab. Click a step for its panel; the panel is where a case or an experiment starts.">
        <ByoaButton />
      </PageHeader>
      <Paper withBorder radius="md" p="md" mb="sm">
        <GraphPreview variant={VARIANTS[0]} start="input" selected="verify" />
      </Paper>
      <Paper withBorder radius="md" p="md" maw={920}>
        <Group justify="space-between" align="flex-start" mb="xs" wrap="nowrap">
          <Box>
            <Group gap={8}>
              <Text fw={700} fz={18}>
                Verify
              </Text>
              <Badge variant="light" color="teal" radius="sm" tt="none">
                completion
              </Badge>
              <Badge variant="light" color="success" radius="sm" tt="none">
                Done
              </Badge>
              <Badge variant="outline" color="active" radius="sm" tt="none" leftSection={<TbFlask size={11} />}>
                3 experiments
              </Badge>
            </Group>
            <Text size="xs" c="dimmed">
              Checks each collated finding against the diff and drops the ones it cannot support. Iteration 1 · Haiku 5.5 · 4¢ · 9s
            </Text>
          </Box>
          <Group gap="xs" wrap="nowrap">
            <ByoaButton />
            <Button size="xs" variant="default" leftSection={<TbBookmarkPlus size={14} />}>
              Save as case
            </Button>
            <Button size="xs" leftSection={<TbFlask size={14} />}>
              Try another setting
            </Button>
          </Group>
        </Group>
        <Tabs value={tab} onChange={setTab}>
          <Tabs.List>
            <Tabs.Tab value="inputs">Inputs · 4</Tabs.Tab>
            <Tabs.Tab value="output">Output</Tabs.Tab>
            <Tabs.Tab value="prompt">Prompt</Tabs.Tab>
            <Tabs.Tab value="calls">Calls · 1</Tabs.Tab>
            <Tabs.Tab value="experiments">Experiments · 3</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="inputs" pt="sm">
            <Table verticalSpacing={6}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Input</Table.Th>
                  <Table.Th>From</Table.Th>
                  <Table.Th>Used to decide</Table.Th>
                  <Table.Th>Stored as</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {[
                  ['Collated findings', 'collate', 'Which findings to check', 'sha256:41ab…9c02 · 2.1 kB'],
                  ['Diff e737fb3..d93b82d', 'intake (code host)', 'The evidence for each finding', 'sha256:d07e…55f1 · 6.4 kB'],
                  ['Prompt verify.instructions', 'built-in default', 'How to judge a finding', 'sha256:9b10…e3aa'],
                  ['Policy collate.keepAt = 0.50', 'settings snapshot', 'The bar for keeping one', 'inline'],
                ].map(([a, b, c, d]) => (
                  <Table.Tr key={a}>
                    <Table.Td>
                      <Text size="sm">{a}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Mono>{b}</Mono>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed">
                        {c}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Mono>{d}</Mono>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
            <Text size="xs" c="dimmed" mt="xs">
              Inputs hash <Mono>7c2f19d</Mono>. A replay of this step reads exactly these, never the live PR.
            </Text>
          </Tabs.Panel>
          <Tabs.Panel value="output" pt="sm">
            <Code block fz={12}>
              {'{\n  "kept": [\n    { "key": "f-01", "title": "Priority has no upper bound", "p": 0.82 },\n    { "key": "f-02", "title": "Sort comparator can return NaN for a missing priority", "p": 0.61 }\n  ],\n  "dropped": [],\n  "verified": 2\n}'}
            </Code>
          </Tabs.Panel>
          <Tabs.Panel value="prompt" pt="sm">
            <Text size="xs" c="dimmed" mb={4}>
              Final text sent. <Mono>verify.instructions</Mono> v<Mono>9b10e3a</Mono> from the built-in default; no layer overrides it.
            </Text>
            <Code block fz={12}>
              {'You are verifying review findings against the diff.\nKeep a finding only when the diff shows the problem it describes.\nDrop a finding when you are not sure.\nAnswer with one line per finding: keep or drop, and a probability.'}
            </Code>
          </Tabs.Panel>
          <Tabs.Panel value="calls" pt="sm">
            <Fact label="anthropic · claude-haiku-5-5">HTTP · 3,120 in / 96 out · 4¢ actual · 8.7s · ok</Fact>
          </Tabs.Panel>
          <Tabs.Panel value="experiments" pt="sm">
            <Text size="sm">3 experiments have replayed this step. Open one from the Experiments history.</Text>
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Box>
  )
}

// ────────────────────────────────────────── B · Case ──────────────────────────────────────────

function CaseDetail() {
  const c = CASES[4]
  return (
    <Box>
      <PageHeader
        title={
          <>
            <Text span ff="monospace" fz={20}>
              {c.name}
            </Text>{' '}
            · {c.title}
          </>
        }
        subtitle="A case is one recorded situation that can be replayed: its frozen inputs, the original output, why it exists and what to expect."
      >
        <ByoaButton />
        <Button variant="default">Edit</Button>
        <Button leftSection={<TbFlask size={16} />}>Run in an experiment</Button>
      </PageHeader>
      <Box style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'start' }}>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="md">
            <Group justify="space-between" mb={6}>
              <SectionLabel>Why this case exists</SectionLabel>
              <Group gap={6}>
                <Badge color={ORIGIN_META[c.origin].color} variant="light" radius="sm" tt="none" leftSection={<TbTargetArrow size={12} />}>
                  {ORIGIN_META[c.origin].label}
                </Badge>
                <Badge color="indigo" variant="light" radius="sm" tt="none">
                  held out
                </Badge>
              </Group>
            </Group>
            <Text size="sm">{c.why}</Text>
            <Paper radius="sm" p="xs" mt="xs" bg="dark.7">
              <Group gap={6} mb={2}>
                <TbMessage2 size={14} />
                <Text size="xs" fw={600}>
                  hmartin on src/Demo.Web/CardEndpoints.cs:31
                </Text>
              </Group>
              <Text size="sm">"This accepts an empty title. Should be a 400."</Text>
            </Paper>
            <Group gap="md" mt="xs">
              <Button size="compact-xs" variant="subtle" leftSection={<TbGitPullRequest size={12} />} rightSection={<TbExternalLink size={11} />}>
                {c.pr}
              </Button>
              <Button size="compact-xs" variant="subtle">
                Original run, iteration 1
              </Button>
              <Text size="xs" c="dimmed">
                Captured by michal-ciechan, 23 Nov
              </Text>
            </Group>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>What to expect</SectionLabel>
            {c.expectations.map((e) => (
              <Group key={e.id} gap={8} wrap="nowrap" align="flex-start">
                <Badge color={KIND_COLOR[e.kind]} variant="light" radius="sm" tt="none" style={{ flexShrink: 0 }}>
                  {e.kind}
                </Badge>
                <Box>
                  <Text size="sm">{e.text}</Text>
                  <Text size="xs" c="dimmed">
                    checked at <Mono>{e.at}</Mono> · lines 27 to 35, tolerance ±5 · meaning judged by the suite's judge
                  </Text>
                </Box>
              </Group>
            ))}
            <Divider my="sm" />
            <Group gap="xs">
              <Button size="xs" variant="default" leftSection={<TbPlus size={14} />}>
                Add expectation
              </Button>
              <Text size="xs" c="dimmed">
                unchanged · finding present or absent · field · budget · script · prompt · all / any / not
              </Text>
            </Group>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>Every trial run on this case</SectionLabel>
            <Table verticalSpacing={4}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Experiment</Table.Th>
                  <Table.Th>Variant</Table.Th>
                  <Table.Th>Result</Table.Th>
                  <Table.Th>Cost</Table.Th>
                  <Table.Th>When</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                <Table.Tr>
                  <Table.Td>
                    <Mono>original run</Mono>
                  </Table.Td>
                  <Table.Td>As recorded</Table.Td>
                  <Table.Td>
                    <ResultBadge result="fail" size="xs" />
                  </Table.Td>
                  <Table.Td>58¢</Table.Td>
                  <Table.Td>22 Nov, 10:02</Table.Td>
                </Table.Tr>
                {TRIAL_HISTORY.map((t) => (
                  <Table.Tr key={t.experiment + t.variant}>
                    <Table.Td>
                      <Mono>{t.experiment}</Mono>
                    </Table.Td>
                    <Table.Td>{t.variant}</Table.Td>
                    <Table.Td>
                      <ResultBadge result={t.result} size="xs" />
                    </Table.Td>
                    <Table.Td>${t.cost.toFixed(2)}</Table.Td>
                    <Table.Td>{t.when}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Paper>
        </Stack>
        <Stack gap="sm">
          <Paper withBorder radius="md" p="md">
            <SectionLabel>Workspace record: rebuilt clean for every run</SectionLabel>
            <Stack gap={4}>
              {WORKSPACE_RECORD.map((w) => (
                <Fact key={w.item} label={w.item}>
                  <Text span size="sm" ff={w.item === 'Uncommitted state' ? undefined : 'monospace'} fz={w.item === 'Uncommitted state' ? undefined : 12}>
                    {w.value}
                  </Text>
                </Fact>
              ))}
            </Stack>
            <Text size="xs" c="dimmed" mt="xs">
              Each run gets a new container and a worktree at the same path, cut from the same parent. The run checks the commit and tree hash before any model call, and one container holds one run.
            </Text>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>Frozen inputs and the original output</SectionLabel>
            <Stack gap={4}>
              <Fact label="Start step inputs">
                <Mono>categorise</Mono> · diff, file list, PR description, check catalogue (8 checks), policy · hash <Mono>3e9a1c4</Mono>
              </Fact>
              <Fact label="Run state">round 1 · no open findings · no steer</Fact>
              <Fact label="Original output">2 findings posted (1 minor, 1 nit) · route findings · $0.58</Fact>
              <Fact label="Settings in force">Sonnet 5.5 reviewers · Haiku 5.5 verify · graph a41c09e</Fact>
              <Fact label="Kept until">the case is deleted</Fact>
            </Stack>
          </Paper>
          <Paper withBorder radius="md" p="md">
            <SectionLabel>As a scenario file</SectionLabel>
            <Code block fz={11}>
              {`# .antiphon/review/scenarios/${c.name}.yaml\nscenario: ${c.name}\nwhy: >\n  A human reviewer commented that POST /cards accepts an empty title.\nsource: { repo: michal-ciechan/antiphon-demo, base: e737fb3, head: d93b82d }\nsuite: ${SUITE.name}\nexpect:\n  finding-present:\n    file: src/Demo.Web/CardEndpoints.cs\n    about: The endpoint accepts an empty title`}
            </Code>
          </Paper>
        </Stack>
      </Box>
    </Box>
  )
}

// ────────────────────────────────────────── C · Suite ──────────────────────────────────────────

function SuiteDetail() {
  return (
    <Box>
      <PageHeader title={`Suite: ${SUITE.name}`} subtitle={`${SUITE.description} The suite decides what runs, so every case in it is exercised the same way.`}>
        <ByoaButton />
        <Button variant="default">Edit</Button>
        <Button leftSection={<TbFlask size={16} />}>New experiment</Button>
      </PageHeader>
      <Paper withBorder radius="md" p="md" mb="sm">
        <Group justify="space-between" mb="xs">
          <SectionLabel>What runs: from the start step to the end of the graph</SectionLabel>
          <Text size="xs" c="dimmed">
            Click a step to make it the start
          </Text>
        </Group>
        <GraphPreview variant={VARIANTS[0]} start={SUITE.start} selected={SUITE.start} />
        <Group gap="xl" mt="sm">
          <Fact label="Start">
            <Mono>{SUITE.start}</Mono>
          </Fact>
          <Fact label="Gate answers">
            <Mono>human-approve</Mono> → approve
          </Fact>
          <Fact label="Default budget">$25, pause when reached</Fact>
        </Group>
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: '420px 1fr', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p="md">
          <SectionLabel>Judge</SectionLabel>
          <Stack gap={6}>
            <Fact label="Who judges">
              A model: {SUITE.judge.who}{' '}
              <Text span size="xs" c="dimmed">
                (or a read-only agent with the workspace)
              </Text>
            </Fact>
            <Fact label="Gather script">
              <Mono>{SUITE.judge.gather ?? ''}</Mono>{' '}
              <Text span size="xs" c="dimmed">
                Python or C#; prints extra data as JSON
              </Text>
            </Fact>
            <Fact label="Receives">both outputs, unlabelled, plus the script's data</Fact>
          </Stack>
          <Textarea label="Judge prompt" mt="xs" autosize minRows={4} defaultValue={SUITE.judge.prompt} />
          <Text size="xs" c="dimmed" mt={6}>
            The step under test returns its normal output. Nothing about judging is added to its prompt.
          </Text>
        </Paper>
        <Paper withBorder radius="md" p="md">
          <Group justify="space-between" mb={6}>
            <SectionLabel>Cases</SectionLabel>
            <Button size="xs" variant="default" leftSection={<TbPlus size={14} />}>
              Add case
            </Button>
          </Group>
          <Table verticalSpacing={6}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Case</Table.Th>
                <Table.Th>From</Table.Th>
                <Table.Th>Expects</Table.Th>
                <Table.Th>Split</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {CASES.map((c) => (
                <Table.Tr key={c.id}>
                  <Table.Td>
                    <Text size="sm" ff="monospace" fw={600}>
                      {c.name}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {c.title}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="dot" color={ORIGIN_META[c.origin].color} radius="sm" tt="none">
                      {ORIGIN_META[c.origin].label}
                    </Badge>
                    <Text size="xs" c="dimmed">
                      {c.pr}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {c.expectations.map((e) => (
                        <Badge key={e.id} size="xs" color={KIND_COLOR[e.kind]} variant="light" radius="sm" tt="none">
                          {e.kind}
                        </Badge>
                      ))}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" color={c.heldOut ? 'indigo' : 'gray'} variant={c.heldOut ? 'light' : 'outline'} radius="sm" tt="none">
                      {c.heldOut ? 'held out' : 'tuning'}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          <Text size="xs" c="dimmed" mt="xs">
            Held-out cases score a proposed prompt or check change, and the agent drafting that change never sees them.
          </Text>
        </Paper>
      </Box>
    </Box>
  )
}

// ──────────────────────────────────── D · Human comments ────────────────────────────────────

function MissedComments() {
  return (
    <Box>
      <PageHeader title="antiphon-demo#1 · What people said that the bot did not" subtitle="Human review comments are read back from the PR and matched against the bot's findings. A comment nothing matched is a miss, and one click makes it a case.">
        <ByoaButton />
      </PageHeader>
      <Paper withBorder radius="md">
        <Table verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Comment</Table.Th>
              <Table.Th>Where</Table.Th>
              <Table.Th>Bot finding</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {HUMAN_COMMENTS.map((h) => (
              <Table.Tr key={h.id}>
                <Table.Td maw={420}>
                  <Text size="sm">"{h.text}"</Text>
                  <Text size="xs" c="dimmed">
                    {h.who}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Mono>{`${h.file}:${h.line}`}</Mono>
                </Table.Td>
                <Table.Td>
                  {h.matched ? (
                    <Box>
                      <Badge size="sm" color="success" variant="light" radius="sm" tt="none">
                        Matched
                      </Badge>
                      <Text size="xs" c="dimmed">
                        {h.matched}
                      </Text>
                    </Box>
                  ) : (
                    <Badge size="sm" color="grape" variant="light" radius="sm" tt="none">
                      No match: the bot missed this
                    </Badge>
                  )}
                </Table.Td>
                <Table.Td>
                  {h.caseName ? (
                    <Text size="xs">
                      Case <Mono>{h.caseName}</Mono>
                    </Text>
                  ) : h.matched ? null : (
                    <Group gap={6} wrap="nowrap">
                      <Button size="xs" leftSection={<TbTargetArrow size={14} />}>
                        Bot missed this
                      </Button>
                      <Button size="xs" variant="default">
                        Not a defect
                      </Button>
                    </Group>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
      <Paper withBorder radius="md" p="md" mt="sm" maw={760}>
        <SectionLabel>How a miss becomes an improvement</SectionLabel>
        <Stack gap={4}>
          {[
            'The comment becomes a case that expects a finding at that file and lines.',
            'The learn loop drafts a change to a prompt or a check as a variant.',
            'It must fix the case, break nothing in the suite, and pass the held-out cases it never saw.',
            'A generality check rejects text that names a file, identifier or PR from the case.',
            'A person approves the proposal PR, which links the experiment as its evidence.',
          ].map((t, i) => (
            <Group key={t} gap={8} wrap="nowrap" align="flex-start">
              <Badge size="sm" circle variant="light" color="active">
                {i + 1}
              </Badge>
              <Text size="sm">{t}</Text>
            </Group>
          ))}
        </Stack>
      </Paper>
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/Experiments/Cases and suites',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) A step's panel on the run graph, with what it read and returned, and the two ways into an experiment. */
export const StepPanelOnGraph: Story = { name: 'A · Step panel on the graph', render: () => <StepPanel /> }

/** (B) A case captured from a human comment the bot missed. */
export const Case: Story = { name: 'B · Case', render: () => <CaseDetail /> }

/** (C) A suite: the start step on the graph, the judge, and its tuning and held-out cases. */
export const SuitePage: Story = { name: 'C · Suite', render: () => <SuiteDetail /> }

/** (D) Human comments matched against bot findings, with "Bot missed this". */
export const HumanComments: Story = { name: 'D · Comments the bot missed', render: () => <MissedComments /> }
