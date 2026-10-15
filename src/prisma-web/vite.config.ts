import fs from 'node:fs'
import path from 'node:path'
import basicSsl from '@vitejs/plugin-basic-ssl'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'
import { legalPages, renderLegalPage } from './scripts/legal-pages.mjs'

// No `npm run dev`, /privacidade e /termos são montadas a cada pedido, do Markdown do momento (etapa A.4). No build, o
// mesmo código grava as páginas (scripts/prerender-legal.mjs), e quem as serve é a API (FrontendHosting).
function legalPagesInDev(): Plugin {
  return {
    name: 'prisma-legal-pages',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use(async (req, res, next) => {
        const pathname = (req.url ?? '').split(/[?#]/)[0].replace(/\/+$/, '').toLowerCase()
        const page = legalPages.find((p) => p.path === pathname)
        if (!page) return next()
        try {
          const raw = fs.readFileSync(path.resolve(import.meta.dirname, 'legal.html'), 'utf8')
          const template = await server.transformIndexHtml(page.path, raw)
          res.setHeader('Content-Type', 'text/html; charset=utf-8')
          res.end(renderLegalPage(template, page).html)
        } catch (error) {
          next(error)
        }
      })
    },
  }
}

// `npm run dev:lan` abre o servidor na rede local com HTTPS (certificado local), para testar
// no celular: o cookie de sessão é Secure e o navegador não o grava em http://192.168...
const lan = process.env.PRISMA_LAN === '1'

export default defineConfig(({ isSsrBuild }) => ({
  // Versão do app nos relatos de erro do navegador (etapa H.3b): o momento do build, que diz de qual
  // deploy o erro veio.
  define: { __APP_VERSION__: JSON.stringify(new Date().toISOString().slice(0, 16) + 'Z') },
  plugins: [react(), tailwindcss(), legalPagesInDev(), ...(lan ? [basicSsl()] : [])],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  // Duas páginas: o app (index.html) e a apresentação (landing.html, docs/landing.md), que não carrega o app. A API
  // decide qual entregar em "/" (FrontendHosting). A apresentação é React pré-renderizado: o build de servidor
  // (--ssr, src/landing/server.tsx) gera o HTML dela, que scripts/prerender-landing.mjs põe no landing.html.
  build: isSsrBuild
    ? {}
    : {
        rollupOptions: {
          input: {
            index: path.resolve(import.meta.dirname, 'index.html'),
            landing: path.resolve(import.meta.dirname, 'landing.html'),
            // Modelo das páginas da política e dos termos (A.4): scripts/prerender-legal.mjs o preenche.
            legal: path.resolve(import.meta.dirname, 'legal.html'),
          },
        },
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
}))
