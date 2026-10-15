import { type ReactElement } from 'react';
import { act, render, renderHook, type RenderOptions } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
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

/**
 * Waits, inside act, until `queryClient` has no query or mutation in flight, flushing React after
 * each round, so the next assertion can be a synchronous getBy* (CARD-1246).
 *
 * A findBy or waitFor after a fetch-driven render polls inside a 1 000 ms window; on a loaded host
 * the MSW reply plus the render can outlast it, and the test fails with nothing wrong. This waits
 * on the cache's own events instead of a clock: each round awaits the idle event, then one
 * zero-delay tick, and act renders what arrived before it returns. React Query hands a result to a
 * component in two scheduler hops (the batch flush, then useQuery's batched store callback); by
 * default each is a setTimeout 0, so the second landed after the tick and the round ended with the
 * data cached but not rendered (seen in AgentsPage). While settling, the scheduler is a microtask,
 * which the tick drains, and the default is restored after. A render can mount new queries in its
 * effects, so the loop ends when a flush leaves nothing in flight. The suite's testTimeout stays
 * the only bound (a hang detector): a request that never answers hangs here, so do not use it with
 * held handlers.
 */
export async function settleQueries(queryClient: QueryClient): Promise<void> {
  const busy = () => queryClient.isFetching() > 0 || queryClient.isMutating() > 0;
  notifyManager.setScheduler(queueMicrotask);
  try {
    do {
      await act(async () => {
        await new Promise<void>((resolve) => {
          if (!busy()) {
            resolve();
            return;
          }
          const check = () => {
            if (busy()) return;
            unsubscribeQueries();
            unsubscribeMutations();
            resolve();
          };
          const unsubscribeQueries = queryClient.getQueryCache().subscribe(check);
          const unsubscribeMutations = queryClient.getMutationCache().subscribe(check);
        });
        await new Promise((resolve) => setTimeout(resolve, 0));
      });
    } while (busy());
  } finally {
    notifyManager.setScheduler(defaultScheduler);
  }
}

/**
 * Focuses `element` and pastes `text` as one input event (CARD-1246). Per-keystroke
 * `userEvent.type` re-renders a large form once per character: seconds of CPU on a loaded host,
 * enough to reach the hang detector, and the abandoned keystrokes of a timed-out test then land in
 * the next test's field (seen at base under a stressor: 'hOrch2', 'redi'). Use it where the test
 * asserts the final value, not intermediate keystrokes.
 */
export async function pasteInto(element: HTMLElement, text: string): Promise<void> {
  const user = userEvent.setup();
  await user.click(element);
  await user.paste(text);
}

// Re-export everything from @testing-library/react for convenience
export { screen, waitFor, within, act } from '@testing-library/react';
export { default as userEvent } from '@testing-library/user-event';
