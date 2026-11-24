import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import {
  Alert,
  Badge,
  Box,
  Button,
  Checkbox,
  Code,
  CopyButton,
  Divider,
  Group,
  Paper,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  TextInput,
} from '@mantine/core'
import { TbCheck, TbCopy, TbKey, TbPlus, TbSearch, TbShieldCheck, TbTrash } from 'react-icons/tb'
import { VARIANTS } from '../experiments/mockData'
import { ByoaButton, ByoaIcon, Fact, GraphPreview, PageHeader, SectionLabel } from '../experiments/shared'

/**
 * PROPOSAL MOCK — not production code. BYOA (bring your own agent),
 * `docs/features/014-byoa/plan.md`: on any page, click the BYOA icon and copy a prompt that lets an
 * agent on your own machine do what the page does, as you.
 *   A — the help drawer on a page, on its "BYOA: for your agent" tab
 *   B — the same drawer with a key included
 *   C — BYOA keys: listed, scoped, revocable
 *   D — the BYOA guide: the queryable index and the placeholder reference
 *   E — the icon, and the line the READMEs carry
 */

const BASE_URL = 'https://review.server2.codeperf.net'

function prompt(withKey: boolean, access: string): string {
  const key = withKey ? 'byoa_live_7Hq2…(shown once)' : '{{API_KEY}}'
  const may =
    access === 'read'
      ? 'You may read only. Do not change anything.'
      : access === 'approve'
        ? 'You may read, change, run and approve.'
        : 'You may read, change and run. You may not approve.'
  return `# Customise a variant

You are acting for michal-ciechan on the Antiphon instance at ${BASE_URL}.
Send \`Authorization: Bearer ${key}\` with every request.
${withKey ? 'Keep the key in the ANTIPHON_API_KEY environment variable. Never write it to a file, a commit or your output.\nKey scopes: experiments.read, experiments.write, experiments.run · expires: never (revocable)' : 'Set ANTIPHON_API_KEY first: make a key under your user menu → BYOA keys.'}
${may}

## What this is
A variant is a graph setup: the workflow graph plus each step's model, prompt and settings.
This one is "Two verify passes" (var-two-verify), compared in suite demo-review.

## What you can do
Paths are relative to ${BASE_URL}.
- Read this variant:         GET   /api/eval/variants/var-two-verify
- Change a step's settings:  PATCH /api/eval/variants/var-two-verify/steps/<stepId>
- Change the graph:          PUT   /api/eval/variants/var-two-verify/graph
- Validate a graph:          POST  /api/review-workflows/validate
- Save as a new variant:     POST  /api/eval/variants
- Run suite demo-review:     POST  /api/eval/experiments

## Rules
- Saving always makes a new variant. You cannot change the live workflow from here.
- An experiment pauses when its budget is reached. Report that; do not raise the budget unless asked.
- Do not approve, decide a gate, dismiss a blocker or merge unless the person asked for that
  action or gave you standing permission for it. When unsure, say what you would approve and why, and wait.

## More
Other pages:  GET /api/byoa?q=<words>
Placeholders: GET /api/byoa/placeholders
Server version: 2026.11.24.2-rc.3`
}

// ───────────────────────────────────────── A/B · Drawer ─────────────────────────────────────────

function HelpDrawer({ withKey = false }: { withKey?: boolean }) {
  const [tab, setTab] = useState<string | null>('agent')
  const [includeKey, setIncludeKey] = useState(withKey)
  const [scope, setScope] = useState('item')
  const [access, setAccess] = useState('change')
  const text = prompt(includeKey, access)
  return (
    <Box style={{ display: 'grid', gridTemplateColumns: '1fr 680px', gap: 0, minHeight: '100vh', margin: '-2rem' }}>
      <Box p="2rem" style={{ opacity: 0.45, pointerEvents: 'none' }}>
        <PageHeader title="Customise variant: Two verify passes" subtitle="Change a step's settings or the graph itself. Saving makes a new variant; the live workflow is never touched.">
          <ByoaButton />
          <Button variant="default">Validate</Button>
          <Button>Save as new variant</Button>
        </PageHeader>
        <Paper withBorder radius="md" p="md">
          <GraphPreview variant={VARIANTS[2]} start="categorise" selected="verify-2" compact />
        </Paper>
      </Box>
      <Paper radius={0} p="md" style={{ borderLeft: '1px solid var(--mantine-color-dark-4)', background: 'var(--mantine-color-dark-7)' }}>
        <Group justify="space-between" mb="xs">
          <Group gap={10}>
            <ByoaIcon size={22} />
            <Box>
              <Text fw={700}>Help: customise a variant</Text>
              <Text size="xs" c="dimmed">
                BYOA (bring your own agent): give this to an agent on your machine
              </Text>
            </Box>
          </Group>
          <Button size="xs" variant="subtle" color="gray">
            Close
          </Button>
        </Group>
        <Tabs value={tab} onChange={setTab}>
          <Tabs.List>
            <Tabs.Tab value="you">For you</Tabs.Tab>
            <Tabs.Tab value="agent" leftSection={<ByoaIcon size={14} />}>
              BYOA: for your agent
            </Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="you" pt="sm">
            <Text size="sm">Click a step to change its model, prompt or thresholds. Use Add step to change the graph. Validate checks it before you save.</Text>
          </Tabs.Panel>
          <Tabs.Panel value="agent" pt="sm">
            <Stack gap="xs">
              <Group gap="lg" align="flex-end">
                <Box>
                  <Text size="xs" c="dimmed" mb={2}>
                    Scope
                  </Text>
                  <SegmentedControl size="xs" value={scope} onChange={setScope} data={[{ value: 'item', label: 'This variant' }, { value: 'area', label: 'All of Experiments' }]} />
                </Box>
                <Box>
                  <Text size="xs" c="dimmed" mb={2}>
                    The agent may
                  </Text>
                  <SegmentedControl size="xs" value={access} onChange={setAccess} data={[{ value: 'read', label: 'Read' }, { value: 'change', label: 'Change and run' }, { value: 'approve', label: 'Also approve' }]} />
                </Box>
              </Group>
              <Switch
                checked={includeKey}
                onChange={(e) => setIncludeKey(e.currentTarget.checked)}
                label="Include a key"
                description={includeKey ? 'A new BYOA key for you, limited to the scope above. It does not expire; revoke it under BYOA keys.' : 'Off: the text keeps {{API_KEY}} and says which environment variable to set.'}
              />
              {includeKey && (
                <Alert color="warning" variant="light" p="xs" icon={<TbKey size={16} />}>
                  <Text size="xs">The copied text contains a key that acts as you. Paste it only into your own agent.</Text>
                </Alert>
              )}
              <Box pos="relative">
                <Code block fz={11.5} mah={520} style={{ overflow: 'auto', whiteSpace: 'pre-wrap' }}>
                  {text}
                </Code>
              </Box>
              <Group justify="space-between">
                <Text size="xs" c="dimmed">
                  Filled from this page: base URL, your login, variant and suite ids.{includeKey ? '' : ' Still to supply: API_KEY.'}
                </Text>
                <CopyButton value={text}>
                  {({ copied, copy }) => (
                    <Button size="sm" onClick={copy} color={copied ? 'success' : undefined} leftSection={copied ? <TbCheck size={16} /> : <TbCopy size={16} />}>
                      {copied ? 'Copied' : 'Copy for agent'}
                    </Button>
                  )}
                </CopyButton>
              </Group>
              <Text size="xs" c="dimmed">
                The source is <Code fz={10}>docs/byoa/experiments/variant.md</Code>. An agent can also read the whole folder from <Code fz={10}>docs/byoa/index.md</Code>.
              </Text>
            </Stack>
          </Tabs.Panel>
        </Tabs>
      </Paper>
    </Box>
  )
}

// ────────────────────────────────────────── C · Keys ──────────────────────────────────────────

const KEYS = [
  { name: 'laptop claude code', scopes: ['experiments.read', 'experiments.write', 'experiments.run'], item: 'All of Experiments', created: '24 Nov', lastUsed: '4 minutes ago', expires: 'Never', uses: 212 },
  { name: 'review helper', scopes: ['reviews.read', 'reviews.write', 'reviews.approve'], item: 'All of Reviews', created: '23 Nov', lastUsed: 'yesterday, 18:02', expires: 'Never', uses: 37 },
  { name: 'variant var-two-verify', scopes: ['experiments.read', 'experiments.write'], item: 'One variant', created: '24 Nov', lastUsed: 'never', expires: 'Never', uses: 0 },
  { name: 'ci suite gate', scopes: ['experiments.read', 'experiments.run'], item: 'Suite demo-review', created: '20 Nov', lastUsed: '2 hours ago', expires: '20 Feb 2027', uses: 64 },
]

function KeysPage() {
  return (
    <Box>
      <PageHeader
        title={
          <Group gap={10}>
            <ByoaIcon size={22} />
            <span>BYOA keys</span>
          </Group>
        }
        subtitle="A BYOA key lets an agent on your machine act as you. It can never do more than you can, it is limited to the scopes below, and everything it does is marked “via BYOA” beside your name."
      >
        <Button leftSection={<TbPlus size={16} />}>New key</Button>
      </PageHeader>
      <Paper withBorder radius="md" mb="sm">
        <Table verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>May</Table.Th>
              <Table.Th>Limited to</Table.Th>
              <Table.Th>Created</Table.Th>
              <Table.Th>Last used</Table.Th>
              <Table.Th>Expires</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {KEYS.map((k) => (
              <Table.Tr key={k.name}>
                <Table.Td>
                  <Text size="sm" fw={600}>
                    {k.name}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {k.uses} requests
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Group gap={4} maw={320}>
                    {k.scopes.map((s) => (
                      <Badge key={s} size="sm" radius="sm" tt="none" variant="light" color={s.endsWith('.approve') ? 'warning' : s.endsWith('.read') ? 'gray' : 'active'} style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
                        {s}
                      </Badge>
                    ))}
                  </Group>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{k.item}</Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{k.created}</Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm" c={k.lastUsed === 'never' ? 'dimmed' : undefined}>
                    {k.lastUsed}
                  </Text>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{k.expires}</Text>
                </Table.Td>
                <Table.Td>
                  <Button size="xs" variant="subtle" color="danger" leftSection={<TbTrash size={14} />}>
                    Revoke
                  </Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p="md">
          <SectionLabel>New key</SectionLabel>
          <Stack gap="xs">
            <TextInput label="Name" placeholder="Which agent or machine is this for?" defaultValue="desktop codex" />
            <Group grow align="flex-start">
              <Select label="Area" data={['Experiments', 'Reviews', 'Review queue', 'Boards', 'Agents']} defaultValue="Reviews" />
              <Select label="Lifetime" data={['Never expires (instance default)', '8 hours', '7 days', '90 days']} defaultValue="Never expires (instance default)" />
            </Group>
            <Group gap="lg">
              <Checkbox label="Read" defaultChecked />
              <Checkbox label="Change" defaultChecked />
              <Checkbox label="Run" defaultChecked />
              <Checkbox label="Approve" />
            </Group>
            <Text size="xs" c="dimmed">
              Approve lets the agent decide gates, approve and dismiss as you. The help text tells it not to do so unless you asked or gave it standing permission.
            </Text>
            <Group justify="flex-end">
              <Button>Create key</Button>
            </Group>
          </Stack>
        </Paper>
        <Paper withBorder radius="md" p="md">
          <SectionLabel>What a key can never do</SectionLabel>
          <Stack gap={6}>
            {['Make or widen keys', 'Change sign-in or identity settings', 'Read secrets', 'Adjust trust scores', 'Do anything you cannot do yourself'].map((t) => (
              <Group key={t} gap={8} wrap="nowrap">
                <TbShieldCheck size={16} color="var(--mantine-color-success-5)" />
                <Text size="sm">{t}</Text>
              </Group>
            ))}
          </Stack>
          <Divider my="sm" />
          <Fact label="Default lifetime">Never expires · set by the instance (Settings → BYOA)</Fact>
          <Fact label="Shown">once, when created. Only a hash is stored.</Fact>
        </Paper>
      </Box>
    </Box>
  )
}

// ────────────────────────────────────────── D · Guide ──────────────────────────────────────────

const AREAS = [
  { area: 'Experiments', files: 4, actions: 'Customise a variant · run a suite · read a comparison · save a case', path: 'experiments/index.md' },
  { area: 'Reviews', files: 3, actions: 'Read a run · respond to a finding · ask for another round · refresh', path: 'reviews/index.md' },
  { area: 'Review queue', files: 1, actions: 'Assign · pass · decide a gate (approve scope)', path: 'review-queue/index.md' },
  { area: 'Boards', files: 2, actions: 'Read a card · move a card · comment', path: 'boards/index.md' },
  { area: 'Agents', files: 2, actions: 'List sessions · send a message · read a transcript', path: 'agents/index.md' },
]

const PLACEHOLDERS = [
  { name: 'BASE_URL', meaning: 'The address you use the site at', from: 'Filled by the page', example: BASE_URL },
  { name: 'API_KEY', meaning: 'A BYOA key that acts as you', from: 'User menu → BYOA keys, or “Include a key”', example: 'byoa_live_…' },
  { name: 'USER_LOGIN', meaning: 'The signed-in person', from: 'Filled by the page', example: 'michal-ciechan' },
  { name: 'VARIANT_ID', meaning: 'One graph setup', from: 'The variant page address, or GET /api/eval/variants', example: 'var-two-verify' },
  { name: 'SUITE_ID', meaning: 'A named list of cases', from: 'GET /api/eval/suites', example: 'suite-demo-review' },
  { name: 'RUN_ID', meaning: 'One review run', from: 'The run page address, or GET /api/review-runs', example: '8c1f…e02a' },
]

function Guide() {
  return (
    <Box>
      <PageHeader
        title={
          <Group gap={10}>
            <ByoaIcon size={22} />
            <span>BYOA guide</span>
          </Group>
        }
        subtitle="Everything an agent needs to work with Antiphon for you, in one folder. Point an agent at it, or search it."
      >
        <CopyButton value={`Read ${BASE_URL}/api/byoa and follow it.`}>
          {({ copied, copy }) => (
            <Button onClick={copy} leftSection={copied ? <TbCheck size={16} /> : <TbCopy size={16} />}>
              Copy “point an agent here”
            </Button>
          )}
        </CopyButton>
      </PageHeader>
      <Paper withBorder radius="md" p="md" mb="sm">
        <Group gap="xs" mb="xs">
          <TextInput placeholder="Search actions, pages and routes" defaultValue="approve a gate" leftSection={<TbSearch size={14} />} w={360} />
          <Text size="xs" c="dimmed">
            Same as <Code fz={10}>GET /api/byoa?q=approve+a+gate</Code>
          </Text>
        </Group>
        <Paper radius="sm" p="xs" bg="dark.7">
          <Group justify="space-between">
            <Box>
              <Text size="sm" fw={600}>
                Decide a gate
              </Text>
              <Text size="xs" c="dimmed">
                <Code fz={10}>review-queue/gate.md</Code> · route /review-queue · needs <Code fz={10}>reviews.approve</Code> · placeholders BASE_URL, API_KEY, GATE_ID
              </Text>
            </Box>
            <Button size="xs" variant="default" leftSection={<TbCopy size={14} />}>
              Copy for agent
            </Button>
          </Group>
        </Paper>
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p="md">
          <SectionLabel>Index: docs/byoa/index.md</SectionLabel>
          <Table verticalSpacing={6}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Area</Table.Th>
                <Table.Th>What an agent can do</Table.Th>
                <Table.Th>Files</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {AREAS.map((a) => (
                <Table.Tr key={a.area}>
                  <Table.Td>
                    <Text size="sm" fw={600}>
                      {a.area}
                    </Text>
                    <Text size="xs" c="dimmed" ff="monospace">
                      {a.path}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{a.actions}</Text>
                  </Table.Td>
                  <Table.Td>{a.files}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          <Text size="xs" c="dimmed" mt="xs">
            The indexes are generated from each help file's header. Reading them needs no key.
          </Text>
        </Paper>
        <Paper withBorder radius="md" p="md">
          <SectionLabel>Placeholders: docs/byoa/placeholders.md</SectionLabel>
          <Table verticalSpacing={6}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Placeholder</Table.Th>
                <Table.Th>Meaning</Table.Th>
                <Table.Th>Where the value comes from</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {PLACEHOLDERS.map((p) => (
                <Table.Tr key={p.name}>
                  <Table.Td>
                    <Code fz={11}>{`{{${p.name}}}`}</Code>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{p.meaning}</Text>
                    <Text size="xs" c="dimmed" ff="monospace">
                      e.g. {p.example}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{p.from}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      </Box>
    </Box>
  )
}

// ────────────────────────────────────────── E · Icon ──────────────────────────────────────────

function IconAndReadme() {
  return (
    <Box maw={900}>
      <PageHeader title="The BYOA icon" subtitle="One icon, one meaning, everywhere on the site: click it to get a prompt for your AI agent." />
      <Paper withBorder radius="md" p="lg" mb="sm">
        <SectionLabel>Sizes</SectionLabel>
        <Group gap="xl" align="flex-end">
          {[14, 18, 24, 36, 56].map((s) => (
            <Stack key={s} gap={6} align="center">
              <ByoaIcon size={s} />
              <Text size="xs" c="dimmed">
                {s}px
              </Text>
            </Stack>
          ))}
        </Group>
      </Paper>
      <Paper withBorder radius="md" p="lg" mb="sm">
        <SectionLabel>Where it appears</SectionLabel>
        <Stack gap="sm">
          <Group gap="sm">
            <ByoaButton />
            <Text size="sm">In every page header, with the tooltip “BYOA: get a prompt for your AI agent”.</Text>
          </Group>
          <Group gap="sm">
            <ByoaButton />
            <Text size="sm">On panels with their own actions: a graph step, a variant, a finding, a gate.</Text>
          </Group>
          <Group gap="sm">
            <Badge size="sm" variant="light" color="grape" radius="sm" tt="none">
              via BYOA
            </Badge>
            <Text size="sm">Beside your name, on anything an agent did with one of your keys.</Text>
          </Group>
        </Stack>
      </Paper>
      <Paper withBorder radius="md" p="lg">
        <SectionLabel>In the README (main README.md and docs/byoa/README.md)</SectionLabel>
        <Paper radius="sm" p="md" bg="dark.7">
          <Text fw={700} fz={18} mb={6}>
            BYOA: bring your own agent
          </Text>
          <Group gap={12} wrap="nowrap" align="center">
            <ByoaIcon size={36} />
            <Text size="sm">
              When you see this icon in Antiphon, click it to get a prompt to give to your AI agent, so your local agent can work with Antiphon for you. The help files are in{' '}
              <Code fz={11}>docs/byoa/</Code>; start at <Code fz={11}>docs/byoa/index.md</Code>.
            </Text>
          </Group>
        </Paper>
      </Paper>
    </Box>
  )
}

const meta: Meta = {
  title: 'Proposals/BYOA/Bring your own agent',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) The help drawer on the variant editor, on its BYOA tab: scope, what the agent may do, and the filled prompt with the key left as a placeholder. */
export const Drawer: Story = { name: 'A · Help drawer', render: () => <HelpDrawer /> }

/** (B) The same drawer with "Include a key" on: the prompt carries a new scoped key and says how to keep it. */
export const DrawerWithKey: Story = { name: 'B · Help drawer with a key', render: () => <HelpDrawer withKey /> }

/** (C) BYOA keys: what each may do, what it is limited to, when it was last used, and revoke. */
export const Keys: Story = { name: 'C · BYOA keys', render: () => <KeysPage /> }

/** (D) The guide: search, the generated index of areas, and the placeholder reference. */
export const GuidePage: Story = { name: 'D · BYOA guide', render: () => <Guide /> }

/** (E) The icon at each size, where it appears, and the README line. */
export const Icon: Story = { name: 'E · Icon and README line', render: () => <IconAndReadme /> }
