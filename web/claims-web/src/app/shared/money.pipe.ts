import { Pipe, PipeTransform } from '@angular/core';

const USD = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2 });

/** Formats an amount as USD (single currency, see ai-log assumptions): `12500 | money` → "$12,500.00". */
export function formatMoney(value: number | null | undefined, signed = false): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  const text = USD.format(value);
  return signed && value > 0 ? `+${text}` : text;
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  /** `signed` adds a "+" to positive changes (reserve history deltas). */
  transform(value: number | null | undefined, signed = false): string {
    return formatMoney(value, signed);
  }
}
