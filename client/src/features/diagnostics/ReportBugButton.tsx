import { useState } from 'react'
import { Button, Modal, Stack, Switch, Text, Textarea } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { toPng } from 'html-to-image'
import { TbBug } from 'react-icons/tb'
import { useLocation } from 'react-router'
import { getApiErrorMessage } from '../../api/client'
import { downloadDiagnosticsBundle } from '../../api/diagnostics'
import { getConsoleRing } from '../../shared/consoleRing'

export function ReportBugButton({
  agentId,
  sessionId,
}: {
  agentId?: string
  sessionId?: string
}) {
  const location = useLocation()
  const [opened, { open, close }] = useDisclosure(false)
  const [note, setNote] = useState('')
  const [includePaths, setIncludePaths] = useState(false)
  const [busy, setBusy] = useState(false)

  async function download() {
    setBusy(true)
    try {
      const screenshotPngBase64 = await toPng(document.body)
      const { blob, filename } = await downloadDiagnosticsBundle({
        route: location.pathname,
        agentId,
        sessionId,
        screenshotPngBase64,
        console: getConsoleRing(),
        includePaths,
        note: note.trim() || undefined,
      })
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = filename
      anchor.click()
      URL.revokeObjectURL(url)
      close()
      setNote('')
    } catch (error) {
      notifications.show({
        color: 'red',
        title: 'Could not build the diagnostics zip',
        message: getApiErrorMessage(error, 'The server did not return a bundle.'),
      })
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Button
        size="xs"
        variant="subtle"
        color="gray"
        leftSection={<TbBug size={14} />}
        onClick={open}
        data-testid="report-bug-button"
      >
        Report bug
      </Button>
      <Modal opened={opened} onClose={close} title="Report bug" data-testid="report-bug-modal">
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Downloads a zip of health, version, session metadata, the visible screen, a screenshot,
            and recent console errors. Redaction is best-effort — glance before you share it.
          </Text>
          <Textarea
            label="Note"
            description="Optional. What you were doing when it went wrong."
            minRows={3}
            value={note}
            onChange={(event) => setNote(event.currentTarget.value)}
          />
          <Switch
            label="Include local paths"
            description="Off by default. Home and project directories become ~ and <project-N>."
            checked={includePaths}
            onChange={(event) => setIncludePaths(event.currentTarget.checked)}
            data-testid="include-paths-switch"
          />
          <Button onClick={download} loading={busy} data-testid="report-bug-download">
            Download
          </Button>
        </Stack>
      </Modal>
    </>
  )
}
