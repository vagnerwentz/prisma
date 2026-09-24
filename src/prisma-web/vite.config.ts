import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    // O frontend chama /api/...; o proxy repassa para a API sem o prefixo. Mesmo host para o
    // navegador, então o cookie de sessão funciona sem CORS (CLAUDE.md, seção 7).
    proxy: {
      '/api': {
        target: 'https://localhost:7153',
        changeOrigin: true,
        secure: false, // certificado de desenvolvimento do ASP.NET
        rewrite: (url) => url.replace(/^\/api/, ''),
      },
    },
  },
})
