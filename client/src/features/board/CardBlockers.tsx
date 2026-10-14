import { Alert, Badge, Button, Group, Loader, Stack, Text, TextInput } from '@mantine/core'
import { useState } from 'react'
import type { CardDto, CardRelationshipDto } from '../../api/boards'
import { useAddBlocker, useCardRelationships, useRemoveBlocker } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import { stateColor, stateLabel } from './boardVisuals'

export function CardBlockers({ card }: { card: CardDto }) {
  const relationships = useCardRelationships(card.id)
  const add = useAddBlocker(card.boardId, card.id)
  const remove = useRemoveBlocker(card.boardId, card.id)
  const [identifier, setIdentifier] = useState('')
  const [writeError, setWriteError] = useState<string | null>(null)
  const pending = add.isPending || remove.isPending
  const editable = !card.archivedAt

  const addBlocker = () => {
    if (!editable || pending || !identifier.trim()) return
    setWriteError(null)
    add.mutate({ concurrencyToken: card.concurrencyToken, kind: 'BlockedBy', relatedCard: identifier.trim() }, {
      onSuccess: () => setIdentifier(''),
      onError: (error) => setWriteError(getApiErrorMessage(error, 'Could not add blocker')),
    })
  }

  const removeBlocker = (relationshipId: string) => {
    if (!editable || pending) return
    setWriteError(null)
    remove.mutate({ relationshipId, request: { concurrencyToken: card.concurrencyToken } }, {
      onError: (error) => setWriteError(getApiErrorMessage(error, 'Could not remove blocker')),
    })
  }

  return (
    <Stack gap="xs" data-testid="card-blockers">
      <Text size="xs" c="dimmed" fw={700} tt="uppercase">Blocked by</Text>
      {relationships.isPending && <Loader size="xs" aria-label="Loading card relationships" />}
      {relationships.error && (
        <Alert color="red">{getApiErrorMessage(relationships.error, 'Could not load card relationships')}</Alert>
      )}
      {relationships.data && (
        <>
          {relationships.data.blockedBy.length === 0 && <Text size="sm" c="dimmed">No blockers</Text>}
          {relationships.data.blockedBy.map((relationship) => (
            <Group key={relationship.id} gap="xs" data-testid={`card-blocker-${relationship.relatedIdentifier}`} data-resolved={relationship.resolved}>
              <RelationshipCard relationship={relationship} />
              {editable && (
                <Button
                  size="compact-xs"
                  variant="subtle"
                  color="red"
                  aria-label={`Remove blocker ${relationship.relatedIdentifier}`}
                  disabled={pending}
                  onClick={() => removeBlocker(relationship.id)}
                >
                  Remove
                </Button>
              )}
            </Group>
          ))}
          {editable && (
            <form onSubmit={(event) => { event.preventDefault(); addBlocker() }}>
              <Stack gap="xs">
                <TextInput
                  label="Blocker identifier"
                  placeholder="CARD-0049 or card GUID"
                  value={identifier}
                  disabled={pending}
                  onChange={(event) => setIdentifier(event.currentTarget.value)}
                />
                <Button type="submit" size="compact-xs" aria-label="Add blocker" disabled={pending || !identifier.trim()}>Add</Button>
              </Stack>
            </form>
          )}
          <Text size="xs" c="dimmed" fw={700} tt="uppercase">Blocks</Text>
          {relationships.data.blocks.length === 0 && <Text size="sm" c="dimmed">No dependent cards</Text>}
          {relationships.data.blocks.map((relationship) => (
            <Group key={relationship.id} gap="xs"><RelationshipCard relationship={relationship} /></Group>
          ))}
        </>
      )}
      {writeError && <Alert color="red">{writeError}</Alert>}
    </Stack>
  )
}

function RelationshipCard({ relationship }: { relationship: CardRelationshipDto }) {
  return (
    <>
      <Text size="sm" title={relationship.relatedTitle} style={{ textDecoration: relationship.resolved ? 'line-through' : undefined }}>
        {relationship.relatedIdentifier}
      </Text>
      <Badge size="xs" color={stateColor(relationship.relatedStatus)} variant="light">
        {stateLabel(relationship.relatedStatus)}
      </Badge>
      {relationship.relatedArchivedAt && <Badge size="xs" color="gray" variant="outline">Archived</Badge>}
    </>
  )
}
