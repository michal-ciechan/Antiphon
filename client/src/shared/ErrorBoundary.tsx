import { Component, type ErrorInfo, type ReactNode } from 'react'
import { Alert, Button, Group, Stack, Text } from '@mantine/core'
import { useNavigate } from 'react-router'

interface ErrorBoundaryProps {
  children: ReactNode
  fallbackTitle?: string
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
}

function ErrorFallback({
  error,
  title,
  onReset,
}: {
  error: Error
  title: string
  onReset: () => void
}) {
  const navigate = useNavigate()

  return (
    <Alert color="red" title={title} variant="light" p="xl">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {error.message}
        </Text>
        <Group>
          <Button variant="light" color="red" onClick={onReset}>
            Retry
          </Button>
          <Button
            variant="subtle"
            onClick={() => {
              onReset()
              navigate('/')
            }}
          >
            Go to Dashboard
          </Button>
        </Group>
      </Stack>
    </Alert>
  )
}

export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('ErrorBoundary caught:', error, errorInfo)
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null })
  }

  render() {
    if (this.state.hasError && this.state.error) {
      return (
        <ErrorFallback
          error={this.state.error}
          title={this.props.fallbackTitle ?? 'Something went wrong'}
          onReset={this.handleReset}
        />
      )
    }

    return this.props.children
  }
}
