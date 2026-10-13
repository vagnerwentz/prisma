import { StrictMode } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'
import { LandingPage } from './LandingPage'
import './landing.css'

// O HTML já veio pronto do build (pré-renderizado): o React só assume a página para as partes que se mexem. No
// `npm run dev` não há pré-renderização, e a página é montada do zero.
const container = document.getElementById('landing')!
const page = (
  <StrictMode>
    <LandingPage />
  </StrictMode>
)

if (container.firstElementChild) hydrateRoot(container, page)
else createRoot(container).render(page)
