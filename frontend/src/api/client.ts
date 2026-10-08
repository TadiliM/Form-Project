import type {
  AuthResponse,
  AuthResponseWire,
  CreateCheckoutSessionResponse,
  CreateFormRequest,
  FormDetail,
  FormResponse,
  FormSummary,
  LoginRequest,
  PlanType,
  PlanTypeWire,
  RegisterRequest,
  SubmitResponseRequest,
  UpdateFormRequest,
  UpdateProfileRequest,
  UserProfile,
  UserProfileWire,
} from './types';

const TOKEN_KEY = 'form-project.token';

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (token === null) localStorage.removeItem(TOKEN_KEY);
  else localStorage.setItem(TOKEN_KEY, token);
}

/** Error thrown by every API call. `status` is the HTTP status code. */
export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

// A 401 on a protected call means the token is gone or expired: the app logs out.
// AuthContext registers the handler so the API layer stays free of routing code.
let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Set to false for the login/register calls, where a 401 is a normal error. */
  notifyUnauthorized?: boolean;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  if (options.body !== undefined) headers['Content-Type'] = 'application/json';

  const response = await fetch(`/api${path}`, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });

  if (!response.ok) {
    // Every error body is { "message": "..." }; a 500 may have no body at all.
    let message = `Request failed (${response.status}).`;
    try {
      const data = (await response.json()) as { message?: string };
      if (data?.message) message = data.message;
    } catch {
      // No JSON body: keep the generic message.
    }

    if (response.status === 401 && options.notifyUnauthorized !== false) {
      setToken(null);
      unauthorizedHandler?.();
    }

    throw new ApiError(response.status, message);
  }

  // 204 (DELETE) and 202 (async cancellation) come back with an empty body, and
  // response.json() would throw "Unexpected end of JSON input" on them. Read the
  // raw text and only parse it when there is something to parse.
  const text = await response.text();
  if (text === '') return undefined as T;
  return JSON.parse(text) as T;
}

// Auth

/**
 * The API serializes enums as numbers (0 = Free, 1 = Pro), so every planType is
 * normalized to the readable union as soon as it comes off the wire.
 */
export function normalizePlanType(value: PlanTypeWire): PlanType {
  if (value === 'Pro' || value === 1) return 'Pro';
  return 'Free';
}

function normalizeAuth(auth: AuthResponseWire): AuthResponse {
  return { ...auth, planType: normalizePlanType(auth.planType) };
}

function normalizeProfile(profile: UserProfileWire): UserProfile {
  return { ...profile, planType: normalizePlanType(profile.planType) };
}

export async function register(body: RegisterRequest): Promise<AuthResponse> {
  return normalizeAuth(
    await request<AuthResponseWire>('/auth/register', {
      method: 'POST',
      body,
      notifyUnauthorized: false,
    }),
  );
}

export async function login(body: LoginRequest): Promise<AuthResponse> {
  return normalizeAuth(
    await request<AuthResponseWire>('/auth/login', {
      method: 'POST',
      body,
      notifyUnauthorized: false,
    }),
  );
}

// Forms (creator)

export function listForms() {
  return request<FormSummary[]>('/forms');
}

export function createForm(body: CreateFormRequest) {
  return request<FormSummary>('/forms', { method: 'POST', body });
}

export function getForm(id: string) {
  return request<FormDetail>(`/forms/${id}`);
}

export function updateForm(id: string, body: UpdateFormRequest) {
  return request<FormDetail>(`/forms/${id}`, { method: 'PUT', body });
}

export function deleteForm(id: string) {
  return request<void>(`/forms/${id}`, { method: 'DELETE' });
}

export function listResponses(id: string) {
  return request<FormResponse[]>(`/forms/${id}/responses`);
}

// Public (no token required)

export function getPublicForm(slug: string) {
  return request<FormDetail>(`/forms/public/${encodeURIComponent(slug)}`);
}

export function submitResponse(slug: string, body: SubmitResponseRequest) {
  return request<{ id: string }>(`/forms/public/${encodeURIComponent(slug)}/responses`, {
    method: 'POST',
    body,
  });
}

// User and subscription

export async function getMe(): Promise<UserProfile> {
  return normalizeProfile(await request<UserProfileWire>('/users/me'));
}

export async function updateMe(body: UpdateProfileRequest): Promise<UserProfile> {
  return normalizeProfile(await request<UserProfileWire>('/users/me', { method: 'PUT', body }));
}

export function createCheckoutSession() {
  return request<CreateCheckoutSessionResponse>('/subscriptions/checkout', { method: 'POST' });
}

export function cancelSubscription() {
  return request<void>('/subscriptions/cancel', { method: 'POST' });
}

/**
 * Confirms the Checkout Session Stripe put in the success URL. The API re-reads that
 * session from Stripe and activates the plan, so the upgrade does not depend on the
 * webhook reaching the API (it cannot, on localhost). Idempotent: safe to call twice.
 */
export function confirmCheckoutSession(sessionId: string) {
  return request<void>('/subscriptions/confirm', { method: 'POST', body: { sessionId } });
}
