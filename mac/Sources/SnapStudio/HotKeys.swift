import AppKit
import Carbon.HIToolbox

/// Atalhos globais (funcionam com qualquer app em primeiro plano) via Carbon RegisterEventHotKey.
/// Não precisa da permissão de Acessibilidade.
final class HotKeyCenter {
    static let shared = HotKeyCenter()

    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var actions: [UInt32: () -> Void] = [:]
    private var handlerInstalled = false

    private init() {}

    private func installHandler() {
        if handlerInstalled { return }
        handlerInstalled = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ -> OSStatus in
            var hk = EventHotKeyID()
            let st = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                       nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            if st == noErr {
                let id = hk.id
                DispatchQueue.main.async { HotKeyCenter.shared.actions[id]?() }
            }
            return noErr
        }, 1, &spec, nil, nil)
    }

    /// Regista um atalho. Devolve false se outra app já o usa.
    @discardableResult
    func register(id: UInt32, _ key: Hotkey, action: @escaping () -> Void) -> Bool {
        installHandler()
        if key.isNone { return true }
        var ref: EventHotKeyRef?
        let hkID = EventHotKeyID(signature: OSType(0x534E_5053), id: id) // 'SNPS'
        let st = RegisterEventHotKey(key.keyCode, key.modifiers, hkID, GetApplicationEventTarget(), 0, &ref)
        guard st == noErr, let r = ref else { return false }
        refs[id] = r
        actions[id] = action
        return true
    }

    func unregisterAll() {
        for (_, r) in refs { UnregisterEventHotKey(r) }
        refs.removeAll()
        actions.removeAll()
    }
}
