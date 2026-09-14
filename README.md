# Valheim World Sync

Aplicativo Windows em .NET 10/WPF que alterna o anfitrião de um mundo local do Valheim entre amigos usando Cloudflare R2. Antes de abrir o jogo, o launcher adquire uma lease e baixa a versão atual; ao fechar o processo do Valheim, cria um snapshot completo e publica o progresso por CAS.

## Criar o bucket e obter os dados do R2

Esta etapa é feita uma única vez pelo dono do mundo. Quem entra por convite pode pular para **Primeiro uso**.

Pré-requisito: conta Cloudflare com R2 ativo.

1. Crie o bucket: no dashboard Cloudflare vá em **Storage & databases → R2 → Overview → Create bucket**. Informe nome, localização e classe de armazenamento e anote o nome exato do bucket (sensível a maiúsculas/minúsculas).
2. Anote o endpoint S3: na página **R2 → Overview** copie o endpoint da conta, no formato `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` (o Account ID aparece no dashboard). Buckets com jurisdição usam o endpoint correspondente, por exemplo `https://<ACCOUNT_ID>.eu.r2.cloudflarestorage.com`. O aplicativo aceita apenas URL HTTPS terminada em `.r2.cloudflarestorage.com`, sem caminho, query ou usuário/senha embutidos.
3. Gere as credenciais S3: em **R2 → Overview → Account Details → Manage** ao lado de **API Tokens**, escolha **Create Account API token** (vale até revogação manual) ou **Create User API token** (herda suas permissões e é desativado se você sair da conta). Em **Permissions** escolha **Object Read & Write** com **Apply to specific buckets only** e selecione apenas o bucket criado. Evite **Admin Read & Write**, que permite criar, listar e excluir buckets e alterar a configuração de todos os buckets da conta.
4. Guarde na hora o **Access Key ID** e o **Secret Access Key**. O segredo não é exibido novamente.
5. No aplicativo, preencha assim:

   | Campo do aplicativo | Valor do R2 |
   | --- | --- |
   | Endpoint | URL do passo 2 |
   | Bucket | Nome do passo 1 |
   | Access Key ID | Chave do passo 4 |
   | Secret Access Key | Segredo do passo 4 |

6. Use **Testar R2**. O teste faz escrita, leitura e exclusão de um arquivo temporário em `worlds/<worldId>/diagnostics/`; por isso um token somente-leitura falha por design. A mensagem esperada é de leitura, escrita e exclusão disponíveis.

Se falhar, confira: endpoint com `https://` e sufixo correto, nome do bucket digitado igual ao criado, par de chaves trocado (secret perdido exige gerar um novo token) e permissão **Object Read & Write** no bucket certo.

Referências oficiais (em inglês): [S3 API e credenciais](https://developers.cloudflare.com/r2/get-started/s3), [autenticação e permissões de tokens](https://developers.cloudflare.com/r2/api/tokens) e [preços de armazenamento e operações](https://developers.cloudflare.com/r2/platform/pricing).

## Primeiro uso

1. Execute `ValheimWorldSync.exe`. O aplicativo é self-contained; Steam e Valheim continuam sendo dependências externas.
2. Informe seu nome local e escolha **Criar mundo compartilhado** ou **Entrar com convite**.
3. Ao criar, use os dados obtidos na seção **Criar o bucket e obter os dados do R2**, clique em **Testar R2** e selecione a pasta completa do mundo. O launcher deriva o nome da pasta e usa `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local` por padrão.
4. Publique o mundo com **Importar mundo local** e exporte um `.vwsinvite`. O convite já sai cifrado por senha; envie arquivo e senha por canais separados.

> ⚠️ Compartilhe o mundo e o convite apenas com amigos de confiança. O convite contém as chaves R2 com leitura e escrita no bucket: quem tiver o arquivo e a senha pode ler, apagar e publicar objetos e gerar custos de armazenamento e operações na sua conta Cloudflare. O aplicativo não isola jogadores por prefixo.
5. Para entrar, importe o convite e informe sua senha. Caminhos, nome do jogador e identidade da instalação nunca vêm do computador do dono.
6. Clique em **Jogar** e escolha no Valheim o nome exato destacado pelo launcher. Aguarde **Sincronizado** após encerrar o jogo.

O aplicativo trabalha apenas com mundos locais. Se detectar sinais de Steam Cloud, mostra a orientação **Manage Saves → Worlds → Move to Local**. Ele nunca altera diretamente os arquivos da Steam Cloud.

## Configuração e credenciais

Os dados ficam em `%LOCALAPPDATA%\ValheimWorldSync`:

- `settings.json`: jogador global, onboarding e perfil selecionado;
- `installation.json`: identidade local desta instalação;
- `profiles/<id>/connection.json`: endpoint, bucket, mundo, prefixo e metadados não secretos;
- `profiles/<id>/local.json`: alias, override avançado do save e referência da credencial;
- `profiles/<id>/session.json`: sessão durável do perfil;
- `recovery/<id>`: ZIPs verificados e metadados de recuperação.

Access Key ID e Secret Access Key ficam no Windows Credential Manager sob `ValheimWorldSync/profile/<id>/r2`.

Vários perfis podem compartilhar um bucket. Cada mundo usa `worlds/<worldId>/` como prefixo remoto, mas o R2 isola por bucket, não por prefixo: quem tem a credencial enxerga todos os mundos do mesmo bucket.

## Concorrência, recuperação e reset

- Heartbeat de 60 segundos e TTL de 180 segundos durante download, jogo, upload, retry e operações administrativas.
- Até cinco tentativas com backoff; a UI mostra bytes, tamanho total, tentativa e espera.
- ZIP imutável publicado antes da troca CAS do manifesto. Perder a lease preserva o progresso local.
- Staging fica em `.vws-work-*` no diretório pai de `worlds_local`, mantendo os renomes no mesmo volume sem aparecer no seletor do jogo.
- O mundo substituído é compactado e verificado em `recovery/<perfil>`.
- **Recuperação** lista data, jogador, tamanho e origem, com exportação, restauração local e exclusão explícita.
- **Voltar à nuvem** preserva o progresso divergente antes de limpar a pendência.
- **Reinicializar remoto** exige Valheim fechado, lease exclusiva, checkbox e digitação do nome exato. O remoto anterior é baixado para recuperação e permanece no histórico antes da publicação CAS.

Não há mesclagem de mundos. Qualquer integrante com credencial de escrita pode reinicializar o remoto; isolamento real de proprietário exigiria um serviço de autorização externo ao R2.

## Protocolo e limites

O manifesto `lock.json` segue o schema v2 com nome exibido, pasta canônica, retenção e autor das novas versões. A atualização ocorre somente com lease e CAS. Snapshots ficam em `backups/<timestamp>-<id>.zip` dentro do prefixo do mundo.

Limites atuais: ZIP de 4 GiB, conteúdo expandido de 32 GiB e 500 mil entradas. Links, junctions, dispositivos Windows e caminhos ZIP inseguros são rejeitados. Personagens, mods, conversão de saves e servidor dedicado ficam fora do escopo.

## Desenvolvimento

Requer SDK .NET 10.0.302 ou feature band posterior:

```powershell
./scripts/verify.ps1
./scripts/publish.ps1
```

O executável único sai em `artifacts/publish/win-x64/ValheimWorldSync.exe`. Testes R2 que alteram manifesto exigem `VWS_R2_TEST_CONFIG` apontando para um bucket ou prefixo exclusivo.
