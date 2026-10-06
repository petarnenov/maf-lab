import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { AgentsProvider } from './agents/AgentsProvider';
import { App } from './App';
import { AuthProvider } from './auth/AuthProvider';
import { PluginsProvider } from './plugins/PluginsProvider';
import { startBrowserTracing } from './telemetry/browserTracing';
import './index.css';

// Before anything fetches, so a chat run's request carries the trace it started.
startBrowserTracing();

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <AgentsProvider>
          <PluginsProvider>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </PluginsProvider>
        </AgentsProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
