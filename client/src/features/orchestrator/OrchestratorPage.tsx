import { Tabs } from '@mantine/core'
import { useSearchParams } from 'react-router'
import { TbCards, TbSitemap } from 'react-icons/tb'
import { DelegationsBoard } from '../delegations/DelegationsBoard'
import { OrchestratorPanel } from './OrchestratorPanel'

/**
 * "What is the fleet doing right now", in its two forms: cards being dispatched onto boards, and
 * delegated tasks fanning out from an orchestrator. They share a page because they answer the same
 * question — and the tab lives in the URL so a link to a run stays a link to a run.
 */
export function OrchestratorPage() {
  const [params, setParams] = useSearchParams()
  const tab = params.get('tab') === 'delegations' ? 'delegations' : 'cards'

  return (
    <Tabs
      value={tab}
      onChange={(value) => {
        const next = new URLSearchParams(params)
        if (value === 'delegations') next.set('tab', 'delegations')
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
      </Tabs.List>

      <Tabs.Panel value="cards">
        <OrchestratorPanel />
      </Tabs.Panel>
      <Tabs.Panel value="delegations">
        <DelegationsBoard />
      </Tabs.Panel>
    </Tabs>
  )
}
