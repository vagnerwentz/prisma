import path from 'node:path'
import basicSsl from '@vitejs/plugin-basic-ssl'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// `npm run dev:lan` abre o servidor na rede local com HTTPS (certificado local), para testar
// no celular: o cookie de sessão é Secure e o navegador não o grava em http://192.168...
const lan = process.env.PRISMA_LAN === '1'

export default defineConfig({
  plugins: [react(), tailwindcss(), ...(lan ? [basicSsl()] : [])],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    host: lan,
    // O frontend chama /api/...; o proxy repassa para a API, que responde sob /api como em
    // produção. Mesmo host para o navegador, então o cookie de sessão funciona sem CORS
    // (CLAUDE.md, seção 7).
    proxy: {
      '/api': {
        target: 'https://localhost:7153',
        changeOrigin: true,
        secure: false, // certificado de desenvolvimento do ASP.NET
      },
    },
  },
})
