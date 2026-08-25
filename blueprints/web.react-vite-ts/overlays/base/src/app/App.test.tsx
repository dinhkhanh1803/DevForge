import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from '@/app/App';

describe('App', () => {
  it('renders the ready state', () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'React workspace ready' })).toBeInTheDocument();
  });
});
