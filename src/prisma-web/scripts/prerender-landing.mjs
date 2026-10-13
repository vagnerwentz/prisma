// Pré-renderiza a página de apresentação (docs/landing.md, D9): o HTML que o React gera entra no dist/landing.html,
// e a primeira tela aparece sem esperar o JavaScript. Roda no fim do `npm run build`, depois do build de servidor.
import fs from 'node:fs'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

const root = path.resolve(import.meta.dirname, '..')
const page = path.join(root, 'dist', 'landing.html')
const server = path.join(root, 'node_modules', '.tmp', 'landing-ssr', 'server.js')
const marker = '<!--landing-html-->'

const { render } = await import(pathToFileURL(server).href)
const html = fs.readFileSync(page, 'utf8')
if (html.split(marker).length !== 2) throw new Error(`landing.html precisa de um ${marker}, e só um.`)

const body = render()
fs.writeFileSync(page, html.replace(marker, body))

// Imagem que o HTML pede precisa existir no build: o build de servidor calcula os endereços sem gravar os arquivos.
const missing = [...body.matchAll(/(?:src|srcSet|srcset)="(\/assets\/[^"]+)"/g)]
  .map((m) => m[1])
  .filter((asset) => !fs.existsSync(path.join(root, 'dist', asset)))
if (missing.length > 0) throw new Error(`A página pede arquivos que o build não gerou: ${missing.join(', ')}`)

console.log(`landing.html pré-renderizada (${Math.round(body.length / 1024)} KB de HTML).`)
