import { publicEnvironment } from '@/config/env';

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  if (
    publicEnvironment.VITE_API_BASE_URL === undefined ||
    publicEnvironment.VITE_API_BASE_URL === ''
  ) {
    throw new Error('VITE_API_BASE_URL is not configured.');
  }

  const response = await fetch(new URL(path, publicEnvironment.VITE_API_BASE_URL), {
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!response.ok) {
    throw new Error(`API request failed with status ${response.status}.`);
  }

  return (await response.json()) as T;
}
