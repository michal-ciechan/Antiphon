import { Menu, Button, Text, Group, Badge } from '@mantine/core'
import { VscHistory } from 'react-icons/vsc'

export interface ArtifactVersion {
  version: number
  createdAt: string
  isCurrent: boolean
}

interface VersionHistoryProps {
  versions: ArtifactVersion[]
  currentVersion: number
  onSelectVersion: (version: number) => void
}

/**
 * Dropdown menu showing artifact version history (FR57).
 * Clicking a version loads it in the artifact viewer.
 */
export function VersionHistory({
  versions,
  currentVersion,
  onSelectVersion,
}: VersionHistoryProps) {
  if (versions.length <= 1) return null

  return (
    <Menu shadow="md" width={240} position="bottom-end">
      <Menu.Target>
        <Button
          variant="subtle"
          size="xs"
          leftSection={<VscHistory size={14} />}
          color="dimmed"
        >
          v{currentVersion} of {versions.length}
        </Button>
      </Menu.Target>

      <Menu.Dropdown>
        <Menu.Label>Version history</Menu.Label>
        {versions
          .slice()
          .sort((a, b) => b.version - a.version)
          .map((v) => (
            <Menu.Item
              key={v.version}
              onClick={() => onSelectVersion(v.version)}
              style={
                v.version === currentVersion
                  ? { backgroundColor: 'var(--mantine-color-dark-5)' }
                  : undefined
              }
            >
              <Group justify="space-between" wrap="nowrap">
                <Text size="sm">Version {v.version}</Text>
                <Group gap={4}>
                  {v.isCurrent && (
                    <Badge size="xs" color="green" variant="light">
                      Current
                    </Badge>
                  )}
                  <Text size="xs" c="dimmed">
                    {new Date(v.createdAt).toLocaleDateString()}
                  </Text>
                </Group>
              </Group>
            </Menu.Item>
          ))}
      </Menu.Dropdown>
    </Menu>
  )
}
