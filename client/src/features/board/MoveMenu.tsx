import { ActionIcon, Alert, Button, Group, Menu, Modal, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbAlertTriangle, TbCopy, TbDots } from 'react-icons/tb'
import { type BoardColumnDto, type CardDto, useMoveCard } from '../../api/boards'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { legalMoveTargets } from './boardShapeModel'

interface MoveMenuProps {
  boardId: string
  card: CardDto
  columns: BoardColumnDto[]
  variant?: 'kebab' | 'button'
}

/**
 * The board's only move surface, replacing drag-and-drop.
 *
 * Drag answered "which adjacent column", which the fully-connected state machine made obsolete;
 * worse, it had nowhere to put the move's Reason and made a gesture out of a side effect — moving
 * a card into an ACTIVE column spawns an agent session. So a move is now explicit, reasoned, and
 * says out loud when it will start an agent.
 */
export function MoveMenu({ boardId, card, columns, variant = 'kebab' }: MoveMenuProps) {
  const [target, setTarget] = useState<BoardColumnDto | null>(null)
  const [reason, setReason] = useState('')
  const moveCard = useMoveCard(boardId)
  const targets = legalMoveTargets(card, columns)

  const close = () => {
    setTarget(null)
    setReason('')
  }

  const submit = () => {
    if (!target) return
    const chosen = target
    moveCard.mutate(
      {
        cardId: card.id,
        request: {
          boardColumnId: chosen.id,
          concurrencyToken: card.concurrencyToken,
          reason: reason.trim() || null,
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${card.identifier} moved to ${chosen.name}` })
        },
        onError: (error) => {
          notifications.show({
            color: 'red',
            message: error instanceof Error ? error.message : 'Move failed',
          })
        },
      },
    )
    close()
  }

  const copyIdentifier = () => {
    // The COPIED form stays canonical: it lands in commit messages and docs where a grep against
    // history has to keep working. `#41` is a rendering, never the thing you paste.
    void navigator.clipboard?.writeText(card.identifier)
    notifications.show({ message: `Copied ${card.identifier}` })
  }

  return (
    <>
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          {/*
            No onClick here: `Menu.Target` clones its child with its own handler, so anything we
            attach is dropped. The row stops the click from opening the card by wrapping this in a
            container that swallows it.
          */}
          {variant === 'kebab'
            ? (
              <ActionIcon
                variant="subtle"
                size="sm"
                color="gray"
                title="Card actions"
                aria-label={`Actions for ${card.identifier}`}
              >
                <TbDots size={16} />
              </ActionIcon>
            )
            : <Button size="xs" variant="light">Move to</Button>}
        </Menu.Target>
        <Menu.Dropdown onClick={(event) => event.stopPropagation()}>
          <Menu.Label>Move to</Menu.Label>
          {targets.length === 0 && (
            <Menu.Item disabled data-testid="no-move-target">No move out of a terminal state</Menu.Item>
          )}
          {targets.map((column) => (
            <Menu.Item
              key={column.id}
              data-testid={`move-to-${column.stateKey}`}
              onClick={(event) => {
                event.stopPropagation()
                setTarget(column)
              }}
            >
              {column.name}{column.isActive ? ' — spawns an agent' : ''}
            </Menu.Item>
          ))}
          <Menu.Divider />
          <Menu.Item
            data-testid="copy-card-id"
            leftSection={<TbCopy size={14} />}
            onClick={(event) => {
              event.stopPropagation()
              copyIdentifier()
            }}
          >
            Copy id ({card.identifier})
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>

      <Modal
        opened={!!target}
        onClose={close}
        title={target ? `Move ${displayIdentifier(card.identifier)} to ${target.name}` : ''}
        zIndex={400}
      >
        <Stack>
          {target?.isActive && (
            <Alert
              color="warning"
              variant="light"
              icon={<TbAlertTriangle size={18} />}
              title="This starts work"
            >
              Moving a card into {target.name} spawns an agent session on it.
            </Alert>
          )}
          <Textarea
            label="Reason"
            placeholder="Why this card is moving"
            autosize
            minRows={2}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
          />
          <Text size="xs" c="dimmed">
            A reason is kept as the card's terminal reason on a move into a terminal state. On any
            other move there is nowhere to store it yet — that arrives with CARD-0019's card
            history. Send one regardless.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>Cancel</Button>
            <Button onClick={submit} loading={moveCard.isPending}>Move</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
