# Arquitetura

O Core contém lease, CAS, estados e contratos. Infrastructure implementa R2, ZIP, diários e recuperação. Platform.Windows contém Credential Manager, descoberta dos saves e processo do Valheim. App compõe os serviços e apresenta WPF com NotifyIcon.

Cada instalação possui identidade global e jogador local. Perfis isolam conexão, caminho opcional, sessão, logs, downloads e recuperação. Credenciais ficam no Windows Credential Manager. Cada mundo usa `worlds/<worldId>/` como prefixo remoto.

`lock.json` é o manifesto autoritativo no schema v2, com nome exibido, pasta canônica, retenção e autor. Toda mutação usa ETag e compare-and-swap. O ZIP é imutável e enviado antes de mudar `Current`; a versão anterior entra em `History`.

Heartbeat ocorre a cada 60 segundos e a lease expira em 180 segundos. Download, jogo, snapshot, upload, reset e backoff permanecem cobertos. Uma sessão só publica se ainda possuir a lease e a versão-base não tiver avançado.

Instalações extraem em `.vws-work-*` fora de `worlds_local`, registram `install.json`, renomeiam no mesmo volume e preservam o mundo anterior como ZIP verificado. O catálogo local não remove cópias automaticamente.

Convites usam AES-256-GCM e PBKDF2-HMAC-SHA256 com parâmetros versionados. O payload inclui conexão e credenciais, mas exclui jogador, caminhos e identidade local.

O grupo compartilha credenciais de escrita. Qualquer integrante pode publicar ou reinicializar seguindo o protocolo. Um papel exclusivo de proprietário exigiria um coordenador externo.
