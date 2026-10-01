import { useCopilotKit } from '@copilotkit/react-core/v2/context';
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { useAuth } from '../auth/useAuth';
import { makeSession } from '../test/render';
import { AgentsProvider } from './AgentsProvider';

function Probe() {
  const { copilotkit } = useCopilotKit();
  const { setSession } = useAuth();
  return (
    <>
      <span data-testid="authorization">{copilotkit.headers.Authorization ?? 'none'}</span>
      <button onClick={() => setSession(makeSession('FIRM_ADMIN'))}>switch</button>
      <button onClick={() => setSession(null)}>sign out</button>
    </>
  );
}

describe('AgentsProvider', () => {
  it("sends the signed-in user's token to every agent, and follows it when it changes", async () => {
    render(
      <AuthProvider initialSession={makeSession('ADVISOR')}>
        <AgentsProvider>
          <Probe />
        </AgentsProvider>
      </AuthProvider>,
    );

    expect(screen.getByTestId('authorization')).toHaveTextContent('Bearer token-ADVISOR');
    await act(async () => screen.getByText('switch').click());
    expect(screen.getByTestId('authorization')).toHaveTextContent('Bearer token-FIRM_ADMIN');
    await act(async () => screen.getByText('sign out').click());
    expect(screen.getByTestId('authorization')).toHaveTextContent('none');
  });
});
