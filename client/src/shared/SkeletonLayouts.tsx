import { Skeleton, Stack, Group, Box } from '@mantine/core'

export function PageSkeleton() {
  return (
    <Stack gap="lg" p="md">
      <Skeleton height={32} width="40%" />
      <Skeleton height={16} width="70%" />
      <Group gap="md" mt="md">
        <Skeleton height={180} width="100%" radius="md" />
      </Group>
      <Stack gap="sm">
        <Skeleton height={14} width="90%" />
        <Skeleton height={14} width="80%" />
        <Skeleton height={14} width="85%" />
      </Stack>
    </Stack>
  )
}

export function PanelSkeleton() {
  return (
    <Stack gap="sm" p="sm">
      <Skeleton height={24} width="50%" />
      <Skeleton height={14} width="100%" />
      <Skeleton height={14} width="90%" />
      <Skeleton height={14} width="95%" />
    </Stack>
  )
}

export function InlineSkeleton() {
  return (
    <Group gap="sm">
      <Skeleton height={14} width={120} />
      <Skeleton height={14} width={80} />
    </Group>
  )
}

export function CardSkeleton() {
  return (
    <Box p="md">
      <Stack gap="sm">
        <Skeleton height={20} width="60%" />
        <Skeleton height={14} width="100%" />
        <Skeleton height={14} width="80%" />
        <Skeleton height={32} width="30%" mt="xs" />
      </Stack>
    </Box>
  )
}
