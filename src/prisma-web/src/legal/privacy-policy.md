# Política de Privacidade — Prisma Money

**Versão**: v1.0 | **Vigência a partir de**: 15/10/2026

## Resumo

- O Prisma Money é um app para você **organizar o próprio dinheiro**. Você digita suas contas, cartões, lançamentos e
  investimentos. **Hoje, o app não se conecta ao seu banco. Ele não pede senha de banco e não movimenta dinheiro.**
- Usamos seus dados **para fazer o app funcionar para você**: guardar o que você lançou, calcular saldos, faturas e
  carteira, manter sua conta segura e mandar os e-mails da conta.
- **Não vendemos seus dados, não mostramos anúncios e não usamos ferramentas de rastreamento.** O app usa um único
  cookie, o que mantém você conectado.
- Seus dados ficam em servidores **nos Estados Unidos**, em empresas que prestam serviço para o Prisma Money.
- O que você apaga **some do app na hora e do banco de dados em até 1 hora**. Você pode apagar a conta inteira no
  próprio app e pedir uma cópia dos seus dados por **contato@prismamoney.com.br**.

---

## 1. Quem é o responsável pelos seus dados

O controlador dos dados é **Vagner Wentz**, pessoa física, que oferece e opera o Prisma Money.

- **Site**: https://prismamoney.com.br
- **Contato**: contato@prismamoney.com.br

## 2. Canal para assuntos de dados pessoais

Pedidos e dúvidas sobre dados pessoais são atendidos pelo próprio controlador, no e-mail
**contato@prismamoney.com.br**. Cada pedido recebido é registrado (data, tipo de pedido e resposta) para controle do
atendimento.

## 3. Quais dados tratamos, para quê e por quanto tempo

### 3.1 Sua conta

- **Dados**: endereço de e-mail e senha; se o e-mail já foi confirmado; e, por segurança, um contador de tentativas de
  entrada erradas, que volta a zero quando você acerta a senha.
- **Como a senha é guardada**: nunca como você a digitou. Guardamos apenas um hash criptográfico feito com um algoritmo
  próprio para senhas (PBKDF2, do ASP.NET Core Identity), com sal e muitas iterações. Ele foi feito para tornar
  inviável, na prática, descobrir a senha original a partir do que fica guardado.
- **Para quê**: criar e manter sua conta, confirmar que o e-mail é seu, permitir que você entre e recupere o acesso.
- **Base legal**: execução de contrato (art. 7º, V): o uso do app que você contratou ao aceitar os Termos de Uso.
- **Por quanto tempo**: enquanto a conta existir.

### 3.2 O que você registra no app

- **Dados**: os nomes que você dá às suas contas e cartões, saldos iniciais, dias de fechamento e vencimento dos
  cartões, lançamentos (valor, data, descrição, categoria, conta), compras parceladas, lançamentos que se repetem,
  faturas e transferências; na parte de investimentos, os ativos da sua carteira, as notas de corretagem que você
  digita (corretora, datas, quantidades, preços e custos), os proventos recebidos, os avisos de ativo que falta no
  catálogo e a sua conferência dos eventos das empresas.
- **O que não pedimos**: senha ou acesso ao seu banco ou corretora, número de cartão, CPF, endereço ou telefone.
- **Para quê**: mostrar seus lançamentos, calcular saldos, faturas, resumo do mês, análises e a posição dos seus
  investimentos, só para você.
- **Base legal**: execução de contrato (art. 7º, V).
- **Por quanto tempo**: enquanto a conta existir. O que você exclui some do app na hora e é apagado de vez do banco de
  dados em até **1 hora** (o "Desfazer" vale por 30 minutos). Isso vale também para uma ocorrência excluída de um
  lançamento que se repete: nada fica guardado dela.
- **Descrições são texto livre**: você escreve o que quiser nelas. Se escrever algo que revele saúde, religião ou outro
  dado sensível (ex.: o nome de um tratamento), esse texto fica guardado como qualquer outra descrição. Por isso:
  - não pedimos nenhum dado sensível;
  - recomendamos que você não registre informação desse tipo além do necessário para se organizar;
  - as descrições são usadas só para mostrar e organizar os seus lançamentos, nunca para publicidade, perfil ou
    qualquer outra finalidade.

### 3.3 E-mails da sua conta

- **Dados**: seu endereço de e-mail e o tipo de mensagem (confirmação de e-mail, senha nova, aviso de senha trocada,
  aviso de tentativa de cadastro com o seu e-mail).
- **Para quê**: enviar só as mensagens necessárias para a conta. **Não enviamos propaganda.**
- **Base legal**: execução de contrato (art. 7º, V).
- **Por quanto tempo**: o registro de cada envio no app é apagado em **30 dias**. Os links enviados deixam de valer
  sozinhos: o de confirmação em 3 dias e o de senha nova em 1 hora. O fornecedor de envio guarda os próprios registros
  de entrega pelo prazo dele (seção 5).

### 3.4 Segurança e funcionamento do serviço

Há registros de tipos diferentes, com prazos diferentes:

| Registro | O que tem | Quem guarda e por quanto tempo |
|---|---|---|
| Eventos técnicos do app | data e hora, a funcionalidade usada, o resultado, o tempo de resposta e o identificador interno da conta | o provedor de hospedagem, por 7 dias |
| Erros da tela | a mensagem técnica do erro, a tela, a versão do app e o identificador interno da conta | o provedor de hospedagem, pelo mesmo prazo |
| Tentativas de entrada | só o contador de erros da conta (seção 3.1) | o app, até a próxima entrada certa |
| Registros de acesso do provedor | endereço IP, data e hora de cada acesso | o provedor de hospedagem, pelo prazo dele |

- **O que nunca entra nos registros do app**: valores, descrições, nomes, e-mail, senhas ou links de acesso.
- O endereço IP também é usado, sem ser guardado pelo app, para limitar tentativas repetidas de entrada.
- **Para quê**: proteger as contas contra acesso indevido e abuso, investigar falhas e manter o serviço funcionando.
- **Base legal**: legítimo interesse (art. 7º, IX), com finalidade específica (segurança e funcionamento), limitado
  ao necessário e sem uso para outros fins.

### 3.5 Contato com o Prisma Money

- **Dados**: o que você escrever para contato@prismamoney.com.br, com o seu endereço de e-mail.
- **Para quê**: responder você e atender os seus pedidos sobre dados pessoais.
- **Base legal**: legítimo interesse (art. 7º, IX) para o atendimento e, nos pedidos de direitos, cumprimento de
  obrigação legal (art. 7º, II).
- **Por quanto tempo**: **1 ano** depois da última mensagem da conversa.

### 3.6 Cópias de segurança

Fazemos uma cópia de segurança diária do banco de dados, só para recuperar o serviço em caso de falha:

- a cópia é **criptografada antes de sair do servidor**, e só o controlador tem a chave para abri-la;
- fica guardada fora do provedor de hospedagem, com acesso restrito ao controlador, por **90 dias**, e depois é
  descartada;
- a recuperação a partir da cópia é testada.

Por isso, o que você apaga some do app e do banco de dados no prazo da seção 3.2, mas pode continuar dentro das cópias
de segurança criptografadas por até 90 dias, sem uso, até o descarte. Se for preciso recuperar o serviço a partir de
uma cópia, as contas apagadas nesse intervalo são apagadas de novo antes de o serviço voltar.

## 4. Como tratamos os dados

- **Coleta**: só o que você digita no app e os registros da seção 3.4.
- **Separação entre contas**: os dados de cada pessoa só aparecem para ela. O app filtra todas as consultas pela conta
  de quem pede, inclusive nas tarefas automáticas, e testes automáticos conferem essa separação a cada mudança no app.
- **Sem decisões automatizadas e sem perfil**: o app não classifica você, não dá nota de crédito, não oferece
  produtos e não usa seus dados para publicidade.
- **Armazenamento**: em servidores nos Estados Unidos (seção 5), com conexão sempre criptografada (HTTPS).

## 5. Com quem compartilhamos

Não vendemos nem cedemos seus dados. Eles passam apenas pelas empresas que prestam serviço para o Prisma Money
funcionar, cada uma só com o necessário:

| Empresa | Para quê | Onde | O que recebe |
|---|---|---|---|
| Railway | hospedagem do app e do banco de dados | Estados Unidos (costa oeste) | todos os dados do app e os registros da seção 3.4 |
| Resend | envio dos e-mails da conta | Estados Unidos (envio pela região de São Paulo) | seu e-mail e o conteúdo da mensagem |
| Cloudflare | endereço do site (DNS), guarda da cópia de segurança criptografada e repasse das mensagens enviadas ao contato | Estados Unidos e rede global | a cópia criptografada (sem a chave) e as mensagens enviadas ao contato |
| Google (Gmail) | caixa de entrada onde chegam as mensagens enviadas ao contato | Estados Unidos e rede global | as mensagens que você enviar ao contato |

Os dados de mercado (lista de ativos da bolsa e logotipos das empresas) vêm da brapi.dev. **Nenhum dado seu nem
identificador da sua conta é enviado a ela**: o app só baixa a lista pública, sem relação com quem usa.

Podemos ter de fornecer dados a autoridades quando houver ordem judicial ou obrigação legal.

## 6. Transferência internacional

As empresas da seção 5 tratam dados fora do Brasil, principalmente nos Estados Unidos. A finalidade é a mesma da
seção 3: hospedar o app, enviar os e-mails da conta e guardar as cópias de segurança.

## 7. Seus direitos

Pela LGPD (art. 18), você pode pedir:

- **confirmação** de que tratamos seus dados e **acesso** a eles;
- **correção** de dados incompletos, errados ou desatualizados (a maior parte você mesmo corrige no app);
- **anonimização, bloqueio ou eliminação** de dados desnecessários, excessivos ou tratados em desacordo com a lei;
- **portabilidade** dos seus dados para outro serviço, nos termos da regulamentação da ANPD;
- **eliminação** dos seus dados;
- **informação** sobre com quem compartilhamos seus dados (seção 5);
- **informação** sobre as consequências de não fornecer dados.

**Como pedir:**

- **Apagar a conta**, com todos os seus dados, você faz no próprio app, no menu da conta (pede a sua senha).
- **Os demais pedidos**, por **contato@prismamoney.com.br**. Para proteger seus dados, podemos pedir uma confirmação
  de que você é o titular da conta, proporcional ao pedido (por exemplo, responder a partir do e-mail da conta ou
  confirmar um código enviado a ele).
- **Uma cópia dos seus dados** (lançamentos, contas, carteira e notas) é enviada em arquivo de formato aberto (JSON),
  a partir de pedido por e-mail.

**Prazos (art. 19):** a confirmação de que tratamos seus dados e o acesso simplificado são respondidos de imediato
sempre que possível; a declaração completa (origem dos dados, critérios e finalidade do tratamento), em até **15 dias**
a partir do pedido.

Você também pode reclamar à Autoridade Nacional de Proteção de Dados (ANPD): https://www.gov.br/anpd

## 8. Cookies e armazenamento no navegador

O app usa **um único cookie**, estritamente necessário: o que mantém você conectado à sua conta. Ele só é enviado ao
próprio Prisma Money, não é lido por outros sites e não serve para rastrear você. A página de apresentação
(prismamoney.com.br para quem não entrou) não grava nenhum cookie.

O navegador também guarda três preferências, que nunca saem do seu aparelho: o tema (claro ou escuro), a última conta
usada ao lançar e a última corretora usada numa nota de corretagem.

Não usamos ferramentas de análise de público, pixels de anúncio nem scripts de terceiros.

## 9. Idade mínima

O Prisma Money é destinado a pessoas com **18 anos ou mais**, condição que a pessoa declara ao criar a conta. Se
soubermos que uma conta pertence a um menor de 18 anos, ela será apagada. Responsáveis podem avisar por
contato@prismamoney.com.br.

## 10. Segurança

Entre as medidas que adotamos:

- conexão sempre criptografada (HTTPS, com TLS 1.3);
- senha guardada só como hash (seção 3.1), com bloqueio temporário depois de várias tentativas erradas;
- confirmação de e-mail e links de acesso que valem uma vez e por pouco tempo;
- separação dos dados de cada conta em todas as consultas, conferida por testes automáticos;
- registros do app sem valores, descrições, e-mails, senhas ou links;
- cópias de segurança diárias criptografadas, guardadas fora do provedor de hospedagem, com recuperação testada;
- verificação em duas etapas nas contas administrativas dos serviços usados.

Nenhum sistema é totalmente imune a falhas. Se acontecer um incidente de segurança que possa trazer risco ou dano
relevante a você, comunicaremos você e a ANPD nos prazos da Resolução CD/ANPD nº 15/2024.

## 11. Mudanças nesta política

Cada versão desta política tem número e data de vigência, e as anteriores ficam disponíveis no site.

- **Mudanças de redação** (clareza, correções que não mudam o tratamento) entram com a nova versão.
- **Mudanças relevantes** (novas finalidades, novas empresas com acesso aos dados, outra forma ou prazo de guarda,
  mudança nos seus direitos) são avisadas por e-mail e no app **antes** de valer.
- Quando uma mudança exigir o seu consentimento, pediremos esse consentimento de forma específica: continuar usando o
  app não substitui o consentimento.

## 12. Contato

- **E-mail**: contato@prismamoney.com.br
- **ANPD**: https://www.gov.br/anpd

---

**Histórico de versões**

- v1.0 (15/10/2026): publicação inicial.
