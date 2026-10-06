import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DomainsContext, type DomainInUse } from '../plugins/context';
import { agentFetch } from '../test/agentFetch';
import { jsonResponse, renderWithProviders } from '../test/render';
import { ChatPage, NO_DOMAIN } from './ChatPage';

/** The chat page as it reads the domains in use (introduce-plugins 4.11, decision 5h). */
function renderWith(domains: readonly DomainInUse[] | undefined) {
  vi.stubGlobal(
    'fetch',
    agentFetch(vi.fn(async () => jsonResponse({ conversations: [], nextCursor: null }))),
  );
  return renderWithProviders(
    <DomainsContext.Provider value={domains}>
      <ChatPage />
    </DomainsContext.Provider>,
  );
}

describe('ChatPage and the domains in use', () => {
  it('says up front that no domain is enabled, and nobody can type into it', () => {
    renderWith([]);

    expect(screen.getByRole('status')).toHaveTextContent(NO_DOMAIN);
    expect(screen.getByLabelText('Message')).toBeDisabled();
    expect(screen.getByLabelText('Message')).toHaveAccessibleDescription(NO_DOMAIN);
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
  });

  it('names the domains in use, as each says it to a user', () => {
    renderWith([
      { id: 'billing', scope: { en: 'your firm’s billing' } },
      { id: 'codebase', scope: { en: 'this lab’s own code' } },
    ]);

    expect(screen.getByLabelText('Message')).toBeEnabled();
    expect(screen.getByLabelText('Message')).toHaveAttribute(
      'placeholder',
      'Ask about your firm’s billing and this lab’s own code…',
    );
    expect(screen.queryByText(NO_DOMAIN)).not.toBeInTheDocument();
  });

  it('names no domain while they are not known yet, and lets the user type', () => {
    renderWith(undefined);

    expect(screen.getByLabelText('Message')).toBeEnabled();
    expect(screen.getByLabelText('Message')).toHaveAttribute('placeholder', 'Ask a question…');
  });
});
