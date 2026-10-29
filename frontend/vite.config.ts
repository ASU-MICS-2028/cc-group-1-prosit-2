import path from "path"
import babel from "@rolldown/plugin-babel"
import tailwindcss from "@tailwindcss/vite"
import react, { reactCompilerPreset } from "@vitejs/plugin-react"
import { VitePWA } from "vite-plugin-pwa"
import { defineConfig } from "vitest/config"

// Placeholder brand colour until the Figma design sets it (docs/adr/0002)
const BRAND = "#0b6e69"

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // React Compiler: auto-memoizes components so we don't hand-write useMemo/useCallback
    babel({ presets: [reactCompilerPreset()] }),
    tailwindcss(),
    // The installable, offline app. Only `npm run build` makes the service worker;
    // test it with `npm run preview`.
    VitePWA({
      // Ask before switching to a new version: never reload under a CHV mid-visit.
      registerType: "prompt",
      manifest: {
        id: "/",
        name: "HealthConnect Ghana",
        short_name: "HealthConnect",
        description:
          "Patient records, vitals and referrals for Community Health Volunteers, even without internet.",
        lang: "en",
        start_url: "/",
        scope: "/",
        display: "standalone",
        theme_color: BRAND,
        background_color: "#fdfdfd",
        icons: [
          { src: "pwa-64x64.png", sizes: "64x64", type: "image/png" },
          { src: "pwa-192x192.png", sizes: "192x192", type: "image/png" },
          { src: "pwa-512x512.png", sizes: "512x512", type: "image/png" },
          {
            src: "maskable-icon-512x512.png",
            sizes: "512x512",
            type: "image/png",
            purpose: "maskable",
          },
        ],
      },
      workbox: {
        // Saved on install: every page's code, styles, icons and language files,
        // so the whole app opens offline.
        globPatterns: ["**/*.{js,css,html,ico,png,svg,webmanifest}"],
        globIgnores: ["app-icon.svg"],
        // Any address opened offline (/patients/123) gets the app, which routes it.
        navigateFallback: "/index.html",
        // ...except the API, which must always go to the server. Patient data is never
        // put in the service worker cache; it lives in the phone's own database (src/db).
        navigateFallbackDenylist: [/^\/api\//, /^\/health/],
        cleanupOutdatedCaches: true,
      },
    }),
  ],
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "./src") },
  },
  // `npm run dev`: send /api to the backend on this machine, the same path nginx forwards
  // on the server, so the app code is identical everywhere.
  server: {
    proxy: { "/api": process.env.API_PROXY_TARGET ?? "http://localhost:8000" },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    // Pages are lazy chunks compiled on first use; under a full parallel run that can pass 5 s.
    testTimeout: 15_000,
    css: false,
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      // shadcn primitives are vendored code; entry/types/tests are not logic
      exclude: [
        "src/components/ui/**",
        "src/main.tsx",
        "src/**/*.d.ts",
        "src/test/**",
        "src/**/*.test.{ts,tsx}",
      ],
    },
  },
})
