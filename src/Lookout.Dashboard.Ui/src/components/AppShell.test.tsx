import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppShell } from './AppShell';

describe('AppShell brand lockup', () => {
  it('renders the mark, wordmark, and tagline as a single labelled link', () => {
    render(
      <AppShell>
        <div>content</div>
      </AppShell>,
    );

    const brand = screen.getByRole('link', {
      name: /lookout\s*[—-]\s*diagnostics for asp\.net core/i,
    });
    expect(brand).toHaveAttribute('href', '#/');
    expect(brand).toHaveTextContent('lookout');
    expect(brand).toHaveTextContent(/diagnostics for asp\.net core/i);

    const svg = brand.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg).toHaveAttribute('aria-hidden', 'true');
  });
});
