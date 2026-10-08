// Types mirroring the backend DTOs (backend/backend/Models/Dtos).
// The API serializes to camelCase, so the names below match the JSON exactly.

export type PlanType = 'Free' | 'Pro';

/**
 * What the API actually sends for an enum: ASP.NET Core serializes enums as their
 * numeric value unless a JsonStringEnumConverter is registered (it is not), so
 * `planType` arrives as 0 (Free) or 1 (Pro). The normalizer in client.ts accepts
 * both forms, so the app keeps using the readable union above.
 */
export type PlanTypeWire = number | string;

export type FieldType = 'text' | 'choice' | 'number';

// Auth

export interface RegisterRequest {
  email: string;
  password: string;
  name: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponseWire {
  token: string;
  email: string;
  name: string;
  planType: PlanTypeWire;
}

/** Normalized version of {@link AuthResponseWire} handed to the rest of the app. */
export interface AuthResponse {
  token: string;
  email: string;
  name: string;
  planType: PlanType;
}

// Forms (creator)

export interface FieldRequest {
  type: FieldType;
  label: string;
  isRequired: boolean;
  order: number;
  maxLength?: number | null;
  options?: string[] | null;
  min?: number | null;
  max?: number | null;
}

export interface CreateFormRequest {
  title: string;
  fields: FieldRequest[];
}

export interface UpdateFormRequest {
  title: string;
  fields: FieldRequest[];
}

export interface FieldResponse {
  id: string;
  type: FieldType;
  label: string;
  isRequired: boolean;
  order: number;
  maxLength?: number | null;
  options?: string[] | null;
  min?: number | null;
  max?: number | null;
}

export interface FormSummary {
  id: string;
  title: string;
  publicUrlSlug: string;
  createdAt: string;
  fieldCount: number;
  responseCount: number;
}

export interface FormDetail {
  id: string;
  title: string;
  publicUrlSlug: string;
  createdAt: string;
  responseCount: number;
  fields: FieldResponse[];
}

export interface AnswerRequest {
  fieldId: string;
  value: string;
}

export interface SubmitResponseRequest {
  answers: AnswerRequest[];
}

export interface AnswerResponse {
  fieldId: string;
  label: string;
  value: string;
}

export interface FormResponse {
  id: string;
  submittedAt: string;
  answers: AnswerResponse[];
}

// User and subscription

export interface UserProfileWire {
  id: string;
  email: string;
  name: string;
  planType: PlanTypeWire;
  createdAt: string;
}

/** Normalized version of {@link UserProfileWire} handed to the rest of the app. */
export interface UserProfile {
  id: string;
  email: string;
  name: string;
  planType: PlanType;
  createdAt: string;
}

export interface UpdateProfileRequest {
  name: string;
}

export interface CreateCheckoutSessionResponse {
  checkoutUrl: string;
}
