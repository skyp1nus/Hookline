import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Pin the workspace root to this app (multiple lockfiles exist higher up).
  turbopack: {
    root: __dirname,
  },
  // Self-contained server output for the Docker image.
  output: "standalone",
};

export default nextConfig;
