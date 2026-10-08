import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';

// Vitest does not expose globals here, so Testing Library's automatic cleanup is
// not registered: unmount the rendered tree after every test explicitly.
afterEach(() => {
  cleanup();
});
