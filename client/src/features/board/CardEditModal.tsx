import { Button, Group, Modal, NumberInput, Stack, Text, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { CARD_LIMITS, type CardDto, useUpdateCardContent } from '../../api/boards'
import { getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import { displayIdentifier } from '../../shared/cardIdentifier'

interface CardEditModalProps {
  boardId: string
  card: CardDto
  onClose: () => void
}

/** Which 422 keys this dialog has an input for. Anything else falls through to a notification. */
const EDITABLE_FIELDS = ['Title', 'Description', 'Priority', 'Labels', 'Reason']

/** The comma-separated labels field, both ways. Consistent with the create dialog, deliberately. */
function parseLabels(value: string): string[] {
  return value.split(',').map((label) => label.trim()).filter(Boolean)
}

function sameLabels(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((label, index) => label === b[index])
}

/**
 * Correcting a card's text — the point of CARD-0019. A card whose title names the wrong bug is
 * worse than no card, and until now the only fix was to close it and write a new one, which
 * scattered the record across two identifiers.
 *
 * Layered over the fullscreen card page as a plain `Modal` (the same `zIndex={400}` as
 * `MoveMenu`'s reason modal), because the header actions group is where every other card-level
 * action already lives.
 *
 * MOUNTED ON OPEN, not toggled: the prefill is `useState` initialisers, so there is no effect to
 * resynchronise and no way for a background refetch of the board to wipe half-typed edits.
 *
 * Only CHANGED fields are sent. The server reads null as "unchanged", so diffing here is what
 * keeps a one-word title fix from also rewriting a 20k description with whatever this dialog
 * happened to have loaded — and what makes the resulting revision read as the correction actually
 * made.
 */
export function CardEditModal({ boardId, card, onClose }: CardEditModalProps) {
  const updateContent = useUpdateCardContent(boardId)
  const [title, setTitle] = useState(card.title)
  const [description, setDescription] = useState(card.description)
  const [priority, setPriority] = useState<number | string>(card.priority)
  const [labels, setLabels] = useState(card.labels.join(', '))
  const [reason, setReason] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const titleOverLimit = title.length > CARD_LIMITS.title
  const descriptionOverLimit = description.length > CARD_LIMITS.description
  const reasonOverLimit = reason.length > CARD_LIMITS.reason
  const canSubmit = reason.trim().length > 0
    && title.trim().length > 0
    && !titleOverLimit
    && !descriptionOverLimit
    && !reasonOverLimit

  const submit = () => {
    if (!canSubmit) return
    const nextLabels = parseLabels(labels)
    const nextPriority = Number(priority)

    setFieldErrors({})
    updateContent.mutate(
      {
        cardId: card.id,
        request: {
          concurrencyToken: card.concurrencyToken,
          reason: reason.trim(),
          title: title.trim() === card.title ? null : title.trim(),
          description: description.trim() === card.description ? null : description.trim(),
          priority: Number.isInteger(nextPriority) && nextPriority !== card.priority ? nextPriority : null,
          labels: sameLabels(nextLabels, card.labels) ? null : nextLabels,
          // Self-reported and never authenticated. The web UI is the operator's surface; agents
          // hit the API directly and name themselves.
          editedBy: 'operator',
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${card.identifier} updated` })
          onClose()
        },
        onError: (error) => {
          // 422 carries a PascalCase-keyed dict; hang each message on its own input, and keep the
          // notification for what has nowhere to land — a 409, or a key with no field here.
          const fields = getApiFieldErrors(error)
          const mapped = Object.fromEntries(
            Object.entries(fields).filter(([key]) => EDITABLE_FIELDS.includes(key)),
          )
          setFieldErrors(mapped)
          if (Object.keys(mapped).length !== Object.keys(fields).length || Object.keys(fields).length === 0) {
            notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Update failed') })
          }
        },
      },
    )
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={`Edit ${displayIdentifier(card.identifier)}`}
      zIndex={400}
    >
      <Stack>
        <TextInput
          label="Title"
          value={title}
          error={fieldErrors.Title
            ?? (titleOverLimit
              ? `Title must be at most ${CARD_LIMITS.title.toLocaleString()} characters.`
              : undefined)}
          onChange={(event) => setTitle(event.currentTarget.value)}
        />
        <Textarea
          label="Description"
          value={description}
          autosize
          minRows={4}
          maxRows={14}
          inputWrapperOrder={['label', 'input', 'description', 'error']}
          description={<LimitCounter value={description.length} limit={CARD_LIMITS.description} />}
          error={fieldErrors.Description}
          onChange={(event) => setDescription(event.currentTarget.value)}
        />
        <NumberInput
          label="Priority"
          min={0}
          value={priority}
          error={fieldErrors.Priority}
          onChange={setPriority}
        />
        <TextInput
          label="Labels"
          value={labels}
          error={fieldErrors.Labels}
          onChange={(event) => setLabels(event.currentTarget.value)}
        />
        <Textarea
          label="Reason"
          placeholder="Why this card is being corrected"
          withAsterisk
          autosize
          minRows={2}
          value={reason}
          inputWrapperOrder={['label', 'input', 'description', 'error']}
          description={<LimitCounter value={reason.length} limit={CARD_LIMITS.reason} />}
          error={fieldErrors.Reason}
          onChange={(event) => setReason(event.currentTarget.value)}
        />
        <Text size="xs" c="dimmed">
          The superseded text is kept on the card's history with this reason. Nothing is lost.
        </Text>
        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button onClick={submit} loading={updateContent.isPending} disabled={!canSubmit}>
            Save
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

/**
 * The client-side limit, shown as a count rather than enforced with `maxLength`: silently
 * truncating a paste is how a correction becomes a new mistake. The server's 422 is the backstop
 * and its message is printed verbatim, because these limits are constants that can drift.
 */
export function LimitCounter({ value, limit }: { value: number; limit: number }) {
  const over = value > limit
  return (
    <Text component="span" size="xs" c={over ? 'red' : 'dimmed'} fw={over ? 700 : undefined}>
      {value.toLocaleString()} / {limit.toLocaleString()}
    </Text>
  )
}
