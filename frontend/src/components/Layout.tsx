import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export default function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  function handleLogout() {
    logout();
    navigate('/login');
  }

  return (
    <div className="app">
      <header className="topbar">
        <Link to={user ? '/dashboard' : '/'} className="brand">
          Form-Project
        </Link>

        <nav className="nav">
          {user ? (
            <>
              <NavLink to="/dashboard">Dashboard</NavLink>
              <NavLink to="/forms/new">New form</NavLink>
              <NavLink to="/account">Account</NavLink>
              <span className={`badge ${user.planType === 'Pro' ? 'badge-pro' : ''}`}>
                {user.planType}
              </span>
              <button type="button" className="link-button" onClick={handleLogout}>
                Log out
              </button>
            </>
          ) : (
            <>
              <NavLink to="/login">Log in</NavLink>
              <NavLink to="/register">Sign up</NavLink>
            </>
          )}
        </nav>
      </header>

      <main className="content">
        <Outlet />
      </main>

      <footer className="footer">Form-Project — a simple form builder.</footer>
    </div>
  );
}
