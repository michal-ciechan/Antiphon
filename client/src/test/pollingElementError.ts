import { configure, getConfig } from '@testing-library/react';

// Testing Library builds a getBy* failure through config.getElementError, and the default
// pretty-prints the whole container into the message. waitFor and every findBy re-run their
// callback on each DOM mutation and every 50 ms, so a callback that is not satisfied yet pays a
// full prettyDOM per poll only for waitFor to discard the error on the next one. Measured on
// server2 (CARD-1690): 7.3 s of 37.4 s CPU in 32 RoutingSettingsTab matrix cases.
//
// While waitFor runs its callback, Testing Library sets `_disableExpensiveErrorDiagnostics`
// (the flag getByRole already reads to skip its role listing). This keeps the message and the
// error name, which waitFor checks, and leaves out only the DOM. A timed-out waitFor still
// reports it: its onTimeout calls getElementError again outside the poll, and that call prints
// the DOM as it stands at the timeout. A getBy* outside waitFor is unchanged.
export function installPollingElementError(): void {
  const printing = getConfig().getElementError;
  configure({
    getElementError(message, container) {
      if (!(getConfig() as { _disableExpensiveErrorDiagnostics?: boolean })._disableExpensiveErrorDiagnostics) {
        return printing(message, container);
      }
      const error = new Error(message ?? '');
      error.name = 'TestingLibraryElementError';
      return error;
    },
  });
}
