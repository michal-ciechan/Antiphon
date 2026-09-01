import { Badge, Stack, Tabs } from '@mantine/core'
import { useSearchParams } from 'react-router'
import { TbAlertHexagon, TbCards, TbHelpCircle, TbSitemap } from 'react-icons/tb'
import { useAttention } from '../../api/attention'
import { AttentionPanel } from '../attention/AttentionPanel'
import { DecisionsPanel } from '../attention/DecisionsPanel'
import { DelegationsBoard } from '../delegations/DelegationsBoard'
import { ModelAvailabilityPanel } from './ModelAvailabilityPanel'
import { OrchestratorPanel } from './OrchestratorPanel'

const TABS = ['cards', 'delegations', 'attention', 'decisions'] as const
type TabValue = (typeof TABS)[number]

/**
 * "What is the fleet doing right now", in its three forms: cards being dispatched onto boards,
 * delegated tasks fanning out from an orchestrator, and — the diagnostic one — what is stuck.
 * They share a page because they answer the same question, and because a stuck row's natural
 * click-through is the task drawer on the tab next door. The tab lives in the URL so a link to a
 * run stays a link to a run.
 */
export function OrchestratorPage() {
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')
  const tab: TabValue = TABS.includes(requested as TabValue) ? (requested as TabValue) : 'cards'
  const attention = useAttention()

  // Settled failures are carried as context and must not make a healthy fleet look busy — the badge
  // counts only conditions that are still open, matching the panel's own header.
  const openCount = (attention.data?.items ?? []).filter((item) => item.kind !== 'RecentFailure').length
  const decisionCount = (attention.data?.items ?? []).filter((item) => item.kind === 'CardNeedsDecision').length

  return (
    <Tabs
      value={tab}
      keepMounted={false}
      onChange={(value) => {
        const next = new URLSearchParams(params)
        if (value && value !== 'cards') next.set('tab', value)
        else next.delete('tab')
        setParams(next, { replace: true })
      }}
    >
      <Tabs.List mb="md">
        <Tabs.Tab value="cards" leftSection={<TbCards size={16} />}>
          Cards
        </Tabs.Tab>
        <Tabs.Tab value="delegations" leftSection={<TbSitemap size={16} />}>
          Delegations
        </Tabs.Tab>
        <Tabs.Tab
          value="attention"
          leftSection={<TbAlertHexagon size={16} />}
          rightSection={
            openCount > 0 ? (
              <Badge size="xs" variant="filled" color="danger" circle>
                {openCount}
              </Badge>
            ) : null
          }
        >
          Needs attention
        </Tabs.Tab>
        <Tabs.Tab
          value="decisions"
          leftSection={<TbHelpCircle size={16} />}
          rightSection={
            decisionCount > 0 ? (
              <Badge size="xs" variant="filled" color="danger" circle>{decisionCount}</Badge>
            ) : null
          }
        >
          Decisions
        </Tabs.Tab>
      </Tabs.List>

      <Tabs.Panel value="cards">
        <OrchestratorPanel />
      </Tabs.Panel>
      <Tabs.Panel value="delegations">
        <DelegationsBoard />
      </Tabs.Panel>
      <Tabs.Panel value="attention">
        <Stack gap="md">
          <ModelAvailabilityPanel />
          <AttentionPanel />
        </Stack>
      </Tabs.Panel>
      <Tabs.Panel value="decisions">
        <DecisionsPanel />
      </Tabs.Panel>
    </Tabs>
  )
}
