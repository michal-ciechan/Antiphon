import type { HttpHandler } from 'msw';

/**
 * Default MSW request handlers.
 * Add shared handlers here that should be available in all tests.
 * Individual tests can override or extend these via server.use().
 */
export const handlers: HttpHandler[] = [
  // Add default handlers as needed, e.g.:
  // http.get('/health', () => HttpResponse.text('Healthy')),
];
