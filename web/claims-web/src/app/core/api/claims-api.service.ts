import { HttpClient, HttpContext, HttpEvent, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { APP_CONFIG } from '../config/app-config';
import { IDEMPOTENCY_KEY, SKIP_ERROR_SNACKBAR } from './api-error';
import {
  AuditLogEntry,
  CauseOfLossCode,
  ClaimChangeResult,
  ClaimChildAddedResult,
  ClaimDetail,
  ClaimDocument,
  ClaimListQuery,
  ClaimReserves,
  ClaimSummary,
  CreateClaimRequest,
  CreateClaimResult,
  PagedResult,
  PartyInput,
  PolicySummary,
  ReserveSubmission,
  ReserveTransactionState,
  SubmitReserveRequest,
  TransitionOption,
  TransitionStatusRequest,
} from './models';

/** Options for a write: an explicit Idempotency-Key, and whether the caller shows the errors itself. */
export interface WriteOptions {
  readonly idempotencyKey?: string;
  readonly inlineErrors?: boolean;
}

function writeContext(options?: WriteOptions): HttpContext {
  const context = new HttpContext();
  if (options?.idempotencyKey) {
    context.set(IDEMPOTENCY_KEY, options.idempotencyKey);
  }
  if (options?.inlineErrors) {
    context.set(SKIP_ERROR_SNACKBAR, true);
  }
  return context;
}

/**
 * The typed service layer for every claims endpoint used by the UI (S11: no HTTP in components).
 * Authentication, Idempotency-Key, loading state and error snackbars are added by the interceptors.
 */
@Injectable({ providedIn: 'root' })
export class ClaimsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;

  // ------------------------------------------------------------ reference data and policies

  causeOfLossCodes(): Observable<CauseOfLossCode[]> {
    return this.http.get<CauseOfLossCode[]>(`${this.base}/api/reference/cause-of-loss-codes`);
  }

  searchPolicies(term: string): Observable<PolicySummary[]> {
    return this.http.get<PolicySummary[]>(`${this.base}/api/policies/search`, { params: { q: term } });
  }

  // ------------------------------------------------------------ claims

  list(query: ClaimListQuery): Observable<PagedResult<ClaimSummary>> {
    let params = new HttpParams();
    for (const status of query.status ?? []) {
      params = params.append('status', status);
    }
    for (const key of ['dateFrom', 'dateTo', 'assignedHandler', 'causeOfLossCode', 'search', 'sortBy'] as const) {
      const value = query[key];
      if (value) {
        params = params.set(key, value);
      }
    }
    if (query.descending !== undefined) {
      params = params.set('descending', String(query.descending));
    }
    if (query.page) {
      params = params.set('page', query.page);
    }
    if (query.pageSize) {
      params = params.set('pageSize', query.pageSize);
    }
    return this.http.get<PagedResult<ClaimSummary>>(`${this.base}/api/claims`, { params });
  }

  get(claimId: string): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`${this.base}/api/claims/${claimId}`);
  }

  create(request: CreateClaimRequest, options?: WriteOptions): Observable<CreateClaimResult> {
    return this.http.post<CreateClaimResult>(`${this.base}/api/claims`, request, { context: writeContext(options) });
  }

  /** PUT /api/claims/{id}/notes (Overview tab). Empty text clears the notes. */
  updateNotes(claimId: string, notes: string | null, options?: WriteOptions): Observable<ClaimChangeResult> {
    return this.http.put<ClaimChangeResult>(`${this.base}/api/claims/${claimId}/notes`, { notes }, { context: writeContext(options) });
  }

  // ------------------------------------------------------------ parties

  addParty(claimId: string, party: PartyInput, options?: WriteOptions): Observable<ClaimChildAddedResult> {
    return this.http.post<ClaimChildAddedResult>(`${this.base}/api/claims/${claimId}/parties`, party, { context: writeContext(options) });
  }

  /** Soft removal (IsActive = false); the last active Claimant can't be removed (BR-P-01, 422). */
  removeParty(claimId: string, partyId: string): Observable<ClaimChangeResult> {
    return this.http.delete<ClaimChangeResult>(`${this.base}/api/claims/${claimId}/parties/${partyId}`);
  }

  transitions(claimId: string): Observable<TransitionOption[]> {
    return this.http.get<TransitionOption[]>(`${this.base}/api/claims/${claimId}/transitions`);
  }

  transition(claimId: string, request: TransitionStatusRequest, options?: WriteOptions): Observable<ClaimChangeResult> {
    return this.http.put<ClaimChangeResult>(`${this.base}/api/claims/${claimId}/status`, request, {
      context: writeContext(options),
    });
  }

  auditLog(claimId: string, page: number, pageSize: number): Observable<PagedResult<AuditLogEntry>> {
    return this.http.get<PagedResult<AuditLogEntry>>(`${this.base}/api/claims/${claimId}/audit`, {
      params: { page, pageSize },
    });
  }

  // ------------------------------------------------------------ reserves

  reserves(claimId: string): Observable<ClaimReserves> {
    return this.http.get<ClaimReserves>(`${this.base}/api/claims/${claimId}/reserves`);
  }

  submitReserve(claimId: string, request: SubmitReserveRequest, options?: WriteOptions): Observable<ReserveSubmission> {
    return this.http.post<ReserveSubmission>(`${this.base}/api/claims/${claimId}/reserves`, request, {
      context: writeContext(options),
    });
  }

  approveReserve(claimId: string, transactionId: string): Observable<ReserveTransactionState> {
    return this.http.post<ReserveTransactionState>(`${this.base}/api/claims/${claimId}/reserves/${transactionId}/approve`, {});
  }

  rejectReserve(claimId: string, transactionId: string, rejectionReason: string): Observable<ReserveTransactionState> {
    return this.http.post<ReserveTransactionState>(`${this.base}/api/claims/${claimId}/reserves/${transactionId}/reject`, {
      rejectionReason,
    });
  }

  retractReserve(claimId: string, transactionId: string): Observable<ReserveTransactionState> {
    return this.http.post<ReserveTransactionState>(`${this.base}/api/claims/${claimId}/reserves/${transactionId}/retract`, {});
  }

  // ------------------------------------------------------------ documents

  documents(claimId: string): Observable<ClaimDocument[]> {
    return this.http.get<ClaimDocument[]>(`${this.base}/api/claims/${claimId}/documents`);
  }

  /** Multipart upload with progress events (S11.3 "shows progress indicator"). */
  uploadDocument(claimId: string, file: File, documentType: string, notes?: string): Observable<HttpEvent<ClaimDocument>> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('documentType', documentType);
    if (notes) {
      form.append('notes', notes);
    }
    return this.http.post<ClaimDocument>(`${this.base}/api/claims/${claimId}/documents`, form, {
      reportProgress: true,
      observe: 'events',
    });
  }
}
