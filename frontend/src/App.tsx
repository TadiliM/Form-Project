import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import Layout from './components/Layout';
import RequireAuth from './components/RequireAuth';
import AccountPage from './pages/AccountPage';
import CheckoutCancelPage from './pages/CheckoutCancelPage';
import CheckoutSuccessPage from './pages/CheckoutSuccessPage';
import DashboardPage from './pages/DashboardPage';
import FormDetailPage from './pages/FormDetailPage';
import FormNewPage from './pages/FormNewPage';
import FormResponsesPage from './pages/FormResponsesPage';
import LandingPage from './pages/LandingPage';
import LoginPage from './pages/LoginPage';
import NotFoundPage from './pages/NotFoundPage';
import PublicFormPage from './pages/PublicFormPage';
import RegisterPage from './pages/RegisterPage';
import ThanksPage from './pages/ThanksPage';

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route element={<Layout />}>
            {/* Public pages */}
            <Route path="/" element={<LandingPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/f/:slug" element={<PublicFormPage />} />
            <Route path="/f/:slug/thanks" element={<ThanksPage />} />

            {/* Creator pages (JWT required) */}
            <Route element={<RequireAuth />}>
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/forms/new" element={<FormNewPage />} />
              <Route path="/forms/:id" element={<FormDetailPage />} />
              <Route path="/forms/:id/responses" element={<FormResponsesPage />} />
              <Route path="/account" element={<AccountPage />} />
              <Route path="/success" element={<CheckoutSuccessPage />} />
              <Route path="/cancel" element={<CheckoutCancelPage />} />
            </Route>

            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
