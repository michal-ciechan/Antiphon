import { type ReactElement } from 'react';
import { render, renderHook, type RenderOptions } from '@testing-library/react';
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
 * - MantineProvider (UI components, in `env="test"` mode)
 * - QueryClientProvider (server state)
 * - BrowserRouter (routing)
 *
 * `env="test"` is REQUIRED, not a nicety. Mantine's Popover (which backs Menu, Select,
 * Tooltip, …) runs Floating UI's `hide()` middleware and, when it reports the reference
 * element as hidden, writes `display: none` onto the dropdown
 * (`PopoverDropdown` -> `ctx.referenceHidden`). Every `getBoundingClientRect()` in jsdom
 * returns an all-zero rect, so that verdict lands non-deterministically once the async
 * position update resolves — leaving a dropdown that is open (`opacity: 1`) but
 * `display: none`, i.e. present in the DOM yet absent from the accessibility tree. Any
 * `findByRole('menuitem', …)` after a menu click then fails its full timeout with
 * "Unable to find role=menuitem", intermittently and in whichever test lost the race.
 * `env="test"` pins `referenceHidden` to false (`Popover.tsx`: `hideDetached && env !== 'test'`).
 *
 * It also makes transitions resolve immediately instead of over a two-rAF + 150ms chain,
 * and renders portalled content inline — both of which remove real waiting from every
 * dropdown/modal/drawer interaction rather than papering over it with longer timeouts.
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

  return {
    ...render(ui, { wrapper: providersFor(queryClient), ...options }),
    queryClient,
  };
}

/**
 * The same provider stack as `renderWithProviders`, for testing a hook directly rather than
 * through a component. Exists here rather than in each test so no test file ever hand-rolls a raw
 * `MantineProvider` — see the `env="test"` note above for why that matters.
 */
export function renderHookWithProviders<Result, Props>(
  hook: (initialProps: Props) => Result,
) {
  const queryClient = createTestQueryClient();

  return {
    ...renderHook(hook, { wrapper: providersFor(queryClient) }),
    queryClient,
  };
}

function providersFor(queryClient: QueryClient) {
  return function AllProviders({ children }: { children: React.ReactNode }) {
    return createElement(
      BrowserRouter,
      null,
      createElement(
        MantineProvider,
        { env: 'test' },
        createElement(QueryClientProvider, { client: queryClient }, children),
      ),
    );
  };
}

// Re-export everything from @testing-library/react for convenience
export { screen, waitFor, within, act } from '@testing-library/react';
export { default as userEvent } from '@testing-library/user-event';
