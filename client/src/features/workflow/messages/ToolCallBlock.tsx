import { useState } from 'react'
import { Box, Text, Group, UnstyledButton, Collapse, Code } from '@mantine/core'
import { VscTerminal, VscChevronDown, VscChevronRight } from 'react-icons/vsc'
import type { TimelineMessage } from '../types'

interface ToolCallBlockProps {
  message: TimelineMessage
}

function getToolSubtitle(toolName: string | null, toolInputJson: string | null): string | null {
  if (!toolName || !toolInputJson) return null
  try {
    const input = JSON.parse(toolInputJson)
    switch (toolName.toLowerCase()) {
      case 'git': return input.args ?? null
      case 'glob': return input.path ? `${input.pattern} in ${input.path}` : (input.pattern ?? null)
      case 'grep': return input.path ? `${input.pattern} in ${input.path}` : (input.pattern ?? null)
      case 'bash': return input.command ?? null
      case 'file_read':
      case 'file_write':
      case 'file_edit': return input.path ?? null
      default: return null
    }
  } catch {
    return null
  }
}

export function ToolCallBlock({ message }: ToolCallBlockProps) {
  const [expanded, setExpanded] = useState(false)
  const subtitle = getToolSubtitle(message.toolName ?? null, message.toolInput ?? null)

  return (
    <Box
      style={{
        padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
        marginBottom: 'var(--mantine-spacing-xs)',
        borderRadius: 'var(--mantine-radius-sm)',
        backgroundColor: 'var(--mantine-color-dark-8)',
        border: '1px solid var(--mantine-color-dark-5)',
      }}
    >
      <UnstyledButton onClick={() => setExpanded((v) => !v)} style={{ width: '100%' }}>
        <Group gap="xs">
          {expanded ? (
            <VscChevronDown size={14} color="var(--mantine-color-dimmed)" />
          ) : (
            <VscChevronRight size={14} color="var(--mantine-color-dimmed)" />
          )}
          <VscTerminal size={14} color="var(--mantine-color-warning-5)" />
          <Text size="xs" fw={600} c="warning">
            {message.toolName ?? 'Tool Call'}
          </Text>
          {subtitle && (
            <Text size="xs" c="dimmed" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 300 }}>
              {subtitle}
            </Text>
          )}
          <Text size="xs" c="dimmed" ml="auto">
            {new Date(message.timestamp).toLocaleTimeString()}
          </Text>
        </Group>
      </UnstyledButton>

      <Collapse in={expanded}>
        <Box mt="xs">
          {message.toolInput && (
            <Box mb="xs">
              <Text size="xs" c="dimmed" mb={2}>
                Input:
              </Text>
              <Code block style={{ fontSize: '0.75rem', maxHeight: 200, overflow: 'auto' }}>
                {message.toolInput}
              </Code>
            </Box>
          )}
          {message.toolOutput && (
            <Box>
              <Text size="xs" c="dimmed" mb={2}>
                Output:
              </Text>
              <Code block style={{ fontSize: '0.75rem', maxHeight: 200, overflow: 'auto' }}>
                {message.toolOutput}
              </Code>
            </Box>
          )}
          {!message.toolInput && !message.toolOutput && (
            <Text size="xs" c="dimmed">
              {message.content}
            </Text>
          )}
        </Box>
      </Collapse>
    </Box>
  )
}
