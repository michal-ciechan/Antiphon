import { setupServer } from 'msw/node';
import { handlers } from './handlers';

/**
 * MSW server instance for intercepting HTTP requests in tests.
 * Started in setup.ts before all tests, reset after each test.
 */
export const server = setupServer(...handlers);
