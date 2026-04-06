import { Modal, Group, Text, Badge, Box, ActionIcon, Tooltip, Loader, Stack } from '@mantine/core'
import { VscFile, VscEdit, VscEye } from 'react-icons/vsc'
import { useState } from 'react'
import { TipTapEditor } from './TipTapEditor'
import { useStageArtifact } from '../../api/artifacts'
import type { ArtifactDto } from '../workflow/types'

interface ArtifactModalProps {
  artifact: ArtifactDto | null
  workflowId: string | undefined
  onClose: () => void
}

/**
 * Modal for viewing (and optionally editing) a workflow artifact.
 * Fetches full content on demand via useStageArtifact, then renders in TipTap.
 */
export function ArtifactModal({ artifact, workflowId, onClose }: ArtifactModalProps) {
  const [editable, setEditable] = useState(false)

  const { data: fullArtifact, isLoading } = useStageArtifact(
    workflowId,
    artifact?.stageId,
    artifact?.version,
  )

  const content = fullArtifact?.content ?? ''

  function handleClose() {
    setEditable(false)
    onClose()
  }

  return (
    <Modal
      opened={!!artifact}
      onClose={handleClose}
      size="90vw"
      styles={{
        body: {
          padding: 0,
          display: 'flex',
          flexDirection: 'column',
          height: '85vh',
        },
        content: {
          display: 'flex',
          flexDirection: 'column',
          height: '85vh',
        },
      }}
      title={
        artifact && (
          <Group gap="sm" wrap="nowrap">
            <VscFile size={16} />
            <Text fw={600} size="sm" style={{ fontFamily: 'monospace' }}>
              {artifact.fileName}
            </Text>
            {artifact.isPrimary && (
              <Badge size="xs" color="active" variant="light">
                Primary
              </Badge>
            )}
            <Badge size="xs" color="gray" variant="outline">
              {artifact.stageName} · v{artifact.version}
            </Badge>
          </Group>
        )
      }
    >
      {artifact && (
        <Box style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}>
          {/* Toolbar */}
          <Group
            justify="flex-end"
            px="sm"
            py="xs"
            style={{ borderBottom: '1px solid var(--mantine-color-dark-4)', flexShrink: 0 }}
          >
            <Tooltip label={editable ? 'Switch to view mode' : 'Switch to edit mode'}>
              <ActionIcon
                variant={editable ? 'filled' : 'subtle'}
                color={editable ? 'active' : 'gray'}
                size="sm"
                onClick={() => setEditable((e) => !e)}
              >
                {editable ? <VscEye size={14} /> : <VscEdit size={14} />}
              </ActionIcon>
            </Tooltip>
          </Group>

          {/* Content area */}
          <Box style={{ flex: 1, overflow: 'auto' }}>
            {isLoading ? (
              <Stack align="center" justify="center" style={{ height: '100%' }}>
                <Loader size="sm" />
                <Text c="dimmed" size="sm">Loading artifact…</Text>
              </Stack>
            ) : (
              <TipTapEditor
                key={`${artifact.id}-${artifact.version}`}
                content={content}
                editable={editable}
              />
            )}
          </Box>
        </Box>
      )}
    </Modal>
  )
}
