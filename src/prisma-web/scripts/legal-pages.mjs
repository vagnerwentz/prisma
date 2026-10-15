// Montagem das páginas da política de privacidade e dos termos de uso (etapa A.4), usada nos dois caminhos: o build
// (scripts/prerender-legal.mjs grava dist/privacidade.html e dist/termos.html) e o `npm run dev` (vite.config.ts monta a
// página a cada pedido, com o Markdown do momento). O texto vem de src/legal/*.md (src/legal/markdown.ts, que o Node lê
// sem build); o modelo é o legal.html.
import fs from 'node:fs'
import path from 'node:path'
import { renderMarkdown } from '../src/legal/markdown.ts'

export const legalPages = [
  {
    source: 'privacy-policy.md',
    file: 'privacidade.html',
    path: '/privacidade',
    description: 'Como o Prisma Money trata os seus dados: o que guarda, para quê, por quanto tempo e os seus direitos.',
  },
  {
    source: 'terms-of-use.md',
    file: 'termos.html',
    path: '/termos',
    description: 'As regras de uso do Prisma Money, app de controle financeiro pessoal.',
  },
]

const root = path.resolve(import.meta.dirname, '..')

const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

const fill = (html, marker, value) => {
  if (!html.includes(marker)) throw new Error(`legal.html precisa de ${marker}.`)
  return html.replaceAll(marker, value)
}

// O modelo (template) já processado pelo Vite: no build, com o CSS de hash; no dev, com o CSS servido pelo Vite.
export function renderLegalPage(template, page) {
  const doc = renderMarkdown(fs.readFileSync(path.join(root, 'src', 'legal', page.source), 'utf8'))
  // O sumário leva às seções numeradas; o resumo fica fora dele.
  const contents = doc.headings
    .filter((h) => h.level === 2 && /^\d/.test(h.text))
    .map((h) => `<li><a href="#${h.id}">${escape(h.text)}</a></li>`)
    .join('')
  // O resumo vem primeiro (é o que a pessoa lê); o sumário, logo antes da seção 1.
  const firstSection = doc.html.indexOf('<h2 id="1-')
  const [summary, sections] = firstSection < 0 ? ['', doc.html] : [doc.html.slice(0, firstSection), doc.html.slice(firstSection)]
  const title = doc.title.replace(/ — Prisma Money$/, '')
  const body = [
    `<h1>${escape(title)}</h1>`,
    '<div class="legal-rule" aria-hidden="true"></div>',
    doc.meta && `<p class="legal-meta">${doc.meta}</p>`,
    summary,
    contents && `<nav class="legal-contents" aria-label="Seções"><p>Nesta página</p><ol>${contents}</ol></nav>`,
    sections,
  ]
    .filter(Boolean)
    .join('\n')

  let html = fill(template, '<!--legal-title-->', escape(title))
  html = fill(html, '<!--legal-description-->', escape(page.description))
  html = fill(html, '<!--legal-path-->', page.path)
  html = fill(html, '<!--legal-html-->', body)
  return { html, sections: doc.headings.length }
}
