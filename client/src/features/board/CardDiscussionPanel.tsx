import { Alert, Badge, Box, Button, Group, Loader, Paper, Stack, Text, Textarea } from '@mantine/core'
import { useState } from 'react'
import { TbAlertCircle, TbMessage } from 'react-icons/tb'
import type { CardDiscussionCommentDto } from '../../api/boards'
import { useCardDiscussion, useCreateCardDiscussion } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'

interface CardDiscussionPanelProps {
  cardId: string
}

/**
 * CARD-0166: stored discussion thread on a card. Distinct from DiffReview's "comment to agent"
 * which injects into a live session via POST /comments.
 */
export function CardDiscussionPanel({ cardId }: CardDiscussionPanelProps) {
  const { data, isLoading, error } = useCardDiscussion(cardId)
  const create = useCreateCardDiscussion(cardId)
  const [body, setBody] = useState('')
  const author = 'operator'

  const handleSubmit = () => {
    const trimmed = body.trim()
    if (!trimmed || create.isPending) return
    create.mutate(
      { body: trimmed, author },
      {
        onSuccess: () => setBody(''),
      },
    )
  }

  if (isLoading) {
    return (
      <Group justify="center" p="xl">
        <Loader size="sm" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light" m="sm">
        {getApiErrorMessage(error, 'Discussion failed to load')}
      </Alert>
    )
  }

  return (
    <Stack gap="sm" p="sm" data-testid="card-discussion">
      {(data?.length ?? 0) === 0 ? (
        <Text c="dimmed" size="sm" data-testid="card-discussion-empty">
          No discussion yet. Comments here sync with the external tracker when tracking is active.
        </Text>
      ) : (
        <Stack gap="xs" data-testid="card-discussion-list">
          {data!.map((comment) => (
            <CommentRow key={comment.id} comment={comment} />
          ))}
        </Stack>
      )}

      <Paper withBorder p="xs" data-testid="card-discussion-composer">
        <Stack gap="xs">
          <Textarea
            aria-label="Discussion comment"
            placeholder="Add a comment…"
            minRows={2}
            autosize
            value={body}
            onChange={(event) => setBody(event.currentTarget.value)}
          />
          <Group justify="space-between" wrap="nowrap">
            <Text size="xs" c="dimmed">
              Author: {author || 'operator'}
            </Text>
            <Button
              size="xs"
              leftSection={<TbMessage size={14} />}
              onClick={handleSubmit}
              loading={create.isPending}
              disabled={!body.trim()}
            >
              Post
            </Button>
          </Group>
          {create.error && (
            <Text size="xs" c="red">
              {getApiErrorMessage(create.error, 'Failed to post comment')}
            </Text>
          )}
        </Stack>
      </Paper>
    </Stack>
  )
}

function CommentRow({ comment }: { comment: CardDiscussionCommentDto }) {
  const external = comment.origin === 'External'
  return (
    <Paper withBorder p="xs" data-testid={`discussion-comment-${comment.id}`}>
      <Group justify="space-between" mb={4} wrap="nowrap">
        <Group gap={6} wrap="nowrap">
          <Text size="xs" fw={700}>
            {comment.author || (external ? 'external' : 'operator')}
          </Text>
          <Badge size="xs" variant="light" color={external ? 'blue' : 'gray'}>
            {external ? 'GitHub' : 'Antiphon'}
          </Badge>
        </Group>
        <Text size="xs" c="dimmed">
          {new Date(comment.createdAt).toLocaleString()}
        </Text>
      </Group>
      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
        {comment.body}
      </Text>
      {comment.externalUrl && (
        <Box mt={4}>
          <Text
            component="a"
            href={comment.externalUrl}
            target="_blank"
            rel="noreferrer"
            size="xs"
            c="dimmed"
          >
            View on tracker
          </Text>
        </Box>
      )}
    </Paper>
  )
}
