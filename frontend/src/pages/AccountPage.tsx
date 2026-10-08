import { useEffect, useState } from 'react';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage, Loading } from '../components/Feedback';

export default function AccountPage() {
  const { user, refresh, setUser } = useAuth();

  const [name, setName] = useState(user?.name ?? '');
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [busy, setBusy] = useState(false);
  const [cancelled, setCancelled] = useState(false);

  // The plan in the JWT is a snapshot from login: always re-read it from the API.
  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (user) setName(user.name);
  }, [user]);

  async function handleSave(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    setSaving(true);

    try {
      const profile = await api.updateMe({ name: name.trim() });
      setUser(profile);
      setSaved(true);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not update the profile.');
    } finally {
      setSaving(false);
    }
  }

  async function handleUpgrade() {
    setError(null);
    setBusy(true);

    try {
      const { checkoutUrl } = await api.createCheckoutSession();
      // Stripe Checkout lives on another domain: leave the SPA.
      window.location.assign(checkoutUrl);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not start the checkout.');
      setBusy(false);
    }
  }

  async function handleCancel() {
    if (!window.confirm('Cancel your Pro subscription?')) return;
    setError(null);
    setSaved(false);
    setCancelled(false);
    setBusy(true);

    try {
      await api.cancelSubscription();
      // The API mirrors the cancellation locally as soon as Stripe accepts it, so the
      // profile is already back to Free: refresh it now instead of waiting for the webhook
      // (which cannot reach a local API).
      await refresh();
      setCancelled(true);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not cancel the subscription.');
    } finally {
      setBusy(false);
    }
  }

  if (!user) return <Loading />;

  const isPro = user.planType === 'Pro';

  return (
    <div>
      <div className="page-head">
        <h1>Account</h1>
        <span className={`badge ${isPro ? 'badge-pro' : ''}`}>{user.planType}</span>
      </div>

      <div className="card">
        <h2>Profile</h2>
        <form onSubmit={handleSave}>
          <label>
            Name
            <input
              type="text"
              value={name}
              required
              onChange={(event) => setName(event.target.value)}
            />
          </label>

          <label>
            Email
            <input type="email" value={user.email} readOnly disabled />
          </label>

          {saved && <p className="success">Saved.</p>}
          {error && <ErrorMessage message={error} />}

          <button type="submit" className="primary" disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </form>
      </div>

      <div className="card">
        <h2>Subscription</h2>
        {cancelled && (
          <p className="notice">
            Your subscription has been cancelled and your account is back on the Free plan.
          </p>
        )}
        {isPro ? (
          <>
            <p className="muted">
              You are on the Pro plan: unlimited forms. Cancelling takes effect immediately and
              moves your account back to the Free plan.
            </p>
            <button type="button" className="danger" onClick={handleCancel} disabled={busy}>
              Cancel subscription
            </button>
          </>
        ) : (
          <>
            <p className="muted">
              The Free plan is limited to 3 forms. Upgrade to Pro for unlimited forms.
            </p>
            <p className="notice">
              Payments run in Stripe test mode: nothing will be charged and no real card is
              saved. On the Stripe page, use the test card <strong>4242 4242 4242 4242</strong>{' '}
              with any future expiry date and any CVC.
            </p>
            <button type="button" className="primary" onClick={handleUpgrade} disabled={busy}>
              {busy ? 'Redirecting…' : 'Upgrade to Pro'}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
