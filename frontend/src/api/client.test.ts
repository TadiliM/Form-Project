import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  cancelSubscription,
  confirmCheckoutSession,
  deleteForm,
  getMe,
  getToken,
  listForms,
  login,
  normalizePlanType,
  setToken,
  setUnauthorizedHandler,
} from './client';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('api client', () => {
  beforeEach(() => {
    setToken(null);
    setUnauthorizedHandler(null);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends the bearer token on protected calls', async () => {
    setToken('abc123');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, []));
    vi.stubGlobal('fetch', fetchMock);

    await listForms();

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/forms');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer abc123');
  });

  it('turns an error body into an ApiError with the API message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(409, { message: 'Email already used.' })));

    const error = await login({ email: 'a@b.c', password: 'x' }).catch((caught) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).message).toBe('Email already used.');
  });

  it('falls back to a generic message when the body is not JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })));

    const error = await listForms().catch((caught) => caught);

    expect((error as ApiError).message).toBe('Request failed (500).');
  });

  it('clears the token and notifies on a 401 from a protected call', async () => {
    setToken('abc123');
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(401, { message: 'Unauthorized' })));

    await listForms().catch(() => undefined);

    expect(getToken()).toBeNull();
    expect(handler).toHaveBeenCalledOnce();
  });

  it('does not notify on a 401 from the login call', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(401, { message: 'Invalid credentials.' })));

    await login({ email: 'a@b.c', password: 'x' }).catch(() => undefined);

    expect(handler).not.toHaveBeenCalled();
  });

  it('returns the parsed body on success', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(200, { token: 't', email: 'a@b.c', name: 'Ada', planType: 'Free' })),
    );

    const auth = await login({ email: 'a@b.c', password: 'x' });

    expect(auth.planType).toBe('Free');
    expect(auth.name).toBe('Ada');
  });

  it('normalizes the numeric planType the API actually sends', async () => {
    // ASP.NET Core serializes enums as numbers: 0 = Free, 1 = Pro.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(200, { token: 't', email: 'a@b.c', name: 'Ada', planType: 1 })),
    );

    const auth = await login({ email: 'a@b.c', password: 'x' });

    expect(auth.planType).toBe('Pro');
  });

  it('normalizes the numeric planType of the profile', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(200, {
          id: 'u1',
          email: 'a@b.c',
          name: 'Ada',
          planType: 0,
          createdAt: '2026-01-01T00:00:00Z',
        }),
      ),
    );

    const profile = await getMe();

    expect(profile.planType).toBe('Free');
  });

  it('resolves with undefined on a 204 with an empty body', async () => {
    setToken('abc123');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    await expect(deleteForm('f1')).resolves.toBeUndefined();
  });

  it('resolves with undefined on a 200 with an empty body', async () => {
    // POST /api/subscriptions/cancel answers 200 OK with no body.
    setToken('abc123');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })));

    await expect(cancelSubscription()).resolves.toBeUndefined();
  });

  it('confirms a checkout session and tolerates the empty 200 body', async () => {
    // POST /api/subscriptions/confirm answers 200 with no body.
    setToken('abc123');
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(confirmCheckoutSession('cs_test_123')).resolves.toBeUndefined();

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/subscriptions/confirm');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ sessionId: 'cs_test_123' });
  });

  it('normalizePlanType accepts both wire formats', () => {
    expect(normalizePlanType(1)).toBe('Pro');
    expect(normalizePlanType('Pro')).toBe('Pro');
    expect(normalizePlanType(0)).toBe('Free');
    expect(normalizePlanType('Free')).toBe('Free');
  });
});
