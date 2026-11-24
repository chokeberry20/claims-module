import { HttpContextToken, HttpErrorResponse } from '@angular/common/http';

/**
 * The API's error body (S10.4 plus `codes`, `detail` and `correlationId`; D-72), sent as
 * application/problem+json for 400, 401, 403, 404, 409, 422, 429 and 500.
 * Field-less messages use the key `General`.
 */
export interface ApiErrorBody {
  readonly type: string;
  readonly title: string;
  readonly status: number;
  readonly detail?: string | null;
  readonly errors?: Readonly<Record<string, readonly string[]>> | null;
  readonly codes?: Readonly<Record<string, readonly string[]>> | null;
  readonly correlationId?: string | null;
}

export const GENERAL_FIELD = 'General';

/** The request's errors are shown by the caller (inline form errors), so the error interceptor stays quiet. */
export const SKIP_ERROR_SNACKBAR = new HttpContextToken<boolean>(() => false);

/** Background calls (the health ping) don't drive the global loading bar. */
export const SKIP_LOADING = new HttpContextToken<boolean>(() => false);

/**
 * The Idempotency-Key for a write. Leave it empty and the interceptor generates one per request; set it
 * when the same logical submit may be sent again (a retried form), so the server replays the first result.
 */
export const IDEMPOTENCY_KEY = new HttpContextToken<string | null>(() => null);

export function isApiErrorBody(body: unknown): body is ApiErrorBody {
  const b = body as Partial<ApiErrorBody> | null;
  return !!b && typeof b === 'object' && typeof b.title === 'string' && typeof b.status === 'number';
}

/** The API error body of a failed call, or a synthetic one for network failures and non-API answers. */
export function toApiError(error: unknown): ApiErrorBody {
  if (error instanceof HttpErrorResponse) {
    if (isApiErrorBody(error.error)) {
      return error.error;
    }
    if (error.status === 0) {
      return { type: 'NetworkError', title: 'The API could not be reached. Check your connection and try again.', status: 0 };
    }
    return { type: 'HttpError', title: `The request failed (HTTP ${error.status}).`, status: error.status };
  }
  return { type: 'ClientError', title: 'Something went wrong in the browser.', status: -1 };
}

/** All messages of an error body, field-specific ones prefixed with their field ("LossDate: …"). */
export function errorMessages(body: ApiErrorBody): string[] {
  const messages: string[] = [];
  for (const [field, list] of Object.entries(body.errors ?? {})) {
    for (const message of list) {
      messages.push(field === GENERAL_FIELD ? message : `${humanizeField(field)}: ${message}`);
    }
  }
  return messages;
}

/** One line for a snackbar: the title, then the first messages, or the detail when there are none. */
export function summarizeError(body: ApiErrorBody, maxMessages = 2): string {
  const messages = errorMessages(body);
  if (messages.length > 0) {
    const shown = messages.slice(0, maxMessages).join(' ');
    const more = messages.length > maxMessages ? ` (+${messages.length - maxMessages} more)` : '';
    return `${shown}${more}`;
  }
  return body.detail && body.status !== 401 ? `${body.title} ${body.detail}` : body.title;
}

/** Whether the error carries the given rule code (in any field). */
export function hasCode(body: ApiErrorBody, code: string): boolean {
  return Object.values(body.codes ?? {}).some((codes) => codes.includes(code));
}

/** "Parties[0].FirstName" → "Parties 1 first name"; "LossDescription" → "Loss description". */
export function humanizeField(field: string): string {
  const spaced = field
    .replace(/\[(\d+)\]/g, (_, i: string) => ` ${Number(i) + 1}`)
    .replace(/\./g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .trim()
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
