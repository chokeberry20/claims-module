import { HttpErrorResponse } from '@angular/common/http';
import { errorMessages, hasCode, humanizeField, summarizeError, toApiError } from './api-error';

describe('API error helpers', () => {
  it('reads the S10.4 body of an API error', () => {
    const body = { type: 'NotFound', title: 'The resource was not found.', status: 404, detail: 'Claim 1 was not found.' };
    expect(toApiError(new HttpErrorResponse({ status: 404, error: body }))).toBe(body);
  });

  it('describes a network failure and a non-API answer', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 })).type).toBe('NetworkError');
    expect(toApiError(new HttpErrorResponse({ status: 502, error: '<html>' })).title).toBe('The request failed (HTTP 502).');
  });

  it('lists messages with readable field names, General without a prefix', () => {
    const body = {
      type: 'ValidationError',
      title: 'x',
      status: 422,
      errors: { 'Parties[0].FirstName': ['First name is required.'], General: ['At least one claimant is required.'] },
      codes: { General: ['NO_CLAIMANT'] },
    };
    expect(errorMessages(body)).toEqual(['Parties 1 first name: First name is required.', 'At least one claimant is required.']);
    expect(hasCode(body, 'NO_CLAIMANT')).toBe(true);
    expect(hasCode(body, 'OTHER')).toBe(false);
  });

  it('summarizes with a count of hidden messages, or title and detail when there are none', () => {
    const many = { type: 'ValidationError', title: 't', status: 422, errors: { General: ['a', 'b', 'c'] } };
    expect(summarizeError(many)).toBe('a b (+1 more)');
    expect(summarizeError({ type: 'Forbidden', title: 'Not allowed.', status: 403, detail: 'Supervisor role required.' })).toBe(
      'Not allowed. Supervisor role required.',
    );
  });

  it('humanizes field paths', () => {
    expect(humanizeField('LossDescription')).toBe('Loss description');
    expect(humanizeField('InitialReserve.Amount')).toBe('Initial reserve amount');
  });
});
