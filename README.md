# SnapStudio

Ferramenta de captura de ecrã para Windows. Um único `.exe`, sem instalação (.NET Framework 4.8, já incluído no Windows 10/11).

## Executar
`bin\SnapStudio.exe` → fica junto ao relógio (bandeja).
**Clique no ícone** para abrir o painel: capturar, ver as capturas recentes, mudar o tema, abrir o histórico. Botão direito: menu completo.

## Capturar
| Tecla | Ação |
|---|---|
| **PrtScn** | Modo captura: clique numa janela, arraste uma região, ou **Enter** = ecrã inteiro, **A** = todos os ecrãs, **Esc** = cancelar |
| Alt+PrtScn | Janela ativa |
| Ctrl+PrtScn | Todos os ecrãs |
| Shift+PrtScn | Gravar vídeo do ecrã (iniciar / parar) |

Depois de capturar, a imagem é guardada em `Imagens\SnapStudio`, **fica na área de transferência** (Ctrl+V em qualquer app) e abre, conforme as Definições:
- **Editor rápido** (pop-up junto ao relógio): seta, caixa, texto, esbater, passos, recorte; *Copiar e fechar*. Esc ou clicar fora guarda e fecha.
- **Editor completo**: todas as ferramentas, propriedades (cor, espessura, tamanho do texto) e lista de anotações.
- **Nada**: só guardar e copiar, com notificação.

> Se outra app ou a Ferramenta de Recorte do Windows estiver a usar a tecla PrtScn, a SnapStudio avisa. Feche essa app, ou desligue em *Definições do Windows › Acessibilidade › Teclado › "Usar a tecla Print Screen para abrir a captura de ecrã"*.

## Gravar vídeo
**Shift+PrtScn** (ou o botão *Gravar vídeo do ecrã* no painel) → escolha uma janela, uma região ou o ecrã inteiro, como no modo captura. Após uma contagem de 3 s começa a gravar: uma moldura vermelha marca a zona e uma barra flutuante mostra o tempo, com **Pausar**, **Parar** e **Cancelar**. Shift+PrtScn outra vez também pára.

O vídeo é guardado em MP4 (H.264) na pasta das capturas, copiado como ficheiro para a área de transferência (Ctrl+V no Teams, Outlook ou numa pasta) e aparece nos recentes e no histórico. A moldura e a barra não aparecem no vídeo. Usa o codificador do próprio Windows (Media Foundation): nada a instalar. Por agora grava sem som.
Em *Definições › Vídeo*: 15/30/60 fps e mostrar ou não o cursor.

## Editor
Seta (A), Caixa (R), Texto (T), **Esbater** (B) e Pixelizar (X) para ocultar zonas, Marcador (H), Número de passo (N), Elipse (E), Linha (L), Desenho livre (P), Recortar (C), Selecionar/mover (V).
Ctrl+S guardar · Ctrl+C copiar · Ctrl+Z/Ctrl+Y anular/refazer · Delete apaga · Shift ao desenhar = ângulos de 45°/quadrados · Ctrl+roda = zoom · botão "Arrastar" para largar a imagem no Outlook/Teams.
As anotações continuam editáveis ao reabrir a captura.

## Histórico
Cartões com miniaturas agrupados por data, pesquisa (Ctrl+F), filtros (Hoje, Esta semana, Com anotações), ações ao passar o rato (copiar, editar, apagar), arrastar para outras apps, F2 para mudar o nome.

## Definições
Geral (iniciar com o Windows, usar PrtScn), Aparência (tema claro/escuro/igual ao Windows, cor de destaque), Depois de capturar, Atalhos, Ficheiros (pasta e formato PNG/JPG), Vídeo (fps, cursor).

## Compilar
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Não precisa de .NET SDK: usa o `csc.exe` do .NET Framework incluído no Windows. Código em `src\`. Dados do utilizador em `%APPDATA%\SnapStudio`.
