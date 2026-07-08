import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The React dev server proxies /api to the ASP.NET Core backend so the
// frontend can call same-origin URLs regardless of the backend port.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5180',
        changeOrigin: true,
      },
    },
  },
})
