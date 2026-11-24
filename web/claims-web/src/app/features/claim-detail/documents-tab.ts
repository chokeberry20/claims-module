import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { catchError, EMPTY, switchMap } from 'rxjs';
import { ClaimsApiService } from '../../core/api/claims-api.service';
import { ClaimDocument } from '../../core/api/models';
import { NotificationService } from '../../core/ui/notification.service';

/** S13: the allowlist and size limit, checked before uploading (the server checks again). */
export const ALLOWED_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png', '.docx', '.xlsx', '.txt', '.csv'];
export const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;
export const DOCUMENT_TYPES = ['Photo', 'Police report', 'Invoice', 'Estimate', 'Correspondence', 'Medical report', 'Other'];

/** Why a file can't be uploaded, or null when it can. */
export function uploadProblem(file: { name: string; size: number }): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!ALLOWED_EXTENSIONS.includes(extension)) {
    return `${file.name}: only PDF, JPEG, PNG, DOCX, XLSX, TXT and CSV files can be uploaded.`;
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return `${file.name} is larger than 50 MB.`;
  }
  if (file.size === 0) {
    return `${file.name} is empty.`;
  }
  return null;
}

/**
 * Documents tab (S11.3, S13): list with name, type, date, uploader and size; download through a fresh
 * short-lived link opened in a new tab; upload through a file picker with a progress bar.
 */
@Component({
  selector: 'app-documents-tab',
  imports: [
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="upload">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Document type</mat-label>
        <mat-select [formControl]="documentType" data-testid="document-type">
          @for (t of documentTypes; track t) {
            <mat-option [value]="t">{{ t }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <input #fileInput type="file" hidden [accept]="accept" (change)="onFile($event)" data-testid="file-input" />
      <button mat-flat-button (click)="fileInput.click()" [disabled]="progress() !== null" data-testid="upload">
        <mat-icon>upload_file</mat-icon> Upload document
      </button>
      <span class="hint">PDF, JPEG, PNG, DOCX, XLSX, TXT or CSV · up to 50 MB</span>
    </div>
    @if (progress() !== null) {
      <div class="progress" data-testid="upload-progress">
        <span>Uploading {{ uploadingName() }} · {{ progress() }}%</span>
        <mat-progress-bar mode="determinate" [value]="progress()" />
      </div>
    }

    <table mat-table [dataSource]="documents()" class="documents">
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let d">{{ d.documentName }}</td>
      </ng-container>
      <ng-container matColumnDef="type">
        <th mat-header-cell *matHeaderCellDef>Type</th>
        <td mat-cell *matCellDef="let d">{{ d.documentType }}</td>
      </ng-container>
      <ng-container matColumnDef="uploaded">
        <th mat-header-cell *matHeaderCellDef>Uploaded</th>
        <td mat-cell *matCellDef="let d">{{ d.uploadedAt | date: 'short' }}</td>
      </ng-container>
      <ng-container matColumnDef="uploader">
        <th mat-header-cell *matHeaderCellDef>Uploaded by</th>
        <td mat-cell *matCellDef="let d">{{ d.uploadedByName }}</td>
      </ng-container>
      <ng-container matColumnDef="size">
        <th mat-header-cell *matHeaderCellDef class="num">Size</th>
        <td mat-cell *matCellDef="let d" class="num">{{ d.fileSizeBytes / 1024 | number: '1.0-1' }} KB</td>
      </ng-container>
      <ng-container matColumnDef="download">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let d">
          <button mat-button (click)="download(d)" [attr.data-testid]="'download-' + d.documentName"><mat-icon>download</mat-icon> Download</button>
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
    @if (documents().length === 0) {
      <p class="muted" data-testid="no-documents">No documents yet.</p>
    }
  `,
  styles: `
    :host { display: block; padding: 16px 4px; }
    .upload { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    .upload mat-form-field { width: 220px; }
    .hint, .muted { opacity: 0.7; font: var(--mat-sys-body-small); }
    .progress { margin-bottom: 12px; max-width: 480px; }
    .documents { width: 100%; }
    .num { text-align: right; }
  `,
})
export class DocumentsTab {
  readonly claimId = input.required<string>();
  readonly version = input(0);
  /** A document was uploaded: the page reloads (header, audit). */
  readonly changed = output<void>();

  private readonly api = inject(ClaimsApiService);
  private readonly notifications = inject(NotificationService);

  protected readonly documentTypes = DOCUMENT_TYPES;
  protected readonly accept = ALLOWED_EXTENSIONS.join(',');
  protected readonly columns = ['name', 'type', 'uploaded', 'uploader', 'size', 'download'];
  protected readonly documentType = new FormControl(DOCUMENT_TYPES[0], { nonNullable: true });
  protected readonly documents = signal<ClaimDocument[]>([]);
  protected readonly progress = signal<number | null>(null);
  protected readonly uploadingName = signal('');

  constructor() {
    toObservable(this.version)
      .pipe(
        switchMap(() => this.api.documents(this.claimId()).pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(),
      )
      .subscribe((docs) => this.documents.set(docs));
  }

  protected onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    const problem = uploadProblem(file);
    if (problem) {
      this.notifications.error(problem);
      return;
    }
    this.uploadingName.set(file.name);
    this.progress.set(0);
    this.api.uploadDocument(this.claimId(), file, this.documentType.value).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.progress.set(Math.round((100 * event.loaded) / event.total));
        } else if (event.type === HttpEventType.Response) {
          this.progress.set(null);
          this.notifications.success(`Uploaded ${file.name}.`);
          this.changed.emit();
        }
      },
      error: () => this.progress.set(null),
    });
  }

  /**
   * S11.3: "calls API to get SAS URL and opens in new tab". The tab is opened first (in the click, so it
   * isn't blocked as a pop-up), then pointed at the fresh 1-hour link from the list endpoint.
   */
  protected download(doc: ClaimDocument): void {
    const tab = window.open('', '_blank');
    if (tab) {
      tab.opener = null;
    }
    this.api.documents(this.claimId()).subscribe({
      next: (docs) => {
        const fresh = docs.find((d) => d.documentId === doc.documentId) ?? doc;
        this.documents.set(docs);
        if (tab) {
          tab.location.href = fresh.downloadUrl;
        } else {
          window.open(fresh.downloadUrl, '_blank', 'noopener');
        }
      },
      error: () => tab?.close(),
    });
  }
}
