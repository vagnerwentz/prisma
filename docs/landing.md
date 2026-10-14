# Página de apresentação (landing)

> **Situação (2026-10-13):** página feita, na branch `feat/landing-page`, e **fora do ar**: ela aparece em `/` sozinha
> quando o cadastro abrir (`Registration:Open`). Antes disso faltam os itens da seção 6 (o domínio já está no ar,
> seção 10). Fora da ordem do `PLAN.md`, a pedido do dono.

Hoje quem chega ao app sem sessão cai direto no `/entrar`. Esta página explica o que o Prisma Money faz e leva a
pessoa a criar a conta.

Referência usada: a skill `landing-page-generator` (borghei/Claude-Skills). Dela vieram a estrutura, os
checklists de celular, de SEO e de velocidade e a regra "um objetivo, um botão". **Não** vieram o estilo
visual (vale a seção 7.1 do `CLAUDE.md`), os números e depoimentos de exemplo (não temos e não inventamos),
os scripts (procuram palavras em inglês em HTML estático) nem o teste A/B (pede mais de 1.000 visitas por
versão).

---

## 1. Decisões do dono

| # | Pergunta | Resposta (2026-10-12) |
|---|---|---|
| D1 | Qual é o botão? | **Criar conta.** O cadastro abre; confirmação e envio de e-mail vêm depois |
| D2 | Para quem? | Quem já controla (planilha ou outro app) e quem quer começar. Atrair pelo que o Prisma tem de diferente |
| D3 | De onde chegam? | Indicação do dono, anúncio pago e busca no Google |
| D4 | Preço? | Ainda não definido. Um dia vai cobrar, barato: a ideia é ajudar as pessoas |
| D5 | Página antes ou depois de abrir o cadastro? | **Antes, com o botão desligado** |
| D6 | Usar as skills de LGPD (goul4rt/lgpd-skills) na construção? | Sim (seção 9) |
| D7 | A primeira versão ficou simples. O que entra? (2026-10-12) | Abertura com a luz decompondo nas faixas Cartão, Parcelas, Pix e Investimentos; "Em qual fatura cai?" interativo; parcelas ao vivo; a nota de corretagem em cena (vários ativos, taxas rateadas, carteira se formando); comparação planilha × app comum × Prisma; celular que acompanha a rolagem; textos mais vivos |
| D8 | Movimento de encenação na página? | **Sim, só na página** (exceção à seção 7.1 do `CLAUDE.md`): toca uma vez, sem repetir, e fica parado com `prefers-reduced-motion`. O app segue a regra de sempre |
| D9 | HTML à mão ou React? | **React pré-renderizado:** escrita com os componentes do app, e o build gera o HTML pronto (a primeira tela continua sem esperar o JavaScript). `/landing.html` direto deixa de abrir: a página só existe em `/` |
| D10 | As faixas da abertura com nomes soltos ficaram fracas. O que mostram? (2026-10-13) | **Pedaços do seu dinheiro:** cada faixa termina num cartãozinho do app com um valor de exemplo (a fatura, uma parcela, um Pix, um rendimento); passar o dedo ou o mouse acende a faixa |
| D11 | Backup criptografado e senha protegida na página? | **Saem:** não é o que convence quem chega. Ficam "Sem senha do banco" e "Só você vê" |
| D12 | Vídeo do app sendo usado? | **Sim, numa seção própria logo depois do problema:** gravação real do app com o usuário de exemplo e um dedo visível tocando, ~20 s, muda e repetindo, carregada só quando a pessoa chega nela; parada para quem pede menos movimento |
| D13 | Vídeo do lançamento da nota de corretagem? (2026-10-13) | **Sim, no lugar da nota encenada:** à esquerda, o vídeo real lançando uma nota com três operações (capítulos: corretora e data, operações, custos, carteira); à direita, o que o app calcula dessa nota (as taxas rateadas por compra e o preço médio de cada uma) |
| D14 | Nome público (2026-10-13) | **Prisma Money**: domínio `prismamoney.com.br`, Instagram "Prisma Money Brasil". No código continua `Prisma` (CLAUDE.md, seção 1) |
| D15 | Falar em "grátis"? | **Não:** "grátis enquanto começa" soa como "vai cobrar e eu perco o que tenho". Sai da página, das perguntas e do FAQPage |
| D16 | Cabeçalho com menu de tópicos? | **Não;** uma barra fina que surge depois da abertura, com o logo, "Entrar" e "Criar conta": o botão a um toque, sem dar saídas |
| D17 | O tour "Tudo o que acontece com o seu dinheiro" | **Sai:** repetia o vídeo de 20 segundos. O "já comprometido" vira uma frase na seção de parcelas |
| D18 | Seção "Por que eu fiz" | **Sim,** em primeira pessoa, a partir das dores que o dono contou ao construir o app (fatura que fecha antes, parcelas, pagamento contado duas vezes, notas de corretagem desde 2020). Sem nome de quem usa |
| D19 | SEO e compartilhamento | Imagem de compartilhamento 1200×630, `canonical`, `robots.txt`, `sitemap.xml` com `https://prismamoney.com.br/`; as telas do app ficam fora do Google (`noindex` no `index.html`) |
| D20 | Compressão | Ligada **só para o front** (HTML, CSS, JS, SVG). As respostas da API ficam sem compressão: dado do usuário comprimido junto com texto que outro site consegue injetar abre a porta para o ataque BREACH |

Consequências:

- **D3 manda a página ser HTML pronto**, não uma tela React que monta depois de carregar o app: o Google
  lê melhor, e anúncio pago cobra pelo clique de quem desiste esperando.
- **D4 e D15 mandam a página não falar de preço:** nem preço, nem "grátis". Embaixo dos botões vai "Sem senha do banco.
  Leva menos de um minuto."

---

## 2. O que a página promete (só o que é verdade hoje)

A regra: cada frase da página precisa existir no app no dia em que ela for ao ar. Nada de "em breve".

| Diferencial | Onde está no app |
|---|---|
| A fatura do cartão como o banco faz: fechamento, vencimento e o dia em que o dinheiro sai de fato | `Statement`, `SettlementDate` |
| Parcelado sem perder centavo: R$ 100 em 3x é 33,34 + 33,33 + 33,33 | `Money.SplitInto` |
| Pagar a fatura não conta o gasto duas vezes | transferência (regra 5) |
| Pix, débito, dinheiro e conta corrente | `PaymentMethod`, `AccountType` |
| Contas que se repetem e débito automático que cai no próximo dia útil | `Recurrence`, `BankCalendar` |
| O sino mostra o que falta conferir (conta que muda de valor) | sino, `AmountEstimated` |
| Investimentos: a nota de corretagem inteira, com as taxas rateadas pelo valor, preço médio, desdobramentos e proventos | Fase I (I.7) |
| Resumo de relance e análise do mês | `/`, `/analise` |
| Não pede senha do banco: nada se conecta à sua conta bancária | não há integração |
| Backup diário criptografado (verdade, mas fora da página, D11) | H.2b |

**Fica fora da página até existir:** importar planilha, extrato ou fatura (Fase 4), app instalável (PWA),
cotação dos ativos (I.7d), conta compartilhada.

---

## 3. Estrutura (como está)

Uma rolagem só, sem menu de tópicos (D16). A barra fina com "Entrar" e "Criar conta" surge depois da abertura.

| # | Seção | O que faz |
|---|---|---|
| 1 | **Abertura** | "A compra é hoje. O dinheiro sai no vencimento.", o botão, e a luz decompondo em pedaços do dinheiro (D10) |
| 2 | **O problema** | "Dinheiro no Brasil não é só somar e subtrair": três dores que a pessoa reconhece |
| 3 | **Veja em 20 segundos** | o vídeo do app em uso, com três capítulos (D12) |
| 4 | **Cartão de crédito** | "O mês que você vê é o mês que você paga": arrastar o dia da compra e ver a fatura (D7) |
| 5 | **Parcelas** | "Parcelou? Nenhum centavo some no caminho": valor e parcelas ao vivo (D7, D17) |
| 6 | **Investimentos** | "Sua corretora manda a nota. Você lança a nota.": o vídeo da nota e a conta do app (D13) |
| 7 | **Comparação** | planilha × app comum × Prisma Money, em cinco linhas |
| 8 | **Por que eu fiz** | a história de quem fez, em primeira pessoa (D18) |
| 9 | **Comece em três passos** | conta, contas e cartões, lançar na hora, e o botão |
| 10 | **Seu dinheiro é assunto seu** | "Sem senha do banco" e "Só você vê" (D11) |
| 11 | **Perguntas** | quatro perguntas, as mesmas do FAQPage do Google |
| 12 | **Fechamento** | "Veja o seu dinheiro em cores." e o botão |
| 13 | **Rodapé** | o Instagram (`@prismamoneybrasil`) e "Entrar"; faltam privacidade, termos e contato (seção 6) |

O botão "Criar minha conta" aparece na abertura, depois dos três passos e no fechamento, além da barra, e leva sempre a
`/cadastro`. A voz é a do app: frases curtas, sem ";", sem jargão.

---

## 4. Confiança sem números inventados

Não temos depoimentos nem contagem de usuários, e nome de quem usa nunca aparece. A seção "Seus dados" usa
só fatos:

- Não pede senha do banco nem se conecta a ele.
- Nenhum outro usuário vê seus dados (o isolamento por usuário é testado).
- ~~Cópia diária criptografada~~ e ~~senha guardada de forma irreversível~~: saíram da página (D11). São verdade, mas
  não convencem quem chega.
- Compromisso a confirmar pelo dono: "Não vendemos nem compartilhamos seus dados."

Quando houver usuários de verdade dispostos a falar, um depoimento entra com autorização por escrito.

---

## 5. Visual

Vale a seção 7.1 do `CLAUDE.md`, com uma exceção: a página pode encenar (D8). A pessoa que sai da página e entra no
app precisa sentir que é o mesmo lugar.

- Papel quente no claro, tinta no escuro, seguindo o sistema (sem botão de tema na página).
- `PrismLogo`; títulos em Instrument Serif; o filete do espectro sob os títulos de seção.
- A "luz que entra" (`text-spectrum`) com parcimônia: o título da abertura, a fatura que muda e os valores que entram.
- O app de verdade em vez de ilustração: vídeos gravados com o usuário de exemplo (nunca dado real), nos dois temas.
- Movimento: cada cena toca uma vez, sem repetir; com `prefers-reduced-motion`, tudo aparece pronto e os vídeos ficam
  parados com um botão de tocar.
- Conferir em 320, 390 e 1280 px, claro e escuro. O botão da abertura aparece sem rolar a 375 px.

---

## 6. Antes de abrir o cadastro

A página pode ficar pronta antes. **O botão só vai ao ar com o cadastro aberto**, e o `PLAN.md` já lista
o que vem antes disso (pendências "antes de abrir cadastro público"). A D1 adia a confirmação de e-mail.
O resto continua de pé, e a investigação de 2026-10-12 achou mais duas lacunas.

| Item | Por que | Situação |
|---|---|---|
| Termos de uso e política de privacidade | LGPD: o app guarda dado financeiro de terceiros. O Google Ads também exige política de privacidade na página de destino | não existe |
| **Apagar a conta** | LGPD: direito de eliminação. A página promete "seus dados são seus" | não existe |
| **Recuperar a senha** | sem ela, quem esquece a senha perde a conta e os dados | não existe; precisa de envio de e-mail, como a confirmação |
| Contador de senhas erradas que expira | aberto ao público, erros espalhados no tempo bloqueiam a conta | pendência no `PLAN.md` |
| Região dos dados e custo no Railway | volume de usuários desconhecidos; LGPD | pendência no `PLAN.md` |
| Confirmação de e-mail | e-mail digitado errado vira conta sem dono | **adiada pela D1**; pesa junto com a recuperação de senha |

Recomendação: a recuperação de senha e a confirmação de e-mail usam o mesmo envio de e-mail. Fazer as duas
juntas custa quase o mesmo que fazer uma. Com só a recuperação, quem digitou o e-mail errado no cadastro
também não conseguiria recuperar.

---

## 7. Em aberto

Respondidas: a ordem (D5), o nome (D14), o domínio (D14, seção 10) e o texto sobre preço (D15).

1. **"Não vendemos seus dados":** o dono assume esse compromisso por escrito? Hoje a frase não está na página.
2. **Medir quem chega:** pixel de anúncio e Google Analytics são scripts de terceiros com cookies, e pedem aviso de
   consentimento (LGPD). Proposta inicial sem nada disso: contar cadastros pelo log (`AppLog`) e guardar a origem
   (`utm_source`) no cadastro. Pixel só quando houver anúncio rodando.
3. **O nome visível do app:** o app ainda se chama "Prisma" nas telas (cabeçalho, login, título da aba), e os vídeos
   mostram esse nome. Trocar para "Prisma Money" pede gravar os vídeos de novo.
4. **Duas frases a confirmar:** "O Prisma Money aplica o evento na sua carteira sozinho" (vale para os eventos da
   curadoria, `corporate-events.json`) e "App comum: nem sempre" na comparação.

---

## 8. Como está feita

- **Endereço:** a página fica em `/`. O servidor decide (`FrontendHosting.ShowsLanding`): na raiz, sem sessão
  e com o cadastro aberto, entrega `landing.html`; senão, o `index.html` do app. Quem tem sessão vê o Resumo no
  mesmo `/`, e o Google indexa a página no endereço principal. As outras telas (`/entrar`, `/cadastro`…)
  não mudam.
- **"Botão desligado" (D5):** a página só aparece com o cadastro aberto, porque o botão leva ao cadastro. Na
  produção o cadastro está fechado, então nada muda lá até ele abrir. Sem configuração nova: abrir o cadastro
  liga a página junto.
- **React pré-renderizado (D9):** segunda entrada do Vite (`landing.html`, `vite.config.ts`), escrita em React com
  peças do app (`PrismLogo`, `Amount`, `splitCents`, `formatCents`, tokens, o logo do Instagram do `simple-icons`). O `npm run build` faz, além do
  build normal, um build de servidor (`src/landing/server.tsx`) e roda `scripts/prerender-landing.mjs`, que grava o
  HTML no lugar do `<!--landing-html-->` e confere que toda imagem pedida existe. No navegador, `main.tsx` hidrata a
  página. O JavaScript dela é ~95 KB comprimidos, quase todo o próprio React, que o app compartilha (fica em cache
  para quem cria a conta).
- **Peças** (`src/landing/`), na ordem da página: `StickyBar` (D16: a barra que surge depois da abertura, com "Entrar"
  e "Criar conta"), `Hero` (D10: a luz decompondo em pedaços do dinheiro; as faixas terminam num cartão com a fatura,
  uma parcela, um Pix e um rendimento, valores do usuário de exemplo; passar o dedo ou o mouse acende a faixa; cada
  linha leva à seção), `Sections.Problem`, `DemoVideo` (D12), `StatementDemo` ("Em qual fatura cai?", cartão que fecha
  25 e vence 5), `InstallmentsDemo` (valor e parcelas ao vivo; o "já comprometido" do tour que saiu, D17, virou uma
  frase aqui), `TradeNoteScene` (D13: o vídeo da nota com capítulos e, ao lado, a conta do app para as mesmas três
  compras), `Sections.Comparison`, `WhyIBuiltIt` (D18), `Sections.HowToStart`, `Sections.Trust` (D11: só "Sem senha do
  banco" e "Só você vê"), `Sections.Faq` (`faq.ts`: perguntas e o FAQPage do Google, do mesmo texto) e
  `Sections.Closing`; no rodapé, o Instagram oficial (`@prismamoneybrasil`, também no schema `Organization` do Google). Comuns: `parts.tsx`, `motion.ts`, `PhoneVideo.tsx` e `useChapterVideo.ts`.
- **Regras nas demonstrações:** `demoMath.ts` refaz o que a página mostra sem a API: a fatura (`StatementCalculator`,
  o dia do fechamento ainda entra), o rateio (`Money.Allocate`) e o preço médio (`AssetPosition`, arredondado para cima
  a partir da metade). `demoMath.test.ts` traz os casos do domínio (véspera, dia e dia seguinte ao fechamento, dia 31
  em mês curto, virada do ano, vencimento no dia do fechamento, soma do rateio em 2.000 entradas); cada regra foi
  provada com uma mutação.
- **Movimento (D8):** a abertura toca ao carregar; a conta da nota toca quando a pessoa chega nela; os vídeos tocam ao
  chegar e param ao sair; a barra desce do topo. Com `prefers-reduced-motion`, tudo aparece pronto. O HTML do build sai com tudo à mostra (quem lê sem
  JavaScript vê a página inteira); no navegador que quer movimento, a nota se esconde até tocar.
- **Tema:** o mesmo script do `index.html` (escolha salva no app, senão o sistema). Os vídeos e as primeiras cenas vêm
  em duas versões, e a do outro tema não carrega (`lazy` e escondida).
- **Vídeos (D12, D13):** `src/landing/video/`: `demo-*.mp4` (~20 s, o app em uso) e `note-*.mp4` (~31 s, a nota de
  corretagem: XP, HGLG11 12 × 158,30, ITUB4 30 × 34,12, MXRF11 200 × 9,68, taxas R$ 1,48), 600×1298, H.264 sem som,
  ~470–520 KB cada, e as primeiras cenas (`*poster-*.webp`, mostradas como imagem `lazy` atrás do vídeo: o atributo
  `poster` baixaria ao abrir a página). A lógica comum fica em `useChapterVideo.ts` e `PhoneVideo.tsx`. A nota da
  gravação é apagada no fim, como o lançamento do outro vídeo. Só baixa o do tema, quando a seção chega na tela (`preload="none"`), e para ao sair dela. Os
  capítulos ao lado acendem pelo tempo do vídeo (`chapters` em `DemoVideo.tsx`: 0 s, 10,8 s e 15 s) e, tocados, pulam
  para o trecho. Gravado do app de verdade com o usuário de exemplo: Chrome sem cabeça com toques reais, um círculo
  marcando cada toque, o screencast do DevTools e o `ffmpeg` (ferramenta da máquina, fora do projeto) montando o vídeo.
  As telas são abertas uma vez antes de gravar (nada de "carregando"), o aviso "Lançamento salvo" some depois da lista,
  e o lançamento da gravação é apagado no fim. Os scripts não ficam no repositório (método na memória de capturas).
  Mudou o roteiro do vídeo, mudam os tempos dos capítulos.
- **Ver em desenvolvimento:** `npm run dev` e abrir `http://localhost:5173/landing.html` (montada do zero, sem
  pré-renderização). Na API, `/landing.html` dá 404: a página só existe em `/`.
- **Compartilhamento e busca (D19):** `public/og.jpg` (1200×630, 66 KB, desenhada em `scripts/og-image.html` com as
  fontes do app; para refazer, capturar esse HTML no Chrome sem cabeça), etiquetas `og:` e `twitter:` com endereços
  absolutos, `canonical`, `public/robots.txt` (só a API fora) e `public/sitemap.xml` com `https://prismamoney.com.br/`.
  As telas do app levam `noindex` (`index.html`): quem busca chega na página.
- **Compressão (D20):** Brotli e gzip só no ramo do front (`FrontendHosting.AddFrontendCompression`), nível "Fastest". A
  API fica sem compressão por causa do BREACH.
- **Medido (2026-10-13, 390 px, API local, com compressão):** ~250 KB transferidos na primeira tela; ~1,3 MB rolando a
  página inteira, dos quais ~1 MB são os dois vídeos, que só baixam ao chegar neles. Página com ~10.700 px de altura no
  celular (eram ~12.300 com o tour). Um único host. Sem rolagem lateral em 320, 390 e 1280 px.
- **Testes:** `HostingTests` (sem sessão → página; com sessão → app; `/entrar`, `/cadastro`, `/lancamentos` → app;
  cadastro fechado → app; `/landing.html` direto → 404; front comprimido; API sem compressão). Cada um provado com o
  defeito correspondente.

### Falta antes de ir ao ar

- Os itens da seção 6.
- Rodapé com os links de **Política de privacidade**, **Termos de uso** e um **e-mail de contato** (as páginas e o
  e-mail ainda não existem).
- O app ainda se chama "Prisma" nas telas (cabeçalho, login, título da aba), e os vídeos mostram esse nome.

---

## 9. LGPD na página (skills `lgpd-*`)

Instaladas em `.claude/skills/lgpd-*` (locais, fora do git), de goul4rt/lgpd-skills (MIT, só texto). Elas
assumem outra stack (Next.js, Prisma ORM, Better Auth), mas as regras valem. Não substituem advogado.

Aplicado na página:

- **Nada sai do navegador de quem só está conhecendo:** sem script de análise, sem pixel, sem fonte ou imagem
  de terceiros, sem cookie. Por isso não precisa de aviso de cookies. Conferido: um único host nos pedidos
  e nenhum `Set-Cookie` em `/`.
- **Só fatos na seção "Seu dinheiro é assunto seu":** cada frase existe no app (seção 4). "Não vendemos seus
  dados" ficou de fora até o dono assumir o compromisso (seção 7).
- **Medir quem chega** com pixel ou Google Analytics passa a coletar dado pessoal de visitante: pede base
  legal, aviso de cookies e entrada na política. Fica para quando houver anúncio (seção 7, item 6).

Próximo passo com as skills, quando o dono quiser: a **política de privacidade** (`lgpd-privacy-policy`, art. 9º)
e os **termos de uso**. O rascunho precisa de dados que só o dono tem:

- **Quem é o controlador:** o dono como pessoa física (agente de pequeno porte, Res. ANPD 2/2022) ou uma empresa.
- **Canal de contato** para os titulares. Agente de pequeno porte de baixo risco pode dispensar o encarregado,
  mas não o canal (Res. 2/2022, art. 11).
- **Onde os dados ficam:** região do Railway e do R2 (backup). Fora do Brasil é transferência internacional
  (art. 33).
- **Quanto tempo guarda** e o que acontece ao apagar a conta, que ainda não existe (seção 6).

A skill manda o texto passar por revisão jurídica antes de publicar. Dado financeiro também pesa num
incidente: a Res. ANPD 15/2024 o lista entre os que tornam a comunicação obrigatória.

---

## 10. Domínio e e-mail

Domínio no ar desde 2026-10-13 (passo a passo e registros em `docs/operacao.md`, seção 7). Um domínio serve ao site e ao
envio de e-mail ao mesmo tempo: cada uso fica em registros DNS diferentes.

- **Compra:** `prismamoney.com.br` na **GoDaddy** (revendedora do Registro.br), com renovação automática (domínio
  vencido derruba o site e os e-mails juntos). A Cloudflare não registra `.com.br`, mas responde o DNS dele.
- **DNS na Cloudflare** (plano gratuito, conta do R2): na GoDaddy, só os nameservers apontam para ela. O Railway pede
  CNAME na raiz, que a Cloudflare aceita (*CNAME flattening*) e o DNS da GoDaddy não.
- **Site:** domínio personalizado no serviço da API no Railway (raiz e `www`), certificado HTTPS do Railway, registros
  com a **nuvem cinza** (motivo em `docs/operacao.md`, seção 7). Nada mudou no código: o cookie de sessão não depende
  do domínio, e `og:image`, `canonical` e o sitemap já usam o endereço.
- **Envio (Resend, aprovado em 2026-10-13):** os registros que o Resend der (DKIM em TXT, e SPF e retorno em CNAME
  nos subdomínios `send` e `rsend`) e o DMARC. O app envia como `nao-responda@`, com "responder para" o `contato@` (`docs/email.md`; passo a
  passo em `docs/operacao.md`, seção 8).
- **Recebimento:** o Resend só envia. Para o `contato@prismamoney.com.br` que a política de privacidade pede, o Email
  Routing da Cloudflare (gratuito) repassa para o e-mail pessoal do dono, que responde como `contato@` pelo Gmail
  ("Enviar e-mail como", pelo SMTP do Resend).
