import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');

  // Every /api request goes through this dev proxy: the browser only ever talks to
  // localhost:3000, so no cross-origin request is involved and CORS never kicks in.
  // (The backend also exposes a CORS policy for http://localhost:3000 now, so calling
  // http://localhost:5050/api directly would work too, but the proxy keeps relative
  // URLs in the code and avoids mixed-content issues in production.)
  // Override the target with VITE_API_PROXY_TARGET (e.g. http://localhost:5095 when
  // the API runs through "dotnet run" instead of docker compose).
  const proxyTarget = env.VITE_API_PROXY_TARGET || 'http://localhost:5050';

  return {
    plugins: [react()],
    server: {
      // Stripe Checkout redirects to http://localhost:3000/success|cancel (hardcoded
      // in the backend), so the dev server must own that exact port.
      port: 3000,
      strictPort: true,
      proxy: {
        '/api': { target: proxyTarget, changeOrigin: true },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      css: false,
      // Vitest only runs the specs next to the code in src: the built output and
      // anything else outside src are left alone.
      include: ['src/**/*.test.{ts,tsx}'],
    },
  };
});
