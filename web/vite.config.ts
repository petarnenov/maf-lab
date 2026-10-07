import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

const here = (path: string) => fileURLToPath(new URL(path, import.meta.url));

const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  resolve: {
    // What a plugin's web part may import from the core (introduce-plugins decision 8): the plugin API and shared code.
    alias: {
      '@maf/plugin-api': here('./src/plugins/api.ts'),
      '@maf/shared': here('./src/shared'),
      // The core's test support, for a plugin's tests only (render helpers, the chat page hosting given plugins).
      '@maf/testing': here('./src/test/index.ts'),
    },
    // A plugin's web part lives outside this folder, so a bare import from it would find no node_modules: these resolve
    // from this project, as Vite's dedupe does for linked packages.
    dedupe: [
      'react',
      'react-dom',
      '@tanstack/react-query',
      'vitest',
      '@testing-library/react',
      '@testing-library/user-event',
      // The observability plugin's browser tracing (OpenTelemetry's web SDK, pinned in this project's package.json).
      '@opentelemetry/api',
      '@opentelemetry/instrumentation',
      '@opentelemetry/instrumentation-fetch',
      '@opentelemetry/exporter-trace-otlp-http',
      '@opentelemetry/resources',
      '@opentelemetry/sdk-trace-web',
      '@opentelemetry/semantic-conventions',
    ],
  },
  server: {
    port: 5174,
    strictPort: true,
    // Each plugin's web part lives in plugins/<name>/web/, outside this folder.
    fs: { allow: ['.', '../plugins'] },
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/dev': { target: apiTarget, changeOrigin: true },
    },
  },
  preview: {
    port: 5174,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // The core's tests, and each plugin's web tests in its own folder (relative to this project).
    include: ['src/**/*.test.{ts,tsx}', '../plugins/*/web/**/*.test.{ts,tsx}'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    restoreMocks: true,
    unstubGlobals: true,
    // Coverage for the Coverage screen: Cobertura, the same format the .NET run writes (see DECISIONS.md).
    coverage: {
      provider: 'v8',
      reporter: ['cobertura', 'text-summary'],
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/**/*.d.ts'],
    },
  },
});
