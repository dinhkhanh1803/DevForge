/** @type {import("next").NextConfig} */
const nextConfig = {
  poweredByHeader: false,
  reactStrictMode: true,
  outputFileTracingRoot: process.cwd(),
  turbopack: { root: process.cwd() },
};

export default nextConfig;
