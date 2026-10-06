import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders } from '@maf/testing';
import { ${Name}Page } from './${Name}Page';

describe('${Name}Page', () => {
  it("shows the plugin's summary for the signed-in user", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ conversations: 3, moreThanAPage: false })),
    );
    renderWithProviders(<${Name}Page />);
    expect(await screen.findByText('Conversations: 3')).toBeInTheDocument();
  });
});
