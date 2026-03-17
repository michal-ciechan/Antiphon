import { useRef, useCallback } from 'react'
import { Box, Tabs, Text } from '@mantine/core'

export type ContextTab = 'outputs' | 'stage-info' | 'conversation' | 'diff' | 'audit'

interface ContextPanelProps {
  activeTab: ContextTab
  onTabChange: (tab: ContextTab) => void
}

const TAB_CONFIG: { value: ContextTab; label: string; emptyText: string }[] = [
  { value: 'outputs', label: 'Outputs', emptyText: 'No outputs yet.' },
  { value: 'stage-info', label: 'Stage Info', emptyText: 'No stage information available.' },
  { value: 'conversation', label: 'Conversation', emptyText: 'No conversation history yet.' },
  { value: 'diff', label: 'Diff', emptyText: 'No diff available yet.' },
  { value: 'audit', label: 'Audit', emptyText: 'No audit data yet.' },
]

export function ContextPanel({ activeTab, onTabChange }: ContextPanelProps) {
  // Per-tab scroll preservation
  const scrollPositions = useRef<Record<string, number>>({})

  const handleTabChange = useCallback(
    (value: string | null) => {
      if (!value) return
      // Save current scroll position
      const currentPanel = document.querySelector('[data-context-panel-content]')
      if (currentPanel) {
        scrollPositions.current[activeTab] = currentPanel.scrollTop
      }
      onTabChange(value as ContextTab)
      // Restore scroll position for the new tab after render
      requestAnimationFrame(() => {
        const panel = document.querySelector('[data-context-panel-content]')
        if (panel) {
          panel.scrollTop = scrollPositions.current[value] ?? 0
        }
      })
    },
    [activeTab, onTabChange],
  )

  return (
    <Box
      style={{
        width: 360,
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        borderLeft: '1px solid var(--mantine-color-dark-4)',
      }}
    >
      <Tabs
        value={activeTab}
        onChange={handleTabChange}
        style={{ display: 'flex', flexDirection: 'column', height: '100%' }}
      >
        <Tabs.List>
          {TAB_CONFIG.map((tab) => (
            <Tabs.Tab key={tab.value} value={tab.value} style={{ fontSize: '0.75rem' }}>
              {tab.label}
            </Tabs.Tab>
          ))}
        </Tabs.List>

        {TAB_CONFIG.map((tab) => (
          <Tabs.Panel
            key={tab.value}
            value={tab.value}
            data-context-panel-content
            style={{
              flex: 1,
              overflow: 'auto',
              padding: 'var(--mantine-spacing-sm)',
            }}
          >
            <Text c="dimmed" size="sm">
              {tab.emptyText}
            </Text>
          </Tabs.Panel>
        ))}
      </Tabs>
    </Box>
  )
}
