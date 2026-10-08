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
    setBusy(true);

    try {
      await api.cancelSubscription();
      // Cancellation is asynchronous (202): the webhook updates the plan afterwards.
      setSaved(true);
      window.setTimeout(() => void refresh(), 3000);
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
        {isPro ? (
          <>
            <p className="muted">
              You are on the Pro plan: unlimited forms. You can cancel at any time; the plan stays
              active until the end of the period.
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
            <button type="button" className="primary" onClick={handleUpgrade} disabled={busy}>
              {busy ? 'Redirecting…' : 'Upgrade to Pro'}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
