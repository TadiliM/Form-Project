import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as client from '../api/client';
import { AuthProvider, useAuth } from './AuthContext';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/** Minimal harness exposing the context through buttons. */
function Harness() {
  const { user, login, logout } = useAuth();

  return (
    <div>
      <p data-testid="state">{user ? `${user.name}:${user.planType}` : 'signed out'}</p>
      <button type="button" onClick={() => void login('ada@example.com', 'secret')}>
        log in
      </button>
      <button type="button" onClick={logout}>
        log out
      </button>
    </div>
  );
}

describe('AuthContext', () => {
  beforeEach(() => {
    client.setToken(null);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('starts signed out when no token is stored', async () => {
    render(
      <AuthProvider>
        <Harness />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed out'));
  });

  it('logs in, stores the token and loads the profile plan from the API', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith('/auth/login')) {
        return Promise.resolve(
          // The API sends enums as numbers: 0 = Free.
          jsonResponse(200, { token: 'jwt-token', email: 'ada@example.com', name: 'Ada', planType: 0 }),
        );
      }
      // GET /users/me is the source of truth for the plan (the JWT one is stale).
      // The API serializes enums as numbers: 1 = Pro.
      return Promise.resolve(
        jsonResponse(200, {
          id: 'u1',
          email: 'ada@example.com',
          name: 'Ada',
          planType: 1,
          createdAt: '2026-01-01T00:00:00Z',
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    render(
      <AuthProvider>
        <Harness />
      </AuthProvider>,
    );

    await userEvent.click(screen.getByRole('button', { name: 'log in' }));

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('Ada:Pro'));
    expect(client.getToken()).toBe('jwt-token');
  });

  it('clears the token on logout', async () => {
    client.setToken('jwt-token');
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(200, {
          id: 'u1',
          email: 'ada@example.com',
          name: 'Ada',
          planType: 0,
          createdAt: '2026-01-01T00:00:00Z',
        }),
      ),
    );

    render(
      <AuthProvider>
        <Harness />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('Ada:Free'));

    await userEvent.click(screen.getByRole('button', { name: 'log out' }));

    expect(client.getToken()).toBeNull();
    expect(screen.getByTestId('state')).toHaveTextContent('signed out');
  });

  it('drops a stored token that the API rejects', async () => {
    client.setToken('expired-token');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(401, { message: 'Unauthorized' })));

    render(
      <AuthProvider>
        <Harness />
      </AuthProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed out'));
    expect(client.getToken()).toBeNull();
  });
});
