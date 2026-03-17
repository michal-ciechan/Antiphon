import { useState, useCallback } from 'react'
import { Group, TextInput, Button } from '@mantine/core'
import { VscSend, VscComment } from 'react-icons/vsc'

interface PromptBarProps {
  onSendToAgent: (text: string) => void
  onAddComment: (text: string) => void
  disabled?: boolean
}

export function PromptBar({ onSendToAgent, onAddComment, disabled = false }: PromptBarProps) {
  const [text, setText] = useState('')

  const handleSendToAgent = useCallback(() => {
    const trimmed = text.trim()
    if (!trimmed) return
    onSendToAgent(trimmed)
    setText('')
  }, [text, onSendToAgent])

  const handleAddComment = useCallback(() => {
    const trimmed = text.trim()
    if (!trimmed) return
    onAddComment(trimmed)
    setText('')
  }, [text, onAddComment])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault()
        handleSendToAgent()
      }
    },
    [handleSendToAgent],
  )

  return (
    <Group gap="xs" style={{ flex: 1 }} wrap="nowrap">
      <TextInput
        placeholder="Type feedback or comment..."
        value={text}
        onChange={(e) => setText(e.currentTarget.value)}
        onKeyDown={handleKeyDown}
        disabled={disabled}
        style={{ flex: 1 }}
        size="sm"
      />
      <Button
        size="sm"
        color="active"
        leftSection={<VscSend size={14} />}
        onClick={handleSendToAgent}
        disabled={disabled || !text.trim()}
      >
        Send to Agent
      </Button>
      <Button
        size="sm"
        variant="default"
        leftSection={<VscComment size={14} />}
        onClick={handleAddComment}
        disabled={disabled || !text.trim()}
      >
        Add Comment
      </Button>
    </Group>
  )
}
