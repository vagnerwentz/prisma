// Gera src/lib/brands/logos.generated.ts com só o desenho (path) e a cor de cada marca usada.
// Os objetos do simple-icons trazem metadados e uma cópia do SVG inteiro, que dobrariam o
// tamanho no bundle; aqui fica só o necessário. Rode `npm run gen:brands` ao mudar a lista.
import { writeFileSync } from 'node:fs'
import * as si from 'simple-icons'

const slugs = [
  'airbnb', 'aliexpress', 'anthropic', 'apple', 'applemusic', 'appletv', 'bookingdotcom', 'burgerking',
  'carrefour', 'claude', 'crunchyroll', 'cursor', 'deezer', 'discord', 'duolingo', 'github', 'google',
  'googleplay', 'hbo', 'icloud', 'ifood', 'ikea', 'leroymerlin', 'mcdonalds', 'mercadopago', 'netflix',
  'nike', 'notion', 'nubank', 'paypal', 'perplexity', 'picpay', 'playstation', 'samsung', 'shell',
  'shopee', 'spotify', 'starbucks', 'steam', 'tinder', 'twitch', 'uber', 'ubereats', 'xiaomi', 'youtube', 'zara',
]

const lines = slugs.map((slug) => {
  const key = `si${slug.charAt(0).toUpperCase()}${slug.slice(1)}`
  const icon = si[key]
  if (!icon) throw new Error(`Marca ausente no simple-icons: ${slug}`)
  return `export const ${slug} = { hex: '#${icon.hex}', path: ${JSON.stringify(icon.path)} }`
})

writeFileSync(
  new URL('../src/lib/brands/logos.generated.ts', import.meta.url),
  `// Gerado por scripts/generate-brands.mjs a partir do simple-icons (CC0). Não edite à mão.\n// As marcas continuam pertencendo aos seus donos.\n${lines.join('\n')}\n`,
)
console.log(`${slugs.length} marcas geradas`)
