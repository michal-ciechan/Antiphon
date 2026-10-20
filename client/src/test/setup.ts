import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeAll, afterAll, vi } from 'vitest';
import { server } from './mocks/server';

// jsdom has no layout: skip positioning work while keeping the real Floating UI
// React hooks, Mantine controls and accessibility style reads. Geometry tests must
// live in a separate file with hoisted vi.unmock('@floating-ui/dom').
vi.mock('@floating-ui/dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@floating-ui/dom')>();
  const computePosition: typeof actual.computePosition = async (_reference, _floating, options) => ({
    x: 0,
    y: 0,
    placement: options?.placement ?? 'bottom',
    strategy: options?.strategy ?? 'absolute',
    middlewareData: {},
  });
  const autoUpdate: typeof actual.autoUpdate = (_reference, _floating, update) => {
    update();
    return () => {};
  };
  return { ...actual, computePosition, autoUpdate };
});

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});

HTMLCanvasElement.prototype.getContext = vi.fn(() => null);
HTMLElement.prototype.scrollIntoView = vi.fn();

class ResizeObserverMock {
  observe = vi.fn();
  unobserve = vi.fn();
  disconnect = vi.fn();
}

Object.defineProperty(window, 'ResizeObserver', {
  writable: true,
  value: ResizeObserverMock,
});

Object.defineProperty(globalThis, 'ResizeObserver', {
  writable: true,
  value: ResizeObserverMock,
});

// Start MSW server before all tests
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

// Reset handlers after each test to avoid test pollution
afterEach(() => {
  cleanup();
  server.resetHandlers();
});

// Close MSW server after all tests
afterAll(() => server.close());
