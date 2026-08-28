export function readPublicEnvironment(
  input: Record<string, string | undefined>,
): {
  siteName: string;
} {
  const supplied = input.NEXT_PUBLIC_SITE_NAME;
  const siteName = supplied === undefined ? "Team Portal" : supplied.trim();
  if (
    siteName.length === 0 ||
    siteName.length > 64 ||
    [...siteName].some((character) => {
      const code = character.charCodeAt(0);
      return code < 32 || code === 127;
    })
  ) {
    throw new Error("Invalid public site name.");
  }
  return { siteName };
}
