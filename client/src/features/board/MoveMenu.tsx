import { ActionIcon, Alert, Button, Group, Menu, Modal, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import { TbAlertTriangle, TbArchive, TbArchiveOff, TbCopy, TbDots, TbRestore } from 'react-icons/tb'
import {
  type BoardColumnDto,
  type CardDto,
  useArchiveCard,
  useMoveCard,
  useReopenCard,
  useUnarchiveCard,
} from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import { displayIdentifier } from '../../shared/cardIdentifier'
import { canReopenFrom, hasLiveSession, legalMoveTargets } from './boardShapeModel'

interface MoveMenuProps {
  boardId: string
  card: CardDto
  columns: BoardColumnDto[]
  variant?: 'kebab' | 'button'
  /**
   * Called after a successful archive. The card leaves the default board payload, so anything
   * showing it (the card modal) has to let go rather than wait for `selectedCard` to resolve null.
   */
  onArchived?: () => void
}

/**
 * The board's only move surface, replacing drag-and-drop — and, since CARD-0019, the card's
 * actions menu generally.
 *
 * Drag answered "which adjacent column", which the fully-connected state machine made obsolete;
 * worse, it had nowhere to put the move's Reason and made a gesture out of a side effect — moving
 * a card into an ACTIVE column spawns an agent session. So a move is now explicit, reasoned, and
 * says out loud when it will start an agent.
 *
 * Archive and reopen live here rather than behind an overflow of their own: this IS the card's
 * overflow, on every row and in the modal header, so both are reachable everywhere a card is.
 */
export function MoveMenu({ boardId, card, columns, variant = 'kebab', onArchived }: MoveMenuProps) {
  const [target, setTarget] = useState<BoardColumnDto | null>(null)
  const [archiveAction, setArchiveAction] = useState<'archive' | 'unarchive' | null>(null)
  const [reopening, setReopening] = useState(false)
  const [reason, setReason] = useState('')
  const moveCard = useMoveCard(boardId)
  const archiveCard = useArchiveCard(boardId)
  const unarchiveCard = useUnarchiveCard(boardId)
  const reopenCard = useReopenCard(boardId)
  const archived = !!card.archivedAt
  // An archived card cannot move — the server refuses with "unarchive it before…", so offering
  // targets would only produce a 409 the operator has to read to understand.
  const targets = archived ? [] : legalMoveTargets(card, columns)

  const close = () => {
    setTarget(null)
    setArchiveAction(null)
    setReopening(false)
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
          // The server does not spawn unasked any more (CARD-0051). Here the human HAS asked: the
          // menu item says "spawns an agent" and the dialog warns again before Move, so this keeps
          // the UI behaving exactly as it did while scripted moves have to opt in.
          spawn: true,
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

  const submitArchive = () => {
    if (!archiveAction || !reason.trim()) return
    const action = archiveAction
    const request = { cardId: card.id, request: { concurrencyToken: card.concurrencyToken, reason: reason.trim() } }

    const onError = (error: unknown) => {
      // The server's refusals here are good sentences ("has a live owner session; stop it before
      // archiving") — show them rather than a generic failure.
      notifications.show({
        color: 'red',
        message: getApiErrorMessage(error, action === 'archive' ? 'Archive failed' : 'Unarchive failed'),
      })
    }

    if (action === 'archive') {
      archiveCard.mutate(
        { ...request, request: { ...request.request, archivedBy: 'operator' } },
        {
          onSuccess: () => {
            notifications.show({ color: 'green', message: `${card.identifier} archived` })
            onArchived?.()
          },
          onError,
        },
      )
    } else {
      unarchiveCard.mutate(
        { ...request, request: { ...request.request, unarchivedBy: 'operator' } },
        {
          onSuccess: () => {
            notifications.show({ color: 'green', message: `${card.identifier} unarchived` })
          },
          onError,
        },
      )
    }
    close()
  }

  const submitReopen = () => {
    if (!reason.trim()) return
    reopenCard.mutate(
      {
        cardId: card.id,
        request: {
          concurrencyToken: card.concurrencyToken,
          reason: reason.trim(),
          reopenedBy: 'operator',
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: `${card.identifier} reopened` })
        },
        onError: (error) => {
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Reopen failed'),
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
          {!archived && (
            <>
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
              {canReopenFrom(card.status) && (
                <Menu.Item
                  data-testid="reopen-card"
                  leftSection={<TbRestore size={14} />}
                  onClick={(event) => {
                    event.stopPropagation()
                    setReopening(true)
                  }}
                >
                  Reopen…
                </Menu.Item>
              )}
              <Menu.Item
                color="red"
                data-testid="archive-card"
                leftSection={<TbArchive size={14} />}
                onClick={(event) => {
                  event.stopPropagation()
                  setArchiveAction('archive')
                }}
              >
                Archive…
              </Menu.Item>
            </>
          )}
          {archived && (
            <Menu.Item
              data-testid="unarchive-card"
              leftSection={<TbArchiveOff size={14} />}
              onClick={(event) => {
                event.stopPropagation()
                setArchiveAction('unarchive')
              }}
            >
              Unarchive…
            </Menu.Item>
          )}
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
            A reason is kept as the reason on the card's history entry for this move; a move into a
            terminal state also stamps the card's terminal reason. Send one regardless.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>Cancel</Button>
            <Button onClick={submit} loading={moveCard.isPending}>Move</Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={!!archiveAction}
        onClose={close}
        title={archiveAction === 'unarchive'
          ? `Unarchive ${displayIdentifier(card.identifier)}`
          : `Archive ${displayIdentifier(card.identifier)}`}
        zIndex={400}
      >
        <Stack>
          {archiveAction === 'archive' && hasLiveSession(card) && (
            <Alert
              color="warning"
              variant="light"
              icon={<TbAlertTriangle size={18} />}
              title="This card has a live session"
            >
              The server will refuse to archive a card an agent is working. Stop the session first.
            </Alert>
          )}
          <Textarea
            label="Reason"
            placeholder={archiveAction === 'unarchive'
              ? 'Why this card is coming back'
              : 'Why this card is being archived'}
            withAsterisk
            autosize
            minRows={2}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
          />
          <Text size="xs" c="dimmed">
            Archiving is not deleting: the card keeps its identifier and its history, and shows up
            again under the board's Archived filter.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>Cancel</Button>
            <Button
              color={archiveAction === 'unarchive' ? undefined : 'red'}
              onClick={submitArchive}
              loading={archiveCard.isPending || unarchiveCard.isPending}
              disabled={!reason.trim()}
            >
              {archiveAction === 'unarchive' ? 'Unarchive' : 'Archive'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={reopening}
        onClose={close}
        title={`Reopen ${displayIdentifier(card.identifier)}`}
        zIndex={400}
      >
        <Stack>
          <Textarea
            label="Reason"
            placeholder="Why this card is being reopened"
            withAsterisk
            autosize
            minRows={2}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
          />
          <Text size="xs" c="dimmed">
            Reopening is a correction, not a move. The card returns to the board's Backlog and
            the superseded close stays on its history.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>Cancel</Button>
            <Button
              onClick={submitReopen}
              loading={reopenCard.isPending}
              disabled={!reason.trim()}
            >
              Reopen
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
