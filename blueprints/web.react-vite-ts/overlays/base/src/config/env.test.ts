import { describe, expect, it } from 'vitest';
import { parsePublicEnvironment } from '@/config/env';

describe('parsePublicEnvironment', () => {
  it('rejects an invalid public API URL', () => {
    expect(() => parsePublicEnvironment({ VITE_API_BASE_URL: 'not-a-url' })).toThrow();
  });
});
