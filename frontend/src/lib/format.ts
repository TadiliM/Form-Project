/** Formats an ISO date as a short, readable local date-time. */
export function formatDateTime(iso: string): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Public URL of a form, to be shared with respondents. */
export function publicFormUrl(slug: string): string {
  return `${window.location.origin}/f/${slug}`;
}
