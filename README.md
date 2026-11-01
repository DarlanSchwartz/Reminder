# Reminder

Widget nativo Windows em C# / WPF (.NET 10). Interface em português, sem WebView, serviços residentes adicionais ou pacotes externos.

## Abrir

Execute `G:\VibeCodes\Reminder\app\Reminder.exe`. É necessário o .NET Desktop Runtime 10, já instalado neste computador. O aplicativo é portátil: mantenha a pasta `app` no mesmo lugar após ativar a integração com o Windows.

- **Nova tarefa:** o botão abre um assistente passo a passo dentro do próprio widget, sem janelas extras: tarefa (nome, descrição, prazo opcional) → alarme (atalhos “Em 1 hora”, “Hoje às 20:00”, “Amanhã às 09:00” ou data e hora) → repetição → som. Sem alarme, o assistente termina no primeiro passo.
- **Editar:** clique no texto ou no lápis da tarefa. Excluir fica no primeiro passo do assistente.
- **Concluir:** marque a caixa. Em tarefas recorrentes, concluir avança para a próxima ocorrência; no fim da série, a tarefa é concluída. Reabrir uma tarefa não rearma alarmes antigos.
- **Repetição:** não repetir, a cada N horas, dias, semanas (escolhendo os dias da semana, com atalhos “Dias úteis”, “Fim de semana” e “Todos”), meses ou anos. Termina “Nunca” ou “Em uma data” (inclusive). O resumo mostra a regra e a próxima ocorrência. Dias 29, 30 e 31 usam o último dia em meses mais curtos sem perder o dia original; 29/02 anual cai em 28/02 fora de anos bissextos. Tarefas mensais antigas são convertidas automaticamente.
- **Alarme:** tela cheia com relógio, sino animado, nome e descrição da tarefa, regra de repetição com a próxima ocorrência e a música atual. A tela treme a cada 3 segundos enquanto toca. Botões: concluir, adiar 5, 10 ou 30 minutos e dispensar (`Esc`). Adiar altera apenas o aviso atual.
- **Música:** no último passo, escolha uma música (MP3 ou WAV, copiada para `app\data\sounds`) ou uma pasta, da qual uma música aleatória toca a cada alarme. A música repete enquanto o alerta estiver aberto; falhas usam o som padrão.
- **Abas:** “A fazer” e “Concluídas” ficam no cabeçalho, com a contagem de cada lista.
- **Alfinete:** alterna entre flutuar acima das janelas e uma janela normal. Arraste pelo cabeçalho e redimensione pelas bordas.
- **Traço:** oculta o widget na bandeja. Clique duas vezes no ícone perto do relógio para reabrir.
- **Engrenagem:** iniciar com o Windows, integração com o Agendador, pasta de músicas padrão, testar o alerta e sair.
- O ícone próprio aparece na barra de tarefas e na bandeja. Para mantê-lo na barra mesmo com o app fechado, use o comando do próprio Windows **Fixar na barra de tarefas**.

## Dados e integração

As tarefas ficam em `app\data\tasks.json`, com a versão anterior em `tasks.json.bak`. As gravações substituem o arquivo de forma atômica. Um arquivo inválido é preservado e causa um erro visível, em vez de apagar suas tarefas. Arquivos e caches de compilação ficam dentro do projeto no G:.

O Agendador de Tarefas é acessado pela API COM oficial. Cada alarme possui um registro próprio, e há um registro separado para a inicialização. Nomes usam `Reminder-<identificador>-<id>`. O Windows mantém esses registros em seu armazenamento interno no disco do sistema; por isso, a primeira ativação pede autorização explícita. Nenhuma senha é armazenada. Desativar toda a integração remove os registros; alarmes já salvos ainda podem disparar enquanto o app estiver aberto.

Os alarmes usam a sessão interativa do usuário. A tela de bloqueio, a tela segura de UAC, aplicativos em tela cheia exclusiva e o computador desligado impedem a exibição imediata sobre tudo. O agendador tenta recuperar horários perdidos e solicitar despertar; isso depende das políticas de energia e dos temporizadores de ativação do computador. Ao abrir o app, alarmes pendentes vencidos são recuperados. Não há promessa de despertar um computador desligado.

Referências: [StartWhenAvailable](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-startwhenavailable) e [WakeToRun](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-waketorun-settingstype-element).

Não há consulta periódica com uma lista sem alarmes. Com alarmes pendentes, há um único temporizador, que confere o próximo horário no máximo uma vez por minuto. A comunicação entre instâncias usa named pipes limitados ao usuário atual. O consumo de memória inclui o runtime WPF; não é um aplicativo de consumo zero.

## Compilar e testar

```powershell
.\build.ps1
$test = Start-Process .\app\Reminder.exe -ArgumentList --self-test -PassThru -Wait
Get-Content .\app\test-results.txt
```

Feche o aplicativo antes de substituir o binário. `build.ps1` redireciona caches e temporários para o projeto e desativa a geração de certificados do .NET. O `NuGet.Config` remove fontes de download: a compilação usa os packs já instalados.

Os testes internos cobrem recorrências por hora, dia, semana, mês e ano, datas de término, anos bissextos, migração de dados antigos, adiamento, lista inicialmente vazia, Unicode, persistência de horários, opt-in do agendador, rollback de edição quando o agendamento falha, preservação em falha de cancelamento, conclusão, exclusão, backup e preservação de arquivo corrompido. Eles usam um agendador falso e dados isolados em `test-output`, sem alterar as tarefas pessoais ou o Agendador do Windows.

Código dividido em modelo de tarefas, armazenamento, serviço de operações, interface do agendador, implementação Windows e janelas WPF. O ícone possui tamanhos de 16 a 256 pixels em `Assets\Reminder.ico`.
