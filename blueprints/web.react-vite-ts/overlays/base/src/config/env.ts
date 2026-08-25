import { z } from 'zod';

const publicEnvironmentSchema = z.object({
  VITE_API_BASE_URL: z.url().optional().or(z.literal('')),
});

export type PublicEnvironment = z.infer<typeof publicEnvironmentSchema>;

export function parsePublicEnvironment(environment: Record<string, unknown>): PublicEnvironment {
  return publicEnvironmentSchema.parse(environment);
}

export const publicEnvironment = parsePublicEnvironment(import.meta.env);
