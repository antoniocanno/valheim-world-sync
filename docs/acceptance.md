# Verificação de aceitação

## Automatizada neste repositório

- Disputa CAS, ETag antigo, expiração de posse, sessão antiga sem poder publicar/liberar.
- Versão-base, histórico, reconciliação após resposta perdida do PUT.
- Pasta completa, inventário de hashes, rejeição de caminhos ZIP inseguros e recuperação de renomeação interrompida.
- Importação, fechamento do jogo, upload pendente, conflito remoto e sessão sem alterações.
- Heartbeat durante upload depois do fechamento do jogo; recuperação após TTL com base inalterada.
- PID reutilizado, jogo já aberto e cancelamento com diário preservado.
- Retenção e exclusão interrompida; WPF sem erros de binding em três estados renderizados.

## Gates externos ainda pendentes

1. **R2 real:** executar Diagnostics e IntegrationTests em bucket de testes; comprovar
   exatamente um vencedor na disputa e rejeição de ETag antigo. Confirmar horário do
   serviço, assinatura/checksums, upload/download de um ZIP grande e acesso negado.
2. **Formato do jogo:** criar um mundo descartável na versão atual, salvar localmente,
   fechar, importar sua pasta completa, baixar em outra pasta/perfil e abrir no jogo.
   Validar que nenhum arquivo necessário fica fora da pasta selecionada.
3. **Dois jogadores Steam:** A hospeda e B entra pela Steam; B não publica. A fecha,
   sincroniza, e B abre como novo anfitrião com as mesmas construções/progresso.
4. **Falhas reais:** interromper rede durante transferência, matar somente o app com
   jogo aberto e suspender/retomar Windows. Confirmar preservação local, ausência de
   publicação por sessão substituída e recuperação antes de abrir o jogo novamente.
5. **Distribuição:** testar o EXE publicado em Windows x64 sem runtime .NET instalado,
   usuário sem privilégios de administrador; verificar bandeja, fechamento da janela,
   comportamento de instância única e saída durante sincronização.
6. **Recursos:** medir CPU/RAM ociosa e durante uma partida real. Não há varredura de
   saves durante o jogo; polling de processos a cada 2 s e heartbeat a cada 60 s.
   Validar que não há impacto perceptível nas máquinas dos jogadores.

Use cópias descartáveis até concluir estes gates. Não confundir hashes íntegros com
validação semântica do save, nem renderização WPF com teste completo de bandeja/Steam.
