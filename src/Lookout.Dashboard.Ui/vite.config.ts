import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  // Emit relative asset URLs (./assets/...) so the browser resolves them against
  // the server-injected <base href="{mountPrefix}/">. Decouples the bundle from
  // the mount path — MapLookout("/anywhere") still works without a rebuild.
  base: './',
  build: {
    outDir: '../../src/Lookout.Dashboard/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    globals: true,
  },
})
