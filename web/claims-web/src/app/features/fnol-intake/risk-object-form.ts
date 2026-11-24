import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ASSET_TYPES, AssetType, RiskObjectInput } from '../../core/api/models';

/** Inline "Add risk object" row (S11.2 step 2): asset type, description, damage, reference. */
@Component({
  selector: 'app-risk-object-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="add()" class="inline-form" data-testid="risk-form">
      <mat-form-field appearance="outline">
        <mat-label>Asset type</mat-label>
        <mat-select formControlName="assetType">
          @for (type of assetTypes; track type) {
            <mat-option [value]="type">{{ type }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="wide">
        <mat-label>Asset description</mat-label>
        <input matInput formControlName="assetDescription" maxlength="500" data-testid="risk-description" />
        <mat-error>Required</mat-error>
      </mat-form-field>
      <mat-form-field appearance="outline" class="wide">
        <mat-label>Damage description</mat-label>
        <input matInput formControlName="damageDescription" maxlength="500" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Reference (optional)</mat-label>
        <input matInput formControlName="assetReference" maxlength="255" />
      </mat-form-field>
      <div class="actions">
        <button mat-flat-button type="submit" data-testid="risk-add">Add</button>
        <button mat-button type="button" (click)="cancelled.emit()">Cancel</button>
      </div>
    </form>
  `,
  styles: `
    .inline-form { display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px 12px; }
    .inline-form mat-form-field { width: 180px; }
    .inline-form .wide { width: 300px; }
    .actions { display: flex; gap: 8px; }
  `,
})
export class RiskObjectForm {
  /** Only one risk object may be primary (server rule RISK_OBJECT_MULTIPLE_PRIMARY). */
  readonly hasPrimary = input(false);
  readonly added = output<RiskObjectInput>();
  readonly cancelled = output<void>();

  protected readonly assetTypes = ASSET_TYPES;

  protected readonly form = inject(NonNullableFormBuilder).group({
    assetType: ['Vehicle' as AssetType],
    assetDescription: ['', [Validators.required, Validators.pattern(/\S/)]],
    damageDescription: [''],
    assetReference: [''],
  });

  protected add(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const text = (s: string) => (s.trim() === '' ? null : s.trim());
    this.added.emit({
      assetType: v.assetType,
      assetDescription: v.assetDescription.trim(),
      damageDescription: text(v.damageDescription),
      assetReference: text(v.assetReference),
      // The first risk object is the primary one (S9.4: one primary per claim).
      isPrimary: !this.hasPrimary(),
    });
    this.form.reset({ assetType: v.assetType });
  }
}
