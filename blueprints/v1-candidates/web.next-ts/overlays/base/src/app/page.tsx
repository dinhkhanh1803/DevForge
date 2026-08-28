import { readPublicEnvironment } from "@/lib/environment";

export default function HomePage() {
  const environment = readPublicEnvironment({
    NEXT_PUBLIC_SITE_NAME: process.env.NEXT_PUBLIC_SITE_NAME,
  });
  return (
    <main>
      <p className="eyebrow">DevForge · Next.js starter</p>
      <h1>{environment.siteName}</h1>
      <p>Your team workspace is ready for its first feature.</p>
      <section aria-labelledby="handoff-heading">
        <h2 id="handoff-heading">Start with the handoff</h2>
        <p>
          Read TEAM_START_HERE.md, run the quality gates, then add one reviewed
          feature.
        </p>
        <a href="/api/health">Check local service health</a>
      </section>
    </main>
  );
}
