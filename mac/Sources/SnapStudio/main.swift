import AppKit

// Modo de compilação: gerar o ícone (usado pelo build.sh)
let args = CommandLine.arguments
if args.count >= 3 && args[1] == "--make-icon" {
    AppIcon.writeIconset(to: URL(fileURLWithPath: args[2]))
    exit(0)
}
// Modo de teste: desenha todas as anotações sobre uma imagem sintética e grava PNGs
if args.count >= 3 && args[1] == "--render-test" {
    exit(SelfTest.run(outDir: URL(fileURLWithPath: args[2])) ? 0 : 1)
}
// Modo de teste: desenha as janelas fora do ecrã (painel, editor, histórico, definições) para PNG
if args.count >= 3 && args[1] == "--ui-snapshot" {
    SelfTest.uiSnapshots(outDir: URL(fileURLWithPath: args[2]), theme: args.count > 3 ? args[3] : "light")
    exit(0)
}

let app = NSApplication.shared
let controller = AppController.shared
app.delegate = controller
app.setActivationPolicy(.accessory)
app.run()
