# Valheim World Sync

Aplicativo Windows em .NET 10/WPF que alterna o anfitrião de um mundo local do Valheim entre amigos usando Cloudflare R2. Antes de abrir o jogo, o launcher adquire uma lease e baixa a versão atual; ao fechar o processo do Valheim, cria um snapshot completo e publica o progresso por CAS.

## Primeiro uso

1. Execute `ValheimWorldSync.exe`. O aplicativo é self-contained; Steam e Valheim continuam sendo dependências externas.
2. Informe seu nome local e escolha **Criar mundo compartilhado** ou **Entrar com convite**.
3. Ao criar, informe endpoint S3, bucket e chaves R2, use **Testar R2** e selecione a pasta completa do mundo. O launcher deriva o nome da pasta e usa `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local` por padrão.
4. Publique o mundo com **Importar mundo local** e exporte um `.vwsinvite`. O convite é cifrado por senha; envie arquivo e senha por canais separados.
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

Access Key ID e Secret Access Key ficam no Windows Credential Manager sob `ValheimWorldSync/profile/<id>/r2`. A migração do antigo `config.json` é automática: o arquivo legado só é removido depois de gravar e reler a credencial protegida e concluir os novos arquivos.

Vários perfis podem compartilhar um bucket. Novos mundos usam `worlds/<worldId>/`; perfis legados preservam o prefixo vazio para não mover objetos existentes.

## Concorrência, recuperação e reset

- Heartbeat de 60 segundos e TTL de 180 segundos durante download, jogo, upload, retry e operações administrativas.
- Até cinco tentativas com backoff; a UI mostra bytes, tamanho total, tentativa e espera.
- ZIP imutável publicado antes da troca CAS do manifesto. Perder a lease preserva o progresso local.
- Staging fica em `.vws-work-*` no diretório pai de `worlds_local`, mantendo os renomes no mesmo volume sem aparecer no seletor do jogo.
- O mundo substituído é compactado e verificado em `recovery/<perfil>`; artefatos legados `.vws-backup-*` e `.vws-staging-*` também são migrados.
- **Recuperação** lista data, jogador, tamanho e origem, com exportação, restauração local e exclusão explícita.
- **Voltar à nuvem** preserva o progresso divergente antes de limpar a pendência.
- **Reinicializar remoto** exige Valheim fechado, lease exclusiva, checkbox e digitação do nome exato. O remoto anterior é baixado para recuperação e permanece no histórico antes da publicação CAS.

Não há mesclagem de mundos. Qualquer integrante com credencial de escrita pode reinicializar o remoto; isolamento real de proprietário exigiria um serviço de autorização externo ao R2.

## Protocolo e limites

O manifesto `lock.json` aceita schemas v1 e v2. O v2 publica nome exibido, pasta canônica, retenção e autor das novas versões. A atualização ocorre somente com lease e CAS. Snapshots ficam em `backups/<timestamp>-<id>.zip` dentro do prefixo do mundo.

Limites atuais: ZIP de 4 GiB, conteúdo expandido de 32 GiB e 500 mil entradas. Links, junctions, dispositivos Windows e caminhos ZIP inseguros são rejeitados. Personagens, mods, conversão de saves legados e servidor dedicado ficam fora do escopo.

## Desenvolvimento

Requer SDK .NET 10.0.302 ou feature band posterior:

```powershell
./scripts/verify.ps1
./scripts/publish.ps1
```

O executável único sai em `artifacts/publish/win-x64/ValheimWorldSync.exe`. Testes R2 que alteram manifesto exigem `VWS_R2_TEST_CONFIG` apontando para um bucket ou prefixo exclusivo.
