import { describe, expect, it } from 'vitest'
import { inline, renderMarkdown, slug } from './markdown'
import privacyPolicy from './privacy-policy.md?raw'
import termsOfUse from './terms-of-use.md?raw'

describe('inline', () => {
  it('escapa o texto antes de qualquer marcação', () => {
    expect(inline('<script>alert("x")</script> & **ok**')).toBe(
      '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; <strong>ok</strong>',
    )
  })

  it('faz link de [texto](url), de endereço solto e de e-mail, sem link dentro de link', () => {
    expect(inline('Leia a [Política](https://prismamoney.com.br/privacidade).')).toBe(
      'Leia a <a href="https://prismamoney.com.br/privacidade">Política</a>.',
    )
    expect(inline('ANPD: https://www.gov.br/anpd.')).toBe('ANPD: <a href="https://www.gov.br/anpd">https://www.gov.br/anpd</a>.')
    expect(inline('**contato@prismamoney.com.br**')).toBe(
      '<strong><a href="mailto:contato@prismamoney.com.br">contato@prismamoney.com.br</a></strong>',
    )
  })

  it('recusa link que não seja http(s): fica como texto', () => {
    expect(inline('[clique](javascript:alert(1))')).toBe('[clique](javascript:alert(1))')
  })
})

describe('renderMarkdown', () => {
  const doc = renderMarkdown(
    [
      '# Título',
      '',
      '**Versão**: v1.0 | **Vigência a partir de**: 15/10/2026',
      '',
      '## 1. Seção é aqui',
      '',
      'Parágrafo que',
      'continua na linha de baixo.',
      '',
      '- item um',
      '  que continua',
      '  - de dentro',
      '- item dois',
      '',
      '| A | B |',
      '|---|---|',
      '| 1 | **2** |',
      '',
      '---',
    ].join('\n'),
  )

  it('separa título, versão e seções', () => {
    expect(doc.title).toBe('Título')
    expect(doc.meta).toBe('<strong>Versão</strong>: v1.0 | <strong>Vigência a partir de</strong>: 15/10/2026')
    expect(doc.headings).toEqual([{ level: 2, id: '1-secao-e-aqui', text: '1. Seção é aqui' }])
  })

  it('monta parágrafos, listas com um nível dentro, tabela e linha divisória', () => {
    expect(doc.html).toBe(
      [
        '<h2 id="1-secao-e-aqui">1. Seção é aqui</h2>',
        '<p>Parágrafo que continua na linha de baixo.</p>',
        '<ul><li>item um que continua<ul><li>de dentro</li></ul></li><li>item dois</li></ul>',
        '<div class="legal-table"><table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td data-label="A">1</td><td data-label="B"><strong>2</strong></td></tr></tbody></table></div>',
        '<hr>',
      ].join('\n'),
    )
  })

  it('gera id sem acento e sem pontuação', () => {
    expect(slug('3.6 Cópias de segurança')).toBe('3-6-copias-de-seguranca')
  })
})

// Os textos publicados: nenhuma nota interna do rascunho escapa para a página, e cada um diz a versão.
describe.each([
  ['privacy-policy.md', privacyPolicy],
  ['terms-of-use.md', termsOfUse],
])('%s', (_, source) => {
  it('não tem nota de rascunho nem lacuna', () => {
    expect(source).not.toMatch(/\[ADVOGADO|\[ANTES DE PUBLICAR|\[CONFIRMAR|RASCUNHO|\[data|\[prazo/)
  })

  it('vira página com título, versão e seções', () => {
    const doc = renderMarkdown(source)
    expect(doc.meta).toContain('Versão')
    expect(doc.headings.length).toBeGreaterThan(5)
    expect(new Set(doc.headings.map((h) => h.id)).size).toBe(doc.headings.length)
  })
})
