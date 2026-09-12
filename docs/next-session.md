# Contexto para a próxima sessão

Implementação concluída sobre `8f77dd9` em commits lógicos: Credential Manager, perfis, recuperação fora de `worlds_local`, manifesto v2, configuração WPF, convites cifrados, onboarding, progresso, catálogo de recuperação, reset remoto e orientação de Steam Cloud.

Estado de validação em 12/09/2026:

- 57 testes descobertos; 53 passam e 4 são ignorados por dependências externas.
- Três testes R2 requerem `VWS_R2_TEST_CONFIG` com bucket ou prefixo isolado.
- O teste do Credential Manager é ignorado neste host porque não há sessão de logon interativa.
- `scripts/verify.ps1` passa, incluindo build Release e smoke WPF.
- `scripts/publish.ps1` gera um único `artifacts/publish/win-x64/ValheimWorldSync.exe`.

Ainda requer validação manual: abrir um mundo restaurado no Valheim, duas contas Steam, falhas reais de rede, máquina limpa sem .NET e consumo durante partida. Nunca imprimir credenciais nem usar o manifesto principal do grupo em testes destrutivos.
