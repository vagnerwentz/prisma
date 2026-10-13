import { renderToString } from 'react-dom/server'
import { LandingPage } from './LandingPage'

// Build de servidor da página de apresentação: scripts/prerender-landing.mjs chama render() e grava o HTML no
// landing.html (docs/landing.md, D9).
export function render(): string {
  return renderToString(<LandingPage />)
}
