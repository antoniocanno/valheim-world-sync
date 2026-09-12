# Arquitetura v1

Windows x64, .NET 10, WPF + NotifyIcon, um mundo local por bucket e anfitriões Steam.
Core define regras e contratos; Infrastructure implementa R2, ZIP e recuperação em disco;
App compõe serviços e controla UI/processo. Diagnostics usa bucket de teste explicitamente.

`lock.json` é o manifesto autoritativo: posse temporária e ponteiro para ZIP imutável
em `backups/`. Todas as mutações do manifesto usam CAS por ETag; liberação mantém o
manifesto, removendo somente a posse. Nunca publicar por sobrescrita de current.zip.
Cada operação tem ID único para reconciliar respostas perdidas. Heartbeat 60s, TTL 180s,
durante toda a operação. Perder posse preserva o progresso local; a retomada automática
exige readquirir posse e a mesma versão-base. Conflitos requerem recuperação manual.

Formato suportado: pasta completa de mundo 1.0, tratada como conteúdo opaco, sem
selecionar apenas .db/.fwl. Conversão de mundos antigos cabe ao jogo. Validar em save
real antes de usar em produção. Personagens, mods e servidor dedicado fora do escopo.

Configuração local fora do executável/Git. Diário local antes de operações destrutivas,
staging e backup antes de instalar save, snapshot durável antes de publicar. Não garantir
transação entre renomeações de diretório; recuperar pelo diário antes de abrir o jogo.
Cada PC cria sua identidade em `installation.json`, separada da configuração compartilhável.

Retenção: vigente + 10 anteriores por padrão; exclusões registradas antes da remoção.
Objetos abandonados não são varridos automaticamente. Bucket exclusivo para cada mundo.
Clientes confiáveis devem usar o protocolo; credenciais compartilhadas não impedem um
cliente alterado de ignorar as regras. Sem mesclagem de mundos ou progresso em memória.

Commits: estrutura; R2/CAS; snapshots; orquestração; jogo; UI; retenção; distribuição.
Build e testes a cada bloco. R2 real e duas contas Steam são gates de aceitação externos.
