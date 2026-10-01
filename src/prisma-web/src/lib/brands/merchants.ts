import agora from './institutions/agora.svg'
import avenue from './institutions/avenue.svg'
import bancoDoBrasil from './institutions/banco-do-brasil.svg'
import bradesco from './institutions/bradesco.svg'
import btgPactual from './institutions/btg-pactual.svg'
import caixa from './institutions/caixa.svg'
import clear from './institutions/clear.svg'
import ion from './institutions/ion.svg'
import itau from './institutions/itau.svg'
import modalmais from './institutions/modalmais.svg'
import rico from './institutions/rico.svg'
import safra from './institutions/safra.svg'
import santander from './institutions/santander.svg'
import toro from './institutions/toro.svg'
import xp from './institutions/xp.svg'
import * as logos from './logos.generated'
import * as manual from './logos.manual'

// Marca reconhecida na descrição ou no nome da conta. Logo quando o Simple Icons tem a marca
// (licença CC0; as marcas continuam dos seus donos). Bancos e corretoras: o ícone da instituição,
// já com o fundo, em institutions/ (arquivos SVG, mostrados como imagem). Monograma na cor da marca
// quando não há desenho (Amazon, varejo, os bancos ainda sem ícone): sem desenhar o logo.
export type BrandMark =
  | { kind: 'logo'; name: string; color: string; path: string }
  | { kind: 'image'; name: string; src: string }
  | { kind: 'monogram'; name: string; color: string; text: string }

type Rule = { patterns: string[]; brand: BrandMark }

const logo = (icon: { hex: string; path: string }, name: string, ...patterns: string[]): Rule => ({
  patterns: patterns.length > 0 ? patterns : [name],
  brand: { kind: 'logo', name, color: icon.hex, path: icon.path },
})

const image = (src: string, name: string, ...patterns: string[]): Rule => ({
  patterns: patterns.length > 0 ? patterns : [name],
  brand: { kind: 'image', name, src },
})

const monogram = (name: string, text: string, color: string, ...patterns: string[]): Rule => ({
  patterns: patterns.length > 0 ? patterns : [name],
  brand: { kind: 'monogram', name, color, text },
})

// Ordem importa: o mais específico vem antes ("uber eats" antes de "uber", "íon itaú" antes de "itaú").
// Padrão com "=" casa só com o texto inteiro: o nome que também é palavra comum ("agora", "rico",
// "clear") reconhece a conta chamada só assim, sem pegar "Pagamento agora" ou "Pastel do Rico".
const rules: Rule[] = [
  logo(logos.ubereats, 'Uber Eats', 'uber eats', 'ubereats'),
  logo(logos.uber, 'Uber', 'uber'),
  logo(logos.ifood, 'iFood', 'ifood', 'ifd'),
  logo(logos.netflix, 'Netflix', 'netflix'),
  logo(logos.spotify, 'Spotify', 'spotify'),
  logo(logos.deezer, 'Deezer', 'deezer'),
  logo(logos.applemusic, 'Apple Music', 'apple music'),
  logo(logos.appletv, 'Apple TV', 'apple tv'),
  logo(logos.icloud, 'iCloud', 'icloud'),
  logo(logos.apple, 'Apple', 'apple', 'apple com'),
  logo(logos.googleplay, 'Google Play', 'google play'),
  logo(logos.youtube, 'YouTube', 'youtube'),
  logo(logos.google, 'Google', 'google'),
  logo(logos.hbo, 'HBO Max', 'hbo', 'hbo max'),
  logo(logos.crunchyroll, 'Crunchyroll', 'crunchyroll'),
  logo(logos.twitch, 'Twitch', 'twitch'),
  logo(logos.steam, 'Steam', 'steam', 'steampowered'),
  logo(logos.playstation, 'PlayStation', 'playstation', 'psn'),
  logo(logos.discord, 'Discord', 'discord'),
  logo(logos.notion, 'Notion', 'notion'),
  logo(logos.duolingo, 'Duolingo', 'duolingo'),
  logo(logos.github, 'GitHub', 'github'),
  logo(logos.claude, 'Claude', 'claude'),
  logo(logos.anthropic, 'Anthropic', 'anthropic'),
  logo(logos.perplexity, 'Perplexity', 'perplexity'),
  logo(logos.cursor, 'Cursor', 'cursor'),
  logo(logos.tinder, 'Tinder', 'tinder'),
  logo(logos.airbnb, 'Airbnb', 'airbnb'),
  logo(logos.bookingdotcom, 'Booking', 'booking'),
  logo(logos.shopee, 'Shopee', 'shopee'),
  logo(logos.aliexpress, 'AliExpress', 'aliexpress'),
  logo(logos.mercadopago, 'Mercado Pago', 'mercado pago', 'mercadopago'),
  logo(logos.starbucks, 'Starbucks', 'starbucks'),
  logo(logos.mcdonalds, "McDonald's", 'mcdonalds', 'mc donalds', 'mcdonald s', 'mequi'),
  logo(logos.burgerking, 'Burger King', 'burger king', 'bk brasil'),
  logo(logos.shell, 'Shell', 'shell'),
  logo(logos.carrefour, 'Carrefour', 'carrefour'),
  logo(logos.ikea, 'IKEA', 'ikea'),
  logo(logos.leroymerlin, 'Leroy Merlin', 'leroy merlin'),
  logo(logos.zara, 'Zara', 'zara'),
  logo(logos.nike, 'Nike', 'nike'),
  logo(logos.samsung, 'Samsung', 'samsung'),
  logo(logos.xiaomi, 'Xiaomi', 'xiaomi'),
  logo(logos.nubank, 'Nubank', 'nubank', 'nu pagamentos'),
  logo(logos.picpay, 'PicPay', 'picpay'),
  logo(logos.paypal, 'PayPal', 'paypal'),
  logo(manual.petz, 'Petz', 'petz'),

  monogram('Prime Video', 'pv', '#1A98FF', 'prime video', 'amazon prime', 'primevideo'),
  monogram('Amazon', 'a', '#FF9900', 'amazon', 'amzn'),
  monogram('Disney+', 'D+', '#113CCF', 'disney', 'disney plus'),
  monogram('Xbox', 'X', '#107C10', 'xbox'),
  monogram('Microsoft', 'MS', '#5E5E5E', 'microsoft'),
  monogram('ChatGPT', 'AI', '#10A37F', 'chatgpt', 'openai'),
  monogram('Mercado Livre', 'ML', '#FFE600', 'mercado livre', 'mercadolivre'),
  monogram('Magalu', 'M', '#0086FF', 'magalu', 'magazine luiza'),
  monogram('Americanas', 'a', '#E60014', 'americanas'),
  monogram('Rappi', 'R', '#FF441F', 'rappi'),
  monogram('99', '99', '#FFDD00', '99app', '99 pop', '99pop', '99 taxi'),
  // Bancos e corretoras depois dos estabelecimentos: "Netflix (cartão Itaú)" é Netflix.
  image(ion, 'Íon', 'ion itau', '=ion'),
  image(itau, 'Itaú', 'itau'),
  image(bancoDoBrasil, 'Banco do Brasil', 'banco do brasil', 'bb'),
  image(bradesco, 'Bradesco', 'bradesco'),
  image(caixa, 'Caixa', 'caixa'),
  image(santander, 'Santander', 'santander'),
  image(btgPactual, 'BTG Pactual', 'btg pactual', 'btg'),
  image(safra, 'Safra', 'banco safra', '=safra'),
  image(xp, 'XP', 'xp investimentos', 'xp'),
  image(rico, 'Rico', 'rico investimentos', 'rico corretora', '=rico'),
  image(clear, 'Clear', 'clear corretora', 'clear investimentos', '=clear'),
  image(toro, 'Toro', 'toro investimentos', '=toro'),
  image(agora, 'Ágora', 'agora investimentos', 'agora corretora', '=agora'),
  image(avenue, 'Avenue', 'avenue securities', '=avenue'),
  image(modalmais, 'Modalmais', 'modalmais', 'modal mais', 'banco modal'),
  monogram('Inter', 'in', '#FF7A00', 'banco inter', 'inter'),
  monogram('C6 Bank', 'C6', '#242424', 'c6 bank', 'c6bank', 'c6'),
  monogram('Pão de Açúcar', 'PA', '#00843D', 'pao de acucar'),
  monogram('Assaí', 'as', '#E30613', 'assai'),
  monogram('Atacadão', 'at', '#E2231A', 'atacadao'),
  monogram('Drogasil', 'D', '#E30613', 'drogasil', 'droga raia', 'raia'),
  monogram('Smart Fit', 'SF', '#FFC400', 'smart fit', 'smartfit'),
  monogram('Wellhub', 'W', '#D8385E', 'wellhub', 'gympass'),
  monogram('Sympla', 'S', '#0097FF', 'sympla'),
  monogram('Cinemark', 'C', '#D1001F', 'cinemark'),
]

// Minúsculas, sem acento e só letras, números e espaço: "IFD*IFOOD" → "ifd ifood".
function normalize(text: string): string {
  return ` ${text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()} `
}

const compiled = rules.map((rule) => ({
  ...rule,
  needles: rule.patterns.map((pattern) => {
    const whole = pattern.startsWith('=')
    return { whole, text: normalize(whole ? pattern.slice(1) : pattern) }
  }),
}))

// Casa por palavra inteira, para "Superuberização" não virar Uber.
export function findBrand(text: string | null | undefined): BrandMark | null {
  if (!text) return null
  const haystack = normalize(text)
  if (haystack.trim() === '') return null
  const matches = (needle: { whole: boolean; text: string }) =>
    needle.whole ? haystack === needle.text : haystack.includes(needle.text)
  return compiled.find((rule) => rule.needles.some(matches))?.brand ?? null
}

// Texto claro ou escuro sobre a cor da marca, pela luminância relativa (WCAG).
export function readableTextOn(hex: string): 'light' | 'dark' {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const channel = parseInt(hex.slice(i, i + 2), 16) / 255
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.5 ? 'dark' : 'light'
}
