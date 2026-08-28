import { HttpResponse, http, type HttpHandler } from 'msw';

/**
 * Default MSW request handlers.
 * Add shared handlers here that should be available in all tests.
 * Individual tests can override or extend these via server.use().
 */
export const handlers: HttpHandler[] = [
  // CARD-0212: useRemoteControlSupport always GETs runner-types, including on screens
  // that never previously asked. Empty list = Unknown-as-Unsupported.
  http.get('/api/agent-tui/runner-types', () => HttpResponse.json([])),
];
