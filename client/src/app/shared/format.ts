import { Pipe, PipeTransform } from '@angular/core';

/** "Sara Khan" -> "SK" for the little avatar circles. */
export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('');
}

/** "2026-10-04T09:00:00Z" -> "5 min ago". */
@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(iso: string): string {
    const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
    if (seconds < 60) return 'just now';
    if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
    return new Date(iso).toLocaleDateString();
  }
}

/** A Date as "2026-10-20" in the user's own time zone (toISOString would use UTC). */
export function localDateText(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** True if a "2026-10-20" due date is before today (in the user's time zone). */
export function isOverdue(dueDate: string | null): boolean {
  return !!dueDate && dueDate < localDateText(new Date());
}

/** "2026-10-20" -> "Oct 20". Parsed as a local calendar day, so it never shifts by time zone. */
export function dueLabel(dueDate: string): string {
  const [year, month, day] = dueDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}
