import { Link } from 'react-router-dom';

export default function CheckoutCancelPage() {
  return (
    <div className="card narrow">
      <h1>Checkout cancelled</h1>
      <p className="muted">No payment was made and your plan is unchanged.</p>
      <p>
        <Link to="/account">Back to my account</Link> · <Link to="/dashboard">Dashboard</Link>
      </p>
    </div>
  );
}
