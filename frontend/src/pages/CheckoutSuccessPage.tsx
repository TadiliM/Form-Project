import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from '../components/Feedback';

// The plan used to change only when Stripe delivered its webhook, which can lag a few
// seconds behind the redirect — and cannot reach a local API at all. So: confirm the
// Checkout Session with the API first (it re-reads it from Stripe), then poll the profile
// for a short window in case the webhook is the one finishing the job.
const POLL_INTERVAL_MS = 2000;
const POLL_DURATION_MS = 20000;
const SESSION_ID_PARAM = 'session_id';

export default function CheckoutSuccessPage() {
  const { user, refresh } = useAuth();
  const [waiting, setWaiting] = useState(true);
  const [confirmError, setConfirmError] = useState<string | null>(null);

  useEffect(() => {
    let stopped = false;
    const startedAt = Date.now();

    async function poll() {
      const profile = await refresh();
      if (stopped) return;

      if (profile?.planType === 'Pro' || Date.now() - startedAt >= POLL_DURATION_MS) {
        setWaiting(false);
        return;
      }

      window.setTimeout(() => void poll(), POLL_INTERVAL_MS);
    }

    async function confirmThenPoll() {
      // The success URL carries the session id ({CHECKOUT_SESSION_ID} in the backend).
      const sessionId = new URLSearchParams(window.location.search).get(SESSION_ID_PARAM);

      if (sessionId) {
        try {
          await api.confirmCheckoutSession(sessionId);
        } catch (caught) {
          if (stopped) return;
          setConfirmError(
            caught instanceof ApiError ? caught.message : 'Could not confirm the payment.',
          );
        }
      }

      if (!stopped) await poll();
    }

    void confirmThenPoll();
    return () => {
      stopped = true;
    };
  }, [refresh]);

  const isPro = user?.planType === 'Pro';

  return (
    <div className="card narrow">
      <h1>Payment received</h1>

      {isPro ? (
        <p className="success">Your account is now on the Pro plan. Enjoy unlimited forms!</p>
      ) : (
        <>
          {confirmError && <ErrorMessage message={confirmError} />}
          <p className="muted">
            {waiting
              ? 'Confirming your subscription… this can take a few seconds.'
              : 'Your payment was accepted. The plan update may still be in flight — check the account page in a moment.'}
          </p>
        </>
      )}

      <p>
        <Link to="/account">Go to my account</Link> · <Link to="/dashboard">Dashboard</Link>
      </p>
    </div>
  );
}
