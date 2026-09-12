# Valheim World Sync

Aplicativo Windows para alternar o anfitrião de um mundo local de Valheim entre amigos.
WPF + bandeja, .NET 10 e armazenamento Cloudflare R2. O app baixa o mundo antes de abrir
o jogo e publica o progresso quando o processo do Valheim termina.

**Estado:** v1 implementada com testes locais e renderização WPF. A validação em R2 real,
em um save real do Valheim 1.0 e em duas contas Steam continua necessária antes de usar
o mundo principal do grupo. Não há bucket de testes configurado neste ambiente.

## Primeiro uso

1. Execute `ValheimWorldSync.exe`. Não é necessário instalar .NET. Steam e Valheim
   precisam estar instalados separadamente.
2. Abra **Configuração**. O app cria
   `%LOCALAPPDATA%\ValheimWorldSync\config.json` sem credenciais.
   Preencha endpoint S3 R2, bucket, chaves, apelido e um `worldId` igual para todo o grupo.
   O arquivo [config.example.json](config.example.json) descreve os campos.
3. Use um bucket privado exclusivo para este mundo. As chaves precisam permitir ler,
   escrever e excluir objetos nesse bucket. Cada instalação mantém seu próprio
   `installationId`. Não publique o arquivo com credenciais nem o coloque no Git.
4. Garanta que o mundo está salvo **localmente**, na pasta de saves do Valheim. Para
   mundos anteriores ao formato 1.0, converta e salve pelo próprio jogo primeiro.
   O app não sincroniza Steam Cloud e não converte saves.
5. No computador que possui o mundo inicial, feche o Valheim e escolha **Importar mundo
   local**. Selecione a pasta de um único mundo 1.0, incluindo todas as partes.
   Não selecione a pasta `worlds_local` inteira. O bucket precisa estar sem versão vigente.
6. Nos demais computadores, configure `worldFolderName` e `savesRoot` para o destino
   local do mesmo mundo, e clique em **Recarregar**. Não importe outra cópia.
7. Clique em **Jogar**. Depois que o app abrir o Valheim, escolha personagem, mundo e
   a opção de iniciar servidor dentro do jogo.
8. Ao encerrar o Valheim, aguarde **Sincronizado**. Fechar somente a janela do app o
   mantém na bandeja. O comando Sair fica bloqueado durante a sessão/sincronização.

Quando outra pessoa estiver hospedando, use **Amigos na Steam** e entre pela lista de
amigos/convite. O convidado não trava, baixa ou publica o mundo. O status indica uma
sessão gerenciada aberta; não garante que o anfitrião já abriu o servidor no jogo.

## Rede, conflitos e recuperação

- O heartbeat continua durante download, jogo, backup e upload: intervalo de 60 s e
  TTL de 180 s. A expiração permite a outro app adquirir posse via CAS; não é uma
  exclusão automática feita pelo R2.
- Após falha transitória, há até cinco tentativas com backoff. Pendências são guardadas
  em disco e verificadas a cada 60 s enquanto o app estiver aberto.
- Perder posse não encerra o jogo. O progresso local só pode ser publicado após
  readquirir posse e confirmar que a versão remota não avançou.
- Em conflito, **Exportar progresso** salva um ZIP para recuperação manual.
  **Voltar à nuvem…** requer confirmação, conserva um snapshot local e encerra a
  pendência. O próximo Jogar baixa a nuvem. Não existe mesclagem de mundos.
- **Recuperação** abre a pasta de dados do app. `session.json` registra a sessão ativa,
  `last-session.json` a última concluída e `snapshots/` guarda as cópias locais.
  Instalações preservam o diretório anterior em `.vws-backup-<id>`, ao lado do mundo.
- Após crash em um ponto em que não foi possível registrar a identidade do processo,
  a recuperação é conservadora: exige fechar o jogo e resolver a pendência manualmente.
- Se o jogo já estava aberto fora do app, seus arquivos não serão substituídos nem
  enviados automaticamente. Não abra o jogo por fora enquanto o app prepara os saves.
- Cópias locais, arquivos de staging e objetos de uploads abandonados não são apagados
  automaticamente na v1. Podem consumir espaço; remova somente após verificar que
  não são necessários à recuperação, com app e jogo fechados.
- A sincronização conserva bytes já salvos em disco; não recupera progresso que o jogo
  ainda não salvou antes de um crash. Um ZIP íntegro também não prova a validade interna
  de um save para o jogo.

## Armazenamento e limites

`lock.json` é o manifesto versionado com posse, referência vigente, histórico e fila
de exclusões. Os ZIPs ficam em `backups/<timestamp>-<id>.zip`. Não existe `current.zip`
mutável: primeiro o ZIP é enviado, depois o ponteiro é publicado por CAS.

Por padrão ficam a versão vigente e **10 anteriores** (`backupCount`, de 0 a 1000).
A limpeza só apaga versões retiradas do histórico por CAS; falhas deixam exclusões
pendentes para a próxima sincronização. Objetos abandonados não fazem parte da contagem.
Custos e disponibilidade de cotas do R2 dependem do uso e da conta.

A v1 usa upload simples: ZIP até **4 GiB**, conteúdo expandido até **32 GiB** e até
500 mil entradas. Links/junctions e caminhos ZIP inseguros são rejeitados. Um mundo por
configuração/bucket, Windows x64, clientes confiáveis na mesma versão de protocolo e
versões compatíveis do jogo. Não inclui personagens, mods, crossplay, servidor dedicado
ou restauração automática de versões históricas.

## Desenvolvimento

SDK .NET 10 (mínimo 10.0.302, com roll-forward para feature bands posteriores).

```powershell
./scripts/verify.ps1
./scripts/publish.ps1
```

O executável sai em `artifacts/publish/win-x64/ValheimWorldSync.exe`.
Ele inclui o runtime; bibliotecas nativas podem ser extraídas em uma pasta temporária
pelo .NET. O build não usa trimming. O executável não é assinado digitalmente.

Projetos: Core (protocolo e estados), Infrastructure (R2 e arquivos), App (WPF/Steam),
Diagnostics (console), UiSmoke (renderização sem acessar jogo ou R2) e dois projetos
de testes. xUnit v3 usa Microsoft Testing Platform no SDK .NET 10; o adaptador
Visual Studio continua disponível. Pacotes estão fixados e seus lockfiles versionados.

Logs locais limitados a dois arquivos de aproximadamente 1 MiB contêm apenas horários
e nomes de estados; não contêm chaves, endpoints ou conteúdo dos saves.

### R2 real — execução explícita

Prepare um arquivo separado com credenciais para **um bucket exclusivo de testes**.
Nunca use o mundo principal para validar operações do protocolo.

```powershell
dotnet run --project tools/ValheimWorldSync.Diagnostics -- status C:\caminho\config-teste.json
dotnet run --project tools/ValheimWorldSync.Diagnostics -- cas-test C:\caminho\config-teste.json
$env:VWS_R2_TEST_CONFIG = 'C:\caminho\config-teste.json'
dotnet test --project tests/ValheimWorldSync.IntegrationTests -c Release
```

Sem essa variável, o teste é marcado como **skipped**, não como validação real concluída.
As operações usam um prefixo `vws-tests/<id>/` exclusivo e preservam evidências para
inspeção. Uma limpeza posterior pode remover somente esses prefixos de teste.

Consulte [arquitetura](docs/architecture.md) e [aceitação](docs/acceptance.md).
