import { configure, getConfig } from '@testing-library/react';

// Testing Library builds a getBy* failure through config.getElementError, and the default
// pretty-prints the whole container into the message. waitFor and every findBy re-run their
// callback on each DOM mutation and every 50 ms, so a callback that is not satisfied yet pays a
// full prettyDOM per poll only for waitFor to discard the error on the next one. Measured on
// server2 (CARD-1690): 7.3 s of 37.4 s CPU in 32 RoutingSettingsTab matrix cases.
//
// While waitFor runs its callback, Testing Library sets `_disableExpensiveErrorDiagnostics`
// (the flag getByRole already reads to skip its role listing). A poll error then defers the
// print: its message is a getter that calls the default printer with the same message and
// container the first time anything reads it, and keeps that text. waitFor drops unread poll
// errors, so those never print. Whatever does read one prints what the default prints: the
// timeout handler and the stack copy read the last error, and a several-matches query reads
// each element's error to build its listing. A getBy* outside waitFor is unchanged.
export function installPollingElementError(): void {
  const printing = getConfig().getElementError;
  configure({
    getElementError(message, container) {
      if (!(getConfig() as { _disableExpensiveErrorDiagnostics?: boolean })._disableExpensiveErrorDiagnostics) {
        return printing(message, container);
      }
      return lazyElementError(() => printing(message, container).message);
    },
  });
}

function lazyElementError(print: () => string): Error {
  const error = new Error();
  error.name = 'TestingLibraryElementError';
  let printed: string | undefined;
  // Configurable: waitFor's onTimeout redefines message on the error it rejects with.
  Object.defineProperty(error, 'message', {
    configurable: true,
    enumerable: false,
    get: () => (printed ??= print()),
    set: (value: string) => {
      Object.defineProperty(error, 'message', { value, writable: true, configurable: true, enumerable: false });
    },
  });
  return error;
}
