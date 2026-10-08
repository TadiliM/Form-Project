import { Link, useParams } from 'react-router-dom';

export default function ThanksPage() {
  const { slug = '' } = useParams();

  return (
    <div className="card narrow">
      <h1>Thanks!</h1>
      <p className="muted">Your response has been recorded.</p>
      <p>
        <Link to={`/f/${slug}`}>Send another response</Link>
      </p>
    </div>
  );
}
