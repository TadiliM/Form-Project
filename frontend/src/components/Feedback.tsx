interface ErrorMessageProps {
  message: string;
  action?: React.ReactNode;
}

/** Shows an API error message (they are already user-facing English sentences). */
export function ErrorMessage({ message, action }: ErrorMessageProps) {
  return (
    <p className="error" role="alert">
      {message} {action}
    </p>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <p className="muted">{label}</p>;
}

export function EmptyState({ children }: { children: React.ReactNode }) {
  return <p className="empty">{children}</p>;
}
