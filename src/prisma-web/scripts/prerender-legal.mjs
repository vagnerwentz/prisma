// Gera as páginas da política de privacidade e dos termos de uso (etapa A.4) no modelo dist/legal.html, que o Vite já
// processou (o CSS com hash). Saem dist/privacidade.html e dist/termos.html, servidas em /privacidade e /termos
// (FrontendHosting); o modelo sai do dist. Roda no fim do `npm run build`; a montagem está em legal-pages.mjs.
import fs from 'node:fs'
import path from 'node:path'
import { legalPages, renderLegalPage } from './legal-pages.mjs'

const dist = path.resolve(import.meta.dirname, '..', 'dist')
const template = fs.readFileSync(path.join(dist, 'legal.html'), 'utf8')

for (const page of legalPages) {
  const { html, sections } = renderLegalPage(template, page)
  fs.writeFileSync(path.join(dist, page.file), html)
  console.log(`${page.file} gerada (${sections} seções).`)
}

fs.rmSync(path.join(dist, 'legal.html'))
