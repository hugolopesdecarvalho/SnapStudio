import AppKit

// Modo de compilação: gerar o ícone (usado pelo build.sh)
let args = CommandLine.arguments
if args.count >= 3 && args[1] == "--make-icon" {
    AppIcon.writeIconset(to: URL(fileURLWithPath: args[2]))
    exit(0)
}
// Gerar o ícone da versão Windows (assets/app.ico) com o mesmo desenho
if args.count >= 3 && args[1] == "--make-ico" {
    AppIcon.writeIco(to: URL(fileURLWithPath: args[2]))
    exit(0)
}
// Modo de teste: desenha todas as anotações sobre uma imagem sintética e grava PNGs
if args.count >= 3 && args[1] == "--render-test" {
    exit(SelfTest.run(outDir: URL(fileURLWithPath: args[2])) ? 0 : 1)
}
// Imagens para o README: janelas reais com capturas fictícias
if args.count >= 3 && args[1] == "--readme-shots" {
    DemoShots.run(outDir: URL(fileURLWithPath: args[2]))
    exit(0)
}
// Teste: imagem anterior/seguinte no editor
if args.count >= 3 && args[1] == "--nav-test" {
    exit(DemoShots.navTest(outDir: URL(fileURLWithPath: args[2])) ? 0 : 1)
}
// Teste: fotografa o painel de propriedades do editor em vários casos
if args.count >= 3 && args[1] == "--props-shots" {
    DemoShots.propsShots(outDir: URL(fileURLWithPath: args[2]))
    exit(0)
}
// Teste: fotografa a janela das Definições em cada secção
if args.count >= 3 && args[1] == "--settings-shots" {
    DemoShots.settingsShots(outDir: URL(fileURLWithPath: args[2]), theme: args.count > 3 ? args[3] : "light")
    exit(0)
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
