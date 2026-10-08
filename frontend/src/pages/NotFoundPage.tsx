import { Link } from 'react-router-dom';

export default function NotFoundPage() {
  return (
    <div className="card narrow">
      <h1>Page not found</h1>
      <p className="muted">The page you are looking for does not exist.</p>
      <p>
        <Link to="/">Back to the home page</Link>
      </p>
    </div>
  );
}
