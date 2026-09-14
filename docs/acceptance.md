# Verificação de aceitação

## Automatizada

- CAS, ETag antigo, lease expirada, resposta perdida e versão-base concorrente.
- Manifesto v2, prefixos por mundo e autor de versões.
- Snapshot completo, segurança ZIP, staging fora de `worlds_local` e crash.
- Isolamento entre perfis e descoberta do save padrão.
- Convite cifrado, senha errada, adulteração e ausência de segredo em texto simples.
- Upload, download, progresso, retry e teste R2 descartável.
- Importação, jogo, conflito, recuperação, retenção e reset preservando a versão anterior.
- Build Release e smoke dos bindings WPF.

## Gates externos pendentes

1. Validar Credential Manager em uma sessão Windows desktop interativa; o host automatizado atual retorna `ERROR_NO_SUCH_LOGON_SESSION`.
2. Fornecer bucket ou prefixo R2 exclusivo para os testes controlados por `VWS_R2_TEST_CONFIG`.
3. Abrir no Valheim uma cópia restaurada de um save real 1.0.
4. Executar o fluxo completo com duas contas Steam e duas instalações.
5. Interromper rede durante transferências grandes e suspender/retomar o Windows.
6. Testar o EXE em máquina Windows x64 limpa, sem runtime .NET.
7. Medir CPU e memória durante uma partida real.

Use cópias e namespaces descartáveis até concluir esses gates. Integridade de hash não substitui a validação do save pelo jogo.
