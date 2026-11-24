import { ChangeDetectionStrategy, Component, inject, Injectable } from '@angular/core';
import { FormControl, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';

export interface ConfirmTextInput {
  readonly label: string;
  readonly required?: boolean;
  readonly minLength?: number;
  readonly maxLength?: number;
  readonly hint?: string;
}

export interface ConfirmDialogData {
  readonly title: string;
  readonly message?: string;
  /** Bullet points: warnings to acknowledge, checklist items. */
  readonly items?: readonly string[];
  readonly confirmLabel?: string;
  /** Asks for a reason or justification as part of confirming. */
  readonly input?: ConfirmTextInput;
  readonly destructive?: boolean;
}

/** What the user confirmed: `text` is the trimmed input, when one was asked for. */
export interface ConfirmResult {
  readonly text: string | null;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (data.message) {
        <p>{{ data.message }}</p>
      }
      @if (data.items?.length) {
        <ul class="items">
          @for (item of data.items; track $index) {
            <li>{{ item }}</li>
          }
        </ul>
      }
      @if (data.input; as field) {
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ field.label }}</mat-label>
          <textarea matInput rows="3" [formControl]="text" data-testid="confirm-text"></textarea>
          @if (field.hint) {
            <mat-hint>{{ field.hint }}</mat-hint>
          }
          @if (field.maxLength) {
            <mat-hint align="end">{{ text.value.length }} / {{ field.maxLength }}</mat-hint>
          }
          @if (text.hasError('required')) {
            <mat-error>{{ field.label }} is required.</mat-error>
          } @else if (text.hasError('minlength')) {
            <mat-error>At least {{ field.minLength }} characters.</mat-error>
          } @else if (text.hasError('maxlength')) {
            <mat-error>At most {{ field.maxLength }} characters.</mat-error>
          }
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="cancel()" data-testid="confirm-cancel">Cancel</button>
      <button
        mat-flat-button
        [class.destructive]="data.destructive"
        [disabled]="text.invalid"
        (click)="confirm()"
        data-testid="confirm-ok"
      >
        {{ data.confirmLabel ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .items { margin: 0 0 12px; padding-left: 20px; }
    .destructive { --mat-button-filled-container-color: var(--mat-sys-error); }
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ConfirmDialog, ConfirmResult>>(MatDialogRef);
  protected readonly text = new FormControl('', { nonNullable: true, validators: validatorsFor(this.data.input) });

  protected cancel(): void {
    this.ref.close(undefined);
  }

  protected confirm(): void {
    if (this.text.invalid) {
      return;
    }
    this.ref.close({ text: this.data.input ? this.text.value.trim() : null });
  }
}

function validatorsFor(input: ConfirmTextInput | undefined): ValidatorFn[] {
  if (!input) {
    return [];
  }
  const validators: ValidatorFn[] = [];
  if (input.required) {
    validators.push(Validators.required, Validators.pattern(/\S/));
  }
  if (input.minLength) {
    validators.push(Validators.minLength(input.minLength));
  }
  if (input.maxLength) {
    validators.push(Validators.maxLength(input.maxLength));
  }
  return validators;
}

/** Opens the confirm dialog; emits the result when confirmed, undefined when cancelled or dismissed. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  confirm(data: ConfirmDialogData): Observable<ConfirmResult | undefined> {
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmResult>(ConfirmDialog, { data, width: '520px', autoFocus: 'dialog' })
      .afterClosed();
  }
}
