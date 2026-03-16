import { type ReactElement } from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router';
import { createElement } from 'react';

/**
 * Creates a fresh QueryClient configured for testing (no retries, no refetch).
 */
function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: Infinity,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

/**
 * Custom render function that wraps the component in all required providers:
 * - MantineProvider (UI components)
 * - QueryClientProvider (server state)
 * - BrowserRouter (routing)
 *
 * Usage:
 *   import { renderWithProviders } from '../test/utils';
 *   const { getByText } = renderWithProviders(<MyComponent />);
 */
export function renderWithProviders(
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'>,
) {
  const queryClient = createTestQueryClient();

  function AllProviders({ children }: { children: React.ReactNode }) {
    return createElement(
      BrowserRouter,
      null,
      createElement(
        MantineProvider,
        null,
        createElement(QueryClientProvider, { client: queryClient }, children),
      ),
    );
  }

  return {
    ...render(ui, { wrapper: AllProviders, ...options }),
    queryClient,
  };
}

// Re-export everything from @testing-library/react for convenience
export { screen, waitFor, within, act } from '@testing-library/react';
export { default as userEvent } from '@testing-library/user-event';
