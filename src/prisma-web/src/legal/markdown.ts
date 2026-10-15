// O Markdown dos textos legais (privacy-policy.md, terms-of-use.md) vira HTML no build (scripts/prerender-legal.mjs,
// etapa A.4). Só o que esses textos usam: títulos, parágrafos, listas (com um nível dentro), tabelas, linha divisória,
// **negrito**, [link](https://…) e endereços soltos (https://… e e-mail). Tudo é escapado antes: nada do texto vira
// outra marcação. Escrito à mão para não trazer dependência (CLAUDE.md, seção 2); roda no Node sem build (só sintaxe
// que o Node apaga: tipos, sem enum).

export type Heading = { level: 2 | 3; id: string; text: string }

export type LegalDocument = { title: string; meta: string; html: string; headings: Heading[] }

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

export function slug(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

// Negrito, links e endereços soltos, sobre o texto já escapado. Endereço dentro de um link ([texto](url)) não vira
// outro link: os links saem primeiro, guardados, e voltam no fim.
export function inline(text: string): string {
  const links: string[] = []
  const keep = (html: string) => `\uE000${links.push(html) - 1}\uE000`
  let html = escape(text)
  html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, (_, label: string, url: string) =>
    keep(`<a href="${url}">${label}</a>`),
  )
  html = html.replace(/https?:\/\/[^\s<)]+[^\s<).,;:]/g, (url) => keep(`<a href="${url}">${url}</a>`))
  html = html.replace(/[\w.+-]+@[\w-]+(\.[\w-]+)+/g, (email) => keep(`<a href="mailto:${email}">${email}</a>`))
  html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
  return html.replace(/\uE000(\d+)\uE000/g, (_, i: string) => links[Number(i)])
}

type Item = { text: string; children: string[] }

export function renderMarkdown(source: string): LegalDocument {
  const lines = source.replace(/\r\n/g, '\n').split('\n')
  const out: string[] = []
  const headings: Heading[] = []
  let title = ''
  let meta = ''
  let i = 0

  const isBlockStart = (line: string) =>
    line.trim() === '' || /^#{1,3} /.test(line) || /^- /.test(line) || line.startsWith('|') || line.trim() === '---'

  while (i < lines.length) {
    const line = lines[i]
    if (line.trim() === '') {
      i++
    } else if (line.startsWith('# ')) {
      title = line.slice(2).trim()
      i++
    } else if (/^#{2,3} /.test(line)) {
      const level = line.startsWith('### ') ? 3 : 2
      const text = line.slice(level + 1).trim()
      const id = slug(text)
      headings.push({ level, id, text })
      out.push(`<h${level} id="${id}">${inline(text)}</h${level}>`)
      i++
    } else if (line.trim() === '---') {
      out.push('<hr>')
      i++
    } else if (line.startsWith('|')) {
      const rows: string[][] = []
      while (i < lines.length && lines[i].startsWith('|')) {
        rows.push(lines[i].trim().replace(/^\||\|$/g, '').split('|').map((cell) => cell.trim()))
        i++
      }
      const [head, , ...body] = rows
      out.push(
        '<div class="legal-table"><table><thead><tr>' +
          head.map((cell) => `<th>${inline(cell)}</th>`).join('') +
          '</tr></thead><tbody>' +
          // O nome da coluna vai em cada célula: no celular a tabela empilha, e a célula mostra o nome (legal.css).
          body
            .map((row) => '<tr>' + row.map((cell, c) => `<td data-label="${escape(head[c] ?? '')}">${inline(cell)}</td>`).join('') + '</tr>')
            .join('') +
          '</tbody></table></div>',
      )
    } else if (line.startsWith('- ')) {
      // Item: "- texto", continuação com dois espaços; item de dentro com "  - ", continuação com quatro.
      const items: Item[] = []
      while (i < lines.length && (lines[i].startsWith('- ') || lines[i].startsWith('  '))) {
        const current = lines[i]
        if (current.startsWith('- ')) items.push({ text: current.slice(2).trim(), children: [] })
        else if (current.startsWith('  - ')) items[items.length - 1].children.push(current.slice(4).trim())
        else {
          const last = items[items.length - 1]
          if (last.children.length > 0) last.children[last.children.length - 1] += ' ' + current.trim()
          else last.text += ' ' + current.trim()
        }
        i++
      }
      out.push(
        '<ul>' +
          items
            .map(
              (item) =>
                `<li>${inline(item.text)}` +
                (item.children.length > 0 ? '<ul>' + item.children.map((c) => `<li>${inline(c)}</li>`).join('') + '</ul>' : '') +
                '</li>',
            )
            .join('') +
          '</ul>',
      )
    } else {
      const paragraph: string[] = []
      while (i < lines.length && !isBlockStart(lines[i])) paragraph.push(lines[i++].trim())
      const text = paragraph.join(' ')
      // A primeira linha antes de qualquer seção é a da versão e da vigência: vai para o cabeçalho da página.
      if (!meta && headings.length === 0 && text.startsWith('**Versão**')) meta = inline(text)
      else out.push(`<p>${inline(text)}</p>`)
    }
  }

  if (!title) throw new Error('O texto precisa de um título (# …).')
  return { title, meta, html: out.join('\n'), headings }
}
