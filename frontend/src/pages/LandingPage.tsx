import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export default function LandingPage() {
  const { user } = useAuth();

  return (
    <div className="landing">
      <section className="hero">
        <h1>Build a form, share a link, read the answers.</h1>
        <p className="muted">
          Form-Project is a simple form builder. Create an account, design your fields, and send the
          public link to anyone — respondents do not need an account.
        </p>

        <div className="hero-actions">
          {user ? (
            <Link className="button primary" to="/dashboard">
              Go to my dashboard
            </Link>
          ) : (
            <>
              <Link className="button primary" to="/register">
                Sign up for free
              </Link>
              <Link className="button" to="/login">
                Log in
              </Link>
            </>
          )}
        </div>
      </section>

      <section className="features">
        <div className="card">
          <h2>Simple builder</h2>
          <p className="muted">
            Text, choice and number fields with validation, required flags and reordering.
          </p>
        </div>
        <div className="card">
          <h2>Public link</h2>
          <p className="muted">
            Every form gets a unique URL. Share it and collect answers without any setup.
          </p>
        </div>
        <div className="card">
          <h2>Responses in one place</h2>
          <p className="muted">Read every answer, newest first, with the field labels.</p>
        </div>
      </section>

      <section className="pricing">
        <div className="card">
          <h2>Free</h2>
          <p className="price">€0</p>
          <ul>
            <li>Up to 3 forms</li>
            <li>Unlimited responses</li>
            <li>Public share link</li>
          </ul>
        </div>
        <div className="card">
          <h2>Pro</h2>
          <p className="price">€5 / month</p>
          <ul>
            <li>Unlimited forms</li>
            <li>Unlimited responses</li>
            <li>Cancel anytime</li>
          </ul>
        </div>
      </section>
    </div>
  );
}
