# SnapStudio para macOS

Versão nativa para Mac (Swift, AppKit e SwiftUI) da SnapStudio. Tem as mesmas funções da versão Windows e vive na **barra de menus**. Requer macOS 14 (Sonoma) ou mais recente e corre nativamente em Apple Silicon e Intel.

## Instalar a partir da release
1. Descarregue **SnapStudio-macOS.zip** da página *Releases* do repositório e abra-o. O Finder extrai a `SnapStudio.app`.
2. Arraste a `SnapStudio.app` para a pasta **Aplicações** e abra-a.
3. A app não está notarizada pela Apple, por isso o macOS bloqueia-a na primeira vez. Vá a *Definições do Sistema › Privacidade e segurança*, desça até à mensagem sobre a SnapStudio e clique em **Abrir mesmo assim**. Só é preciso uma vez.

## Compilar, instalar e executar
```bash
cd mac
./build.sh --install --run
```
A app fica em `/Applications/SnapStudio.app` e aparece na barra de menus (ícone de visor).
**Clique** no ícone para abrir o painel: capturar, gravar vídeo, ver as capturas recentes, mudar o tema e abrir o histórico. **Clique com o botão direito** (ou ⌃‑clique) para o menu completo.

### Autorização de Gravação do ecrã
Na primeira captura, o macOS pede autorização. Ative a SnapStudio em
*Definições do Sistema › Privacidade e segurança › Gravação do ecrã e áudio do sistema* e volte a abrir a app.
As *Definições › Geral* da SnapStudio mostram se a autorização está dada.

> A partir da 1.1.2, a app é assinada com uma identidade fixa (`com.snapstudio.mac`) e a autorização mantém-se ao atualizar ou recompilar. Se vem de uma versão anterior e a app continua a pedir autorização apesar de aparecer ativada, remova a entrada antiga com “−” nessa lista e autorize de novo (só uma vez). Para assinar com um certificado *Developer ID*: `SIGN_ID="Developer ID Application: …" ./build.sh`.

## Capturar
No Mac não existe tecla PrtScn. Os atalhos por omissão evitam os do sistema (⌘⇧3/4/5, que continuam a funcionar) e podem ser mudados em *Definições › Atalhos*:

| Tecla | Ação |
|---|---|
| **⌃⇧4** | Modo captura: clique numa janela, arraste uma região, ou **↩** = ecrã inteiro, **A** = todos os ecrãs, **Esc** = cancelar |
| ⌃⇧6 | Janela ativa |
| ⌃⇧3 | Todos os ecrãs |
| ⌃⇧5 | Gravar vídeo do ecrã (iniciar / parar) |

Depois de capturar, a imagem é guardada em `Imagens/SnapStudio`, **fica na área de transferência** (⌘V em qualquer app) e abre conforme as Definições:
- **Editor rápido** (pop-up junto à barra de menus): seta, caixa, texto, esbater, passos, recorte; *Copiar e fechar* (⌘↩). Esc ou clicar fora guarda e fecha.
- **Editor completo**: todas as ferramentas, propriedades (cor, espessura, tamanho do texto) e lista de anotações.
- **Nada**: só guardar e copiar, com notificação. Clicar na notificação abre o editor.

## Gravar vídeo
**⌃⇧5** (ou *Gravar vídeo do ecrã* no painel) → escolha uma janela, uma região ou o ecrã inteiro. Após uma contagem de 3 s, a gravação começa: uma moldura vermelha marca a zona e uma barra flutuante mostra o tempo, com **Pausar**, **Parar** e **Cancelar**. O tempo também aparece na barra de menus. ⌃⇧5 outra vez pára a gravação.
O vídeo é guardado em MP4 (H.264) e copiado como ficheiro (⌘V no Finder, Mail, Teams…). A moldura e a barra não aparecem no vídeo. Por agora grava sem som.

## Editor
Selecionar (V), Seta (A), Caixa (R), Texto (T), **Esbater** (B) e Pixelizar (X) para ocultar zonas, Marcador (H), Passo numerado (N), Elipse (E), Linha (L), Desenho livre (P), Recortar (C).
⌘S guardar · ⇧⌘S guardar como · ⌘C copiar · ⌘Z/⇧⌘Z anular/refazer · ⌫ apaga · setas movem (⇧ = 10 px) · ⇧ ao desenhar = ângulos de 45°/quadrados · pinça ou ⌘+roda = zoom · ⌘9 ajustar · ⌘0 tamanho real · botão *Arrastar* para largar a imagem noutra app.
As anotações continuam editáveis ao reabrir a captura (ficheiros auxiliares em `~/Library/Application Support/SnapStudio/edit`).

## Histórico
Cartões com miniaturas (imagens e vídeos) agrupados por data, pesquisa (⌘F), filtros (Hoje, Esta semana, Com anotações, Vídeos), ações ao passar o rato (copiar, editar, mostrar no Finder, Lixo), arrastar para outras apps, menu de contexto com *Mudar o nome…*. ⌫ envia a captura selecionada para o Lixo.

## Definições
Geral (iniciar ao arrancar o Mac, área de transferência, notificações, estado da autorização), Aparência (tema claro/escuro/igual ao macOS, cor de destaque), Depois de capturar, Atalhos, Ficheiros (pasta e formato PNG/JPG), Vídeo (15/30/60 fps, cursor). As alterações aplicam-se de imediato.
Dados do utilizador em `~/Library/Application Support/SnapStudio`.

## Compilar
Precisa só das Command Line Tools (`xcode-select --install`); o Xcode não é necessário.
```bash
./build.sh            # binário universal (arm64 + x86_64) → build/SnapStudio.app
FAST=1 ./build.sh     # só a arquitetura deste Mac (mais rápido)
./build.sh --run      # compila e abre
```
O `build.sh` chama o `swiftc` diretamente. O `Package.swift` serve para abrir o projeto no Xcode (`open Package.swift`).

Testes sem interface:
```bash
build/SnapStudio.app/Contents/MacOS/SnapStudio --render-test /tmp/snap-test
```
Este teste desenha todas as anotações sobre uma imagem sintética, verifica a serialização, a geometria e o esbatimento, e grava PNGs. `--ui-snapshot <pasta> [light|dark]` desenha o painel, os editores, o histórico e as definições fora do ecrã.

## Código (`Sources/SnapStudio`)
| Ficheiro | Equivalente Windows | Conteúdo |
|---|---|---|
| `AppController.swift` | `TrayApp.cs` | barra de menus, atalhos, fluxos de captura e vídeo, janelas, notificações |
| `Capture.swift`, `RegionSelector.swift` | `CaptureService.cs`, `RegionSelector.cs`, `Native.cs` | ScreenCaptureKit, lista de janelas, modo captura |
| `Annotations.swift` | `Annotations.cs` | modelo das anotações e desenho (Core Graphics + Core Image) |
| `EditorModel.swift`, `CanvasView.swift`, `EditorView.swift` | `EditorForm.cs` | editor rápido e completo |
| `VideoRecorder.swift`, `RecordingUI.swift` | `VideoRecorder.cs`, `RecordingUI.cs` | SCStream → AVAssetWriter (MP4/H.264), moldura e barra |
| `TrayPanel.swift`, `HistoryView.swift`, `SettingsView.swift` | `TrayPanel.cs`, `HistoryForm.cs`, `SettingsForm.cs` | painel, histórico, definições |
| `HotKeys.swift` | `MessageWindow` | atalhos globais (Carbon, sem pedir Acessibilidade) |
| `Settings.swift`, `Theme.swift`, `Strings.swift` | idem | definições, tema, textos PT/EN |
