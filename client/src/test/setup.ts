import { afterEach, beforeAll, afterAll } from 'vitest';
import { server } from './mocks/server';

// DOM-free tests opt into the Node environment and share the same HTTP guard.
const browserSetup = typeof window === 'undefined' ? undefined : await import('./setupDom');

// Start MSW server before all tests
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

// Reset handlers after each test to avoid test pollution
afterEach(() => {
  browserSetup?.cleanupBrowserFixtures();
  server.resetHandlers();
});

// Close MSW server after all tests
afterAll(() => server.close());
