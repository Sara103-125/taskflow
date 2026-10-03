import { HttpErrorResponse } from '@angular/common/http';

/** Turns any API error into one friendly sentence for the UI. */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return 'Cannot reach the server. Is the API running?';
    if (err.status === 429) return 'Too many attempts. Please wait a minute and try again.';

    const body = err.error;
    if (body?.message) return body.message; // our own errors: { message }
    if (body?.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(' '); // validation
    if (body?.title) return body.title;
  }
  return 'Something went wrong. Please try again.';
}
