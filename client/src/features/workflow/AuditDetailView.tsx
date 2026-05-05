import { Box, Text, Badge, Group, Button, Stack, Code, ScrollArea } from '@mantine/core'
import { TbArrowLeft, TbBrain, TbTool } from 'react-icons/tb'
import type { AuditRecordDto } from '../../api/audit'

interface AuditDetailViewProps {
  record: AuditRecordDto
  onBack: () => void
}

interface LlmFullContent {
  model?: string
  stage?: string
  tokensIn?: number
  tokensOut?: number
  prompt?: Array<{ role: string; content: string }>
  output?: string
}

interface ToolFullContent {
  tool?: string
  input?: string
  output?: string
}

function tryParseJson<T>(json: string | null | undefined): T | null {
  if (!json) return null
  try {
    return JSON.parse(json) as T
  } catch {
    return null
  }
}

function MessageBubble({ role, content }: { role: string; content: string }) {
  const isSystem = role === 'system'
  const isUser = role === 'user'
  return (
    <Box
      style={{
        padding: '8px 12px',
        borderRadius: 'var(--mantine-radius-sm)',
        backgroundColor: isSystem
          ? 'var(--mantine-color-dark-6)'
          : isUser
            ? 'var(--mantine-color-dark-7)'
            : 'var(--mantine-color-blue-9)',
        borderLeft: `3px solid ${
          isSystem
            ? 'var(--mantine-color-gray-6)'
            : isUser
              ? 'var(--mantine-color-green-7)'
              : 'var(--mantine-color-blue-5)'
        }`,
      }}
    >
      <Text size="xs" fw={600} c="dimmed" mb={4} tt="uppercase">
        {role}
      </Text>
      <Text size="sm" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
        {content}
      </Text>
    </Box>
  )
}

function LlmCallDetail({ record, fullContent }: { record: AuditRecordDto; fullContent: LlmFullContent | null }) {
  return (
    <Stack gap="md">
      {/* Meta */}
      <Group gap="xs">
        {record.modelName && (
          <Badge size="sm" variant="light" color="blue">{record.modelName}</Badge>
        )}
        {record.tokensIn > 0 && (
          <Text size="xs" c="dimmed">
            {record.tokensIn.toLocaleString()} in / {record.tokensOut.toLocaleString()} out
          </Text>
        )}
        {record.durationMs > 0 && (
          <Text size="xs" c="dimmed">
            {record.durationMs < 1000 ? `${record.durationMs}ms` : `${(record.durationMs / 1000).toFixed(1)}s`}
          </Text>
        )}
      </Group>

      {/* Prompt messages */}
      {fullContent?.prompt && fullContent.prompt.length > 0 ? (
        <Box>
          <Text size="sm" fw={600} mb="xs">Prompt Messages</Text>
          <Stack gap="xs">
            {fullContent.prompt.map((msg, i) => (
              <MessageBubble key={i} role={msg.role} content={msg.content} />
            ))}
          </Stack>
        </Box>
      ) : (
        <Text size="sm" c="dimmed">No prompt data available (recorded before this feature was added).</Text>
      )}

      {/* Response */}
      {fullContent?.output ? (
        <Box>
          <Text size="sm" fw={600} mb="xs">Response</Text>
          <Box
            style={{
              padding: '12px',
              borderRadius: 'var(--mantine-radius-sm)',
              backgroundColor: 'var(--mantine-color-blue-9)',
              borderLeft: '3px solid var(--mantine-color-blue-5)',
            }}
          >
            <Text size="sm" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {fullContent.output}
            </Text>
          </Box>
        </Box>
      ) : (
        <Text size="sm" c="dimmed">No response data available.</Text>
      )}
    </Stack>
  )
}

function ToolCallDetail({ fullContent }: { fullContent: ToolFullContent | null }) {
  const formatJson = (str: string | null | undefined) => {
    if (!str) return ''
    try {
      return JSON.stringify(JSON.parse(str), null, 2)
    } catch {
      return str
    }
  }

  return (
    <Stack gap="md">
      {/* Input */}
      <Box>
        <Text size="sm" fw={600} mb="xs">Input</Text>
        {fullContent?.input ? (
          <ScrollArea.Autosize mah={300}>
            <Code block style={{ fontSize: '0.75rem', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>
              {formatJson(fullContent.input)}
            </Code>
          </ScrollArea.Autosize>
        ) : (
          <Text size="sm" c="dimmed">No input data available.</Text>
        )}
      </Box>

      {/* Output */}
      <Box>
        <Text size="sm" fw={600} mb="xs">Output</Text>
        {fullContent?.output ? (
          <ScrollArea.Autosize mah={400}>
            <Code block style={{ fontSize: '0.75rem', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>
              {fullContent.output}
            </Code>
          </ScrollArea.Autosize>
        ) : (
          <Text size="sm" c="dimmed">No output data available.</Text>
        )}
      </Box>
    </Stack>
  )
}

export function AuditDetailView({ record, onBack }: AuditDetailViewProps) {
  const isLlmCall = record.eventType === 'LlmCall'
  const isToolCall = record.eventType === 'ToolInvocation'

  const llmContent = isLlmCall ? tryParseJson<LlmFullContent>(record.fullContent) : null
  const toolContent = isToolCall ? tryParseJson<ToolFullContent>(record.fullContent) : null

  const toolName = toolContent?.tool ?? record.summary

  return (
    <Box style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <Box
        style={{
          flexShrink: 0,
          padding: '8px 12px',
          borderBottom: '1px solid var(--mantine-color-dark-4)',
          display: 'flex',
          alignItems: 'center',
          gap: 8,
        }}
      >
        <Button
          variant="subtle"
          size="xs"
          leftSection={<TbArrowLeft size={14} />}
          onClick={onBack}
        >
          Back
        </Button>
        {isLlmCall && <TbBrain size={16} color="var(--mantine-color-blue-4)" />}
        {isToolCall && <TbTool size={16} color="var(--mantine-color-grape-4)" />}
        <Text size="sm" fw={600}>
          {isLlmCall
            ? `LLM Call — ${record.modelName ?? 'unknown model'}`
            : isToolCall
              ? `Tool Call — ${toolName}`
              : record.eventType}
        </Text>
        <Text size="xs" c="dimmed" ml="auto">
          {new Date(record.createdAt).toLocaleTimeString()}
        </Text>
      </Box>

      {/* Content */}
      <ScrollArea style={{ flex: 1 }} p="md">
        {isLlmCall && <LlmCallDetail record={record} fullContent={llmContent} />}
        {isToolCall && <ToolCallDetail fullContent={toolContent} />}
        {!isLlmCall && !isToolCall && (
          <Stack gap="xs">
            <Text size="sm" fw={600}>{record.summary}</Text>
            {record.fullContent && (
              <Code block style={{ fontSize: '0.75rem' }}>{record.fullContent}</Code>
            )}
          </Stack>
        )}
      </ScrollArea>
    </Box>
  )
}
