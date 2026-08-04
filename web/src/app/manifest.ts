import type { MetadataRoute } from "next";

/**
 * Special Next.js file: rendered at `/manifest.webmanifest` and linked automatically, no manual
 * `<link>` tag needed. This is what makes the install prompt (`InstallAppButton`) and "Add to
 * Home Screen" possible at all — without a manifest a browser has nothing to install.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Aegis — Infrastructure Operations",
    short_name: "Aegis",
    description:
      "Asset registry, incident intake and predictive maintenance for utilities and municipal infrastructure authorities.",
    start_url: "/dashboard",
    display: "standalone",
    background_color: "#07090c",
    theme_color: "#07090c",
    icons: [
      { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
