import { Suspense, type ReactNode } from 'react'
import {
  PageSkeleton,
  PanelSkeleton,
  InlineSkeleton,
  CardSkeleton,
} from './SkeletonLayouts'
import { useDelayedVisible } from './useDelayedVisible'

type SuspenseVariant = 'page' | 'panel' | 'inline' | 'card'

const skeletonMap: Record<SuspenseVariant, () => ReactNode> = {
  page: () => <PageSkeleton />,
  panel: () => <PanelSkeleton />,
  inline: () => <InlineSkeleton />,
  card: () => <CardSkeleton />,
}

function DelayedFallback({ variant }: { variant: SuspenseVariant }) {
  const visible = useDelayedVisible(200)

  if (!visible) {
    return null
  }

  return <>{skeletonMap[variant]()}</>
}

interface SuspenseBoundaryProps {
  children: ReactNode
  variant?: SuspenseVariant
}

export function SuspenseBoundary({
  children,
  variant = 'page',
}: SuspenseBoundaryProps) {
  return (
    <Suspense fallback={<DelayedFallback variant={variant} />}>
      {children}
    </Suspense>
  )
}
