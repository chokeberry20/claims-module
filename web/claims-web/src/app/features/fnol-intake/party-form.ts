import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { PARTY_ROLES, PartyInput, PartyType } from '../../core/api/models';
import { labelOf } from '../../shared/status-badge';

/** Inline "Add party" row (S11.2 step 2): role, Person/Company, name fields, email, phone. */
@Component({
  selector: 'app-party-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatButtonToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="add()" class="inline-form" data-testid="party-form">
      <mat-form-field appearance="outline">
        <mat-label>Role</mat-label>
        <mat-select formControlName="role" data-testid="party-role">
          @for (role of roles; track role) {
            <mat-option [value]="role">{{ label(role) }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-button-toggle-group formControlName="type" aria-label="Party type">
        <mat-button-toggle value="Person">Person</mat-button-toggle>
        <mat-button-toggle value="Company" data-testid="party-type-company">Company</mat-button-toggle>
      </mat-button-toggle-group>
      @if (form.controls.type.value === 'Person') {
        <mat-form-field appearance="outline">
          <mat-label>First name</mat-label>
          <input matInput formControlName="firstName" maxlength="100" data-testid="party-first-name" />
          <mat-error>Required</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Last name</mat-label>
          <input matInput formControlName="lastName" maxlength="100" data-testid="party-last-name" />
          <mat-error>Required</mat-error>
        </mat-form-field>
      } @else {
        <mat-form-field appearance="outline" class="wide">
          <mat-label>Company name</mat-label>
          <input matInput formControlName="companyName" maxlength="255" data-testid="party-company-name" />
          <mat-error>Required</mat-error>
        </mat-form-field>
      }
      <mat-form-field appearance="outline">
        <mat-label>Email</mat-label>
        <input matInput type="email" formControlName="email" maxlength="255" />
        <mat-error>Not a valid email</mat-error>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Phone</mat-label>
        <input matInput formControlName="phone" maxlength="50" />
      </mat-form-field>
      <div class="actions">
        <button mat-flat-button type="submit" data-testid="party-add">Add</button>
        <button mat-button type="button" (click)="cancelled.emit()">Cancel</button>
      </div>
    </form>
  `,
  styles: `
    .inline-form { display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px 12px; }
    .inline-form mat-form-field { width: 180px; }
    .inline-form .wide { width: 372px; }
    .actions { display: flex; gap: 8px; }
  `,
})
export class PartyForm {
  readonly added = output<PartyInput>();
  readonly cancelled = output<void>();

  protected readonly roles = PARTY_ROLES;
  protected readonly label = labelOf;

  protected readonly form = inject(NonNullableFormBuilder).group({
    role: ['Claimant' as PartyInput['role'], Validators.required],
    type: ['Person' as PartyType],
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    companyName: [{ value: '', disabled: true }, Validators.required],
    email: ['', Validators.email],
    phone: [''],
  });

  constructor() {
    this.form.controls.type.valueChanges.pipe(takeUntilDestroyed()).subscribe((type) => {
      const person = type === 'Person';
      for (const name of ['firstName', 'lastName'] as const) {
        if (person) this.form.controls[name].enable();
        else this.form.controls[name].disable();
      }
      if (person) this.form.controls.companyName.disable();
      else this.form.controls.companyName.enable();
    });
  }

  protected add(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const text = (s: string) => (s.trim() === '' ? null : s.trim());
    this.added.emit({
      role: v.role,
      type: v.type,
      firstName: v.type === 'Person' ? text(v.firstName) : null,
      lastName: v.type === 'Person' ? text(v.lastName) : null,
      companyName: v.type === 'Company' ? text(v.companyName) : null,
      email: text(v.email),
      phone: text(v.phone),
    });
    this.form.reset({ role: 'Claimant', type: v.type });
  }
}
