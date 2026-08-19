import { Suspense, lazy, useState } from 'react'
import { Alert, Badge, Box, Button, Group, Loader, Modal, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { TbAlertCircle, TbDeviceFloppy } from 'react-icons/tb'
import { useBoardWorkflow, useUpdateBoardWorkflow } from '../../api/boards'

const MonacoEditor = lazy(() => import('@monaco-editor/react'))

interface WorkflowEditorProps {
  boardId: string
  opened: boolean
  onClose: () => void
}

export function WorkflowEditor({ boardId, opened, onClose }: WorkflowEditorProps) {
  const { data, isLoading, error } = useBoardWorkflow(opened ? boardId : undefined)
  const updateWorkflow = useUpdateBoardWorkflow(boardId)
  const [content, setContent] = useState('')
  const [sourceContent, setSourceContent] = useState('')

  // Adopt fresh server content unless the user has unsaved edits, and clear everything on close.
  // Adjusted during render (tracking the previous `opened`/`data`), not in an effect — the old
  // effect double-rendered and could clobber a keystroke landing between fetch and setState.
  const [prevOpened, setPrevOpened] = useState(opened)
  const [prevData, setPrevData] = useState<typeof data>(undefined)
  if (opened !== prevOpened) {
    setPrevOpened(opened)
    if (!opened) {
      setContent('')
      setSourceContent('')
      setPrevData(undefined)
    }
  }
  if (opened && data && data !== prevData) {
    setPrevData(data)
    if (content === sourceContent) {
      setContent(data.content)
    }
    setSourceContent(data.content)
  }

  const hasChanges = content !== sourceContent

  const handleSave = () => {
    updateWorkflow.mutate(
      { content },
      {
        onSuccess: (workflow) => {
          setContent(workflow.content)
          setSourceContent(workflow.content)
          notifications.show({ color: 'green', message: 'Workflow saved' })
        },
        onError: (mutationError) => {
          notifications.show({
            color: 'red',
            message: mutationError instanceof Error ? mutationError.message : 'Workflow save failed',
          })
        },
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title="WORKFLOW.md" size="80rem">
      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              {data?.name ?? 'Workflow'}
            </Text>
            {data && <Badge variant="light">v{data.version}</Badge>}
          </Group>
          <Button
            leftSection={<TbDeviceFloppy size={16} />}
            onClick={handleSave}
            loading={updateWorkflow.isPending}
            disabled={!data || !hasChanges}
          >
            Save
          </Button>
        </Group>

        {isLoading && (
          <Group justify="center" p="xl">
            <Loader />
          </Group>
        )}

        {error && (
          <Alert icon={<TbAlertCircle size={18} />} color="red" variant="light">
            {error instanceof Error ? error.message : 'Workflow failed to load'}
          </Alert>
        )}

        {!isLoading && !error && (
          <Box
            data-testid="workflow-editor"
            aria-label="WORKFLOW.md content"
            style={{
              border: '1px solid var(--mantine-color-dark-4)',
              borderRadius: 8,
              overflow: 'hidden',
              minHeight: 440,
            }}
          >
            <Suspense
              fallback={(
                <Group justify="center" p="xl">
                  <Loader />
                </Group>
              )}
            >
              <MonacoEditor
                height="58vh"
                defaultLanguage="markdown"
                path={`${boardId}-WORKFLOW.md`}
                theme="vs-dark"
                value={content}
                onChange={(value) => setContent(value ?? '')}
                options={{
                  fontSize: 14,
                  minimap: { enabled: false },
                  scrollBeyondLastLine: false,
                  wordWrap: 'on',
                  wrappingIndent: 'same',
                  tabSize: 2,
                  fixedOverflowWidgets: true,
                  renderLineHighlight: 'line',
                }}
              />
            </Suspense>
          </Box>
        )}
      </Stack>
    </Modal>
  )
}
